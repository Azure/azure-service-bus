// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import com.azure.core.util.BinaryData;
import com.azure.core.amqp.models.AmqpMessageId;
import com.azure.messaging.servicebus.ServiceBusMessage;
import com.azure.messaging.servicebus.ServiceBusReceivedMessage;
import java.io.ByteArrayOutputStream;
import java.io.PrintStream;
import java.lang.reflect.Constructor;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicIntegerArray;
import java.util.concurrent.atomic.AtomicLong;
import java.util.concurrent.atomic.AtomicReference;
import org.junit.Test;
import reactor.core.publisher.Flux;
import reactor.core.publisher.Mono;
import reactor.core.Disposable;
import reactor.core.scheduler.Schedulers;
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

    @Test(timeout = 15000)
    public void clientCleanupUsesBlockingThreadAndPreservesDistinctBodyFailure() {
        for (boolean failBody : new boolean[] {false, true}) {
            for (boolean failCleanup : new boolean[] {false, true}) {
                AtomicInteger closed = new AtomicInteger();
                AtomicBoolean terminalNonBlocking = new AtomicBoolean();
                AtomicBoolean cleanupNonBlocking = new AtomicBoolean(true);
                IllegalArgumentException bodyFailure = new IllegalArgumentException("body");
                IllegalStateException cleanupFailure = new IllegalStateException("cleanup");
                Mono<Void> body = QueueMessages.assertNoExtra(Flux.never(), Duration.ofMillis(20))
                    .then(failBody ? Mono.<Void>error(bodyFailure) : Mono.<Void>empty())
                    .doOnEach(signal -> {
                        if (signal.isOnComplete() || signal.isOnError()) {
                            terminalNonBlocking.set(Schedulers.isInNonBlockingThread());
                        }
                    });
                Mono<Void> operation = QueueMessages.usingClient(Mono.just(new Object()), ignored -> body, ignored -> {
                    closed.incrementAndGet();
                    cleanupNonBlocking.set(Schedulers.isInNonBlockingThread());
                    Mono.delay(Duration.ofMillis(5)).block(Duration.ofSeconds(1));
                    if (failCleanup) {
                        throw cleanupFailure;
                    }
                });
                if (failBody) {
                    RuntimeException actual = assertThrows(RuntimeException.class,
                        () -> operation.block(Duration.ofSeconds(2)));
                    assertSame(bodyFailure, actual);
                    assertEquals(failCleanup, List.of(actual.getSuppressed()).contains(cleanupFailure));
                } else if (failCleanup) {
                    assertSame(cleanupFailure, assertThrows(RuntimeException.class,
                        () -> operation.block(Duration.ofSeconds(2))).getCause());
                } else {
                    operation.block(Duration.ofSeconds(2));
                }
                assertTrue(terminalNonBlocking.get());
                assertFalse(cleanupNonBlocking.get());
                assertEquals(1, closed.get());
            }
        }
    }

    @Test(timeout = 15000)
    public void cancellationClosesOnceOffTimerAndReportsCleanupFailureWithoutSecrets() throws Exception {
        for (boolean failCleanup : new boolean[] {false, true}) {
            AtomicInteger closed = new AtomicInteger();
            AtomicBoolean cancellationNonBlocking = new AtomicBoolean();
            AtomicBoolean cleanupNonBlocking = new AtomicBoolean(true);
            CountDownLatch closeReturned = new CountDownLatch(1);
            CountDownLatch reported = new CountDownLatch(1);
            ByteArrayOutputStream bytes = new ByteArrayOutputStream();
            PrintStream previous = System.err;
            PrintStream errors = new PrintStream(bytes, true, StandardCharsets.UTF_8) {
                @Override
                public void println(String text) {
                    super.println(text);
                    reported.countDown();
                }
            };
            System.setErr(errors);
            Disposable subscription = null;
            try {
                subscription = QueueMessages.usingClient(Mono.just(new Object()), ignored -> Mono.never(), ignored -> {
                    closed.incrementAndGet();
                    cleanupNonBlocking.set(Schedulers.isInNonBlockingThread());
                    try {
                        Mono.delay(Duration.ofMillis(5)).block(Duration.ofSeconds(1));
                        if (failCleanup) {
                            throw new IllegalStateException("SharedAccessKey=NEVER_PRINT_SECRET");
                        }
                    } finally {
                        closeReturned.countDown();
                    }
                }).subscribe();
                Disposable active = subscription;
                Mono.delay(Duration.ofMillis(20)).doOnNext(ignored -> {
                    cancellationNonBlocking.set(Schedulers.isInNonBlockingThread());
                    active.dispose();
                    active.dispose();
                }).block(Duration.ofSeconds(2));
                assertTrue(closeReturned.await(2, TimeUnit.SECONDS));
                assertTrue(cancellationNonBlocking.get());
                assertFalse(cleanupNonBlocking.get());
                assertEquals(1, closed.get());
                if (failCleanup) {
                    assertTrue(reported.await(2, TimeUnit.SECONDS));
                    String text = bytes.toString(StandardCharsets.UTF_8);
                    assertTrue(text.contains("Client cleanup after cancellation NOT verified"));
                    assertTrue(text.contains("IllegalStateException"));
                    assertFalse(text.contains("NEVER_PRINT_SECRET"));
                    assertFalse(text.contains("Async resource cleanup failed"));
                } else {
                    assertEquals("", bytes.toString(StandardCharsets.UTF_8));
                }
            } finally {
                if (subscription != null) {
                    subscription.dispose();
                }
                System.setErr(previous);
                errors.close();
            }
        }
    }

    @Test
    public void acquisitionFailureDoesNotCloseAnUnacquiredClient() {
        AtomicInteger closed = new AtomicInteger();
        IllegalArgumentException failure = new IllegalArgumentException("acquisition");
        assertSame(failure, assertThrows(IllegalArgumentException.class, () ->
            QueueMessages.usingClient(Mono.error(failure), ignored -> {
                throw new AssertionError("Work must not run");
            }, ignored -> closed.incrementAndGet()).block()));
        assertEquals(0, closed.get());
    }

    @Test(timeout = 10000)
    public void bothVariantsKeepOneSubscriptionUntilSettlementAndFullQuietWindow() throws Exception {
        for (boolean session : new boolean[] {false, true}) {
            List<ServiceBusReceivedMessage> expected = expectedMessages(session);
            AtomicInteger subscriptions = new AtomicInteger();
            AtomicInteger cancellations = new AtomicInteger();
            CountDownLatch receiveReleased = new CountDownLatch(1);
            AtomicInteger completed = new AtomicInteger();
            AtomicLong lastCompletion = new AtomicLong();
            Duration quiet = Duration.ofMillis(30);
            Flux<ServiceBusReceivedMessage> messages = Flux.defer(() -> {
                assertEquals(1, subscriptions.incrementAndGet());
                return Flux.fromIterable(expected).concatWith(Flux.never());
            }).doOnCancel(() -> {
                cancellations.incrementAndGet();
                receiveReleased.countDown();
            });
            QueueMessages.receiveAndVerify(messages, message -> Mono.delay(Duration.ofMillis(5)).doOnNext(ignored -> {
                assertEquals(0, cancellations.get());
                assertSame(expected.get(completed.getAndIncrement()), message);
                lastCompletion.set(System.nanoTime());
            }).then(), RUN, session, Duration.ofSeconds(1), quiet).block(Duration.ofSeconds(2));
            assertEquals(expected.size(), completed.get());
            assertEquals(1, subscriptions.get());
            // FlatMap can drain upstream cancellation after the terminal signal wakes block().
            assertTrue(receiveReleased.await(2, TimeUnit.SECONDS));
            assertEquals(1, cancellations.get());
            assertTrue(System.nanoTime() - lastCompletion.get() >= quiet.toNanos());
        }
    }

    @Test(timeout = 10000)
    public void extrasAreDetectedDuringSettlementAndDuringQuietWindowForBothVariants() {
        for (boolean session : new boolean[] {false, true}) {
            for (boolean whileSettling : new boolean[] {false, true}) {
                List<ServiceBusReceivedMessage> expected = expectedMessages(session);
                AtomicInteger completions = new AtomicInteger();
                AtomicBoolean extraDelivered = new AtomicBoolean();
                Flux<ServiceBusReceivedMessage> messages = Flux.fromIterable(expected)
                    .concatWith(Mono.delay(Duration.ofMillis(20)).map(ignored -> {
                        extraDelivered.set(true);
                        return expected.get(0);
                    })).concatWith(Flux.never());
                assertThrows(IllegalStateException.class, () -> QueueMessages.receiveAndVerify(messages,
                    message -> (whileSettling ? Mono.delay(Duration.ofMillis(200)).then() : Mono.<Void>empty())
                        .doOnSuccess(ignored -> completions.incrementAndGet()),
                    RUN, session, Duration.ofSeconds(1), Duration.ofMillis(100)).block(Duration.ofSeconds(2)));
                assertTrue(extraDelivered.get());
                assertEquals(whileSettling ? 0 : expected.size(), completions.get());
            }
        }
    }

    @Test(timeout = 10000)
    public void streamAndSettlementErrorsStayDistinctAndCancelTheOnlySubscription() {
        for (boolean session : new boolean[] {false, true}) {
            List<ServiceBusReceivedMessage> expected = expectedMessages(session);
            IllegalArgumentException transport = new IllegalArgumentException("transport");
            Flux<ServiceBusReceivedMessage> brokenStream = Flux.fromIterable(expected)
                .concatWith(Mono.delay(Duration.ofMillis(20)).then(Mono.error(transport)));
            assertSame(transport, assertThrows(IllegalArgumentException.class, () ->
                QueueMessages.receiveAndVerify(brokenStream, message -> Mono.empty(),
                    RUN, session, Duration.ofSeconds(1), Duration.ofMillis(100)).block(Duration.ofSeconds(2))));
            AtomicInteger cancelled = new AtomicInteger();
            AtomicInteger attempts = new AtomicInteger();
            IllegalStateException settlement = new IllegalStateException("settlement");
            Flux<ServiceBusReceivedMessage> messages = Flux.fromIterable(expected).concatWith(Flux.never())
                .doOnCancel(cancelled::incrementAndGet);
            assertSame(settlement, assertThrows(IllegalStateException.class, () ->
                QueueMessages.receiveAndVerify(messages, message -> {
                    assertEquals(0, cancelled.get());
                    attempts.incrementAndGet();
                    return Mono.error(settlement);
                }, RUN, session, Duration.ofSeconds(1), Duration.ofMillis(20)).block(Duration.ofSeconds(2))));
            assertEquals(1, attempts.get());
            assertEquals(1, cancelled.get());
        }
    }

    @Test(timeout = 10000)
    public void missingMessagesAndEarlyStreamCompletionCannotQualifyAQuietWindow() {
        for (boolean session : new boolean[] {false, true}) {
            for (Flux<ServiceBusReceivedMessage> stream : List.of(Flux.<ServiceBusReceivedMessage>empty(),
                Flux.fromIterable(expectedMessages(session)))) {
                assertThrows(IllegalStateException.class, () -> QueueMessages.receiveAndVerify(stream,
                    message -> Mono.empty(), RUN, session, Duration.ofSeconds(1), Duration.ofMillis(20))
                    .block(Duration.ofSeconds(2)));
            }
            RuntimeException timeout = assertThrows(RuntimeException.class, () -> QueueMessages.receiveAndVerify(
                Flux.never(), message -> Mono.empty(), RUN, session, Duration.ofMillis(20), Duration.ofMillis(20))
                .block(Duration.ofSeconds(2)));
            assertTrue(timeout.getCause() instanceof TimeoutException);
        }
    }

    @Test
    public void continuousReceiveStillValidatesTheWholeBatchBeforeSettlement() {
        for (boolean session : new boolean[] {false, true}) {
            List<ServiceBusReceivedMessage> expected = expectedMessages(session);
            expected.get(expected.size() - 1).getRawAmqpMessage().getProperties()
                .setCorrelationId(new AmqpMessageId("foreign-run"));
            AtomicInteger completed = new AtomicInteger();
            assertThrows(IllegalStateException.class, () -> QueueMessages.receiveAndVerify(
                Flux.fromIterable(expected).concatWith(Flux.never()),
                message -> Mono.fromRunnable(completed::incrementAndGet), RUN, session,
                Duration.ofSeconds(1), Duration.ofMillis(20)).block(Duration.ofSeconds(2)));
            assertEquals(0, completed.get());
        }
    }

    @Test(timeout = 15000)
    public void cancellationDuringReceiveOrQuietClosesEveryNestedClientOnce() throws Exception {
        for (boolean session : new boolean[] {false, true}) {
            for (boolean duringQuiet : new boolean[] {false, true}) {
                int clients = session ? 3 : 2;
                AtomicIntegerArray closeCounts = new AtomicIntegerArray(clients);
                AtomicInteger receiveCancellations = new AtomicInteger();
                CountDownLatch receiveReleased = new CountDownLatch(1);
                AtomicBoolean badCleanupThread = new AtomicBoolean();
                AtomicReference<Throwable> failure = new AtomicReference<>();
                CountDownLatch closed = new CountDownLatch(clients);
                CountDownLatch ready = new CountDownLatch(duringQuiet ? expectedMessages(session).size() : 1);
                java.util.function.Consumer<Integer> close = client -> {
                    if (Schedulers.isInNonBlockingThread()) {
                        badCleanupThread.set(true);
                    }
                    closeCounts.incrementAndGet(client);
                    closed.countDown();
                };
                Flux<ServiceBusReceivedMessage> messages = (duringQuiet
                    ? Flux.fromIterable(expectedMessages(session)).concatWith(Flux.never())
                    : Flux.<ServiceBusReceivedMessage>never()).doOnSubscribe(ignored -> {
                        if (!duringQuiet) {
                            ready.countDown();
                        }
                    }).doOnCancel(() -> {
                        receiveCancellations.incrementAndGet();
                        receiveReleased.countDown();
                    });
                Mono<Void> receiver = QueueMessages.usingClient(Mono.just(0), ignored ->
                    QueueMessages.receiveAndVerify(messages, message -> Mono.fromRunnable(ready::countDown),
                        RUN, session, Duration.ofSeconds(3), Duration.ofSeconds(10)), close);
                Mono<Void> parent = session
                    ? QueueMessages.usingClient(Mono.just(1), ignored -> receiver, close) : receiver;
                Mono<Void> sender = QueueMessages.usingClient(Mono.just(clients - 1), ignored -> parent, close);
                Disposable subscription = sender.subscribe(ignored -> { }, failure::set);
                try {
                    assertTrue(ready.await(2, TimeUnit.SECONDS));
                    Mono.delay(Duration.ofMillis(20)).doOnNext(ignored -> subscription.dispose())
                        .block(Duration.ofSeconds(2));
                    assertTrue(closed.await(2, TimeUnit.SECONDS));
                    assertTrue(receiveReleased.await(2, TimeUnit.SECONDS));
                    assertNull(failure.get());
                    assertFalse(badCleanupThread.get());
                    assertEquals(1, receiveCancellations.get());
                    for (int i = 0; i < clients; i++) {
                        assertEquals(1, closeCounts.get(i));
                    }
                } finally {
                    subscription.dispose();
                }
            }
        }
    }

    private static List<ServiceBusReceivedMessage> expectedMessages(boolean session) {
        return session ? List.of(received(true, 0, 10), received(true, 1, 11)) : List.of(received(false, 0, 10));
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
