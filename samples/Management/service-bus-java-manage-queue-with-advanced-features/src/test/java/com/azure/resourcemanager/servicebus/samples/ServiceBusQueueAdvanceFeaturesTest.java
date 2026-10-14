// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import java.io.ByteArrayOutputStream;
import java.io.PrintStream;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.atomic.AtomicBoolean;
import org.junit.Test;
import reactor.core.publisher.Mono;
import static org.junit.Assert.*;

public class ServiceBusQueueAdvanceFeaturesTest {
    private final ByteArrayOutputStream output = new ByteArrayOutputStream();
    private final PrintStream stream = new PrintStream(output, true, StandardCharsets.UTF_8);
    private static final Duration LIMIT = Duration.ofMillis(100);
    private static final String SENTINEL = "SharedAccessKey=NEVER_PRINT_SECRET;Endpoint=sb://secret/";

    @Test
    public void noArgumentsRejectsBeforeCreatingAnyClient() {
        assertEquals(2, ServiceBusQueueAdvanceFeatures.execute(new String[0], Map.of(), stream, stream,
            config -> { throw new AssertionError("No clients allowed"); }));
        assertTrue(text().contains("no Azure operation was started"));
    }

    @Test
    public void helpIsOffline() {
        assertEquals(0, ServiceBusQueueAdvanceFeatures.execute(new String[] {"--help"}, Map.of(), stream, stream,
            config -> { throw new AssertionError("No clients allowed"); }));
    }

    @Test
    public void invalidConfigurationDoesNotEchoInputOrInitializeClients() {
        assertEquals(2, ServiceBusQueueAdvanceFeatures.execute(new String[] {"--run"},
            Map.of("AZURE_TENANT_ID", SENTINEL), stream, stream,
            config -> { throw new AssertionError("No clients allowed"); }));
        assertFalse(text().contains(SENTINEL));
    }

    @Test
    public void standardDefaultAndPremiumAreExplicitVariants() {
        Map<String, String> env = environment();
        assertFalse(ServiceBusQueueAdvanceFeatures.Config.parse(new String[] {"--run"}, env).premium);
        assertTrue(ServiceBusQueueAdvanceFeatures.Config.parse(new String[] {"--run", "--sku=premium"}, env).premium);
        assertThrows(IllegalArgumentException.class, () ->
            ServiceBusQueueAdvanceFeatures.Config.parse(new String[] {"--run", "--sku=basic"}, env));
    }

    @Test
    public void numericRegionSuffixesReachOfflineFactoryForBothTiers() {
        for (String location : new String[] {"westus", "eastus2", "westus2", "westus3"}) {
            for (String sku : new String[] {"standard", "premium"}) {
                Map<String, String> env = new HashMap<>(environment());
                env.put("AZURE_LOCATION", location);
                AtomicBoolean initialized = new AtomicBoolean();
                assertEquals(0, ServiceBusQueueAdvanceFeatures.execute(
                    new String[] {"--run", "--sku=" + sku}, env, stream, stream, config -> {
                        initialized.set(true);
                        assertEquals(location, config.location);
                        assertEquals("premium".equals(sku), config.premium);
                        return new ServiceBusQueueAdvanceFeatures.QueueWork() {
                            public Mono<Void> provision() { return Mono.empty(); }
                            public Mono<Void> scenarios() { return Mono.empty(); }
                            public Mono<Void> cleanup() { return Mono.empty(); }
                        };
                    }));
                assertTrue(initialized.get());
            }
        }
    }

    @Test
    public void regionSyntaxRetainsTwoToFortyCharacterBoundaries() {
        for (String location : new String[] {"ab", "a" + "0".repeat(39)}) {
            Map<String, String> env = new HashMap<>(environment());
            env.put("AZURE_LOCATION", location);
            assertEquals(location, ServiceBusQueueAdvanceFeatures.Config.parse(
                new String[] {"--run"}, env).location);
        }
    }

    @Test
    public void malformedLocationsRejectBeforeCreatingClientsOrEchoingInput() {
        for (String location : new String[] {
            "", "a", "a" + "0".repeat(40), "2eastus", "east-us", "Westus2",
            " westus2", "westus2 ", "westus2\n", SENTINEL
        }) {
            Map<String, String> env = new HashMap<>(environment());
            env.put("AZURE_LOCATION", location);
            output.reset();
            assertThrows(IllegalArgumentException.class, () ->
                ServiceBusQueueAdvanceFeatures.Config.parse(new String[] {"--run"}, env));
            assertEquals(2, ServiceBusQueueAdvanceFeatures.execute(new String[] {"--run"},
                env, stream, stream, config -> { throw new AssertionError("No clients allowed"); }));
            assertEquals("Configuration invalid. Use --help; no Azure operation was started."
                + System.lineSeparator(), text());
        }
    }

    @Test
    public void missingLocationRejectsBeforeCreatingAnyClient() {
        Map<String, String> env = new HashMap<>(environment());
        env.remove("AZURE_LOCATION");
        assertThrows(IllegalArgumentException.class, () ->
            ServiceBusQueueAdvanceFeatures.Config.parse(new String[] {"--run"}, env));
        assertEquals(2, ServiceBusQueueAdvanceFeatures.execute(new String[] {"--run"},
            env, stream, stream, config -> { throw new AssertionError("No clients allowed"); }));
        assertTrue(text().contains("no Azure operation was started"));
    }

    @Test
    public void identifiersAreFreshAndCannotBeSuppliedByCaller() {
        ServiceBusQueueAdvanceFeatures.Config a = ServiceBusQueueAdvanceFeatures.Config.parse(
            new String[] {"--run"}, environment());
        ServiceBusQueueAdvanceFeatures.Config b = ServiceBusQueueAdvanceFeatures.Config.parse(
            new String[] {"--run"}, environment());
        assertNotEquals(a.group, b.group);
        assertNotEquals(a.namespace, b.namespace);
        assertTrue(a.namespace.matches("[a-z][a-z0-9-]{5,49}"));
        assertThrows(IllegalArgumentException.class, () -> ServiceBusQueueAdvanceFeatures.Config.parse(
            new String[] {"--run", "--group=existing"}, environment()));
    }

    @Test
    public void successfulRunRequiresScenarioAndCleanupCompletion() {
        AtomicBoolean cleaned = new AtomicBoolean();
        assertEquals(0, run(Mono.empty(), Mono.empty(), Mono.fromRunnable(() -> cleaned.set(true))));
        assertTrue(cleaned.get());
        assertTrue(text().contains("All queue scenarios and cleanup completed"));
    }

    @Test
    public void provisioningFailureReturnsNonzeroAndStillAttemptsCleanup() {
        AtomicBoolean cleaned = new AtomicBoolean();
        assertEquals(1, run(Mono.error(new IllegalStateException(SENTINEL)),
            Mono.error(new AssertionError("Should not run")), Mono.fromRunnable(() -> cleaned.set(true))));
        assertTrue(cleaned.get());
        assertFalse(text().contains(SENTINEL));
        assertFalse(text().contains("All queue scenarios"));
    }

    @Test
    public void messagingAndSettlementFailureCannotBecomeSuccessOrRevealSecrets() {
        assertEquals(1, run(Mono.empty(), Mono.error(new IllegalStateException(SENTINEL)), Mono.empty()));
        assertFalse(text().contains(SENTINEL));
        assertFalse(text().contains("All queue scenarios"));
    }

    @Test
    public void cleanupFailureOverridesScenarioSuccess() {
        assertEquals(1, run(Mono.empty(), Mono.empty(), Mono.error(new IllegalStateException(SENTINEL))));
        assertTrue(text().contains("Cleanup NOT verified"));
        assertTrue(text().contains("retain the printed resource IDs"));
        assertFalse(text().contains("Cleanup verified:"));
        assertFalse(text().contains(SENTINEL));
    }

    @Test
    public void acceptedButPendingDeletionTimesOutAndCancelsWithoutClaimingCompletion() {
        AtomicBoolean cancelled = new AtomicBoolean();
        assertEquals(1, run(Mono.empty(), Mono.empty(), Mono.<Void>never().doOnCancel(() -> cancelled.set(true))));
        assertTrue(cancelled.get());
        assertFalse(text().contains("Cleanup verified:"));
        assertFalse(text().contains("All queue scenarios"));
    }

    @Test
    public void scenarioDeadlineCancelsAndPreservesIndependentCleanupBudget() {
        AtomicBoolean cancelled = new AtomicBoolean();
        AtomicBoolean cleaned = new AtomicBoolean();
        assertEquals(1, run(Mono.empty(), Mono.<Void>never().doOnCancel(() -> cancelled.set(true)),
            Mono.fromRunnable(() -> cleaned.set(true))));
        assertTrue(cancelled.get());
        assertTrue(cleaned.get());
    }

    @Test
    public void synchronousScenarioAndCleanupErrorsAreReported() {
        ServiceBusQueueAdvanceFeatures.QueueWork work = new ServiceBusQueueAdvanceFeatures.QueueWork() {
            public Mono<Void> provision() { throw new IllegalStateException(SENTINEL); }
            public Mono<Void> scenarios() { throw new AssertionError(); }
            public Mono<Void> cleanup() { throw new IllegalStateException(SENTINEL); }
        };
        assertEquals(1, ServiceBusQueueAdvanceFeatures.run(work, stream, stream, LIMIT, LIMIT));
        assertFalse(text().contains(SENTINEL));
        assertTrue(text().contains("Scenario failed"));
        assertTrue(text().contains("Cleanup NOT verified"));
    }

    @Test
    public void diagnosticKeepsSafeStepAndOwnershipReasonButNotSdkCauseText() {
        ServiceBusQueueAdvanceFeatures.report(stream, "failed",
            new AzureQueueWork.StepFailure("namespace-create", new IllegalStateException(SENTINEL)));
        assertTrue(text().contains("namespace-create"));
        assertFalse(text().contains(SENTINEL));
        ServiceBusQueueAdvanceFeatures.report(stream, "failed",
            new AzureQueueWork.SafetyFailure("Ownership mismatch; refusing mutation"));
        assertTrue(text().contains("Ownership mismatch; refusing mutation"));
    }

    private int run(Mono<Void> provision, Mono<Void> scenarios, Mono<Void> cleanup) {
        return ServiceBusQueueAdvanceFeatures.run(new ServiceBusQueueAdvanceFeatures.QueueWork() {
            public Mono<Void> provision() { return provision; }
            public Mono<Void> scenarios() { return scenarios; }
            public Mono<Void> cleanup() { return cleanup; }
        }, stream, stream, LIMIT, LIMIT);
    }

    private String text() {
        return output.toString(StandardCharsets.UTF_8);
    }

    static Map<String, String> environment() {
        return Map.of("AZURE_TENANT_ID", "11111111-1111-1111-1111-111111111111",
            "AZURE_SUBSCRIPTION_ID", "22222222-2222-2222-2222-222222222222", "AZURE_LOCATION", "westus");
    }
}
