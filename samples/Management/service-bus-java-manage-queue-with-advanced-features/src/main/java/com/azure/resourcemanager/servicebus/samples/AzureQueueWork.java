// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import com.azure.core.credential.TokenCredential;
import com.azure.core.http.HttpClient;
import com.azure.core.http.HttpPipeline;
import com.azure.core.http.HttpPipelineBuilder;
import com.azure.core.http.netty.NettyAsyncHttpClientBuilder;
import com.azure.core.http.policy.HttpLogDetailLevel;
import com.azure.core.http.policy.HttpLogOptions;
import com.azure.core.http.policy.HttpPipelinePolicy;
import com.azure.core.http.rest.Response;
import com.azure.core.http.rest.RestProxy;
import com.azure.core.management.AzureEnvironment;
import com.azure.core.management.exception.ManagementException;
import com.azure.core.management.profile.AzureProfile;
import com.azure.core.management.serializer.SerializerFactory;
import com.azure.core.util.Context;
import com.azure.identity.DefaultAzureCredentialBuilder;
import com.azure.resourcemanager.resources.ResourceManager;
import com.azure.resourcemanager.resources.fluent.ResourceGroupsClient;
import com.azure.resourcemanager.resources.fluent.ResourcesClient;
import com.azure.resourcemanager.resources.fluent.models.ResourceGroupInner;
import com.azure.resourcemanager.resources.fluentcore.policy.ProviderRegistrationPolicy;
import com.azure.resourcemanager.resources.fluentcore.utils.HttpPipelineProvider;
import com.azure.resourcemanager.servicebus.ServiceBusManager;
import com.azure.resourcemanager.servicebus.fluent.NamespacesClient;
import com.azure.resourcemanager.servicebus.fluent.QueuesClient;
import com.azure.resourcemanager.servicebus.fluent.ServiceBusManagementClient;
import com.azure.resourcemanager.servicebus.fluent.TopicsClient;
import com.azure.resourcemanager.servicebus.fluent.models.SBAuthorizationRuleInner;
import com.azure.resourcemanager.servicebus.fluent.models.SBNamespaceInner;
import com.azure.resourcemanager.servicebus.fluent.models.SBQueueInner;
import com.azure.resourcemanager.servicebus.models.AccessRights;
import com.azure.resourcemanager.servicebus.models.CheckNameAvailability;
import com.azure.resourcemanager.servicebus.models.SBAuthorizationRuleListResult;
import com.azure.resourcemanager.servicebus.models.SBSku;
import com.azure.resourcemanager.servicebus.models.SkuName;
import com.azure.resourcemanager.servicebus.models.SkuTier;
import com.azure.resourcemanager.servicebus.implementation.QueuesClientImpl.QueuesService;
import java.net.URI;
import java.time.Duration;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Set;
import java.util.function.Supplier;
import reactor.core.publisher.Flux;
import reactor.core.publisher.Mono;

final class AzureQueueWork implements ServiceBusQueueAdvanceFeatures.QueueWork {
    static final String OWNER = "advanced-queue-sample-run";
    static final String SESSIONS = "sessions";
    static final String DUPLICATES = "duplicates";
    static final String SEND = "SampleSend";
    static final String LISTEN = "SampleListen";
    static final Duration OPERATION_LIMIT = Duration.ofMinutes(5);
    private static final Duration POLL_INTERVAL = Duration.ofSeconds(2);
    private final ServiceBusQueueAdvanceFeatures.Config config;
    private final ResourceGroupsClient groups;
    private final ResourcesClient resources;
    private final NamespacesClient namespaces;
    private final QueuesClient queues;
    private final TopicsClient topics;
    private final MessageVerifier messages;
    private final RulePages rulePages;
    private final Set<String> ownedQueues = new HashSet<>();
    private final ServiceBusQueueAdvanceFeatures.UpdateResults updates
        = new ServiceBusQueueAdvanceFeatures.UpdateResults();
    private boolean groupAttempted;
    private boolean groupOwned;
    private boolean namespaceAttempted;
    private boolean namespaceOwned;

    interface MessageVerifier {
        Mono<Void> verify(String sendConnection, String listenConnection, String queue, String runId, boolean session);
    }

    interface RulePages {
        Mono<Response<SBAuthorizationRuleListResult>> read(String queue, String nextLink);
    }

    AzureQueueWork(ServiceBusQueueAdvanceFeatures.Config config) {
        this.config = config;
        AzureProfile profile = new AzureProfile(config.tenant, config.subscription, AzureEnvironment.AZURE);
        TokenCredential credential = new DefaultAzureCredentialBuilder().tenantId(config.tenant).build();
        com.azure.core.http.HttpClient http = new NettyAsyncHttpClientBuilder()
            .connectTimeout(Duration.ofSeconds(10)).readTimeout(Duration.ofSeconds(30))
            .responseTimeout(Duration.ofSeconds(30)).writeTimeout(Duration.ofSeconds(30)).build();
        HttpPipeline pipeline = managementPipeline(credential, profile, http);
        ResourceManager resourceManager = ResourceManager.authenticate(pipeline, profile)
            .withSubscription(config.subscription);
        ServiceBusManager manager = ServiceBusManager.authenticate(pipeline, profile);
        this.groups = resourceManager.serviceClient().getResourceGroups();
        this.resources = resourceManager.serviceClient().getResources();
        this.namespaces = manager.serviceClient().getNamespaces();
        this.queues = manager.serviceClient().getQueues();
        this.topics = manager.serviceClient().getTopics();
        this.messages = QueueMessages::verify;
        this.rulePages = authorizationRulePages(manager.serviceClient(), config);
    }

    AzureQueueWork(ServiceBusQueueAdvanceFeatures.Config config, ResourceGroupsClient groups,
        ResourcesClient resources, NamespacesClient namespaces, QueuesClient queues, TopicsClient topics,
        MessageVerifier messages, RulePages rulePages) {
        this.config = config;
        this.groups = groups;
        this.resources = resources;
        this.namespaces = namespaces;
        this.queues = queues;
        this.topics = topics;
        this.messages = messages;
        this.rulePages = rulePages;
    }

    static HttpPipeline managementPipeline(TokenCredential credential, AzureProfile profile, HttpClient http) {
        HttpPipeline original = HttpPipelineProvider.buildHttpPipeline(credential, profile, null,
            new HttpLogOptions().setLogLevel(HttpLogDetailLevel.NONE), null, null, null, http);
        List<HttpPipelinePolicy> policies = new ArrayList<>();
        for (int i = 0; i < original.getPolicyCount(); ++i) {
            HttpPipelinePolicy policy = original.getPolicy(i);
            // Registration is a subscription-wide write, outside this sample's owned resources.
            if (!(policy instanceof ProviderRegistrationPolicy)) {
                policies.add(policy);
            }
        }
        return new HttpPipelineBuilder().httpClient(original.getHttpClient())
            .policies(policies.toArray(new HttpPipelinePolicy[0])).build();
    }

    static RulePages authorizationRulePages(ServiceBusManagementClient client,
        ServiceBusQueueAdvanceFeatures.Config config) {
        // PagedFlux normalizes a missing value array to empty. Keep the SDK's raw model to reject that response.
        QueuesService service = RestProxy.create(QueuesService.class, client.getHttpPipeline(),
            SerializerFactory.createDefaultManagementSerializerAdapter());
        return (queue, nextLink) -> nextLink == null
            ? service.listAuthorizationRules(client.getEndpoint(), config.group, config.namespace,
                queue, client.getApiVersion(), config.subscription, "application/json", Context.NONE)
            : service.listAuthorizationRulesNext(nextLink, client.getEndpoint(), "application/json", Context.NONE);
    }

    @Override
    public Mono<Void> provision() {
        return step("resource-group-preflight", () -> groups.checkExistenceAsync(config.group).flatMap(exists ->
            require(!exists, "Resource group name collision")))
            .then(step("namespace-name-preflight", () -> namespaces
                .checkNameAvailabilityAsync(new CheckNameAvailability().withName(config.namespace))
                .flatMap(result -> require(Boolean.TRUE.equals(result.nameAvailable()), "Namespace name collision"))))
            .then(step("resource-group-create", () -> {
                groupAttempted = true;
                return groups.createOrUpdateWithResponseAsync(config.group, new ResourceGroupInner()
                    .withLocation(config.location).withTags(Map.of(OWNER, config.runId))).flatMap(response -> {
                        check(response.getStatusCode() == 201, "Resource group creation not confirmed");
                        checkOwner(response.getValue().id(), response.getValue().tags(), config.groupId());
                        groupOwned = true;
                        return Mono.empty();
                    });
            }))
            .then(step("namespace-create", () -> ownedGroup()
                .then(absentNamespace(() -> namespaces.getByResourceGroupAsync(config.group, config.namespace)))
                .flatMap(missing -> require(missing, "Namespace name collision"))
                .then(Mono.defer(() -> {
                namespaceAttempted = true;
                return namespaces.createOrUpdateAsync(config.group, config.namespace, namespaceSettings(config))
                    .doOnNext(value -> {
                        checkOwner(value.id(), value.tags(), config.namespaceId());
                        namespaceOwned = true;
                        check(value.sku() != null
                            && value.sku().name().equals(config.premium ? SkuName.PREMIUM : SkuName.STANDARD),
                            "Namespace SKU mismatch");
                    }).then();
            }))));
    }

    static SBNamespaceInner namespaceSettings(ServiceBusQueueAdvanceFeatures.Config config) {
        SBSku sku = new SBSku().withName(config.premium ? SkuName.PREMIUM : SkuName.STANDARD)
            .withTier(config.premium ? SkuTier.PREMIUM : SkuTier.STANDARD);
        if (config.premium) {
            sku.withCapacity(1);
        }
        return new SBNamespaceInner().withLocation(config.location).withSku(sku)
            .withTags(Map.of(OWNER, config.runId)).withDisableLocalAuth(false);
    }

    static SBQueueInner sessionSettings(boolean deadLetterExpired) {
        return new SBQueueInner().withRequiresSession(true).withDefaultMessageTimeToLive(Duration.ofMinutes(10))
            .withDeadLetteringOnMessageExpiration(deadLetterExpired).withMaxDeliveryCount(40);
    }

    static SBQueueInner duplicateSettings(int idleMinutes) {
        return new SBQueueInner().withMaxSizeInMegabytes(2048).withRequiresDuplicateDetection(true)
            .withDuplicateDetectionHistoryTimeWindow(Duration.ofMinutes(10))
            .withAutoDeleteOnIdle(Duration.ofMinutes(idleMinutes));
    }

    @Override
    public ServiceBusQueueAdvanceFeatures.UpdateResults updates() {
        return updates;
    }

    @Override
    public Mono<Void> scenarios() {
        return createQueue(SESSIONS, sessionSettings(true))
            .then(createQueue(DUPLICATES, duplicateSettings(10)))
            .then(updateQueue(DUPLICATES, duplicateSettings(5)))
            .then(updateQueue(SESSIONS, sessionSettings(false)))
            .then(step("namespace-authorization-list", () -> namespaces
                .listAuthorizationRulesAsync(config.group, config.namespace).collectList()
                .flatMap(rules -> require(!rules.isEmpty(), "Default namespace authorization rule missing"))))
            .then(verifyQueue(SESSIONS, true))
            .then(verifyQueue(DUPLICATES, false));
    }

    private Mono<Void> createQueue(String name, SBQueueInner settings) {
        return step(name + "-create", () -> ownedNamespace()
            .then(absent(() -> queues.getAsync(config.group, config.namespace, name)))
            .flatMap(missing -> require(missing, "Queue name collision"))
            .then(Mono.defer(() -> queues.createOrUpdateAsync(config.group, config.namespace, name, settings)))
            .doOnNext(value -> {
                check(queueId(name).equalsIgnoreCase(value.id()), "Queue identity mismatch");
                ownedQueues.add(name);
            }).then(Mono.defer(() -> verifySettings(name, settings))));
    }

    private Mono<Void> updateQueue(String name, SBQueueInner settings) {
        ServiceBusQueueAdvanceFeatures.QueueUpdate result = SESSIONS.equals(name)
            ? updates.sessions : updates.duplicates;
        return step(name + "-update", () -> ownedNamespace()
            .then(require(ownedQueues.contains(name), "Queue is not owned"))
            .then(identifyOperation(name + "-update-response",
                Mono.defer(() -> queues.createOrUpdateAsync(config.group, config.namespace, name, settings))
                    .switchIfEmpty(Mono.error(new SafetyFailure("Queue update response missing")))
                    .doOnNext(actual -> {
                        checkSettings(name, settings, actual);
                        result.acknowledge();
                    })
                    .doOnError(error -> result.failResponse()).doOnCancel(result::failResponse)))
            .then(identifyOperation(name + "-immediate-get-readback",
                Mono.defer(() -> queues.getAsync(config.group, config.namespace, name))
                    .switchIfEmpty(Mono.error(new SafetyFailure("Queue readback missing")))
                    .doOnNext(actual -> {
                        checkUnchangedSettings(name, settings, actual);
                        if (SESSIONS.equals(name)) {
                            result.observe(actual.deadLetteringOnMessageExpiration());
                        } else {
                            result.observe(actual.autoDeleteOnIdle());
                        }
                    })
                    .doOnError(error -> result.failReadback()).doOnCancel(result::failReadback))).then());
    }

    private Mono<Void> verifySettings(String name, SBQueueInner expected) {
        return queues.getAsync(config.group, config.namespace, name)
            .switchIfEmpty(Mono.error(new SafetyFailure("Queue readback missing")))
            .doOnNext(actual -> checkSettings(name, expected, actual)).then();
    }

    private void checkSettings(String name, SBQueueInner expected, SBQueueInner actual) {
        checkUnchangedSettings(name, expected, actual);
        if (SESSIONS.equals(name)) {
            checkSetting("deadLetteringOnMessageExpiration", expected.deadLetteringOnMessageExpiration(),
                actual.deadLetteringOnMessageExpiration());
        } else {
            checkSetting("autoDeleteOnIdle", expected.autoDeleteOnIdle(), actual.autoDeleteOnIdle());
        }
    }

    private void checkUnchangedSettings(String name, SBQueueInner expected, SBQueueInner actual) {
        check(queueId(name).equalsIgnoreCase(actual.id()), "Queue identity mismatch");
        if (SESSIONS.equals(name)) {
            checkSetting("requiresSession", true, actual.requiresSession());
            checkSetting("defaultMessageTimeToLive", expected.defaultMessageTimeToLive(),
                actual.defaultMessageTimeToLive());
            checkSetting("maxDeliveryCount", expected.maxDeliveryCount(), actual.maxDeliveryCount());
        } else {
            checkSetting("requiresDuplicateDetection", true, actual.requiresDuplicateDetection());
            checkSetting("maxSizeInMegabytes", expected.maxSizeInMegabytes(), actual.maxSizeInMegabytes());
            checkSetting("duplicateDetectionHistoryTimeWindow", expected.duplicateDetectionHistoryTimeWindow(),
                actual.duplicateDetectionHistoryTimeWindow());
        }
    }

    private static void checkSetting(String property, Object expected, Object actual) {
        check(Objects.equals(expected, actual),
            "Queue setting mismatch; property=" + property + "; expected=" + expected + "; actual=" + actual);
    }

    private Mono<Void> verifyQueue(String name, boolean session) {
        return step(name + "-authorization-and-messaging", () -> ownedNamespace()
            .then(identifyOperation(name + "-authorization-list",
                queues.listAuthorizationRulesAsync(config.group, config.namespace, name).collectList()))
            .flatMap(rules -> require(rules.isEmpty(), "Unexpected queue authorization rule"))
            .then(addRule(name, SEND, AccessRights.SEND))
            .then(addRule(name, LISTEN, AccessRights.LISTEN))
            .then(Mono.zip(
                identifyOperation(name + "-" + SEND + "-authorization-keys",
                    queues.listKeysAsync(config.group, config.namespace, name, SEND)),
                identifyOperation(name + "-" + LISTEN + "-authorization-keys",
                    queues.listKeysAsync(config.group, config.namespace, name, LISTEN))))
            .flatMap(keys -> messages.verify(keys.getT1().primaryConnectionString(),
                keys.getT2().primaryConnectionString(), name, config.runId, session))
            .then(removeRule(name, SEND)).then(removeRule(name, LISTEN)));
    }

    private Mono<Void> addRule(String queue, String rule, AccessRights right) {
        return Mono.defer(() -> identifyOperation(queue + "-" + rule + "-authorization-create",
            queues.createOrUpdateAuthorizationRuleAsync(config.group, config.namespace, queue, rule,
                new SBAuthorizationRuleInner().withRights(List.of(right)))).doOnNext(value ->
                check(List.of(right).equals(value.rights()), "Authorization scope mismatch")).then());
    }

    private Mono<Void> removeRule(String queue, String rule) {
        return Mono.defer(() -> identifyOperation(queue + "-" + rule + "-authorization-delete",
            queues.deleteAuthorizationRuleAsync(config.group, config.namespace, queue, rule)))
            .then(identifyOperation(queue + "-" + rule + "-authorization-absence-readback",
                awaitRuleAbsent(queue, rule, POLL_INTERVAL)));
    }

    Mono<Void> awaitRuleAbsent(String queue, String rule, Duration interval) {
        return ruleAbsent(queue, rule).flatMap(missing -> missing ? Mono.empty()
            : Mono.delay(interval).then(Mono.defer(() -> awaitRuleAbsent(queue, rule, interval))));
    }

    Mono<Boolean> ruleAbsent(String queue, String rule) {
        return ownedNamespace().then(require(ownedQueues.contains(queue), "Queue is not owned"))
            .then(Mono.defer(() -> queues.getAsync(config.group, config.namespace, queue))
                .switchIfEmpty(Mono.error(new SafetyFailure("Queue readback missing")))
                .doOnNext(value -> check(queueId(queue).equalsIgnoreCase(value.id()), "Queue identity mismatch")))
            .then(Mono.defer(() -> readRulePages(queue, null, new HashSet<>()).collectList()))
            .map(rules -> rules.stream().noneMatch(value -> rule.equalsIgnoreCase(value.name())));
    }

    private Flux<SBAuthorizationRuleInner> readRulePages(String queue, String nextLink, Set<String> seen) {
        return Mono.defer(() -> rulePages.read(queue, nextLink))
            .switchIfEmpty(Mono.error(new SafetyFailure("Authorization rule page missing")))
            .flatMapMany(response -> {
                check(response.getStatusCode() == 200, "Authorization rule list was not successful");
                SBAuthorizationRuleListResult page = response.getValue();
                check(page != null && page.value() != null, "Authorization rule list is malformed");
                for (SBAuthorizationRuleInner value : page.value()) {
                    check(value != null && value.name() != null && !value.name().isBlank()
                        && !value.name().contains("/") && !value.name().contains("\\")
                        && (queueId(queue) + "/authorizationRules/" + value.name()).equalsIgnoreCase(value.id()),
                        "Authorization rule identity mismatch");
                }
                Flux<SBAuthorizationRuleInner> values = Flux.fromIterable(page.value());
                String next = page.nextLink();
                if (next == null || next.isEmpty()) {
                    return values;
                }
                URI uri = URI.create(next);
                check("https".equalsIgnoreCase(uri.getScheme())
                    && "management.azure.com".equalsIgnoreCase(uri.getHost())
                    && (uri.getPort() == -1 || uri.getPort() == 443)
                    && uri.getRawUserInfo() == null && uri.getRawFragment() == null
                    && (queueId(queue) + "/authorizationRules").equalsIgnoreCase(uri.getPath())
                    && seen.add(next), "Invalid authorization rule continuation");
                return values.concatWith(Flux.defer(() -> readRulePages(queue, next, seen)));
            });
    }

    @Override
    public Mono<Void> cleanup() {
        return Mono.defer(() -> {
            if (!groupAttempted) {
                return Mono.empty();
            }
            check(groupOwned, "Resource group creation uncertain; manual inspection required");
            // Never adopt a resource merely because a timed-out PUT might have created it.
            check(!namespaceAttempted || namespaceOwned, "Namespace creation uncertain; manual inspection required");
            Mono<Void> deleteNamespace = namespaceOwned ? cleanupNamespace() : Mono.empty();
            return deleteNamespace.then(step("resource-group-delete", () -> ownedGroup()
                .then(resources.listByResourceGroupAsync(config.group).collectList())
                .flatMap(remaining -> require(remaining.isEmpty(), "Refusing to delete a nonempty resource group"))
                .then(Mono.defer(() -> groups.deleteAsync(config.group)))
                .then(awaitAbsent(() -> groups.getAsync(config.group), POLL_INTERVAL))));
        });
    }

    private Mono<Void> cleanupNamespace() {
        return step("namespace-cleanup-preflight", () -> ownedNamespace()
            .then(queues.listByNamespaceAsync(config.group, config.namespace).collectList())
            .flatMap(found -> {
                for (SBQueueInner queue : found) {
                    check(ownedQueues.contains(queue.name()) && queueId(queue.name()).equalsIgnoreCase(queue.id()),
                        "Unowned queue found; refusing namespace deletion");
                }
                return topics.listByNamespaceAsync(config.group, config.namespace).collectList()
                    .flatMap(foundTopics -> require(foundTopics.isEmpty(), "Unowned topic found"));
            }))
            .thenMany(Flux.fromIterable(Set.copyOf(ownedQueues)))
            .concatMap(name -> step(name + "-delete", () -> ownedNamespace()
                .then(absent(() -> queues.getAsync(config.group, config.namespace, name)))
                .flatMap(missing -> missing ? Mono.empty()
                    : queues.deleteAsync(config.group, config.namespace, name))
                .then(awaitAbsent(() -> queues.getAsync(config.group, config.namespace, name), POLL_INTERVAL))))
            .then(step("namespace-delete", () -> ownedNamespace()
                .then(queues.listByNamespaceAsync(config.group, config.namespace).collectList())
                .flatMap(found -> require(found.isEmpty(), "Namespace not empty"))
                .then(topics.listByNamespaceAsync(config.group, config.namespace).collectList())
                .flatMap(found -> require(found.isEmpty(), "Namespace not empty"))
                .then(Mono.defer(() -> namespaces.deleteAsync(config.group, config.namespace)))
                .then(awaitNamespaceAbsent(() -> namespaces.getByResourceGroupAsync(config.group, config.namespace),
                    POLL_INTERVAL))));
    }

    private Mono<Void> ownedGroup() {
        return identifyOperation("resource-group-get", groups.getAsync(config.group)
            .switchIfEmpty(Mono.error(new SafetyFailure("Resource group readback missing")))).doOnNext(value ->
            checkOwner(value.id(), value.tags(), config.groupId())).then();
    }

    private Mono<Void> ownedNamespace() {
        return ownedGroup().then(Mono.defer(() -> identifyOperation("namespace-get",
            namespaces.getByResourceGroupAsync(config.group, config.namespace)
                .switchIfEmpty(Mono.error(new SafetyFailure("Namespace readback missing"))))))
            .doOnNext(value -> checkOwner(value.id(), value.tags(), config.namespaceId())).then();
    }

    private void checkOwner(String id, Map<String, String> tags, String expectedId) {
        check(expectedId.equalsIgnoreCase(id) && tags != null && config.runId.equals(tags.get(OWNER)),
            "Ownership mismatch; refusing mutation");
    }

    private String queueId(String name) {
        return config.namespaceId() + "/queues/" + name;
    }

    static Mono<Boolean> absent(Supplier<? extends Mono<?>> get) {
        return Mono.defer(get).thenReturn(false).onErrorResume(ManagementException.class, error -> {
            // Authorization, transport and service errors must not be interpreted as absence.
            if (error.getResponse() != null && error.getResponse().getStatusCode() == 404
                && error.getValue() != null
                && error.getValue().getCode() != null
                && Set.of("ResourceNotFound", "ResourceGroupNotFound", "MessagingEntityNotFound", "EntityNotFound")
                    .contains(error.getValue().getCode())) {
                return Mono.just(true);
            }
            return Mono.error(error);
        });
    }

    static Mono<Void> awaitAbsent(Supplier<? extends Mono<?>> get, Duration interval) {
        return absent(get).flatMap(missing -> missing ? Mono.empty()
            : Mono.delay(interval).then(Mono.defer(() -> awaitAbsent(get, interval))));
    }

    static Mono<Boolean> absentNamespace(Supplier<? extends Mono<?>> get) {
        return absent(get).onErrorResume(ManagementException.class, error -> {
            // The RP uses this code for a namespace GET after its DELETE LRO has completed.
            if (error.getResponse() != null && error.getResponse().getStatusCode() == 404
                && error.getValue() != null && "NamespaceNotFound".equals(error.getValue().getCode())) {
                return Mono.just(true);
            }
            return Mono.error(error);
        });
    }

    static Mono<Void> awaitNamespaceAbsent(Supplier<? extends Mono<?>> get, Duration interval) {
        return absentNamespace(get).flatMap(missing -> missing ? Mono.empty()
            : Mono.delay(interval).then(Mono.defer(() -> awaitNamespaceAbsent(get, interval))));
    }

    private static Mono<Void> require(boolean condition, String reason) {
        return condition ? Mono.empty() : Mono.error(new SafetyFailure(reason));
    }

    private static void check(boolean condition, String reason) {
        if (!condition) {
            throw new SafetyFailure(reason);
        }
    }

    private static Mono<Void> step(String name, Supplier<Mono<Void>> work) {
        return Mono.defer(work).timeout(OPERATION_LIMIT).onErrorMap(error -> new StepFailure(name, error));
    }

    private static <T> Mono<T> identifyOperation(String name, Mono<T> work) {
        return work.onErrorMap(error -> new StepFailure(name, error));
    }

    static final class StepFailure extends RuntimeException {
        final String step;

        StepFailure(String step, Throwable cause) {
            super("Sample step failed", cause);
            this.step = step;
        }
    }

    static final class SafetyFailure extends IllegalStateException {
        SafetyFailure(String reason) {
            super(reason);
        }
    }
}
