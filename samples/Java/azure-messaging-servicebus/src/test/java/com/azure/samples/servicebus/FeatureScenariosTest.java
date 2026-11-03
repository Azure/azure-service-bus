package com.azure.samples.servicebus;

import com.azure.core.credential.TokenCredential;
import com.azure.messaging.servicebus.ServiceBusMessage;
import com.azure.messaging.servicebus.administration.models.MessagingSku;
import org.junit.Test;
import reactor.core.publisher.Mono;
import java.time.Duration;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.atomic.AtomicBoolean;
import static org.junit.Assert.*;

public class FeatureScenariosTest {
    private final TokenCredential noNetwork = request -> Mono.error(new AssertionError("Tests must not authenticate."));

    @Test
    public void requestReplyPreservesCorrelationAndSession() {
        ServiceBusMessage request = FeatureScenarios.request("request-1", "replies", "session-1");
        ServiceBusMessage reply = FeatureScenarios.reply(request.getMessageId(), request.getReplyToSessionId());
        assertEquals("replies", request.getReplyTo());
        assertEquals(request.getMessageId(), reply.getCorrelationId());
        assertEquals(request.getReplyToSessionId(), reply.getSessionId());
        assertTrue(FeatureScenarios.queueOptions(true, false, false, false).isSessionRequired());
    }

    @Test
    public void retryUsesStableBusinessIdAndExplicitWindow() {
        assertEquals(FeatureScenarios.businessMessage("id").getMessageId(), FeatureScenarios.businessMessage("id").getMessageId());
        assertNotEquals(FeatureScenarios.businessMessage("id").getMessageId(), FeatureScenarios.businessMessage("new-id").getMessageId());
        assertTrue(FeatureScenarios.queueOptions(false, true, false, false).isDuplicateDetectionRequired());
        assertEquals(Duration.ofMinutes(10), FeatureScenarios.queueOptions(false, true, false, false).getDuplicateDetectionHistoryTimeWindow());
    }

    @Test
    public void ttlAndDeadLetterSettingsAreExplicit() {
        assertTrue(FeatureScenarios.queueOptions(false, false, false, true).isDeadLetteringOnMessageExpiration());
        assertEquals(Duration.ofSeconds(3), FeatureScenarios.expiringMessage().getTimeToLive());
    }

    @Test
    public void forwardingUsesConfiguredDestination() {
        assertEquals("target", FeatureScenarios.forwardingOptions("target", false).getForwardTo());
        assertFalse(FeatureScenarios.forwardingOptions("target", false).isSessionRequired());
    }

    @Test
    public void partitionSessionAndMessageKeysAgree() {
        ServiceBusMessage message = FeatureScenarios.partitionMessage("key", 1);
        assertEquals(message.getSessionId(), message.getPartitionKey());
        assertEquals("key-1", message.getMessageId());
        assertTrue(FeatureScenarios.queueOptions(true, false, true, false).isPartitioningEnabled());
        assertFalse(FeatureScenarios.queueOptions(true, false, false, false).isPartitioningEnabled());
    }

    @Test
    public void standardPartitioningOnlyAppliesToPartitionScenario() {
        for (String scenario : Arrays.asList("request-reply", "duplicate-detection", "ttl", "autoforward", "partitioning")) {
            boolean partitioned = FeatureScenarios.partitionQueues(MessagingSku.STANDARD, scenario, null);
            assertEquals("partitioning".equals(scenario),
                FeatureScenarios.queueOptions(false, false, partitioned, false).isPartitioningEnabled());
        }
    }

    @Test
    public void premiumQueuesMatchNamespacePartitionCount() {
        for (int count : new int[] {1, 2, 4}) {
            for (String scenario : Arrays.asList("request-reply", "duplicate-detection", "ttl", "autoforward", "partitioning")) {
                boolean partitioned = FeatureScenarios.partitionQueues(MessagingSku.PREMIUM, scenario, count);
                assertEquals(count > 1,
                    FeatureScenarios.queueOptions(false, false, partitioned, false).isPartitioningEnabled());
                assertEquals(count > 1, FeatureScenarios.forwardingOptions("target", partitioned).isPartitioningEnabled());
                assertEquals("target", FeatureScenarios.forwardingOptions("target", partitioned).getForwardTo());
            }
        }
    }

    @Test
    public void premiumPartitionCountHasAnExplicitValidatedFormat() {
        assertNull(FeatureScenarios.parsePremiumPartitions(null));
        for (String count : Arrays.asList("1", "2", "4")) {
            assertEquals(Integer.valueOf(count), FeatureScenarios.parsePremiumPartitions(count));
        }
        for (String invalid : Arrays.asList("", "0", "3", "-1", "two", " 2 ", "2\n", "16")) {
            assertThrows(IllegalArgumentException.class, () -> FeatureScenarios.parsePremiumPartitions(invalid));
        }
    }

    @Test
    public void missingOrMismatchedNamespaceConfigurationIsRejected() {
        assertThrows(IllegalArgumentException.class,
            () -> FeatureScenarios.partitionQueues(MessagingSku.PREMIUM, "partitioning", null));
        for (int count : new int[] {1, 2, 4}) {
            assertThrows(IllegalArgumentException.class,
                () -> FeatureScenarios.partitionQueues(MessagingSku.STANDARD, "partitioning", count));
        }
        assertThrows(IllegalArgumentException.class,
            () -> FeatureScenarios.partitionQueues(MessagingSku.BASIC, "partitioning", null));
    }

    @Test
    public void webSocketsProxyConfigurationBuildsWithoutAuthentication() {
        try (var client = FeatureScenarios.clientBuilder("example.servicebus.windows.net", noNetwork, "127.0.0.1", "8888")
                .sender().queueName("offline-test").buildClient()) {
            assertEquals("offline-test", client.getEntityPath());
        }
    }

    @Test(expected = IllegalArgumentException.class)
    public void rejectsInvalidProxyPort() {
        FeatureScenarios.clientBuilder("example.servicebus.windows.net", noNetwork, "127.0.0.1", "65536");
    }

    @Test(expected = IllegalArgumentException.class)
    public void rejectsProxyPortWithoutHost() {
        FeatureScenarios.clientBuilder("example.servicebus.windows.net", noNetwork, null, "8888");
    }

    @Test
    public void cleanupAttemptsIndependentQueuesAndPreservesPrimaryFailure() {
        List<FeatureScenarios.QueueResource> queues = new ArrayList<>();
        FeatureScenarios.createOwned(queues, new FeatureScenarios.QueueResource("first", null), () -> { });
        FeatureScenarios.createOwned(queues, new FeatureScenarios.QueueResource("second", null), () -> { });
        List<String> attempts = new ArrayList<>();
        RuntimeException primary = new IllegalArgumentException("scenario failure");
        IllegalStateException result = assertThrows(IllegalStateException.class, () ->
            FeatureScenarios.cleanup(queues, (name, remaining) -> {
                attempts.add(name);
                if ("second".equals(name)) throw new IllegalArgumentException("delete failed");
            }, primary, Duration.ofSeconds(1)));
        assertSame(primary, result.getCause());
        assertEquals(1, result.getSuppressed().length);
        assertEquals(Arrays.asList("second", "first"), attempts);
        assertEquals(FeatureScenarios.QueueState.DELETED, queues.get(0).state);
    }

    @Test
    public void unconfirmedForwardingSourceProtectsDestination() {
        List<FeatureScenarios.QueueResource> queues = new ArrayList<>();
        FeatureScenarios.createOwned(queues, new FeatureScenarios.QueueResource("destination", null), () -> { });
        assertThrows(IllegalStateException.class, () ->
            FeatureScenarios.createOwned(queues, new FeatureScenarios.QueueResource("source", "destination"),
                () -> { throw new IllegalStateException("create response lost"); }));
        List<String> deleted = new ArrayList<>();
        assertThrows(IllegalStateException.class, () ->
            FeatureScenarios.cleanup(queues, (name, remaining) -> deleted.add(name), null, Duration.ofSeconds(1)));
        assertTrue(deleted.isEmpty());
        assertEquals("source", queues.get(1).name);
        assertEquals(FeatureScenarios.QueueState.UNCONFIRMED, queues.get(1).state);
    }

    @Test
    public void forwardingSourceIsDeletedBeforeDestination() {
        List<FeatureScenarios.QueueResource> queues = new ArrayList<>();
        FeatureScenarios.createOwned(queues, new FeatureScenarios.QueueResource("destination", null), () -> { });
        FeatureScenarios.createOwned(queues, new FeatureScenarios.QueueResource("source", "destination"), () -> { });
        List<String> deleted = new ArrayList<>();
        FeatureScenarios.cleanup(queues, (name, remaining) -> deleted.add(name), null, Duration.ofSeconds(1));
        assertEquals(Arrays.asList("source", "destination"), deleted);
    }

    @Test
    public void failedSourceDeletionProtectsDestination() {
        List<FeatureScenarios.QueueResource> queues = new ArrayList<>();
        FeatureScenarios.createOwned(queues, new FeatureScenarios.QueueResource("destination", null), () -> { });
        FeatureScenarios.createOwned(queues, new FeatureScenarios.QueueResource("source", "destination"), () -> { });
        List<String> attempted = new ArrayList<>();
        assertThrows(IllegalStateException.class, () ->
            FeatureScenarios.cleanup(queues, (name, remaining) -> {
                attempted.add(name);
                throw new IllegalStateException("delete failed");
            }, null, Duration.ofSeconds(1)));
        assertEquals(Collections.singletonList("source"), attempted);
    }

    @Test
    public void cleanupDeadlineDoesNotAuthorizeUnboundedCalls() {
        List<FeatureScenarios.QueueResource> queues = new ArrayList<>();
        FeatureScenarios.createOwned(queues, new FeatureScenarios.QueueResource("owned", null), () -> { });
        assertThrows(IllegalStateException.class, () ->
            FeatureScenarios.cleanup(queues, (name, remaining) -> fail("No call after deadline"), null, Duration.ZERO));
        assertEquals(FeatureScenarios.QueueState.CREATED, queues.get(0).state);
    }

    @Test
    public void failedReplyIsNotSettledAndWrongCallerIsRejected() {
        AtomicBoolean settled = new AtomicBoolean();
        assertThrows(IllegalStateException.class, () ->
            FeatureScenarios.replyAndComplete("request-a", "session-a",
                message -> { throw new IllegalStateException("send failed"); }, () -> settled.set(true)));
        assertFalse(settled.get());
        assertThrows(IllegalStateException.class, () -> FeatureScenarios.validateReply("a", "a", "b", "a"));
        assertThrows(IllegalStateException.class, () -> FeatureScenarios.validateReply("a", "a", "a", "b"));
    }

    @Test
    public void transactionFailureRollsBackWithoutMaskingSendFailure() {
        List<String> operations = new ArrayList<>();
        RuntimeException sendFailure = new IllegalStateException("send failed");
        RuntimeException result = assertThrows(IllegalStateException.class, () ->
            FeatureScenarios.transact(() -> operations.add("first"), () -> { throw sendFailure; },
                () -> operations.add("commit"), () -> {
                    operations.add("rollback");
                    throw new IllegalArgumentException("rollback failed");
                }));
        assertSame(sendFailure, result);
        assertEquals(Arrays.asList("first", "rollback"), operations);
        assertEquals(1, result.getSuppressed().length);
    }

    @Test
    public void receiveDeadlineRejectsMissingMessage() {
        assertThrows(IllegalStateException.class, () ->
            FeatureScenarios.receiveRequired(Collections::emptyList, Duration.ZERO));
    }
}
