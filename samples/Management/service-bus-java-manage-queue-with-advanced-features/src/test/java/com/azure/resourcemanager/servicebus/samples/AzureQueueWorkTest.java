// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import com.azure.core.http.HttpHeaders;
import com.azure.core.http.HttpMethod;
import com.azure.core.http.HttpRequest;
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
import java.io.ByteArrayOutputStream;
import java.io.IOException;
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
        OfflineResponse(int status) {
            super(new HttpRequest(HttpMethod.GET, "https://offline.invalid"));
            this.status = status;
        }
        public int getStatusCode() { return status; }
        public String getHeaderValue(String name) { return null; }
        public HttpHeaders getHeaders() { return new HttpHeaders(); }
        public reactor.core.publisher.Flux<java.nio.ByteBuffer> getBody() {
            return reactor.core.publisher.Flux.empty();
        }
        public Mono<byte[]> getBodyAsByteArray() { return Mono.just(new byte[0]); }
        public Mono<String> getBodyAsString() { return Mono.just(""); }
        public Mono<String> getBodyAsString(java.nio.charset.Charset charset) { return Mono.just(""); }
    }

    private static final class Fake {
        final ServiceBusQueueAdvanceFeatures.Config config = ServiceBusQueueAdvanceFeatures.Config.parse(
            new String[] {"--run"}, ServiceBusQueueAdvanceFeaturesTest.environment());
        final Map<String, SBQueueInner> queues = new HashMap<>();
        final Map<String, SBAuthorizationRuleInner> rules = new HashMap<>();
        final List<SBQueueInner> queueVersions = new ArrayList<>();
        final List<String> events = new ArrayList<>();
        final List<String> deliveries = new ArrayList<>();
        ResourceGroupInner group;
        SBNamespaceInner namespace;
        boolean available = true;
        boolean failNamespaceCreate;
        boolean failNamespaceDelete;
        boolean pendingNamespaceDelete;
        boolean foreignTopic;
        boolean foreignResource;
        boolean failSend;
        int groupCreateStatus = 201;
        int deletedRules;
        int namespaceRuleLists;
        int namespaceKeyLists;

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
                });
        }

        <T> T client(Class<T> type) {
            return type.cast(Proxy.newProxyInstance(type.getClassLoader(), new Class<?>[] {type}, (proxy, method, args) -> {
                String name = method.getName();
                if (name.startsWith("list") && !name.equals("listKeysAsync")) {
                    return pages(() -> list(type, name));
                }
                return Mono.defer(() -> call(type, name, args));
            }));
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
                return new ArrayList<>(queues.values());
            }
            throw new AssertionError(type + "." + name);
        }

        Mono<?> call(Class<?> type, String name, Object[] args) {
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
                    case "getByResourceGroupAsync": return get(namespace);
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
                    case "getAsync": return get(queues.get(queue));
                    case "createOrUpdateAsync":
                        SBQueueInner settings = queue((SBQueueInner) args[3], queue);
                        queueVersions.add(settings);
                        queues.put(queue, settings);
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
                        rules.put(queue + ":" + args[3], rule);
                        return Mono.just(rule);
                    case "getAuthorizationRuleAsync": return get(rules.get(queue + ":" + args[3]));
                    case "deleteAuthorizationRuleAsync":
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
