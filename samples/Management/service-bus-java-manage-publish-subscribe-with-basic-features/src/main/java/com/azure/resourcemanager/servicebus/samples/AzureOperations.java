// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import com.azure.core.amqp.AmqpRetryOptions;
import com.azure.core.credential.TokenCredential;
import com.azure.core.exception.HttpResponseException;
import com.azure.core.http.netty.NettyAsyncHttpClientBuilder;
import com.azure.core.http.policy.HttpLogDetailLevel;
import com.azure.core.http.policy.RetryOptions;
import com.azure.core.http.policy.FixedDelayOptions;
import com.azure.core.management.AzureEnvironment;
import com.azure.core.management.Region;
import com.azure.core.management.profile.AzureProfile;
import com.azure.identity.DefaultAzureCredentialBuilder;
import com.azure.messaging.servicebus.ServiceBusClientBuilder;
import com.azure.messaging.servicebus.ServiceBusMessage;
import com.azure.messaging.servicebus.ServiceBusReceivedMessage;
import com.azure.messaging.servicebus.ServiceBusReceiverAsyncClient;
import com.azure.messaging.servicebus.ServiceBusSenderAsyncClient;
import com.azure.messaging.servicebus.models.ServiceBusReceiveMode;
import com.azure.resourcemanager.resources.ResourceManager;
import com.azure.resourcemanager.resources.models.ResourceGroup;
import com.azure.resourcemanager.samples.Utils;
import com.azure.resourcemanager.servicebus.ServiceBusManager;
import com.azure.resourcemanager.servicebus.fluent.models.RuleInner;
import com.azure.resourcemanager.servicebus.models.AuthorizationKeys;
import com.azure.resourcemanager.servicebus.models.KeyType;
import com.azure.resourcemanager.servicebus.models.NamespaceAuthorizationRule;
import com.azure.resourcemanager.servicebus.models.NamespaceSku;
import com.azure.resourcemanager.servicebus.models.ServiceBusNamespace;
import com.azure.resourcemanager.servicebus.models.ServiceBusSubscription;
import com.azure.resourcemanager.servicebus.models.Topic;
import java.io.PrintStream;
import java.net.URI;
import java.time.Duration;
import java.util.Arrays;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.Function;
import java.util.function.Supplier;
import reactor.core.publisher.Flux;
import reactor.core.publisher.Mono;
import reactor.core.publisher.Sinks;

import static com.azure.resourcemanager.servicebus.samples.ResourceLifecycle.await;
import static com.azure.resourcemanager.servicebus.samples.ResourceLifecycle.require;

final class AzureOperations implements ServiceBusPublishSubscribeBasic.Operations {
    static final String TOPIC = "orders";
    static final String FIRST = "first";
    static final String SECOND = "second";
    static final Duration MESSAGE_TIMEOUT = Duration.ofSeconds(60);
    static final Duration QUIET_PERIOD = Duration.ofSeconds(5);
    private final ServiceBusPublishSubscribeBasic.Configuration configuration;
    private final PrintStream out;
    private final ResourceManager resources;
    private final ServiceBusManager serviceBus;
    private final TokenCredential credential;
    private final String runId = UUID.randomUUID().toString();
    private final String groupName = "rg-sb-pubsub-" + runId;
    private final String namespaceName = "sb-pubsub-" + runId.replace("-", "");
    private final String namespaceId;
    private final ResourceLifecycle lifecycle;
    private final AccessCheckpoint accessCheckpoint;
    private ServiceBusNamespace namespace;
    private Topic topic;

    AzureOperations(ServiceBusPublishSubscribeBasic.Configuration configuration) {
        this.configuration = configuration;
        this.out = System.out;
        accessCheckpoint = new AccessCheckpoint(configuration.accessCheckpoint, out);
        AzureProfile profile = new AzureProfile(configuration.tenantId, configuration.subscriptionId,
            AzureEnvironment.AZURE);
        credential = new DefaultAzureCredentialBuilder().tenantId(configuration.tenantId).build();
        com.azure.core.http.HttpClient http = new NettyAsyncHttpClientBuilder()
            .connectTimeout(Duration.ofSeconds(10))
            .responseTimeout(Duration.ofSeconds(30))
            .readTimeout(Duration.ofSeconds(30))
            .writeTimeout(Duration.ofSeconds(30))
            .build();
        RetryOptions retry = new RetryOptions(new FixedDelayOptions(2, Duration.ofSeconds(1)));
        resources = ResourceManager.configure().withHttpClient(http).withRetryOptions(retry)
            .withLogLevel(HttpLogDetailLevel.NONE).authenticate(credential, profile)
            .withSubscription(configuration.subscriptionId);
        serviceBus = ServiceBusManager.configure().withHttpClient(http).withRetryOptions(retry)
            .withLogLevel(HttpLogDetailLevel.NONE).authenticate(credential, profile);
        String groupId = "/subscriptions/" + configuration.subscriptionId + "/resourceGroups/" + groupName;
        namespaceId = groupId + "/providers/Microsoft.ServiceBus/namespaces/" + namespaceName;
        lifecycle = new ResourceLifecycle(groupId, runId, out);
    }

    AzureOperations(ServiceBusPublishSubscribeBasic.Configuration configuration, ResourceManager resources,
        ServiceBusManager serviceBus, TokenCredential credential, PrintStream out) {
        this.configuration = configuration;
        this.resources = resources;
        this.serviceBus = serviceBus;
        this.credential = credential;
        this.out = out;
        accessCheckpoint = new AccessCheckpoint(configuration.accessCheckpoint, out);
        String groupId = "/subscriptions/" + configuration.subscriptionId + "/resourceGroups/" + groupName;
        namespaceId = groupId + "/providers/Microsoft.ServiceBus/namespaces/" + namespaceName;
        lifecycle = new ResourceLifecycle(groupId, runId, out);
    }

    @Override
    public void createResourceGroup() {
        lifecycle.create(() -> present(resources.resourceGroups().getByNameAsync(groupName)),
            () -> resources.resourceGroups().define(groupName).withRegion(configuration.region)
                .withTag(ResourceLifecycle.OWNER_TAG, runId).createAsync().map(this::owner),
            this::readOwner);
    }

    private String owner(ResourceGroup group) {
        return group.tags().getOrDefault(ResourceLifecycle.OWNER_TAG, "");
    }

    private Mono<String> readOwner() {
        return resources.resourceGroups().getByNameAsync(groupName).map(this::owner);
    }

    @Override
    public void createNamespace() {
        lifecycle.requireOwnership(this::readOwner);
        out.println("Planned namespace: " + namespaceId);
        require(Boolean.TRUE.equals(await(serviceBus.namespaces().checkNameAvailabilityAsync(namespaceName))
            .isAvailable()), "Namespace name collision; refusing adoption");
        namespace = await(serviceBus.namespaces().define(namespaceName)
            .withRegion(Region.fromName(configuration.region)).withExistingResourceGroup(groupName)
            .withSku(configuration.premium ? NamespaceSku.PREMIUM_CAPACITY1 : NamespaceSku.STANDARD)
            .withTag(ResourceLifecycle.OWNER_TAG, runId).createAsync());
        require(namespace != null && runId.equals(namespace.tags().get(ResourceLifecycle.OWNER_TAG)),
            "Namespace creation ownership was not confirmed");
        NamespaceSku expectedSku = configuration.premium ? NamespaceSku.PREMIUM_CAPACITY1 : NamespaceSku.STANDARD;
        require(expectedSku.name().equals(namespace.sku().name())
            && expectedSku.tier().equals(namespace.sku().tier())
            && expectedSku.capacity() == namespace.sku().capacity(), "Namespace SKU readback mismatch");
        require(namespaceId.equalsIgnoreCase(namespace.id()), "Namespace ID readback mismatch");
        out.println("Created namespace: " + namespace.id() + " SKU=" + namespace.sku().name());
        accessCheckpoint.awaitAccess(namespaceId);
    }

    @Override
    public void createTopicAndSubscriptions() {
        topic = await(namespace.topics().define(TOPIC).withSizeInMB(2048).createAsync());
        require(topic != null && topic.maxSizeInMB() == 2048, "Topic creation size mismatch");
        topic = await(namespace.topics().getByNameAsync(TOPIC));
        topic = await(topic.update().withNewSubscription(FIRST).withSizeInMB(3072).applyAsync());
        require(topic != null && topic.maxSizeInMB() == 3072, "Topic resize failed");
        ServiceBusSubscription second = await(topic.subscriptions().define(SECOND)
            .withDeleteOnIdleDurationInMinutes(10).createAsync());
        require(second != null && second.deleteOnIdleDurationInMinutes() == 10,
            "Second subscription idle timeout mismatch");
        out.println("Created topic (2048 -> 3072 MB), subscription via update, and second subscription (idle 10 min).");
    }

    @Override
    public void listAndVerifyEntities() {
        List<Topic> topics = await(namespace.topics().listAsync().collectList());
        require(topics.size() == 1 && TOPIC.equals(topics.get(0).name()), "Topic listing mismatch");
        List<ServiceBusSubscription> subscriptions = await(topic.subscriptions().listAsync().collectList());
        Set<String> names = new HashSet<>();
        for (ServiceBusSubscription subscription : subscriptions) {
            names.add(subscription.name());
            out.println("Subscription: " + subscription.id());
            List<RuleInner> rules = await(serviceBus.serviceClient().getRules()
                .listBySubscriptionsAsync(groupName, namespaceName, TOPIC, subscription.name()).collectList());
            verifyDefaultRule(rules);
        }
        require(subscriptions.size() == 2 && names.equals(new HashSet<>(Arrays.asList(FIRST, SECOND))),
            "Subscription listing mismatch");
        out.println("Listed one topic and two subscriptions; each has only the match-all $Default rule.");
    }

    static void verifyDefaultRule(List<RuleInner> rules) {
        require(rules.size() == 1 && "$Default".equals(rules.get(0).name())
            && rules.get(0).sqlFilter() != null
            && rules.get(0).sqlFilter().sqlExpression() != null
            && "1=1".equals(rules.get(0).sqlFilter().sqlExpression().replaceAll("\\s", ""))
            && (rules.get(0).action() == null || rules.get(0).action().sqlExpression() == null
                || rules.get(0).action().sqlExpression().isBlank()),
            "Expected only the unmodified match-all $Default rule");
    }

    @Override
    public void inspectAuthorization(boolean rotateKeys) {
        List<NamespaceAuthorizationRule> rules = await(namespace.authorizationRules().listAsync().collectList());
        for (NamespaceAuthorizationRule rule : rules) {
            out.println("Namespace authorization rule: " + rule.name() + " rights=" + rule.rights());
        }
        if (rotateKeys) {
            NamespaceAuthorizationRule root = rules.stream()
                .filter(rule -> "RootManageSharedAccessKey".equals(rule.name())).findFirst()
                .orElseThrow(() -> new IllegalStateException("Default namespace authorization rule absent"));
            AuthorizationKeys keys = await(root.getKeysAsync());
            require(keys != null, "Key retrieval did not return a result");
            Utils.print(keys, out);
            keys = await(root.regenerateKeyAsync(KeyType.SECONDARY_KEY));
            require(keys != null, "Key regeneration did not return a result");
            Utils.print(keys, out);
            out.println("Retrieved default keys and regenerated the secondary key; all values redacted.");
        } else {
            out.println("Key retrieval/regeneration skipped (requires --rotate-keys).");
        }
    }

    private ServiceBusClientBuilder messagingBuilder() {
        return messagingBuilder(namespace.fqdn(), credential);
    }

    static ServiceBusClientBuilder messagingBuilder(String namespaceEndpoint, TokenCredential credential) {
        require(namespaceEndpoint != null, "Namespace endpoint is missing");
        // The management SDK's fqdn() returns serviceBusEndpoint, an HTTPS URL, not an AMQP hostname.
        URI endpoint = URI.create(namespaceEndpoint);
        require("https".equalsIgnoreCase(endpoint.getScheme()) && endpoint.getHost() != null
            && (endpoint.getPort() == -1 || endpoint.getPort() == 443)
            && endpoint.getRawUserInfo() == null && endpoint.getRawQuery() == null
            && endpoint.getRawFragment() == null
            && ("".equals(endpoint.getRawPath()) || "/".equals(endpoint.getRawPath())),
            "Expected an HTTPS namespace endpoint without credentials, query, or entity path");
        return new ServiceBusClientBuilder().credential(endpoint.getHost(), credential)
            .retryOptions(new AmqpRetryOptions().setMaxRetries(2).setTryTimeout(Duration.ofSeconds(15)));
    }

    @Override
    public void sendAndVerifyFanOut() {
        String messageId = runId + "-hello";
        try (ServiceBusSenderAsyncClient sender = messagingBuilder().sender().topicName(TOPIC).buildAsyncClient();
            ServiceBusReceiverAsyncClient first = receiver(FIRST);
            ServiceBusReceiverAsyncClient second = receiver(SECOND)) {
            sender.sendMessage(new ServiceBusMessage("Hello World").setMessageId(messageId))
                .block(MESSAGE_TIMEOUT);
            verifyDelivery(first::receiveMessages, first::complete, messageId, MESSAGE_TIMEOUT, QUIET_PERIOD);
            verifyDelivery(second::receiveMessages, second::complete, messageId, MESSAGE_TIMEOUT, QUIET_PERIOD);
        }
        out.println("Verified Hello World fan-out and settlement on both subscriptions.");
    }

    private ServiceBusReceiverAsyncClient receiver(String name) {
        return messagingBuilder().receiver().topicName(TOPIC).subscriptionName(name)
            .receiveMode(ServiceBusReceiveMode.PEEK_LOCK).disableAutoComplete().prefetchCount(0)
            .maxAutoLockRenewDuration(Duration.ZERO).buildAsyncClient();
    }

    static void verifyMessage(ServiceBusReceivedMessage message, String expectedId) {
        require(message != null && expectedId.equals(message.getMessageId())
            && "Hello World".equals(message.getBody().toString()), "Message readback mismatch");
    }

    static void verifyDelivery(Supplier<Flux<ServiceBusReceivedMessage>> receive,
        Function<ServiceBusReceivedMessage, Mono<Void>> complete, String messageId,
        Duration timeout, Duration quietPeriod) {
        AtomicBoolean received = new AtomicBoolean();
        Sinks.One<Void> settled = Sinks.one();
        // Keep the receive link alive until settlement and the duplicate quiet window finish.
        receive.get().concatMap(message -> {
            require(received.compareAndSet(false, true), "Unexpected additional message on subscription");
            verifyMessage(message, messageId);
            return complete.apply(message)
                .doOnSuccess(ignored -> settled.emitEmpty(Sinks.EmitFailureHandler.FAIL_FAST))
                .thenReturn(message);
        }, 1).concatWith(Mono.error(new IllegalStateException("Receive ended before the quiet window completed")))
            .takeUntilOther(settled.asMono().then(Mono.delay(quietPeriod)))
            .then().block(timeout);
        require(received.get(), "Message readback mismatch");
    }

    @Override
    public void deleteSubscriptionsAndTopic() {
        requireNamespaceOwnership();
        topic = await(topic.update().withoutSubscription(FIRST).applyAsync());
        List<ServiceBusSubscription> remaining = await(topic.subscriptions().listAsync().collectList());
        require(remaining.size() == 1 && SECOND.equals(remaining.get(0).name()),
            "First subscription was not removed by topic update");
        await(topic.subscriptions().deleteByNameAsync(SECOND));
        require(await(topic.subscriptions().listAsync().collectList()).isEmpty(),
            "Second subscription was not removed");
        ResourceLifecycle.deleteAndConfirm(topic.id(), () -> namespace.topics().deleteByNameAsync(TOPIC),
            () -> present(namespace.topics().getByNameAsync(TOPIC)), ResourceLifecycle.CLEANUP_TIMEOUT,
            Duration.ofSeconds(2), out);
        out.println("Confirmed both subscriptions and topic deleted.");
    }

    @Override
    public void deleteNamespace() {
        accessCheckpoint.beforeCleanup();
        requireNamespaceOwnership();
        ResourceLifecycle.deleteAndConfirm(namespaceId, () -> serviceBus.namespaces().deleteByIdAsync(namespaceId),
            () -> present(serviceBus.namespaces().getByIdAsync(namespaceId)), ResourceLifecycle.CLEANUP_TIMEOUT,
            Duration.ofSeconds(2), out);
    }

    private void requireNamespaceOwnership() {
        lifecycle.requireOwnership(this::readOwner);
        ServiceBusNamespace current = await(serviceBus.namespaces().getByIdAsync(namespaceId));
        require(current != null && runId.equals(current.tags().get(ResourceLifecycle.OWNER_TAG)),
            "Namespace ownership changed; refusing mutation");
    }

    @Override
    public void cleanupResourceGroup() {
        accessCheckpoint.beforeCleanup();
        lifecycle.cleanup(this::readOwner, () -> verifyGroupContents()
            .then(resources.resourceGroups().deleteByNameAsync(groupName)),
            () -> present(resources.resourceGroups().getByNameAsync(groupName)), ResourceLifecycle.CLEANUP_TIMEOUT);
    }

    private Mono<Void> verifyGroupContents() {
        return resources.genericResources().listByResourceGroupAsync(groupName).collectList()
            .flatMap(contents -> {
                require(contents.stream().allMatch(resource -> namespaceId.equalsIgnoreCase(resource.id())),
                    "Unexpected resource in group; refusing deletion");
                return serviceBus.namespaces().getByIdAsync(namespaceId).doOnNext(current ->
                    require(runId.equals(current.tags().get(ResourceLifecycle.OWNER_TAG)),
                        "Namespace ownership changed; refusing deletion")).then()
                    .onErrorResume(HttpResponseException.class, failure ->
                        failure.getResponse() != null && failure.getResponse().getStatusCode() == 404
                            ? Mono.empty() : Mono.error(failure));
            });
    }

    static Mono<Boolean> present(Mono<?> lookup) {
        return lookup.map(ignored -> true).defaultIfEmpty(false).onErrorResume(HttpResponseException.class,
            failure -> failure.getResponse() != null && failure.getResponse().getStatusCode() == 404
                ? Mono.just(false) : Mono.error(failure));
    }
}
