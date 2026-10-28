// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import com.azure.core.amqp.AmqpRetryOptions;
import com.azure.messaging.servicebus.ServiceBusClientBuilder;
import com.azure.messaging.servicebus.ServiceBusMessage;
import com.azure.messaging.servicebus.ServiceBusReceivedMessage;
import com.azure.messaging.servicebus.ServiceBusReceiverAsyncClient;
import com.azure.messaging.servicebus.ServiceBusSenderAsyncClient;
import com.azure.messaging.servicebus.ServiceBusSessionReceiverAsyncClient;
import com.azure.messaging.servicebus.models.ServiceBusReceiveMode;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.function.Consumer;
import java.util.function.Function;
import reactor.core.publisher.Flux;
import reactor.core.publisher.Mono;
import reactor.core.scheduler.Schedulers;

final class QueueMessages {
    static final String SESSION = "23424";
    static final Duration RECEIVE_LIMIT = Duration.ofSeconds(45);
    static final Duration QUIET_WINDOW = Duration.ofSeconds(5);

    private QueueMessages() {
    }

    static ServiceBusClientBuilder builder(String connectionString) {
        return new ServiceBusClientBuilder().connectionString(connectionString)
            .retryOptions(new AmqpRetryOptions().setTryTimeout(Duration.ofSeconds(20)).setMaxRetries(2));
    }

    static ServiceBusMessage message(String runId, boolean session, int index) {
        return new ServiceBusMessage(index == 0 ? "Hello" : "Hello " + index)
            .setMessageId(runId + "-" + (session ? "session-" : "dedup-") + index)
            .setCorrelationId(runId)
            .setSessionId(session ? SESSION : null);
    }

    static Mono<Void> verify(String sendConnection, String listenConnection, String queue, String runId,
        boolean session) {
        return usingClient(Mono.fromSupplier(() -> builder(sendConnection).sender().queueName(queue).buildAsyncClient()),
            sender -> send(sender::sendMessage, runId, session).then(Mono.defer(() -> session
                ? usingClient(Mono.fromSupplier(() -> builder(listenConnection).sessionReceiver().queueName(queue)
                    .receiveMode(ServiceBusReceiveMode.PEEK_LOCK).disableAutoComplete().prefetchCount(0)
                    .maxAutoLockRenewDuration(Duration.ofMinutes(2)).buildAsyncClient()),
                    sessions -> usingClient(sessions.acceptSession(SESSION),
                        receiver -> receive(receiver, runId, true), ServiceBusReceiverAsyncClient::close),
                    ServiceBusSessionReceiverAsyncClient::close)
                : usingClient(Mono.fromSupplier(() -> builder(listenConnection).receiver().queueName(queue)
                    .receiveMode(ServiceBusReceiveMode.PEEK_LOCK).disableAutoComplete().prefetchCount(0)
                    .buildAsyncClient()), receiver -> receive(receiver, runId, false),
                    ServiceBusReceiverAsyncClient::close))),
            ServiceBusSenderAsyncClient::close);
    }

    static <T> Mono<Void> usingClient(Mono<T> resource, Function<T, Mono<Void>> work, Consumer<T> close) {
        Function<T, Mono<Void>> cleanup = client -> Mono.<Void>fromRunnable(() -> close.accept(client))
            .subscribeOn(Schedulers.boundedElastic());
        return Mono.usingWhen(resource, work, cleanup, (client, failure) ->
            cleanup.apply(client).onErrorResume(cleanupFailure -> {
                if (failure != cleanupFailure) {
                    failure.addSuppressed(cleanupFailure);
                }
                return Mono.empty();
            }), client -> cleanup.apply(client).onErrorResume(cleanupFailure -> {
                // Cancellation has no downstream error channel; avoid Reactor's raw exception logging.
                ServiceBusQueueAdvanceFeatures.report(System.err, "Client cleanup after cancellation NOT verified",
                    cleanupFailure);
                return Mono.empty();
            }));
    }

    static Mono<Void> send(Function<ServiceBusMessage, Mono<Void>> sender, String runId, boolean session) {
        // Individual awaited sends make session ordering explicit. Dedup uses the same MessageId twice.
        return Mono.defer(() -> sender.apply(message(runId, session, 0)))
            .then(Mono.defer(() -> sender.apply(message(runId, session, session ? 1 : 0))));
    }

    private static Mono<Void> receive(ServiceBusReceiverAsyncClient receiver, String runId, boolean session) {
        return receiveAndVerify(receiver.receiveMessages(), receiver::complete, runId, session,
            RECEIVE_LIMIT, QUIET_WINDOW);
    }

    static Mono<Void> receiveAndVerify(Flux<ServiceBusReceivedMessage> messages,
        Function<ServiceBusReceivedMessage, Mono<Void>> complete, String runId, boolean session,
        Duration receiveLimit, Duration quietWindow) {
        return Mono.defer(() -> {
            int count = session ? 2 : 1;
            List<ServiceBusReceivedMessage> received = new ArrayList<>(count);
            return messages.concatWith(Flux.error(new IllegalStateException(
                "Receive stream ended before the quiet window completed")))
                .<List<ServiceBusReceivedMessage>>handle((message, sink) -> {
                    if (received.size() == count) {
                        sink.error(new IllegalStateException("Unexpected message or duplicate received"));
                    } else {
                        received.add(message);
                        if (received.size() == count) {
                            sink.next(List.copyOf(received));
                        }
                    }
                })
                .timeout(Mono.delay(receiveLimit), batch -> Mono.never())
                // Keep demand for an extra message while settlement and the quiet timer are active.
                .flatMap(batch -> receiveAndSettle(Flux.fromIterable(batch), complete, runId, session, count)
                    .then(Mono.delay(quietWindow)), 2)
                .next().then();
        });
    }

    static Mono<Void> receiveAndSettle(Flux<ServiceBusReceivedMessage> messages,
        Function<ServiceBusReceivedMessage, Mono<Void>> complete, String runId, boolean session, int count) {
        return messages.take(count).collectList().timeout(RECEIVE_LIMIT).flatMap(received -> {
            validate(received, runId, session, count);
            return Flux.fromIterable(received).concatMap(complete).then();
        });
    }

    static void validate(List<ServiceBusReceivedMessage> received, String runId, boolean session, int count) {
        if (received.size() != count) {
            throw new IllegalStateException("Required messages not received");
        }
        long previous = -1;
        for (int i = 0; i < count; i++) {
            ServiceBusReceivedMessage actual = received.get(i);
            ServiceBusMessage expected = message(runId, session, i);
            if (!expected.getMessageId().equals(actual.getMessageId())
                || !runId.equals(actual.getCorrelationId())
                || !expected.getBody().toString().equals(actual.getBody().toString())
                || (session ? !SESSION.equals(actual.getSessionId()) : actual.getSessionId() != null)
                || actual.getSequenceNumber() <= previous) {
                throw new IllegalStateException("Message identity, session, body or ordering mismatch");
            }
            previous = actual.getSequenceNumber();
        }
    }

    static Mono<Void> assertNoExtra(Flux<ServiceBusReceivedMessage> messages, Duration window) {
        return messages.take(window).next().flatMap(message ->
            Mono.<Void>error(new IllegalStateException("Unexpected message or duplicate received")));
    }
}
