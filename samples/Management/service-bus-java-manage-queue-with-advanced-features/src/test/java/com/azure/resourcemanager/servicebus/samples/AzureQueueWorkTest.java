// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import com.azure.core.http.HttpHeaders;
import com.azure.core.http.HttpMethod;
import com.azure.core.http.HttpRequest;
import com.azure.core.http.HttpPipelineBuilder;
import com.azure.core.http.rest.PagedFlux;
import com.azure.core.http.rest.PagedResponseBase;
import com.azure.core.http.rest.SimpleResponse;
import com.azure.core.management.exception.ManagementException;
import com.azure.core.management.exception.ManagementError;
import com.azure.json.JsonProviders;
import com.azure.json.JsonReader;
import com.azure.json.JsonSerializable;
import com.azure.json.JsonWriter;
import com.azure.resourcemanager.resources.fluent.ResourceGroupsClient;
import com.azure.resourcemanager.resources.fluent.ResourcesClient;
import com.azure.resourcemanager.resources.fluent.models.ResourceGroupInner;
import com.azure.resourcemanager.resources.fluent.models.GenericResourceExpandedInner;
import com.azure.resourcemanager.servicebus.fluent.NamespacesClient;
import com.azure.resourcemanager.servicebus.fluent.QueuesClient;
import com.azure.resourcemanager.servicebus.fluent.TopicsClient;
import com.azure.resourcemanager.servicebus.fluent.models.AccessKeysInner;
import com.azure.resourcemanager.servicebus.fluent.models.CheckNameAvailabilityResultInner;
import com.azure.resourcemanager.servicebus.fluent.models.SBAuthorizationRuleInner;
import com.azure.resourcemanager.servicebus.fluent.models.SBNamespaceInner;
import com.azure.resourcemanager.servicebus.fluent.models.SBQueueInner;
import com.azure.resourcemanager.servicebus.fluent.models.SBTopicInner;
import com.azure.resourcemanager.servicebus.models.AccessRights;
import com.azure.resourcemanager.servicebus.models.SkuName;
import com.azure.resourcemanager.servicebus.models.SBAuthorizationRuleListResult;
import com.azure.resourcemanager.servicebus.implementation.ServiceBusManagementClientBuilder;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.PrintStream;
import java.lang.reflect.Proxy;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.Test;
import reactor.core.publisher.Mono;
import static org.junit.Assert.*;

public class AzureQueueWorkTest {
    @Test
    public void managementPipelinePreservesClientAndOtherPolicyOrder() {
        com.azure.core.credential.TokenCredential credential = request -> Mono.empty();
        com.azure.core.management.profile.AzureProfile profile =
            new com.azure.core.management.profile.AzureProfile("offline", "offline",
                com.azure.core.management.AzureEnvironment.AZURE);
        com.azure.core.http.HttpClient http = request -> Mono.error(new AssertionError("Unexpected transport"));
        com.azure.core.http.HttpPipeline original =
            com.azure.resourcemanager.resources.fluentcore.utils.HttpPipelineProvider.buildHttpPipeline(
                credential, profile, null, new com.azure.core.http.policy.HttpLogOptions()
                    .setLogLevel(com.azure.core.http.policy.HttpLogDetailLevel.NONE), null, null, null, http);
        com.azure.core.http.HttpPipeline actual = AzureQueueWork.managementPipeline(credential, profile, http);
        List<Class<?>> expectedPolicies = new ArrayList<>();
        List<Class<?>> actualPolicies = new ArrayList<>();
        for (int i = 0; i < original.getPolicyCount(); i++) {
            if (!(original.getPolicy(i) instanceof
                com.azure.resourcemanager.resources.fluentcore.policy.ProviderRegistrationPolicy)) {
                expectedPolicies.add(original.getPolicy(i).getClass());
            }
        }
        for (int i = 0; i < actual.getPolicyCount(); i++) {
            actualPolicies.add(actual.getPolicy(i).getClass());
        }
        assertEquals(original.getPolicyCount() - 1, actual.getPolicyCount());
        assertEquals(expectedPolicies, actualPolicies);
        assertSame(http, actual.getHttpClient());
    }

    @Test
    public void bothManagersPropagateMissingRegistrationWithoutProviderWritesOrRetry() {
        Fake cloud = new Fake();
        com.azure.core.credential.TokenCredential credential = request -> Mono.just(
            new com.azure.core.credential.AccessToken("offline-fixture-not-a-credential",
                java.time.OffsetDateTime.now().plusHours(1)));
        com.azure.core.management.profile.AzureProfile profile =
            new com.azure.core.management.profile.AzureProfile(cloud.config.tenant, cloud.config.subscription,
                com.azure.core.management.AzureEnvironment.AZURE);
        for (boolean resourceManager : List.of(false, true)) {
            AtomicInteger requests = new AtomicInteger();
            com.azure.core.http.HttpPipeline pipeline = AzureQueueWork.managementPipeline(credential, profile,
                request -> Mono.defer(() -> {
                    requests.incrementAndGet();
                    assertEquals(HttpMethod.GET, request.getHttpMethod());
                    assertEquals(resourceManager ? cloud.config.groupId().replace("/resourceGroups/", "/resourcegroups/")
                        : cloud.config.namespaceId(),
                        request.getUrl().getPath());
                    return Mono.just(new OfflineResponse(request, 409,
                        "{\"error\":{\"code\":\"MissingSubscriptionRegistration\",\"message\":\"Offline fixture\","
                        + "\"details\":[{\"code\":\"MissingSubscriptionRegistration\",\"target\":\"Microsoft.ServiceBus\"}]}}"));
                }));
            Mono<?> operation = resourceManager
                ? com.azure.resourcemanager.resources.ResourceManager.authenticate(pipeline, profile)
                    .withSubscription(cloud.config.subscription).serviceClient().getResourceGroups()
                    .getAsync(cloud.config.group)
                : com.azure.resourcemanager.servicebus.ServiceBusManager.authenticate(pipeline, profile)
                    .serviceClient().getNamespaces().getByResourceGroupAsync(cloud.config.group, cloud.config.namespace);
            try {
                operation.block(Duration.ofSeconds(5));
                fail("Missing registration must fail");
            } catch (ManagementException error) {
                assertEquals("MissingSubscriptionRegistration", error.getValue().getCode());
                assertEquals(409, error.getResponse().getStatusCode());
            }
            assertEquals(1, requests.get());
        }
    }

    @Test
    public void authorizationFailuresIdentifyExactOperationAndPreserveOriginalCause() {
        for (String queue : List.of("sessions", "duplicates")) {
            assertOperationFailure(ResourceGroupsClient.class, "getAsync", queue, null, "resource-group-get");
            assertOperationFailure(NamespacesClient.class, "getByResourceGroupAsync", queue, null, "namespace-get");
            assertOperationFailure(QueuesClient.class, "listAuthorizationRulesAsync", queue, null,
                queue + "-authorization-list");
            for (String rule : List.of(AzureQueueWork.SEND, AzureQueueWork.LISTEN)) {
                assertOperationFailure(QueuesClient.class, "createOrUpdateAuthorizationRuleAsync", queue, rule,
                    queue + "-" + rule + "-authorization-create");
                assertOperationFailure(QueuesClient.class, "listKeysAsync", queue, rule,
                    queue + "-" + rule + "-authorization-keys");
                assertOperationFailure(QueuesClient.class, "deleteAuthorizationRuleAsync", queue, rule,
                    queue + "-" + rule + "-authorization-delete");
                assertOperationFailure(QueuesClient.class, "listAuthorizationRulesPage", queue, rule,
                    queue + "-" + rule + "-authorization-absence-readback");
            }
        }
    }

    private static void assertOperationFailure(Class<?> client, String method, String queue, String rule,
        String operation) {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().block();
        ManagementException original = failure(404, "SENTINEL-DIAGNOSTIC-CODE");
        cloud.failureClient = client;
        cloud.failureMethod = method;
        cloud.failureQueue = queue;
        cloud.failureRule = rule;
        cloud.operationFailure = original;
        AzureQueueWork.StepFailure observed = assertThrows(AzureQueueWork.StepFailure.class,
            () -> work.scenarios().block());
        assertEquals(queue + "-authorization-and-messaging", observed.step);
        assertTrue(observed.getCause() instanceof AzureQueueWork.StepFailure);
        AzureQueueWork.StepFailure local = (AzureQueueWork.StepFailure) observed.getCause();
        assertEquals(operation, local.step);
        assertSame(original, local.getCause());
        assertEquals(1, cloud.injectedFailures);
        boolean afterBroker = method.equals("deleteAuthorizationRuleAsync")
            || method.equals("listAuthorizationRulesPage");
        assertEquals(afterBroker, cloud.deliveries.contains(queue + ":send/listen"));
        if (queue.equals("sessions")) {
            assertFalse(cloud.deliveries.contains("duplicates:send/listen"));
        }
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        ServiceBusQueueAdvanceFeatures.report(new PrintStream(bytes, true, StandardCharsets.UTF_8),
            "failed", observed);
        String diagnostic = bytes.toString(StandardCharsets.UTF_8);
        assertTrue(diagnostic.contains("; operation=" + operation));
        assertTrue(diagnostic.contains("; cause=ManagementException"));
        assertTrue(diagnostic.contains("; httpStatus=404"));
        assertFalse(diagnostic.contains("SENTINEL"));
        cloud.operationFailure = null;
        work.cleanup().block();
        assertNull(cloud.group);
        assertNull(cloud.namespace);
    }

    @Test
    public void operationLabelsPreserveAuthorizationOrderAndSuccessfulAbsence() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().then(work.scenarios()).then(Mono.defer(work::cleanup)).block();
        List<String> expected = new ArrayList<>();
        for (String queue : List.of("sessions", "duplicates")) {
            expected.add("listAuthorizationRulesAsync:" + queue);
            for (String rule : List.of(AzureQueueWork.SEND, AzureQueueWork.LISTEN)) {
                expected.add("createOrUpdateAuthorizationRuleAsync:" + queue + ":" + rule);
            }
            for (String rule : List.of(AzureQueueWork.SEND, AzureQueueWork.LISTEN)) {
                expected.add("listKeysAsync:" + queue + ":" + rule);
            }
            for (String rule : List.of(AzureQueueWork.SEND, AzureQueueWork.LISTEN)) {
                expected.add("deleteAuthorizationRuleAsync:" + queue + ":" + rule);
                expected.add("listAuthorizationRulesPage:" + queue + ":" + rule);
            }
        }
        assertEquals(expected, cloud.authorizationCalls);
        assertEquals(List.of("sessions:send/listen", "duplicates:send/listen"), cloud.deliveries);
        assertEquals(4, cloud.deletedRules);
        assertTrue(cloud.rules.isEmpty());
        assertNull(cloud.group);
        assertEquals(0, cloud.injectedFailures);
    }

    @Test
    public void namespaceVariantsRetainStandardAndPremiumCapacity() {
        Fake cloud = new Fake();
        assertEquals(SkuName.STANDARD, AzureQueueWork.namespaceSettings(cloud.config).sku().name());
        ServiceBusQueueAdvanceFeatures.Config premium = ServiceBusQueueAdvanceFeatures.Config.parse(
            new String[] {"--run", "--sku=premium"}, ServiceBusQueueAdvanceFeaturesTest.environment());
        assertEquals(SkuName.PREMIUM, AzureQueueWork.namespaceSettings(premium).sku().name());
        assertEquals(Integer.valueOf(1), AzureQueueWork.namespaceSettings(premium).sku().capacity());
    }

    @Test
    public void completeLifecycleRetainsEveryImplementedQueueVariant() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().then(work.scenarios()).then(Mono.defer(work::cleanup)).block();
        assertEquals(List.of("sessions:send/listen", "duplicates:send/listen"), cloud.deliveries);
        assertEquals(4, cloud.queueVersions.size());
        SBQueueInner session = cloud.queueVersions.get(0);
        assertTrue(session.requiresSession());
        assertEquals(Duration.ofMinutes(10), session.defaultMessageTimeToLive());
        assertEquals(Integer.valueOf(40), session.maxDeliveryCount());
        assertTrue(session.deadLetteringOnMessageExpiration());
        SBQueueInner duplicate = cloud.queueVersions.get(1);
        assertTrue(duplicate.requiresDuplicateDetection());
        assertEquals(Integer.valueOf(2048), duplicate.maxSizeInMegabytes());
        assertEquals(Duration.ofMinutes(10), duplicate.duplicateDetectionHistoryTimeWindow());
        assertEquals(Duration.ofMinutes(10), duplicate.autoDeleteOnIdle());
        assertEquals(Duration.ofMinutes(5), cloud.queueVersions.get(2).autoDeleteOnIdle());
        assertFalse(cloud.queueVersions.get(3).deadLetteringOnMessageExpiration());
        assertEquals(Duration.ofMinutes(10), cloud.queueVersions.get(3).defaultMessageTimeToLive());
        assertEquals(4, cloud.deletedRules);
        assertEquals(1, cloud.namespaceRuleLists);
        assertEquals(0, cloud.namespaceKeyLists);
        assertTrue(cloud.rules.isEmpty());
        assertTrue(cloud.queues.isEmpty());
        assertNull(cloud.namespace);
        assertNull(cloud.group);
        assertTrue(cloud.events.indexOf("namespace-delete") > cloud.events.indexOf("queue-delete:sessions"));
        assertTrue(cloud.events.indexOf("group-delete") > cloud.events.indexOf("namespace-delete"));
    }

    @Test
    public void groupCollisionDoesNotMutateOrDeleteExistingResources() {
        Fake cloud = new Fake();
        cloud.group = new ResourceGroupInner();
        AzureQueueWork work = cloud.work();
        assertThrows(AzureQueueWork.StepFailure.class, () -> work.provision().block());
        work.cleanup().block();
        assertTrue(cloud.events.isEmpty());
    }

    @Test
    public void namespaceGlobalCollisionDoesNotCreateGroup() {
        Fake cloud = new Fake();
        cloud.available = false;
        AzureQueueWork work = cloud.work();
        assertThrows(AzureQueueWork.StepFailure.class, () -> work.provision().block());
        work.cleanup().block();
        assertTrue(cloud.events.isEmpty());
    }

    @Test
    public void resourceGroupCreateUpdateResponseIsNeverAdopted() {
        Fake cloud = new Fake();
        cloud.groupCreateStatus = 200;
        AzureQueueWork work = cloud.work();
        assertThrows(AzureQueueWork.StepFailure.class, () -> work.provision().block());
        assertThrows(IllegalStateException.class, () -> work.cleanup().block());
        assertFalse(cloud.events.contains("group-delete"));
    }

    @Test
    public void uncertainCreationRetainsIdsAndNeverDeletes() {
        Fake cloud = new Fake();
        cloud.failNamespaceCreate = true;
        AzureQueueWork work = cloud.work();
        assertThrows(AzureQueueWork.StepFailure.class, () -> work.provision().block());
        assertThrows(IllegalStateException.class, () -> work.cleanup().block());
        assertNotNull(cloud.group);
        assertFalse(cloud.events.contains("group-delete"));
    }

    @Test
    public void ownershipTagMismatchBlocksCleanup() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().block();
        cloud.group.withTags(Map.of(AzureQueueWork.OWNER, "someone-else"));
        assertThrows(AzureQueueWork.StepFailure.class, () -> work.cleanup().block());
        assertFalse(cloud.events.contains("namespace-delete"));
        assertFalse(cloud.events.contains("group-delete"));
    }

    @Test
    public void unexpectedQueueIsNeitherAdoptedNorDeleted() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().block();
        cloud.queues.put("foreign", cloud.queue(new SBQueueInner(), "foreign"));
        assertThrows(AzureQueueWork.StepFailure.class, () -> work.cleanup().block());
        assertFalse(cloud.events.contains("namespace-delete"));
    }

    @Test
    public void existingQueueCollisionPreventsMutationAndNamespaceDeletion() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().block();
        cloud.queues.put("sessions", cloud.queue(new SBQueueInner(), "sessions"));
        assertThrows(AzureQueueWork.StepFailure.class, () -> work.scenarios().block());
        assertTrue(cloud.queueVersions.isEmpty());
        assertThrows(AzureQueueWork.StepFailure.class, () -> work.cleanup().block());
        assertFalse(cloud.events.contains("queue-delete:sessions"));
    }

    @Test
    public void foreignTopicBlocksNamespaceDeletion() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().block();
        cloud.foreignTopic = true;
        assertThrows(AzureQueueWork.StepFailure.class, () -> work.cleanup().block());
        assertFalse(cloud.events.contains("namespace-delete"));
    }

    @Test
    public void nonemptyGroupIsNotRecursivelyDeleted() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().block();
        cloud.foreignResource = true;
        assertThrows(AzureQueueWork.StepFailure.class, () -> work.cleanup().block());
        assertFalse(cloud.events.contains("group-delete"));
    }

    @Test
    public void sendFailurePropagatesAndOwnedResourcesCanStillBeCleaned() {
        Fake cloud = new Fake();
        cloud.failSend = true;
        AzureQueueWork work = cloud.work();
        work.provision().block();
        assertThrows(AzureQueueWork.StepFailure.class, () -> work.scenarios().block());
        work.cleanup().block();
        assertNull(cloud.group);
    }

    @Test
    public void deletionFailureDoesNotDeleteParentOrClaimAbsence() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().block();
        cloud.failNamespaceDelete = true;
        assertThrows(AzureQueueWork.StepFailure.class, () -> work.cleanup().block());
        assertNotNull(cloud.namespace);
        assertFalse(cloud.events.contains("group-delete"));
    }

    @Test
    public void namespaceNotFoundAfterDeleteAllowsGuardedEmptyGroupCleanup() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().then(work.scenarios()).block();
        cloud.namespaceMissingCode = "NamespaceNotFound";
        work.cleanup().block();
        assertNull(cloud.namespace);
        assertNull(cloud.group);
        assertTrue(cloud.events.indexOf("group-delete") > cloud.events.indexOf("namespace-delete"));
        assertEquals(List.of("sessions:send/listen", "duplicates:send/listen"), cloud.deliveries);
    }

    @Test
    public void namespaceSpecificAbsenceAlsoWorksDuringOwnedGroupPreflight() {
        Fake cloud = new Fake();
        cloud.namespaceMissingCode = "NamespaceNotFound";
        AzureQueueWork work = cloud.work();
        work.provision().block();
        assertNotNull(cloud.namespace);
        work.cleanup().block();
        assertNull(cloud.group);
    }

    @Test
    public void namespaceAbsenceDoesNotBroadenQueueOrGroupAbsence() {
        ManagementException original = failure(404, "NamespaceNotFound");
        assertSame(original, assertThrows(ManagementException.class,
            () -> AzureQueueWork.absent(() -> Mono.error(original)).block()));
    }

    @Test
    public void namespaceAbsenceRequiresStructured404AndFreshReadback() {
        for (RuntimeException original : List.of(failure(500, "NamespaceNotFound"),
            failure(404, null), new ManagementException("offline", null),
            new IllegalStateException("transport"))) {
            assertSame(original, assertThrows(RuntimeException.class,
                () -> AzureQueueWork.absentNamespace(() -> Mono.error(original)).block()));
        }
        AtomicInteger reads = new AtomicInteger();
        AzureQueueWork.awaitNamespaceAbsent(() -> reads.incrementAndGet() < 3 ? Mono.just("present")
            : Mono.error(failure(404, "NamespaceNotFound")), Duration.ofMillis(1)).block(Duration.ofSeconds(1));
        assertEquals(3, reads.get());
    }

    @Test
    public void namespaceNotFoundDoesNotAuthorizeDeletingNonemptyGroup() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().block();
        cloud.namespaceMissingCode = "NamespaceNotFound";
        cloud.foreignResource = true;
        assertThrows(AzureQueueWork.StepFailure.class, () -> work.cleanup().block());
        assertFalse(cloud.events.contains("group-delete"));
        assertNotNull(cloud.group);
    }

    @Test
    public void namespaceReadbackRejectsUnknownParentAndNon404Errors() {
        for (String code : List.of("ParentResourceNotFound", "MessagingGatewayNotFoundStatusCode",
            "Unknown", "NamespaceNotFound")) {
            Fake cloud = new Fake();
            AzureQueueWork work = cloud.work();
            work.provision().block();
            cloud.namespaceMissingCode = code;
            cloud.namespaceMissingStatus = code.equals("NamespaceNotFound") ? 403 : 404;
            AzureQueueWork.StepFailure observed = assertThrows(AzureQueueWork.StepFailure.class,
                () -> work.cleanup().block());
            assertEquals("namespace-delete", observed.step);
            assertTrue(observed.getCause() instanceof ManagementException);
            ManagementException cause = (ManagementException) observed.getCause();
            assertEquals(cloud.namespaceMissingStatus, cause.getResponse().getStatusCode());
            assertEquals(code, cause.getValue().getCode());
            assertFalse(cloud.events.contains("group-delete"));
        }
    }

    @Test
    public void unchangedUpdateReadbackSettingsRemainFatal() {
        for (String queue : List.of("duplicates", "sessions")) {
            for (Map.Entry<String, java.util.function.Consumer<SBQueueInner>> mismatch
                : queueSettingMismatches(queue).entrySet()) {
                if (mismatch.getKey().equals("autoDeleteOnIdle")
                    || mismatch.getKey().equals("deadLetteringOnMessageExpiration")) {
                    continue;
                }
                Fake cloud = new Fake();
                cloud.updateTarget = queue;
                cloud.updateReadback = value -> {
                    mismatch.getValue().accept(value);
                    return Mono.just(value);
                };
                assertUpdateFailure(cloud, "immediate-get-readback", mismatch.getKey());
            }
        }
    }

    @Test
    public void updateResponseRejectsEveryTypedSettingMismatch() {
        for (String queue : List.of("duplicates", "sessions")) {
            for (Map.Entry<String, java.util.function.Consumer<SBQueueInner>> mismatch
                : queueSettingMismatches(queue).entrySet()) {
                Fake cloud = new Fake();
                cloud.updateTarget = queue;
                cloud.updateResponse = value -> {
                    mismatch.getValue().accept(value);
                    return Mono.just(value);
                };
                assertUpdateFailure(cloud, "update-response", mismatch.getKey());
            }
        }
    }

    @Test
    public void updatePhasesRejectEmptyMissingAndWrongModels() {
        for (String queue : List.of("duplicates", "sessions")) {
            for (boolean response : List.of(true, false)) {
                for (String fault : List.of("empty", "missing-identity", "wrong-identity", "missing-settings")) {
                    Fake cloud = new Fake();
                    cloud.updateTarget = queue;
                    java.util.function.Function<SBQueueInner, Mono<SBQueueInner>> invalid = value -> {
                        if (fault.equals("empty")) {
                            return Mono.empty();
                        }
                        if (fault.equals("missing-identity")) {
                            return Mono.just(new SBQueueInner());
                        }
                        if (fault.equals("wrong-identity")) {
                            return Mono.just(cloud.queue(value, "foreign"));
                        }
                        return Mono.just(cloud.queue(new SBQueueInner(), queue));
                    };
                    if (response) {
                        cloud.updateResponse = invalid;
                    } else {
                        cloud.updateReadback = invalid;
                    }
                    String reason = fault.equals("empty")
                        ? (response ? "Queue update response missing" : "Queue readback missing")
                        : fault.equals("missing-settings") ? "Queue setting mismatch" : "Queue identity mismatch";
                    assertUpdateFailure(cloud, response ? "update-response" : "immediate-get-readback", reason);
                }
            }
        }
    }

    @Test
    public void updatePhasesPreserveTransportErrorsAndDoNotTreat404AsAbsence() {
        for (String queue : List.of("duplicates", "sessions")) {
            for (boolean response : List.of(true, false)) {
                for (int status : List.of(403, 404, 500)) {
                    Fake cloud = new Fake();
                    cloud.updateTarget = queue;
                    ManagementException original = failure(status, "SENTINEL-CODE");
                    if (response) {
                        cloud.updateResponse = value -> Mono.error(original);
                    } else {
                        cloud.updateReadback = value -> Mono.error(original);
                    }
                    String diagnostic = assertUpdateFailure(cloud,
                        response ? "update-response" : "immediate-get-readback", "httpStatus=" + status);
                    assertTrue(diagnostic.contains("cause=ManagementException"));
                }
                Fake cloud = new Fake();
                cloud.updateTarget = queue;
                java.util.function.Function<SBQueueInner, Mono<SBQueueInner>> transport =
                    value -> Mono.error(new IOException("SENTINEL-TRANSPORT"));
                if (response) {
                    cloud.updateResponse = transport;
                } else {
                    cloud.updateReadback = transport;
                }
                assertUpdateFailure(cloud, response ? "update-response" : "immediate-get-readback",
                    "cause=IOException");
            }
        }
    }

    @Test(timeout = 10000)
    public void updatePhaseTimeoutsCancelTheOperationAndPreserveCleanup() {
        for (String queue : List.of("duplicates", "sessions")) {
            for (boolean response : List.of(true, false)) {
                Fake cloud = new Fake();
                cloud.updateTarget = queue;
                AtomicInteger cancellations = new AtomicInteger();
                java.util.function.Function<SBQueueInner, Mono<SBQueueInner>> timeout =
                    value -> Mono.<SBQueueInner>never().doOnCancel(cancellations::incrementAndGet)
                        .timeout(Duration.ofMillis(20));
                if (response) {
                    cloud.updateResponse = timeout;
                } else {
                    cloud.updateReadback = timeout;
                }
                assertUpdateFailure(cloud, response ? "update-response" : "immediate-get-readback",
                    "cause=TimeoutException");
                assertEquals(1, cancellations.get());
            }
        }
    }

    @Test
    public void changedFieldMismatchKeepsNotPassWhileCompletingLifecycle() {
        for (String queue : List.of("duplicates", "sessions")) {
            for (int minutes : List.of(10, 7)) {
                Fake cloud = new Fake();
                cloud.updateTarget = queue;
                cloud.updateReadback = value -> Mono.just(queue.equals("sessions")
                    ? value.withDeadLetteringOnMessageExpiration(true)
                    : value.withAutoDeleteOnIdle(Duration.ofMinutes(minutes)));
                ByteArrayOutputStream out = new ByteArrayOutputStream();
                ByteArrayOutputStream err = new ByteArrayOutputStream();
                assertEquals(0, ServiceBusQueueAdvanceFeatures.run(cloud.work(),
                    new PrintStream(out), new PrintStream(err), Duration.ofSeconds(5), Duration.ofSeconds(2)));
                String text = out.toString(StandardCharsets.UTF_8);
                assertTrue(text, text.contains(queue + ": update-response=PASS; immediate-get-readback=NOTPASS"));
                assertTrue(text, text.contains(queue.equals("sessions")
                    ? "expected=false; actual=true" : "expected=PT5M; actual=PT" + minutes + "M"));
                assertTrue(text, text.contains("Lifecycle=PASS; cleanup=PASS"));
                assertTrue(text, text.contains("Immediate update confirmation: NOTPASS"));
                assertEquals(2, text.split(queue + ": update-response=PASS; immediate-get-readback=NOTPASS", -1).length - 1);
                assertFalse(text.contains("All queue scenarios and cleanup completed."));
                assertEquals("", err.toString(StandardCharsets.UTF_8));
                assertEquals(List.of("PUT", "GET"), cloud.updateCalls);
                assertEquals(List.of("sessions:send/listen", "duplicates:send/listen"), cloud.deliveries);
                assertEquals(4, cloud.deletedRules);
                assertEquals(Duration.ofMinutes(5), cloud.queueVersions.get(2).autoDeleteOnIdle());
                assertFalse(cloud.queueVersions.get(3).deadLetteringOnMessageExpiration());
                assertNull(cloud.group);
                assertNull(cloud.namespace);
                assertTrue(cloud.queues.isEmpty());
            }
        }
    }

    @Test
    public void invalidChangedFieldReadbackStillFailsBeforeMessaging() {
        for (String queue : List.of("duplicates", "sessions")) {
            Fake cloud = new Fake();
            cloud.updateTarget = queue;
            cloud.updateReadback = value -> Mono.just(queue.equals("sessions")
                ? value.withDeadLetteringOnMessageExpiration(null) : value.withAutoDeleteOnIdle(null));
            assertUpdateFailure(cloud, "immediate-get-readback",
                queue.equals("sessions") ? "deadLetteringOnMessageExpiration" : "autoDeleteOnIdle");
        }
        for (Duration value : List.of(Duration.ZERO, Duration.ofMinutes(-1), Duration.ofSeconds(299))) {
            Fake cloud = new Fake();
            cloud.updateReadback = model -> Mono.just(model.withAutoDeleteOnIdle(value));
            assertUpdateFailure(cloud, "immediate-get-readback", "autoDeleteOnIdle");
        }
    }

    @Test
    public void everyInspectedGetFieldMustBePresentAndTyped() {
        Map<String, java.util.function.Consumer<SBQueueInner>> fields = Map.of(
            "requiresSession", value -> value.withRequiresSession(null),
            "defaultMessageTimeToLive", value -> value.withDefaultMessageTimeToLive(null),
            "maxDeliveryCount", value -> value.withMaxDeliveryCount(null),
            "requiresDuplicateDetection", value -> value.withRequiresDuplicateDetection(null),
            "maxSizeInMegabytes", value -> value.withMaxSizeInMegabytes(null),
            "duplicateDetectionHistoryTimeWindow", value -> value.withDuplicateDetectionHistoryTimeWindow(null));
        for (Map.Entry<String, java.util.function.Consumer<SBQueueInner>> field : fields.entrySet()) {
            Fake cloud = new Fake();
            cloud.updateTarget = List.of("requiresSession", "defaultMessageTimeToLive", "maxDeliveryCount")
                .contains(field.getKey()) ? "sessions" : "duplicates";
            cloud.updateReadback = value -> {
                field.getValue().accept(value);
                return Mono.just(value);
            };
            assertUpdateFailure(cloud, "immediate-get-readback", field.getKey());
        }
        for (String queue : List.of("duplicates", "sessions")) {
            Fake cloud = new Fake();
            cloud.updateTarget = queue;
            cloud.updateReadback = value -> Mono.fromCallable(() -> {
                String property = queue.equals("sessions") ? "deadLetteringOnMessageExpiration" : "autoDeleteOnIdle";
                try (JsonReader reader = JsonProviders.createReader("{\"properties\":{\"" + property
                    + "\":\"SENTINEL_INVALID_TYPE\"}}")) {
                    return SBQueueInner.fromJson(reader);
                }
            });
            assertUpdateFailure(cloud, "immediate-get-readback", "cause=");
        }
    }

    @Test
    public void mismatchIsRetainedWhenLaterMessagingOrCleanupFails() {
        for (String queue : List.of("duplicates", "sessions")) {
            for (boolean cleanupFailure : List.of(true, false)) {
                Fake cloud = new Fake();
                cloud.updateTarget = queue;
                cloud.updateReadback = value -> Mono.just(queue.equals("sessions")
                    ? value.withDeadLetteringOnMessageExpiration(true)
                    : value.withAutoDeleteOnIdle(Duration.ofMinutes(10)));
                cloud.failSend = !cleanupFailure;
                cloud.failNamespaceDelete = cleanupFailure;
                ByteArrayOutputStream out = new ByteArrayOutputStream();
                ByteArrayOutputStream err = new ByteArrayOutputStream();
                assertEquals(1, ServiceBusQueueAdvanceFeatures.run(cloud.work(),
                    new PrintStream(out), new PrintStream(err), Duration.ofSeconds(5), Duration.ofSeconds(2)));
                String text = out.toString(StandardCharsets.UTF_8);
                assertTrue(text, text.contains("Final update evidence: " + queue
                    + ": update-response=PASS; immediate-get-readback=NOTPASS"));
                assertTrue(text, text.contains("Lifecycle=FAIL; cleanup=" + (cleanupFailure ? "FAIL" : "PASS")));
                assertFalse(text.contains("Lifecycle completed:"));
                assertFalse(err.toString(StandardCharsets.UTF_8).contains("SENTINEL"));
                assertEquals(List.of("PUT", "GET"), cloud.updateCalls);
                assertFalse(cloud.deliveries.isEmpty());
                assertEquals(cleanupFailure, cloud.group != null);
            }
        }
    }

    @Test
    public void mismatchDoesNotOverrideLaterAuthorizationOrOwnershipFailure() {
        for (String queue : List.of("duplicates", "sessions")) {
            for (boolean ownershipFailure : List.of(true, false)) {
                Fake cloud = new Fake();
                cloud.updateTarget = queue;
                cloud.updateReadback = value -> {
                    if (ownershipFailure) {
                        cloud.group.withTags(Map.of(AzureQueueWork.OWNER, "SENTINEL-FOREIGN"));
                    }
                    return Mono.just(queue.equals("sessions") ? value.withDeadLetteringOnMessageExpiration(true)
                        : value.withAutoDeleteOnIdle(Duration.ofMinutes(10)));
                };
                if (!ownershipFailure) {
                    cloud.failureClient = QueuesClient.class;
                    cloud.failureMethod = "createOrUpdateAuthorizationRuleAsync";
                    cloud.failureQueue = "sessions";
                    cloud.failureRule = AzureQueueWork.SEND;
                    cloud.operationFailure = failure(403, "SENTINEL-DENIED");
                }
                ByteArrayOutputStream out = new ByteArrayOutputStream();
                ByteArrayOutputStream err = new ByteArrayOutputStream();
                assertEquals(1, ServiceBusQueueAdvanceFeatures.run(cloud.work(),
                    new PrintStream(out), new PrintStream(err), Duration.ofSeconds(5), Duration.ofSeconds(2)));
                String text = out.toString(StandardCharsets.UTF_8);
                assertTrue(text, text.contains("Final update evidence: " + queue
                    + ": update-response=PASS; immediate-get-readback=NOTPASS"));
                assertTrue(text.contains("Lifecycle=FAIL"));
                assertTrue(cloud.deliveries.isEmpty());
                assertFalse(text.contains("SENTINEL"));
                assertFalse(err.toString(StandardCharsets.UTF_8).contains("SENTINEL"));
                assertEquals(ownershipFailure, cloud.group != null);
                if (ownershipFailure) {
                    assertFalse(cloud.events.contains("namespace-delete"));
                    assertFalse(cloud.events.contains("group-delete"));
                }
            }
        }
    }

    @Test(timeout = 10000)
    public void enclosingDeadlineCancelsUpdatePhasesWithoutInventingObservations() {
        for (String queue : List.of("duplicates", "sessions")) {
            for (boolean response : List.of(true, false)) {
                Fake cloud = new Fake();
                cloud.updateTarget = queue;
                AtomicInteger cancellations = new AtomicInteger();
                java.util.function.Function<SBQueueInner, Mono<SBQueueInner>> pending =
                    value -> Mono.<SBQueueInner>never().doOnCancel(cancellations::incrementAndGet);
                if (response) {
                    cloud.updateResponse = pending;
                } else {
                    cloud.updateReadback = pending;
                }
                AzureQueueWork work = cloud.work();
                work.provision().block();
                ByteArrayOutputStream out = new ByteArrayOutputStream();
                ByteArrayOutputStream err = new ByteArrayOutputStream();
                assertEquals(1, ServiceBusQueueAdvanceFeatures.run(new ServiceBusQueueAdvanceFeatures.QueueWork() {
                    public Mono<Void> provision() { return Mono.empty(); }
                    public Mono<Void> scenarios() { return work.scenarios(); }
                    public Mono<Void> cleanup() { return work.cleanup(); }
                    public ServiceBusQueueAdvanceFeatures.UpdateResults updates() { return work.updates(); }
                }, new PrintStream(out), new PrintStream(err), Duration.ofMillis(500), Duration.ofSeconds(2)));
                assertEquals(1, cancellations.get());
                String text = out.toString(StandardCharsets.UTF_8);
                assertTrue(text, text.contains(queue + (response
                    ? ": update-response=FAIL; immediate-get-readback=NOT_RUN"
                    : ": update-response=PASS; immediate-get-readback=FAIL")));
                assertTrue(text.contains("Lifecycle=FAIL; cleanup=PASS"));
                assertTrue(err.toString(StandardCharsets.UTF_8).contains("TimeoutException"));
                assertEquals(response ? List.of("PUT") : List.of("PUT", "GET"), cloud.updateCalls);
                assertTrue(cloud.deliveries.isEmpty());
                assertNull(cloud.group);
            }
        }
    }

    @Test
    public void cleanupTimeoutRetainsEarlierMismatchAndNeverReportsCompletion() {
        Fake cloud = new Fake();
        cloud.updateReadback = value -> Mono.just(value.withAutoDeleteOnIdle(Duration.ofMinutes(10)));
        cloud.pendingNamespaceDelete = true;
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        ByteArrayOutputStream err = new ByteArrayOutputStream();
        assertEquals(1, ServiceBusQueueAdvanceFeatures.run(cloud.work(),
            new PrintStream(out), new PrintStream(err), Duration.ofSeconds(5), Duration.ofMillis(50)));
        String text = out.toString(StandardCharsets.UTF_8);
        assertTrue(text, text.contains("Final update evidence: duplicates: update-response=PASS;"
            + " immediate-get-readback=NOTPASS"));
        assertTrue(text.contains("Lifecycle=FAIL; cleanup=FAIL"));
        assertFalse(text.contains("Cleanup verified:"));
        assertFalse(text.contains("Lifecycle completed:"));
        assertNotNull(cloud.namespace);
        assertNotNull(cloud.group);
    }

    @Test
    public void matchingUpdateResponsesKeepOneIndependentReadbackForEachUpdate() {
        for (String queue : List.of("duplicates", "sessions")) {
            Fake cloud = new Fake();
            cloud.updateTarget = queue;
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            ByteArrayOutputStream err = new ByteArrayOutputStream();
            assertEquals(0, ServiceBusQueueAdvanceFeatures.run(cloud.work(),
                new PrintStream(out), new PrintStream(err), Duration.ofSeconds(5), Duration.ofSeconds(2)));
            assertEquals(List.of("PUT", "GET"), cloud.updateCalls);
            assertEquals(List.of("sessions:send/listen", "duplicates:send/listen"), cloud.deliveries);
            assertEquals(4, cloud.queueVersions.size());
            assertEquals(Duration.ofMinutes(5), cloud.queueVersions.get(2).autoDeleteOnIdle());
            assertFalse(cloud.queueVersions.get(3).deadLetteringOnMessageExpiration());
            assertEquals("", err.toString(StandardCharsets.UTF_8));
            assertTrue(out.toString(StandardCharsets.UTF_8).contains("Immediate update confirmation: PASS"));
            assertNull(cloud.group);
        }
    }

    @Test
    public void sharedSettingsValidationRejectsEmptyCreationReadback() {
        Fake cloud = new Fake();
        cloud.emptyCreatedQueueReadback = true;
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        ByteArrayOutputStream err = new ByteArrayOutputStream();
        assertEquals(1, ServiceBusQueueAdvanceFeatures.run(cloud.work(),
            new PrintStream(out), new PrintStream(err), Duration.ofSeconds(5), Duration.ofSeconds(2)));
        String diagnostic = err.toString(StandardCharsets.UTF_8);
        assertTrue(diagnostic, diagnostic.contains("step=sessions-create"));
        assertTrue(diagnostic, diagnostic.contains("Queue readback missing"));
        assertEquals(1, cloud.queueVersions.size());
        assertTrue(cloud.updateCalls.isEmpty());
        assertTrue(cloud.deliveries.isEmpty());
        assertTrue(out.toString(StandardCharsets.UTF_8).contains("Cleanup verified:"));
        assertNull(cloud.group);
        assertNull(cloud.namespace);
        assertTrue(cloud.queues.isEmpty());
    }

    private static Map<String, java.util.function.Consumer<SBQueueInner>> queueSettingMismatches(String queue) {
        if (queue.equals("sessions")) {
            return Map.of(
                "requiresSession", value -> value.withRequiresSession(false),
                "defaultMessageTimeToLive", value -> value.withDefaultMessageTimeToLive(Duration.ofMinutes(2)),
                "deadLetteringOnMessageExpiration", value -> value.withDeadLetteringOnMessageExpiration(true),
                "maxDeliveryCount", value -> value.withMaxDeliveryCount(10));
        }
        return Map.of(
            "requiresDuplicateDetection", value -> value.withRequiresDuplicateDetection(false),
            "maxSizeInMegabytes", value -> value.withMaxSizeInMegabytes(1024),
            "duplicateDetectionHistoryTimeWindow",
                value -> value.withDuplicateDetectionHistoryTimeWindow(Duration.ofMinutes(2)),
            "autoDeleteOnIdle", value -> value.withAutoDeleteOnIdle(Duration.ofMinutes(10)));
    }

    private static String assertUpdateFailure(Fake cloud, String phase, String reason) {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        ByteArrayOutputStream err = new ByteArrayOutputStream();
        assertEquals(1, ServiceBusQueueAdvanceFeatures.run(cloud.work(),
            new PrintStream(out), new PrintStream(err), Duration.ofSeconds(5), Duration.ofSeconds(2)));
        String diagnostic = err.toString(StandardCharsets.UTF_8);
        assertTrue(diagnostic, diagnostic.contains("step=" + cloud.updateTarget + "-update"));
        assertTrue(diagnostic, diagnostic.contains("operation=" + cloud.updateTarget + "-" + phase));
        assertTrue(diagnostic, diagnostic.contains(reason));
        assertFalse(diagnostic, diagnostic.contains("SENTINEL"));
        String output = out.toString(StandardCharsets.UTF_8);
        assertFalse(output, output.contains("SENTINEL"));
        assertTrue(output, output.contains(cloud.updateTarget + (phase.equals("update-response")
            ? ": update-response=FAIL; immediate-get-readback=NOT_RUN"
            : ": update-response=PASS; immediate-get-readback=FAIL")));
        assertTrue(output.contains("Lifecycle=FAIL; cleanup=PASS"));
        assertEquals(phase.equals("update-response") ? List.of("PUT") : List.of("PUT", "GET"), cloud.updateCalls);
        assertEquals(cloud.updateTarget.equals("duplicates") ? 3 : 4, cloud.queueVersions.size());
        assertTrue(cloud.deliveries.isEmpty());
        assertEquals(0, cloud.namespaceRuleLists);
        assertTrue(out.toString(StandardCharsets.UTF_8).contains("Cleanup verified:"));
        assertFalse(out.toString(StandardCharsets.UTF_8).contains("Lifecycle completed:"));
        assertNull(cloud.group);
        assertNull(cloud.namespace);
        assertTrue(cloud.queues.isEmpty());
        assertTrue(cloud.rules.isEmpty());
        return diagnostic;
    }

    @Test
    public void acceptedNamespaceDeleteStillPresentTimesOutWithoutDeletingGroup() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().block();
        cloud.pendingNamespaceDelete = true;
        assertThrows(RuntimeException.class, () -> work.cleanup().timeout(Duration.ofMillis(30)).block());
        assertNotNull(cloud.namespace);
        assertFalse(cloud.events.contains("group-delete"));
    }

    @Test
    public void alreadyAutoDeletedOwnedQueueIsSafeToSkip() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().then(work.scenarios()).block();
        cloud.queues.remove("duplicates");
        work.cleanup().block();
        assertFalse(cloud.events.contains("queue-delete:duplicates"));
        assertNull(cloud.group);
    }

    @Test
    public void cleanupWaitsForReadbackNotJustDeleteAcceptance() {
        AtomicInteger polls = new AtomicInteger();
        AzureQueueWork.awaitAbsent(() -> polls.incrementAndGet() < 3 ? Mono.just("still-present")
            : Mono.error(notFound()), Duration.ofMillis(1)).block(Duration.ofSeconds(1));
        assertEquals(3, polls.get());
    }

    @Test
    public void ruleAbsenceEnumeratesEveryPageBeforeDeciding() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().then(work.scenarios()).block();
        String next = cloud.ruleNext();
        AtomicInteger reads = new AtomicInteger();
        cloud.pageOverride = (queue, link) -> {
            reads.incrementAndGet();
            return Mono.just(rulePage(List.of(cloud.rule(AzureQueueWork.LISTEN)), link == null ? next : null));
        };
        assertTrue(work.ruleAbsent("sessions", AzureQueueWork.SEND).block());
        assertEquals(2, reads.get());
        reads.set(0);
        cloud.pageOverride = (queue, link) -> {
            reads.incrementAndGet();
            return Mono.just(rulePage(link == null ? List.of() : List.of(cloud.rule(AzureQueueWork.SEND)),
                link == null ? next : null));
        };
        assertFalse(work.ruleAbsent("sessions", AzureQueueWork.SEND).block());
        assertEquals(2, reads.get());
    }

    @Test
    public void ruleAbsenceWaitsForSuccessfulListWithoutTheRule() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().then(work.scenarios()).block();
        AtomicInteger reads = new AtomicInteger();
        cloud.pageOverride = (queue, link) -> Mono.just(rulePage(reads.incrementAndGet() < 3
            ? List.of(cloud.rule(AzureQueueWork.SEND)) : List.of(), null));
        work.awaitRuleAbsent("sessions", AzureQueueWork.SEND, Duration.ofMillis(1)).block(Duration.ofSeconds(1));
        assertEquals(3, reads.get());
    }

    @Test
    public void ruleAbsenceRejectsMalformedAndIncompletePages() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().then(work.scenarios()).block();
        List<SBAuthorizationRuleInner> nullEntry = new ArrayList<>();
        nullEntry.add(null);
        for (List<SBAuthorizationRuleInner> values : java.util.Arrays.asList(null,
            nullEntry, List.of(new SBAuthorizationRuleInner()))) {
            cloud.pageOverride = (queue, link) -> Mono.just(rulePage(values, null));
            assertThrows(AzureQueueWork.SafetyFailure.class,
                () -> work.ruleAbsent("sessions", AzureQueueWork.SEND).block());
        }
        cloud.pageOverride = (queue, link) -> Mono.empty();
        assertThrows(AzureQueueWork.SafetyFailure.class,
            () -> work.ruleAbsent("sessions", AzureQueueWork.SEND).block());
        cloud.pageOverride = (queue, link) -> Mono.just(new SimpleResponse<>(
            new HttpRequest(HttpMethod.GET, "https://offline.invalid"), 200, new HttpHeaders(), null));
        assertThrows(AzureQueueWork.SafetyFailure.class,
            () -> work.ruleAbsent("sessions", AzureQueueWork.SEND).block());
        cloud.pageOverride = (queue, link) -> link == null ? Mono.just(rulePage(List.of(), cloud.ruleNext()))
            : Mono.empty();
        assertThrows(AzureQueueWork.SafetyFailure.class,
            () -> work.ruleAbsent("sessions", AzureQueueWork.SEND).block());
        for (String next : List.of(cloud.ruleNext().replace("management.azure.com", "foreign.invalid"),
            cloud.ruleNext().replace("/queues/sessions/", "/queues/foreign/"), " ", cloud.ruleNext())) {
            cloud.pageOverride = (queue, link) -> Mono.just(rulePage(List.of(), next));
            assertThrows(RuntimeException.class, () -> work.ruleAbsent("sessions", AzureQueueWork.SEND).block());
        }
    }

    @Test
    public void ruleAbsencePropagatesFirstAndLaterPageFailuresWithoutRetry() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().then(work.scenarios()).block();
        for (RuntimeException failure : List.of(failure(404, "ResourceNotFound"),
            failure(403, "AuthorizationFailed"), failure(500, "InternalServerError"),
            new IllegalStateException("transport"))) {
            for (boolean later : List.of(false, true)) {
                AtomicInteger reads = new AtomicInteger();
                cloud.pageOverride = (queue, link) -> {
                    reads.incrementAndGet();
                    return later && link == null ? Mono.just(rulePage(List.of(), cloud.ruleNext()))
                        : Mono.error(failure);
                };
                assertSame(failure, assertThrows(RuntimeException.class, () -> work
                    .awaitRuleAbsent("sessions", AzureQueueWork.SEND, Duration.ofMillis(1)).block()));
                assertEquals(later ? 2 : 1, reads.get());
            }
        }
    }

    @Test
    public void ruleAbsenceRequiresExistingOwnedParentAndExactQueue() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().then(work.scenarios()).block();
        cloud.pageOverride = (queue, link) -> { throw new AssertionError("List must not run"); };
        for (Class<?> type : List.of(ResourceGroupsClient.class, NamespacesClient.class, QueuesClient.class)) {
            cloud.emptyGetClient = type;
            assertThrows(RuntimeException.class, () -> work.ruleAbsent("sessions", AzureQueueWork.SEND).block());
        }
        cloud.emptyGetClient = null;
        SBQueueInner owned = cloud.queues.remove("sessions");
        assertThrows(ManagementException.class, () -> work.ruleAbsent("sessions", AzureQueueWork.SEND).block());
        cloud.queues.put("sessions", cloud.queue(new SBQueueInner(), "foreign"));
        assertThrows(AzureQueueWork.SafetyFailure.class,
            () -> work.ruleAbsent("sessions", AzureQueueWork.SEND).block());
        cloud.queues.put("sessions", owned);
        SBNamespaceInner namespace = cloud.namespace;
        cloud.namespace = null;
        assertThrows(AzureQueueWork.StepFailure.class,
            () -> work.ruleAbsent("sessions", AzureQueueWork.SEND).block());
        cloud.namespace = namespace;
        ResourceGroupInner group = cloud.group;
        cloud.group = null;
        assertThrows(AzureQueueWork.StepFailure.class,
            () -> work.ruleAbsent("sessions", AzureQueueWork.SEND).block());
        cloud.group = group;
        cloud.group.withTags(Map.of(AzureQueueWork.OWNER, "foreign"));
        assertThrows(AzureQueueWork.SafetyFailure.class,
            () -> work.ruleAbsent("sessions", AzureQueueWork.SEND).block());
    }

    @Test
    public void ruleAbsenceRejectsNonSuccessAndUnfinishedReadback() {
        Fake cloud = new Fake();
        AzureQueueWork work = cloud.work();
        work.provision().then(work.scenarios()).block();
        for (int status : new int[] {204, 403, 404, 500}) {
            cloud.pageOverride = (queue, link) -> Mono.just(new SimpleResponse<>(
                new HttpRequest(HttpMethod.GET, "https://offline.invalid"), status, new HttpHeaders(),
                new SBAuthorizationRuleListResult().withValue(List.of())));
            assertThrows(AzureQueueWork.SafetyFailure.class,
                () -> work.ruleAbsent("sessions", AzureQueueWork.SEND).block());
        }
        cloud.pageOverride = (queue, link) -> link == null ? Mono.just(rulePage(List.of(), cloud.ruleNext()))
            : Mono.never();
        assertThrows(RuntimeException.class, () -> work.ruleAbsent("sessions", AzureQueueWork.SEND)
            .timeout(Duration.ofMillis(30)).block());
    }

    @Test
    public void sdkRawRulePagesRetainMalformedValuesAndRejectHttpFailures() {
        Fake cloud = new Fake();
        for (String json : List.of("{}", "{\"value\":null}", "{\"value\":[]}")) {
            List<HttpRequest> requests = new ArrayList<>();
            AzureQueueWork.RulePages pages = AzureQueueWork.authorizationRulePages(
                new ServiceBusManagementClientBuilder().subscriptionId(cloud.config.subscription)
                    .pipeline(new HttpPipelineBuilder().httpClient(request -> {
                        requests.add(request);
                        return Mono.just(new OfflineResponse(request, 200, json));
                    }).build()).buildClient(), cloud.config);
            SBAuthorizationRuleListResult page = pages.read("sessions", null).block().getValue();
            if (json.equals("{\"value\":[]}")) {
                assertTrue(page.value().isEmpty());
            } else {
                assertNull(page.value());
            }
            assertEquals(cloud.config.namespaceId() + "/queues/sessions/authorizationRules",
                requests.get(0).getUrl().getPath());
            pages.read("sessions", cloud.ruleNext()).block();
            assertEquals(cloud.ruleNext(), requests.get(1).getUrl().toString());
        }
        for (int status : new int[] {403, 404, 500}) {
            AzureQueueWork.RulePages pages = AzureQueueWork.authorizationRulePages(
                new ServiceBusManagementClientBuilder().subscriptionId(cloud.config.subscription)
                    .pipeline(new HttpPipelineBuilder().httpClient(request ->
                        Mono.just(new OfflineResponse(request, status,
                            "{\"error\":{\"code\":\"ResourceNotFound\",\"message\":\"SENTINEL\"}}"))).build())
                    .buildClient(), cloud.config);
            ManagementException error = assertThrows(ManagementException.class,
                () -> pages.read("sessions", null).block());
            assertEquals(status, error.getResponse().getStatusCode());
        }
    }

    private static com.azure.core.http.rest.Response<SBAuthorizationRuleListResult> rulePage(
        List<SBAuthorizationRuleInner> values, String next) {
        return new SimpleResponse<>(new HttpRequest(HttpMethod.GET, "https://offline.invalid"),
            200, new HttpHeaders(), new SBAuthorizationRuleListResult().withValue(values).withNextLink(next));
    }

    @Test
    public void entityNotFound404IsAbsence() {
        ManagementException missing = failure(404, "EntityNotFound");
        AtomicInteger calls = new AtomicInteger();
        assertTrue(AzureQueueWork.absent(() -> {
            calls.incrementAndGet();
            return Mono.error(missing);
        }).block());
        assertEquals(1, calls.get());
    }

    @Test
    public void entityNotFoundOtherStatusesAreNotAbsence() {
        for (int status : new int[] {400, 401, 403, 409, 429, 500, 503}) {
            ManagementException failure = failure(status, "EntityNotFound");
            AtomicInteger calls = new AtomicInteger();
            assertSame(failure, assertThrows(ManagementException.class, () -> AzureQueueWork.absent(() -> {
                calls.incrementAndGet();
                return Mono.error(failure);
            }).block()));
            assertEquals(1, calls.get());
        }
    }

    @Test
    public void entityNotFoundDoesNotBroadenUnknown404() {
        for (String code : List.of("NamespaceNotFound", "MessagingGatewayNotFoundStatusCode",
            "SubscriptionNotFound", "UnknownCode", "entitynotfound", "EntityNotFound ",
            "EntityNotFound\nSharedAccessKey=NEVER_PRINT_SECRET")) {
            ManagementException failure = failure(404, code);
            AtomicInteger calls = new AtomicInteger();
            assertSame(failure, assertThrows(ManagementException.class, () -> AzureQueueWork.absent(() -> {
                calls.incrementAndGet();
                return Mono.error(failure);
            }).block()));
            assertEquals(1, calls.get());
        }
    }

    @Test
    public void existingAbsenceCodesAndPresentResponseUnchanged() {
        for (String code : List.of("ResourceNotFound", "ResourceGroupNotFound", "MessagingEntityNotFound")) {
            assertTrue(AzureQueueWork.absent(() -> Mono.error(failure(404, code))).block());
        }
        assertFalse(AzureQueueWork.absent(() -> Mono.just("present")).block());
    }

    @Test
    public void authorizationOrUnknown404IsNotAbsence() {
        assertThrows(ManagementException.class, () -> AzureQueueWork.absent(() ->
            Mono.error(failure(403, "AuthorizationFailed"))).block());
        assertThrows(ManagementException.class, () -> AzureQueueWork.absent(() ->
            Mono.error(failure(404, "SubscriptionNotFound"))).block());
        assertThrows(IllegalStateException.class, () -> AzureQueueWork.absent(() ->
            Mono.error(new IllegalStateException("transport"))).block());
        assertTrue(AzureQueueWork.absent(() -> Mono.error(notFound())).block());
    }

    private static ManagementException notFound() {
        return failure(404, "ResourceNotFound");
    }

    private static ManagementException failure(int status, String code) {
        return new ManagementException("SENTINEL-SECRET", new OfflineResponse(status),
            new ManagementError(code, "SENTINEL-SECRET"));
    }

    private static final class OfflineResponse extends com.azure.core.http.HttpResponse {
        private final int status;
        private final String body;
        OfflineResponse(int status) {
            this(new HttpRequest(HttpMethod.GET, "https://offline.invalid"), status, "");
        }
        OfflineResponse(HttpRequest request, int status, String body) {
            super(request);
            this.status = status;
            this.body = body;
        }
        public int getStatusCode() { return status; }
        public String getHeaderValue(String name) { return getHeaders().getValue(name); }
        public HttpHeaders getHeaders() { return new HttpHeaders().set("Content-Type", "application/json"); }
        public reactor.core.publisher.Flux<java.nio.ByteBuffer> getBody() {
            return reactor.core.publisher.Flux.just(java.nio.ByteBuffer.wrap(body.getBytes(StandardCharsets.UTF_8)));
        }
        public Mono<byte[]> getBodyAsByteArray() { return Mono.just(body.getBytes(StandardCharsets.UTF_8)); }
        public Mono<String> getBodyAsString() { return Mono.just(body); }
        public Mono<String> getBodyAsString(java.nio.charset.Charset charset) { return Mono.just(body); }
    }

    private static final class Fake {
        final ServiceBusQueueAdvanceFeatures.Config config = ServiceBusQueueAdvanceFeatures.Config.parse(
            new String[] {"--run"}, ServiceBusQueueAdvanceFeaturesTest.environment());
        final Map<String, SBQueueInner> queues = new HashMap<>();
        final Map<String, SBAuthorizationRuleInner> rules = new HashMap<>();
        final List<SBQueueInner> queueVersions = new ArrayList<>();
        final List<String> events = new ArrayList<>();
        final List<String> deliveries = new ArrayList<>();
        final List<String> authorizationCalls = new ArrayList<>();
        Class<?> failureClient;
        String failureMethod;
        String failureQueue;
        String failureRule;
        ManagementException operationFailure;
        int injectedFailures;
        ResourceGroupInner group;
        SBNamespaceInner namespace;
        boolean available = true;
        boolean failNamespaceCreate;
        boolean failNamespaceDelete;
        boolean pendingNamespaceDelete;
        String namespaceMissingCode = "ResourceNotFound";
        int namespaceMissingStatus = 404;
        String updateTarget = "duplicates";
        final List<String> updateCalls = new ArrayList<>();
        java.util.function.Function<SBQueueInner, Mono<SBQueueInner>> updateResponse = Mono::just;
        java.util.function.Function<SBQueueInner, Mono<SBQueueInner>> updateReadback = Mono::just;
        boolean cleanupStarted;
        boolean emptyCreatedQueueReadback;
        boolean foreignTopic;
        boolean foreignResource;
        boolean failSend;
        int groupCreateStatus = 201;
        int deletedRules;
        int namespaceRuleLists;
        int namespaceKeyLists;
        int completedRuleAbsenceReads;
        String lastDeletedRule;
        Class<?> emptyGetClient;
        AzureQueueWork.RulePages pageOverride;

        AzureQueueWork work() {
            return new AzureQueueWork(config, client(ResourceGroupsClient.class), client(ResourcesClient.class),
                client(NamespacesClient.class), client(QueuesClient.class), client(TopicsClient.class),
                (send, listen, queue, run, session) -> {
                    assertEquals("SENTINEL-SEND-" + queue, send);
                    assertEquals("SENTINEL-LISTEN-" + queue, listen);
                    assertEquals(config.runId, run);
                    assertEquals("sessions".equals(queue), session);
                    deliveries.add(queue + ":send/listen");
                    return failSend ? Mono.error(new IllegalStateException("SENTINEL-SECRET")) : Mono.empty();
                }, (queue, next) -> {
                    if (pageOverride != null) {
                        return pageOverride.read(queue, next);
                    }
                    beforeCall(QueuesClient.class, "listAuthorizationRulesPage",
                        new Object[] {config.group, config.namespace, queue, lastDeletedRule});
                    List<SBAuthorizationRuleInner> values = new ArrayList<>();
                    rules.forEach((key, value) -> {
                        if (key.startsWith(queue + ":")) {
                            values.add(value);
                        }
                    });
                    return Mono.just(rulePage(values, null)).doOnSuccess(value -> completedRuleAbsenceReads++);
                });
        }

        <T> T client(Class<T> type) {
            return type.cast(Proxy.newProxyInstance(type.getClassLoader(), new Class<?>[] {type}, (proxy, method, args) -> {
                String name = method.getName();
                if (name.startsWith("list") && !name.equals("listKeysAsync")) {
                    return pages(() -> {
                        beforeCall(type, name, args);
                        return list(type, name);
                    });
                }
                return Mono.defer(() -> {
                    beforeCall(type, name, args);
                    return call(type, name, args);
                });
            }));
        }

        private void beforeCall(Class<?> type, String method, Object[] args) {
            if (type == QueuesClient.class && (method.contains("AuthorizationRule") || method.equals("listKeysAsync"))) {
                authorizationCalls.add(method + ":" + args[2] + (args.length > 3 ? ":" + args[3] : ""));
            }
            if (operationFailure == null || namespaceRuleLists == 0 || type != failureClient
                || !method.equals(failureMethod)
                || (type != QueuesClient.class
                    && (failureQueue.equals("duplicates") ? completedRuleAbsenceReads < 2
                        : completedRuleAbsenceReads >= 2))
                || (type == QueuesClient.class && !failureQueue.equals(args[2]))
                || (failureRule != null && !failureRule.equals(args[3]))) {
                return;
            }
            injectedFailures++;
            throw operationFailure;
        }

        List<?> list(Class<?> type, String name) {
            if (type == ResourcesClient.class) {
                return foreignResource ? List.of(read("{}", GenericResourceExpandedInner::fromJson)) : List.of();
            }
            if (type == TopicsClient.class) {
                return foreignTopic ? List.of(new SBTopicInner()) : List.of();
            }
            if (type == NamespacesClient.class && name.equals("listAuthorizationRulesAsync")) {
                namespaceRuleLists++;
                return List.of(new SBAuthorizationRuleInner().withRights(
                    List.of(AccessRights.MANAGE, AccessRights.SEND, AccessRights.LISTEN)));
            }
            if (type == QueuesClient.class && name.equals("listAuthorizationRulesAsync")) {
                return List.of();
            }
            if (type == QueuesClient.class && name.equals("listByNamespaceAsync")) {
                cleanupStarted = true;
                return new ArrayList<>(queues.values());
            }
            throw new AssertionError(type + "." + name);
        }

        Mono<?> call(Class<?> type, String name, Object[] args) {
            if (emptyGetClient == type && (name.equals("getAsync") || name.equals("getByResourceGroupAsync"))) {
                return Mono.empty();
            }
            if (type == ResourceGroupsClient.class) {
                switch (name) {
                    case "checkExistenceAsync": return Mono.just(group != null);
                    case "getAsync": return get(group);
                    case "createOrUpdateWithResponseAsync":
                        events.add("group-create");
                        group = group((ResourceGroupInner) args[1]);
                        return Mono.just(new SimpleResponse<>(new HttpRequest(HttpMethod.PUT, "https://offline.invalid"),
                            groupCreateStatus, new HttpHeaders(), group));
                    case "deleteAsync":
                        events.add("group-delete");
                        group = null;
                        return Mono.empty();
                    default: throw new AssertionError(name);
                }
            }
            if (type == NamespacesClient.class) {
                switch (name) {
                    case "checkNameAvailabilityAsync":
                        return Mono.just(new CheckNameAvailabilityResultInner().withNameAvailable(available));
                    case "getByResourceGroupAsync":
                        return namespace == null ? Mono.error(failure(namespaceMissingStatus, namespaceMissingCode))
                            : Mono.just(namespace);
                    case "createOrUpdateAsync":
                        if (failNamespaceCreate) { return Mono.error(new IllegalStateException("uncertain")); }
                        events.add("namespace-create");
                        namespace = namespace((SBNamespaceInner) args[2]);
                        return Mono.just(namespace);
                    case "deleteAsync":
                        if (failNamespaceDelete) { return Mono.error(new IllegalStateException("delete failed")); }
                        events.add("namespace-delete");
                        if (!pendingNamespaceDelete) { namespace = null; }
                        return Mono.empty();
                    case "listKeysAsync":
                        namespaceKeyLists++;
                        throw new AssertionError("Namespace keys must not be requested");
                    default: throw new AssertionError(name);
                }
            }
            if (type == QueuesClient.class) {
                String queue = (String) args[2];
                switch (name) {
                    case "getAsync":
                        return get(queues.get(queue)).flatMap(value -> {
                            if (!cleanupStarted && emptyCreatedQueueReadback && queueVersions.size() == 1) {
                                return Mono.empty();
                            }
                            if (!cleanupStarted && namespaceRuleLists == 0
                                && queue.equals(updateTarget) && updateCalls.contains("PUT")) {
                                updateCalls.add("GET");
                                return updateReadback.apply(queue(value, queue));
                            }
                            return Mono.just(value);
                        });
                    case "createOrUpdateAsync":
                        boolean updating = queues.containsKey(queue);
                        SBQueueInner settings = queue((SBQueueInner) args[3], queue);
                        queueVersions.add(settings);
                        queues.put(queue, settings);
                        if (updating && queue.equals(updateTarget)) {
                            updateCalls.add("PUT");
                            return updateResponse.apply(queue(settings, queue));
                        }
                        return Mono.just(settings);
                    case "deleteAsync":
                        events.add("queue-delete:" + queue);
                        queues.remove(queue);
                        rules.keySet().removeIf(key -> key.startsWith(queue + ":"));
                        return Mono.empty();
                    case "createOrUpdateAuthorizationRuleAsync":
                        SBAuthorizationRuleInner rule = (SBAuthorizationRuleInner) args[4];
                        assertEquals(List.of("SampleSend".equals(args[3]) ? AccessRights.SEND : AccessRights.LISTEN),
                            rule.rights());
                        try (JsonReader reader = identity(rule, config.namespaceId() + "/queues/" + queue
                            + "/authorizationRules/" + args[3], (String) args[3])) {
                            rule = SBAuthorizationRuleInner.fromJson(reader);
                        } catch (IOException e) { throw new AssertionError(e); }
                        rules.put(queue + ":" + args[3], rule);
                        return Mono.just(rule);
                    case "getAuthorizationRuleAsync": return get(rules.get(queue + ":" + args[3]));
                    case "deleteAuthorizationRuleAsync":
                        lastDeletedRule = (String) args[3];
                        rules.remove(queue + ":" + args[3]);
                        deletedRules++;
                        return Mono.empty();
                    case "listKeysAsync":
                        assertTrue(rules.containsKey(queue + ":" + args[3]));
                        return Mono.just(read("{\"primaryConnectionString\":\"SENTINEL-"
                            + ("SampleSend".equals(args[3]) ? "SEND-" : "LISTEN-") + queue + "\"}",
                            AccessKeysInner::fromJson));
                    default: throw new AssertionError(name);
                }
            }
            throw new AssertionError(type + "." + name);
        }

        private ResourceGroupInner group(ResourceGroupInner settings) {
            try (JsonReader reader = identity(settings, config.groupId(), config.group)) {
                return ResourceGroupInner.fromJson(reader);
            } catch (IOException e) { throw new AssertionError(e); }
        }

        private SBNamespaceInner namespace(SBNamespaceInner settings) {
            try (JsonReader reader = identity(settings, config.namespaceId(), config.namespace)) {
                return SBNamespaceInner.fromJson(reader);
            } catch (IOException e) { throw new AssertionError(e); }
        }

        SBQueueInner queue(SBQueueInner settings, String name) {
            try (JsonReader reader = identity(settings, config.namespaceId() + "/queues/" + name, name)) {
                return SBQueueInner.fromJson(reader);
            } catch (IOException e) { throw new AssertionError(e); }
        }

        SBAuthorizationRuleInner rule(String name) {
            return read("{\"id\":\"" + config.namespaceId() + "/queues/sessions/authorizationRules/" + name
                + "\",\"name\":\"" + name + "\"}", SBAuthorizationRuleInner::fromJson);
        }

        String ruleNext() {
            return "https://management.azure.com" + config.namespaceId()
                + "/queues/sessions/authorizationRules?api-version=2024-01-01&$skiptoken=next";
        }
    }

    private static <T> Mono<T> get(T value) {
        return value == null ? Mono.error(notFound()) : Mono.just(value);
    }

    private interface Reader<T> {
        T read(JsonReader reader) throws IOException;
    }

    private static <T> T read(String json, Reader<T> factory) {
        try (JsonReader reader = JsonProviders.createReader(json)) {
            return factory.read(reader);
        } catch (IOException e) {
            throw new AssertionError(e);
        }
    }

    private static JsonReader identity(JsonSerializable<?> settings, String id, String name) throws IOException {
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        try (JsonWriter writer = JsonProviders.createWriter(bytes)) {
            settings.toJson(writer).flush();
        }
        String properties = bytes.toString(StandardCharsets.UTF_8);
        String json = "{\"id\":\"" + id + "\",\"name\":\"" + name + "\""
            + ("{}".equals(properties) ? "}" : "," + properties.substring(1));
        return JsonProviders.createReader(json);
    }

    private static <T> PagedFlux<T> pages(java.util.function.Supplier<List<T>> values) {
        return new PagedFlux<>(() -> Mono.fromSupplier(() -> new PagedResponseBase<Void, T>(
            new HttpRequest(HttpMethod.GET, "https://offline.invalid"), 200, new HttpHeaders(), values.get(), null, null)));
    }
}
