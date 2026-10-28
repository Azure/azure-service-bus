// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import com.azure.core.amqp.models.AmqpMessageId;
import com.azure.core.util.BinaryData;
import com.azure.messaging.servicebus.ServiceBusReceivedMessage;
import java.io.ByteArrayOutputStream;
import java.io.PrintStream;
import java.lang.reflect.Constructor;
import java.nio.charset.StandardCharsets;
import java.nio.file.FileAlreadyExistsException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.stream.Stream;
import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;
import reactor.core.publisher.Flux;
import reactor.core.publisher.Mono;

import static org.junit.Assert.*;

public class CompositionRegressionTest {
    private static final String NAMESPACE = "/subscriptions/11111111-2222-3333-4444-555555555555/"
        + "resourceGroups/owned/providers/Microsoft.ServiceBus/namespaces/owned";
    @Rule public TemporaryFolder temporary = new TemporaryFolder();

    @Test
    public void publishedRequestIsCompleteAndNeverReplacesAnotherFile() throws Exception {
        Path request = temporary.getRoot().toPath().resolve("access-request.txt");
        byte[] expected = (NAMESPACE + "\n").getBytes(StandardCharsets.UTF_8);
        AccessCheckpoint.publishRequest(request, expected);
        assertArrayEquals(expected, Files.readAllBytes(request));
        assertThrows(FileAlreadyExistsException.class,
            () -> AccessCheckpoint.publishRequest(request, "foreign".getBytes(StandardCharsets.UTF_8)));
        assertArrayEquals(expected, Files.readAllBytes(request));
        try (Stream<Path> files = Files.list(request.getParent())) {
            assertEquals(1, files.count());
        }
    }

    @Test
    public void bothHandshakesObserveCompletePublishedRequests() throws Exception {
        Path directory = temporary.getRoot().toPath().resolve("checkpoint");
        ByteArrayOutputStream output = new ByteArrayOutputStream();
        AccessCheckpoint checkpoint = new AccessCheckpoint(directory, new PrintStream(output),
            Duration.ofSeconds(2), Duration.ofMillis(5));
        ExecutorService executor = Executors.newSingleThreadExecutor();
        try {
            Future<?> otherSide = executor.submit(() -> {
                try {
                    for (String[] exchange : new String[][] {
                        {"access-request.txt", "access-ready.txt"}, {"cleanup-request.txt", "roles-removed.txt"}}) {
                        long stop = System.nanoTime() + TimeUnit.SECONDS.toNanos(3);
                        Path request = directory.resolve(exchange[0]);
                        while (!Files.exists(request) && System.nanoTime() < stop) {
                            Thread.sleep(1);
                        }
                        assertEquals(NAMESPACE + "\n", Files.readString(request));
                        AccessCheckpoint.publishRequest(directory.resolve(exchange[1]), Files.readAllBytes(request));
                    }
                } catch (Exception failure) {
                    throw new AssertionError(failure);
                }
            });
            checkpoint.awaitAccess(NAMESPACE);
            checkpoint.beforeCleanup();
            checkpoint.beforeCleanup();
            otherSide.get(4, TimeUnit.SECONDS);
            assertTrue(output.toString().contains("acknowledged: roles-removed.txt"));
        } finally {
            executor.shutdownNow();
            assertTrue(executor.awaitTermination(2, TimeUnit.SECONDS));
        }
    }

    @Test
    public void failedCleanupAcknowledgmentStillCannotBeRetried() throws Exception {
        Path directory = temporary.getRoot().toPath().resolve("checkpoint");
        AccessCheckpoint checkpoint = new AccessCheckpoint(directory, new PrintStream(new ByteArrayOutputStream()),
            Duration.ofMillis(50), Duration.ofMillis(5));
        AccessCheckpoint.publishRequest(directory.resolve("access-ready.txt"),
            (NAMESPACE + "\n").getBytes(StandardCharsets.UTF_8));
        checkpoint.awaitAccess(NAMESPACE);
        assertThrows(IllegalStateException.class, checkpoint::beforeCleanup);
        AccessCheckpoint.publishRequest(directory.resolve("roles-removed.txt"),
            (NAMESPACE + "\n").getBytes(StandardCharsets.UTF_8));
        assertThrows(IllegalStateException.class, checkpoint::beforeCleanup);
    }

    @Test
    public void receiveLinkStaysOpenUntilSettlementAndQuietWindowComplete() throws Exception {
        ServiceBusReceivedMessage message = message();
        AtomicBoolean cancelled = new AtomicBoolean();
        AtomicBoolean settled = new AtomicBoolean();
        AtomicInteger subscriptions = new AtomicInteger();
        long[] settledAt = {0};
        AzureOperations.verifyDelivery(() -> {
            subscriptions.incrementAndGet();
            return Flux.concat(Flux.just(message), Flux.never()).doOnCancel(() -> cancelled.set(true));
        }, received -> Mono.delay(Duration.ofMillis(10)).then(Mono.fromRunnable(() -> {
            assertFalse(cancelled.get());
            settledAt[0] = System.nanoTime();
            settled.set(true);
        })), "id", Duration.ofSeconds(2), Duration.ofMillis(30));
        assertTrue(settled.get());
        assertTrue(cancelled.get());
        assertEquals(1, subscriptions.get());
        assertTrue(System.nanoTime() - settledAt[0] >= TimeUnit.MILLISECONDS.toNanos(30));
    }

    @Test
    public void delayedDuplicateDuringQuietWindowFailsWithoutSecondSettlement() throws Exception {
        ServiceBusReceivedMessage message = message();
        AtomicInteger completions = new AtomicInteger();
        AtomicBoolean cancelled = new AtomicBoolean();
        assertThrows(IllegalStateException.class, () -> AzureOperations.verifyDelivery(
            () -> Flux.concat(Flux.just(message), Mono.delay(Duration.ofMillis(20)).map(ignored -> message),
                Flux.never()).doOnCancel(() -> cancelled.set(true)),
            received -> { completions.incrementAndGet(); return Mono.empty(); },
            "id", Duration.ofSeconds(2), Duration.ofMillis(100)));
        assertEquals(1, completions.get());
        assertTrue(cancelled.get());
    }

    @Test
    public void duplicateBufferedDuringSettlementAlsoFails() throws Exception {
        ServiceBusReceivedMessage message = message();
        AtomicInteger completions = new AtomicInteger();
        assertThrows(IllegalStateException.class, () -> AzureOperations.verifyDelivery(
            () -> Flux.just(message, message),
            received -> { completions.incrementAndGet(); return Mono.delay(Duration.ofMillis(20)).then(); },
            "id", Duration.ofSeconds(2), Duration.ofMillis(100)));
        assertEquals(1, completions.get());
    }

    @Test
    public void emptyReceiveAndStalledSettlementCannotSucceed() throws Exception {
        ServiceBusReceivedMessage message = message();
        for (boolean empty : new boolean[] {true, false}) {
            assertThrows(IllegalStateException.class, () -> AzureOperations.verifyDelivery(
                () -> empty ? Flux.empty() : Flux.just(message), received -> Mono.never(),
                "id", Duration.ofMillis(50), Duration.ofMillis(5)));
        }
    }

    @Test
    public void earlyReceiveCompletionCannotSubstituteForTheQuietWindow() throws Exception {
        ServiceBusReceivedMessage message = message();
        IllegalStateException failure = assertThrows(IllegalStateException.class,
            () -> AzureOperations.verifyDelivery(() -> Flux.just(message), received -> Mono.empty(),
                "id", Duration.ofSeconds(1), Duration.ofMillis(100)));
        assertEquals("Receive ended before the quiet window completed", failure.getMessage());
    }

    @Test
    public void loggerAndCheckpointConfigurationMatchesTheBoundProduct() {
        Map<String, String> environment = new HashMap<>();
        environment.put("AZURE_TENANT_ID", "11111111-2222-3333-4444-555555555555");
        environment.put("AZURE_SUBSCRIPTION_ID", "11111111-2222-3333-4444-555555555555");
        environment.put("AZURE_LOG_LEVEL", "5");
        Path directory = temporary.getRoot().toPath().resolve("fresh");
        assertEquals(directory, ServiceBusPublishSubscribeBasic.Configuration.parse(
            new String[] {"--run", "--access-checkpoint=" + directory}, environment).accessCheckpoint);
        assertThrows(IllegalArgumentException.class, () -> ServiceBusPublishSubscribeBasic.Configuration.parse(
            new String[] {"--run", "--access-checkpoint=relative"}, environment));
        environment.put("AZURE_LOG_LEVEL", "none");
        assertThrows(IllegalArgumentException.class, () -> ServiceBusPublishSubscribeBasic.Configuration.parse(
            new String[] {"--run"}, environment));
    }

    private static ServiceBusReceivedMessage message() throws Exception {
        Constructor<ServiceBusReceivedMessage> constructor
            = ServiceBusReceivedMessage.class.getDeclaredConstructor(BinaryData.class);
        constructor.setAccessible(true);
        ServiceBusReceivedMessage message = constructor.newInstance(BinaryData.fromString("Hello World"));
        message.getRawAmqpMessage().getProperties().setMessageId(new AmqpMessageId("id"));
        return message;
    }
}
