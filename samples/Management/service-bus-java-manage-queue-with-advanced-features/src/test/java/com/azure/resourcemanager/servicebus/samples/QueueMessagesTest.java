// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import com.azure.core.util.BinaryData;
import com.azure.core.amqp.models.AmqpMessageId;
import com.azure.messaging.servicebus.ServiceBusMessage;
import com.azure.messaging.servicebus.ServiceBusReceivedMessage;
import java.lang.reflect.Constructor;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.Test;
import reactor.core.publisher.Flux;
import reactor.core.publisher.Mono;
import static org.junit.Assert.*;

public class QueueMessagesTest {
    private static final String RUN = "offline-run";

    @Test
    public void sessionsRetainHelloAndSessionIdAndSendInOrder() {
        List<ServiceBusMessage> sent = new ArrayList<>();
        QueueMessages.send(message -> Mono.fromRunnable(() -> sent.add(message)), RUN, true).block();
        assertEquals(2, sent.size());
        assertEquals("Hello", sent.get(0).getBody().toString());
        assertEquals("23424", sent.get(0).getSessionId());
        assertEquals(RUN, sent.get(0).getCorrelationId());
        assertEquals(RUN + "-session-1", sent.get(1).getMessageId());
    }

    @Test
    public void duplicateSendsUseExactlyTheSameMessageIdWithoutASession() {
        List<ServiceBusMessage> sent = new ArrayList<>();
        QueueMessages.send(message -> Mono.fromRunnable(() -> sent.add(message)), RUN, false).block();
        assertEquals(2, sent.size());
        assertEquals(sent.get(0).getMessageId(), sent.get(1).getMessageId());
        assertNull(sent.get(0).getSessionId());
    }

    @Test
    public void firstSendFailurePreventsLaterSends() {
        AtomicInteger calls = new AtomicInteger();
        assertThrows(IllegalStateException.class, () -> QueueMessages.send(message -> {
            calls.incrementAndGet();
            return Mono.error(new IllegalStateException("injected"));
        }, RUN, true).block());
        assertEquals(1, calls.get());
    }

    @Test
    public void sessionMessagesAreValidatedBeforeExplicitSequentialCompletion() {
        List<ServiceBusReceivedMessage> expected = List.of(received(true, 0, 10), received(true, 1, 11));
        List<ServiceBusReceivedMessage> completed = new ArrayList<>();
        QueueMessages.receiveAndSettle(Flux.fromIterable(expected),
            message -> Mono.fromRunnable(() -> completed.add(message)), RUN, true, 2).block();
        assertEquals(expected, completed);
    }

    @Test
    public void mismatchedCorrelationIsNotSettled() {
        ServiceBusReceivedMessage bad = received(false, 0, 1);
        bad.getRawAmqpMessage().getProperties().setCorrelationId(new AmqpMessageId("foreign-run"));
        AtomicInteger settled = new AtomicInteger();
        assertThrows(IllegalStateException.class, () -> QueueMessages.receiveAndSettle(Flux.just(bad),
            message -> Mono.fromRunnable(settled::incrementAndGet), RUN, false, 1).block());
        assertEquals(0, settled.get());
    }

    @Test
    public void wrongSessionBodyMessageIdAndOrderAreRejected() {
        ServiceBusReceivedMessage wrongSession = received(true, 0, 1);
        wrongSession.getRawAmqpMessage().getProperties().setGroupId("wrong");
        assertThrows(IllegalStateException.class, () -> QueueMessages.validate(List.of(wrongSession), RUN, true, 1));
        assertThrows(IllegalStateException.class, () ->
            QueueMessages.validate(List.of(received(true, 1, 1), received(true, 0, 2)), RUN, true, 2));
        assertThrows(IllegalStateException.class, () ->
            QueueMessages.validate(List.of(received(true, 0, 9), received(true, 1, 8)), RUN, true, 2));
        ServiceBusReceivedMessage wrongBody = received(false, 0, 1, "different");
        assertThrows(IllegalStateException.class, () -> QueueMessages.validate(List.of(wrongBody), RUN, false, 1));
    }

    @Test
    public void incompleteReceiveIsNotSuccess() {
        assertThrows(IllegalStateException.class, () -> QueueMessages.receiveAndSettle(
            Flux.just(received(true, 0, 1)), message -> Mono.empty(), RUN, true, 2).block());
    }

    @Test
    public void failedSettlementPropagates() {
        assertThrows(IllegalStateException.class, () -> QueueMessages.receiveAndSettle(
            Flux.just(received(false, 0, 1)), message -> Mono.error(new IllegalStateException("settlement")),
            RUN, false, 1).block());
    }

    @Test
    public void quietWindowIsBoundedAndDuplicateOrTransportFailureIsNotSilenced() {
        QueueMessages.assertNoExtra(Flux.never(), Duration.ofMillis(20)).block(Duration.ofSeconds(1));
        assertThrows(IllegalStateException.class, () -> QueueMessages.assertNoExtra(
            Flux.just(received(false, 0, 1)), Duration.ofMillis(20)).block());
        assertThrows(IllegalStateException.class, () -> QueueMessages.assertNoExtra(
            Flux.error(new IllegalStateException("transport")), Duration.ofMillis(20)).block());
    }

    private static ServiceBusReceivedMessage received(boolean session, int index, long sequence) {
        return received(session, index, sequence, index == 0 ? "Hello" : "Hello " + index);
    }

    private static ServiceBusReceivedMessage received(boolean session, int index, long sequence, String body) {
        try {
            // The SDK intentionally has no public received-message constructor.
            Constructor<ServiceBusReceivedMessage> constructor =
                ServiceBusReceivedMessage.class.getDeclaredConstructor(BinaryData.class);
            constructor.setAccessible(true);
            ServiceBusReceivedMessage result = constructor.newInstance(BinaryData.fromString(body));
            ServiceBusMessage expected = QueueMessages.message(RUN, session, index);
            result.getRawAmqpMessage().getProperties().setMessageId(new AmqpMessageId(expected.getMessageId()));
            result.getRawAmqpMessage().getProperties().setCorrelationId(new AmqpMessageId(RUN));
            result.getRawAmqpMessage().getProperties().setGroupId(expected.getSessionId());
            result.getRawAmqpMessage().getMessageAnnotations().put("x-opt-sequence-number", sequence);
            return result;
        } catch (ReflectiveOperationException e) {
            throw new AssertionError(e);
        }
    }
}
