// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import com.azure.core.amqp.exception.AmqpErrorCondition;
import com.azure.core.amqp.exception.AmqpErrorContext;
import com.azure.core.amqp.exception.AmqpException;
import com.azure.core.exception.HttpResponseException;
import com.azure.core.http.HttpHeaders;
import com.azure.core.http.HttpMethod;
import com.azure.core.http.HttpRequest;
import com.azure.core.http.HttpResponse;
import com.azure.core.management.exception.ManagementError;
import com.azure.core.management.exception.ManagementException;
import com.azure.messaging.servicebus.ServiceBusErrorSource;
import com.azure.messaging.servicebus.ServiceBusException;
import java.io.ByteArrayOutputStream;
import java.io.PrintStream;
import java.nio.ByteBuffer;
import java.nio.charset.Charset;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.TimeoutException;
import java.util.concurrent.atomic.AtomicBoolean;
import org.junit.Test;
import reactor.core.publisher.Flux;
import reactor.core.publisher.Mono;
import static org.junit.Assert.*;

public class ServiceBusQueueAdvanceFeaturesTest {
    private final ByteArrayOutputStream output = new ByteArrayOutputStream();
    private final PrintStream stream = new PrintStream(output, true, StandardCharsets.UTF_8);
    private static final Duration LIMIT = Duration.ofMillis(100);
    private static final String SENTINEL = "SharedAccessKey=NEVER_PRINT_SECRET;Endpoint=sb://secret/";

    @Test
    public void nestedOperationLabelsShareTheExistingDiagnosticBudget() {
        Throwable detail = new IllegalStateException(SENTINEL);
        for (int i = 0; i < 10; i++) {
            detail = new AzureQueueWork.StepFailure("sessions-authorization-list", detail);
        }
        ServiceBusQueueAdvanceFeatures.report(stream, "failed",
            new AzureQueueWork.StepFailure("sessions-authorization-and-messaging", detail));
        assertEquals(8, text().split("; cause=", -1).length - 1);
        assertEquals(8, text().split("; operation=", -1).length - 1);
        assertTrue(text().contains("; diagnostics-truncated=true"));
        assertFalse(text().contains(SENTINEL));
    }

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
                            final ServiceBusQueueAdvanceFeatures.UpdateResults updates
                                = new ServiceBusQueueAdvanceFeatures.UpdateResults();
                            public Mono<Void> provision() { return Mono.empty(); }
                            public Mono<Void> scenarios() { return Mono.fromRunnable(() -> matchingUpdates(updates)); }
                            public Mono<Void> cleanup() { return Mono.empty(); }
                            public ServiceBusQueueAdvanceFeatures.UpdateResults updates() { return updates; }
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
        assertTrue(text().contains("Lifecycle=PASS; cleanup=PASS"));
        assertTrue(text().contains("Immediate update confirmation: PASS"));
    }

    @Test
    public void provisioningFailureReturnsNonzeroAndStillAttemptsCleanup() {
        AtomicBoolean cleaned = new AtomicBoolean();
        assertEquals(1, run(Mono.error(new IllegalStateException(SENTINEL)),
            Mono.error(new AssertionError("Should not run")), Mono.fromRunnable(() -> cleaned.set(true))));
        assertTrue(cleaned.get());
        assertFalse(text().contains(SENTINEL));
        assertFalse(text().contains("Lifecycle completed:"));
    }

    @Test
    public void messagingAndSettlementFailureCannotBecomeSuccessOrRevealSecrets() {
        assertEquals(1, run(Mono.empty(), Mono.error(new IllegalStateException(SENTINEL)), Mono.empty()));
        assertFalse(text().contains(SENTINEL));
        assertFalse(text().contains("Lifecycle completed:"));
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
        assertFalse(text().contains("Lifecycle completed:"));
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
            public ServiceBusQueueAdvanceFeatures.UpdateResults updates() {
                return new ServiceBusQueueAdvanceFeatures.UpdateResults();
            }
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

    @Test
    public void nestedAmqpDiagnosticsRetainTypedReasonWithoutSecrets() {
        AmqpException amqp = new AmqpException(false, AmqpErrorCondition.UNAUTHORIZED_ACCESS, SENTINEL,
            new IllegalStateException(SENTINEL), new AmqpErrorContext(SENTINEL, Map.of("secret", SENTINEL)));
        RuntimeException error = new RuntimeException(SENTINEL,
            new ServiceBusException(amqp, ServiceBusErrorSource.RECEIVE));
        error.addSuppressed(new IllegalArgumentException(SENTINEL));
        ServiceBusQueueAdvanceFeatures.report(stream, "failed",
            new AzureQueueWork.StepFailure("sessions-authorization-and-messaging", error));
        assertEquals("failed (StepFailure; step=sessions-authorization-and-messaging"
            + "; cause=RuntimeException; cause=ServiceBusException; serviceBusReason=UNAUTHORIZED; transient=false"
            + "; cause=AmqpException; amqpCondition=UNAUTHORIZED_ACCESS; transient=false"
            + "; cause=IllegalStateException; suppressed={; cause=IllegalArgumentException})."
            + System.lineSeparator(), textWithoutFrames());
        assertFalse(text().contains(SENTINEL));
    }

    @Test
    public void nestedHttpDiagnosticsReadOnlyStatus() {
        HttpResponse response = new HttpResponse(new HttpRequest(HttpMethod.GET,
            "https://offline.invalid/?sig=NEVER_PRINT_SECRET")) {
            public int getStatusCode() { return 403; }
            public String getHeaderValue(String name) { throw new AssertionError("No header access"); }
            public HttpHeaders getHeaders() { throw new AssertionError("No header access"); }
            public Flux<ByteBuffer> getBody() { throw new AssertionError("No body access"); }
            public Mono<byte[]> getBodyAsByteArray() { throw new AssertionError("No body access"); }
            public Mono<String> getBodyAsString() { throw new AssertionError("No body access"); }
            public Mono<String> getBodyAsString(Charset charset) { throw new AssertionError("No body access"); }
        };
        ServiceBusQueueAdvanceFeatures.report(stream, "failed",
            new AzureQueueWork.StepFailure("sessions-authorization-and-messaging",
                new RuntimeException(SENTINEL,
                    new ManagementException(SENTINEL, response, new ManagementError(SENTINEL, SENTINEL)))));
        assertEquals("failed (StepFailure; step=sessions-authorization-and-messaging"
            + "; cause=RuntimeException; cause=ManagementException; httpStatus=403)."
            + System.lineSeparator(), textWithoutFrames());
        assertFalse(text().contains("NEVER_PRINT_SECRET"));
    }

    @Test
    public void nestedTimeoutKeepsFailureAndIndependentCleanup() {
        AtomicBoolean cleaned = new AtomicBoolean();
        assertEquals(1, run(Mono.empty(), Mono.error(new AzureQueueWork.StepFailure(
            "sessions-authorization-and-messaging", new RuntimeException(SENTINEL, new TimeoutException(SENTINEL)))),
            Mono.fromRunnable(() -> cleaned.set(true))));
        assertTrue(cleaned.get());
        assertTrue(textWithoutFrames().contains("cause=RuntimeException; cause=TimeoutException"));
        assertTrue(text().contains("Cleanup verified:"));
        assertFalse(text().contains("Lifecycle completed:"));
        assertFalse(text().contains(SENTINEL));
    }

    @Test
    public void missingStructuredFieldsAndCauseDoNotBreakDiagnostics() {
        for (RuntimeException error : new RuntimeException[] {
            new AmqpException(false, SENTINEL, null),
            new HttpResponseException(SENTINEL, null),
            new AzureQueueWork.StepFailure("sessions-authorization-and-messaging", null)
        }) {
            output.reset();
            ServiceBusQueueAdvanceFeatures.report(stream, "failed", error);
            assertFalse(text().contains("amqpCondition="));
            assertFalse(text().contains("httpStatus="));
            assertFalse(text().contains(SENTINEL));
        }
    }

    @Test(timeout = 1000)
    public void causeCyclesAndLongChainsAreExplicitlyBounded() {
        RuntimeException first = new RuntimeException(SENTINEL);
        RuntimeException second = new RuntimeException(SENTINEL, first);
        first.initCause(second);
        RuntimeException deep = new RuntimeException(SENTINEL);
        for (int i = 0; i < 20; i++) {
            deep = new RuntimeException(SENTINEL, deep);
        }
        for (RuntimeException error : new RuntimeException[] {first, deep}) {
            output.reset();
            ServiceBusQueueAdvanceFeatures.report(stream, "failed", error);
            assertEquals(error == first ? 2 : 8, text().split("; cause=", -1).length - 1);
            assertTrue(text().contains("; diagnostics-truncated=true"));
            assertFalse(text().contains(SENTINEL));
        }
    }

    @Test
    public void bareRuntimeFailureDoesNotInventAReason() {
        ServiceBusQueueAdvanceFeatures.report(stream, "failed",
            new AzureQueueWork.StepFailure("sessions-authorization-and-messaging", new RuntimeException(SENTINEL)));
        assertEquals("failed (StepFailure; step=sessions-authorization-and-messaging; cause=RuntimeException)."
            + System.lineSeparator(), textWithoutFrames());
    }

    @Test
    public void framesAndSuppressedOriginalTypesAreUsefulWithoutRawExceptionContent() {
        IllegalStateException cleanup = new IllegalStateException(SENTINEL);
        cleanup.setStackTrace(new StackTraceElement[] {
            new StackTraceElement("customer.SecretData", "customerArgument", SENTINEL, 123),
            new StackTraceElement("reactor.core.publisher.BlockingSingleSubscriber", "blockingGet", SENTINEL, 99)
        });
        RuntimeException wrapper = new RuntimeException(SENTINEL, cleanup);
        wrapper.setStackTrace(new StackTraceElement[] {
            new StackTraceElement("reactor.core.publisher.FluxUsingWhen$RollbackInner", "onError", SENTINEL, 88)
        });
        AmqpException original = new AmqpException(false, AmqpErrorCondition.UNAUTHORIZED_ACCESS, SENTINEL, null);
        wrapper.addSuppressed(original);
        ServiceBusQueueAdvanceFeatures.report(stream, "failed",
            new AzureQueueWork.StepFailure("sessions-authorization-and-messaging", wrapper));
        assertTrue(text().contains("frame=reactor.core.publisher.FluxUsingWhen$RollbackInner.onError:88"));
        assertTrue(text().contains("frame=reactor.core.publisher.BlockingSingleSubscriber.blockingGet:99"));
        assertTrue(text().contains("suppressed={; cause=AmqpException"));
        assertTrue(text().contains("amqpCondition=UNAUTHORIZED_ACCESS"));
        assertFalse(text().contains("customer"));
        assertFalse(text().contains(SENTINEL));
    }

    @Test
    public void frameSelectionIsAllowlistedRedactedAndBoundedToSixteenFrames() {
        RuntimeException error = new RuntimeException(SENTINEL);
        StackTraceElement[] frames = new StackTraceElement[17];
        for (int i = 0; i < 16; i++) {
            frames[i] = new StackTraceElement("customer.SecretData", "customerArgument", SENTINEL, i);
        }
        frames[16] = new StackTraceElement("reactor.core.publisher.Mono", "block", SENTINEL, 123);
        error.setStackTrace(frames);
        ServiceBusQueueAdvanceFeatures.report(stream, "failed", error);
        assertFalse(text().contains("frame="));
        output.reset();
        error.setStackTrace(new StackTraceElement[] {
            new StackTraceElement("com.azure." + SENTINEL, "read", SENTINEL, 1),
            new StackTraceElement("com.azure.Client", SENTINEL, SENTINEL, 2),
            new StackTraceElement("com.azure." + "x".repeat(200), "read", SENTINEL, 3),
            new StackTraceElement("com.azure.Client", "x".repeat(100), SENTINEL, 4),
            new StackTraceElement("com.azure.messaging.servicebus.ServiceBusReceiverAsyncClient", "close", SENTINEL, 1485)
        });
        ServiceBusQueueAdvanceFeatures.report(stream, "failed", error);
        assertEquals(1, text().split("; frame=", -1).length - 1);
        assertTrue(text().contains("ServiceBusReceiverAsyncClient.close:1485"));
        assertFalse(text().contains(SENTINEL));
    }

    @Test(timeout = 1000)
    public void suppressedCyclesAndFanoutShareTheEightNodeDiagnosticBudget() {
        RuntimeException root = new RuntimeException(SENTINEL);
        IllegalStateException original = new IllegalStateException(SENTINEL);
        root.addSuppressed(original);
        original.addSuppressed(root);
        for (int i = 0; i < 100; i++) {
            root.addSuppressed(new IllegalArgumentException(SENTINEL));
        }
        ServiceBusQueueAdvanceFeatures.report(stream, "failed", root);
        assertEquals(8, text().split("; cause=", -1).length - 1);
        assertTrue(text().contains("diagnostics-truncated=true"));
        assertTrue(text().contains("suppressed-truncated=true"));
        assertTrue(text().length() < 12000);
        assertFalse(text().contains(SENTINEL));
    }

    @Test
    public void missingUpdateEvidenceCannotBecomeLifecyclePass() {
        for (int completed = -1; completed < 3; completed++) {
            output.reset();
            ServiceBusQueueAdvanceFeatures.UpdateResults updates = completed < 0 ? null
                : new ServiceBusQueueAdvanceFeatures.UpdateResults();
            if (completed > 0) {
                updates.duplicates.acknowledge();
            }
            if (completed > 1) {
                updates.duplicates.observe(Duration.ofMinutes(10));
            }
            AtomicBoolean cleaned = new AtomicBoolean();
            assertEquals(1, runWithEvidence(updates, Mono.empty(),
                Mono.fromRunnable(() -> cleaned.set(true))));
            assertTrue(cleaned.get());
            assertTrue(text(), text().contains("sessions: update-response=NOT_RUN; immediate-get-readback=NOT_RUN"));
            assertTrue(text().contains("Lifecycle=FAIL; cleanup=PASS"));
            assertFalse(text().contains("Lifecycle completed:"));
        }
    }

    @Test
    public void runnerPreservesBothDimensionsAndRejectsLatePromotion() {
        for (String later : new String[] {"none", "scenario", "cleanup", "deadline"}) {
            output.reset();
            ServiceBusQueueAdvanceFeatures.UpdateResults updates = new ServiceBusQueueAdvanceFeatures.UpdateResults();
            AtomicBoolean cancelled = new AtomicBoolean();
            Mono<Void> scenarios = Mono.fromRunnable(() -> {
                updates.duplicates.acknowledge();
                updates.duplicates.observe(Duration.ofMinutes(10));
                updates.sessions.acknowledge();
                updates.sessions.observe(false);
            });
            if (later.equals("scenario")) {
                scenarios = scenarios.then(Mono.error(new IllegalStateException(SENTINEL)));
            } else if (later.equals("deadline")) {
                scenarios = scenarios.then(Mono.<Void>never().doOnCancel(() -> cancelled.set(true)));
            }
            Mono<Void> cleanup = later.equals("cleanup")
                ? Mono.error(new IllegalStateException(SENTINEL)) : Mono.empty();
            assertEquals(later.equals("none") ? 0 : 1, runWithEvidence(updates, scenarios, cleanup));
            assertEquals(later.equals("deadline"), cancelled.get());
            String finalEvidence = "Final update evidence: duplicates: update-response=PASS;"
                + " immediate-get-readback=NOTPASS; property=autoDeleteOnIdle; expected=PT5M; actual=PT10M";
            assertTrue(text(), text().contains(finalEvidence));
            assertEquals(2, text().split("duplicates: update-response=PASS; immediate-get-readback=NOTPASS", -1).length - 1);
            assertTrue(text().indexOf("actual=PT10M") < text().indexOf("Cleanup starting;"));
            assertEquals(later.equals("none"), text().contains("Lifecycle=PASS"));
            assertFalse(text().contains(SENTINEL));
            assertThrows(AzureQueueWork.SafetyFailure.class,
                () -> updates.duplicates.observe(Duration.ofMinutes(5)));
            updates.duplicates.failReadback();
            assertTrue(updates.duplicates.summary().contains("actual=PT10M; reason=MISMATCH"));
        }
    }

    @Test
    public void missingAndInvalidObservationsDoNotDefaultToPass() {
        for (boolean invalidResponse : new boolean[] {true, false}) {
            output.reset();
            ServiceBusQueueAdvanceFeatures.UpdateResults updates = new ServiceBusQueueAdvanceFeatures.UpdateResults();
            if (invalidResponse) {
                updates.duplicates.failResponse();
            } else {
                updates.duplicates.acknowledge();
                updates.duplicates.failReadback();
            }
            assertEquals(1, runWithEvidence(updates, Mono.empty(), Mono.empty()));
            assertTrue(text().contains(invalidResponse
                ? "update-response=FAIL; immediate-get-readback=NOT_RUN"
                : "update-response=PASS; immediate-get-readback=FAIL"));
            assertTrue(text().contains("actual=NOT_OBSERVED"));
            assertFalse(text().contains("Lifecycle completed:"));
        }
        ServiceBusQueueAdvanceFeatures.UpdateResults updates = new ServiceBusQueueAdvanceFeatures.UpdateResults();
        assertThrows(AzureQueueWork.SafetyFailure.class, () -> updates.duplicates.observe(Duration.ofMinutes(5)));
        updates.duplicates.acknowledge();
        assertThrows(AzureQueueWork.SafetyFailure.class, () -> updates.duplicates.observe((Duration) null));
        assertThrows(AzureQueueWork.SafetyFailure.class, () -> updates.duplicates.observe(Duration.ofMinutes(2)));
        assertThrows(AzureQueueWork.SafetyFailure.class, () -> updates.duplicates.observe(false));
        updates.sessions.acknowledge();
        assertThrows(AzureQueueWork.SafetyFailure.class, () -> updates.sessions.observe((Boolean) null));
        assertThrows(AzureQueueWork.SafetyFailure.class, () -> updates.sessions.observe(Duration.ofMinutes(5)));
        assertThrows(AzureQueueWork.SafetyFailure.class, updates::requireComplete);
    }

    private int runWithEvidence(ServiceBusQueueAdvanceFeatures.UpdateResults updates,
        Mono<Void> scenarios, Mono<Void> cleanup) {
        return ServiceBusQueueAdvanceFeatures.run(new ServiceBusQueueAdvanceFeatures.QueueWork() {
            public Mono<Void> provision() { return Mono.empty(); }
            public Mono<Void> scenarios() { return scenarios; }
            public Mono<Void> cleanup() { return cleanup; }
            public ServiceBusQueueAdvanceFeatures.UpdateResults updates() { return updates; }
        }, stream, stream, LIMIT, LIMIT);
    }

    private static void matchingUpdates(ServiceBusQueueAdvanceFeatures.UpdateResults updates) {
        updates.duplicates.acknowledge();
        updates.duplicates.observe(Duration.ofMinutes(5));
        updates.sessions.acknowledge();
        updates.sessions.observe(false);
    }

    private int run(Mono<Void> provision, Mono<Void> scenarios, Mono<Void> cleanup) {
        ServiceBusQueueAdvanceFeatures.UpdateResults updates = new ServiceBusQueueAdvanceFeatures.UpdateResults();
        return ServiceBusQueueAdvanceFeatures.run(new ServiceBusQueueAdvanceFeatures.QueueWork() {
            public Mono<Void> provision() { return provision; }
            public Mono<Void> scenarios() { return scenarios.then(Mono.fromRunnable(() -> matchingUpdates(updates))); }
            public Mono<Void> cleanup() { return cleanup; }
            public ServiceBusQueueAdvanceFeatures.UpdateResults updates() { return updates; }
        }, stream, stream, LIMIT, LIMIT);
    }

    private String text() {
        return output.toString(StandardCharsets.UTF_8);
    }

    private String textWithoutFrames() {
        return text().replaceAll("; frame=[A-Za-z0-9_.$:<>]+", "");
    }

    static Map<String, String> environment() {
        return Map.of("AZURE_TENANT_ID", "11111111-1111-1111-1111-111111111111",
            "AZURE_SUBSCRIPTION_ID", "22222222-2222-2222-2222-222222222222", "AZURE_LOCATION", "westus");
    }
}
