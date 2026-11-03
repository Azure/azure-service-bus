package com.azure.samples.servicebus;

import com.azure.core.amqp.AmqpRetryOptions;
import com.azure.core.amqp.AmqpTransportType;
import com.azure.core.amqp.ProxyAuthenticationType;
import com.azure.core.amqp.ProxyOptions;
import com.azure.core.credential.TokenCredential;
import com.azure.identity.DefaultAzureCredentialBuilder;
import com.azure.messaging.servicebus.*;
import com.azure.messaging.servicebus.administration.ServiceBusAdministrationAsyncClient;
import com.azure.messaging.servicebus.administration.ServiceBusAdministrationClientBuilder;
import com.azure.messaging.servicebus.administration.models.CreateQueueOptions;
import com.azure.messaging.servicebus.administration.models.MessagingSku;
import com.azure.messaging.servicebus.models.SubQueue;

import java.net.InetSocketAddress;
import java.net.Proxy;
import java.time.Duration;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;
import java.util.Objects;
import java.util.UUID;
import java.util.function.BiConsumer;
import java.util.function.Consumer;
import java.util.function.Supplier;

public final class FeatureScenarios {
    private static final List<String> SCENARIOS = Arrays.asList(
        "request-reply", "duplicate-detection", "ttl", "autoforward", "partitioning");
    private final ServiceBusAdministrationAsyncClient admin;
    private final ServiceBusClientBuilder clients;
    private final Integer premiumPartitions;
    private final List<QueueResource> resources = new ArrayList<>();
    private final String prefix = "sample-" + UUID.randomUUID().toString().replace("-", "");

    private FeatureScenarios(String host, Integer premiumPartitions) {
        this.premiumPartitions = premiumPartitions;
        TokenCredential credential = new DefaultAzureCredentialBuilder().build();
        admin = new ServiceBusAdministrationClientBuilder().credential(host, credential).buildAsyncClient();
        clients = clientBuilder(host, credential, System.getenv("SERVICEBUS_PROXY_HOST"), System.getenv("SERVICEBUS_PROXY_PORT"));
    }

    public static void main(String[] args) {
        if (args.length == 0 || Arrays.equals(args, new String[] {"--help"})) {
            System.out.println("Scenarios: " + String.join(", ", SCENARIOS));
            System.out.println("Usage: java -jar <jar> <scenario> --allow-entity-management");
            System.out.println("Set SERVICEBUS_NAMESPACE. Optional SERVICEBUS_PROXY_HOST and SERVICEBUS_PROXY_PORT enable AMQP over WebSockets.");
            System.out.println("For Premium, set SERVICEBUS_PREMIUM_PARTITIONS to its verified namespace partition count (1, 2 or 4). Leave it unset for Standard.");
            return;
        }
        if (args.length != 2 || !SCENARIOS.contains(args[0]) || !"--allow-entity-management".equals(args[1])) {
            throw new IllegalArgumentException("Choose a scenario and explicitly allow temporary queue creation/deletion. Use --help.");
        }
        String host = System.getenv("SERVICEBUS_NAMESPACE");
        if (host == null || host.isBlank()) {
            throw new IllegalArgumentException("Set SERVICEBUS_NAMESPACE to <namespace>.servicebus.windows.net.");
        }
        Integer premiumPartitions = parsePremiumPartitions(System.getenv("SERVICEBUS_PREMIUM_PARTITIONS"));
        new FeatureScenarios(host, premiumPartitions).run(args[0]);
    }

    static ServiceBusClientBuilder clientBuilder(String host, TokenCredential credential, String proxyHost, String proxyPort) {
        ServiceBusClientBuilder builder = new ServiceBusClientBuilder().credential(host, credential)
            .retryOptions(new AmqpRetryOptions().setTryTimeout(Duration.ofSeconds(15)).setMaxRetries(2));
        if (proxyHost != null && !proxyHost.isBlank()) {
            if (proxyPort == null || !proxyPort.matches("[0-9]+")) {
                throw new IllegalArgumentException("Set SERVICEBUS_PROXY_PORT to a numeric port.");
            }
            int port = Integer.parseInt(proxyPort);
            if (port < 1 || port > 65535) {
                throw new IllegalArgumentException("Proxy port must be between 1 and 65535.");
            }
            Proxy proxy = new Proxy(Proxy.Type.HTTP, new InetSocketAddress(proxyHost, port));
            builder.transportType(AmqpTransportType.AMQP_WEB_SOCKETS)
                .proxyOptions(new ProxyOptions(ProxyAuthenticationType.NONE, proxy, null, null));
        } else if (proxyPort != null) {
            throw new IllegalArgumentException("SERVICEBUS_PROXY_PORT requires SERVICEBUS_PROXY_HOST.");
        }
        return builder;
    }

    private void run(String scenario) {
        MessagingSku sku = Objects.requireNonNull(admin.getNamespaceProperties().block(Duration.ofSeconds(45)),
            "Namespace lookup returned no data.").getMessagingSku();
        boolean partitionQueues = partitionQueues(sku, scenario, premiumPartitions);
        RuntimeException scenarioFailure = null;
        try {
            switch (scenario) {
                case "request-reply":
                    requestReply(create("requests", queueOptions(false, false, partitionQueues, false)),
                        create("replies", queueOptions(true, false, partitionQueues, false)));
                    break;
                case "duplicate-detection":
                    duplicateDetection(create("dedup", queueOptions(false, true, partitionQueues, false)));
                    break;
                case "ttl":
                    expiry(create("expiry", queueOptions(false, false, partitionQueues, true)));
                    break;
                case "autoforward":
                    String destination = create("destination", queueOptions(false, false, partitionQueues, false));
                    String source = create("source", forwardingOptions(destination, partitionQueues));
                    autoForward(source, destination);
                    break;
                case "partitioning":
                    System.out.println(MessagingSku.STANDARD.equals(sku) ? "Standard: enabling entity-level partitioning at creation."
                        : "Premium: matching namespace partition count " + premiumPartitions + "; EnablePartitioning="
                            + partitionQueues + ". Namespace topology is unchanged.");
                    partitioning(create("partitioned", queueOptions(true, false, partitionQueues, false)));
                    break;
                default:
                    throw new IllegalArgumentException("Unknown scenario.");
            }
        } catch (RuntimeException failure) {
            scenarioFailure = failure;
            throw failure;
        } finally {
            cleanup(resources, (queue, remaining) -> admin.deleteQueue(queue).block(remaining),
                scenarioFailure, Duration.ofMinutes(2));
        }
    }

    static Integer parsePremiumPartitions(String value) {
        if (value == null) return null;
        switch (value) {
            case "1": return 1;
            case "2": return 2;
            case "4": return 4;
            default: throw new IllegalArgumentException("SERVICEBUS_PREMIUM_PARTITIONS must be 1, 2 or 4.");
        }
    }

    static boolean partitionQueues(MessagingSku sku, String scenario, Integer premiumPartitions) {
        if (MessagingSku.PREMIUM.equals(sku)) {
            if (premiumPartitions == null || !Arrays.asList(1, 2, 4).contains(premiumPartitions)) {
                throw new IllegalArgumentException("Set SERVICEBUS_PREMIUM_PARTITIONS to the Premium namespace's verified partition count (1, 2 or 4).");
            }
            return premiumPartitions > 1;
        }
        if (!MessagingSku.STANDARD.equals(sku)) {
            throw new IllegalArgumentException("Use Standard or Premium, not Basic.");
        }
        if (premiumPartitions != null) {
            throw new IllegalArgumentException("Leave SERVICEBUS_PREMIUM_PARTITIONS unset for Standard.");
        }
        return "partitioning".equals(scenario);
    }

    static CreateQueueOptions queueOptions(boolean sessions, boolean duplicates, boolean partitioned, boolean expiry) {
        return new CreateQueueOptions().setSessionRequired(sessions).setDuplicateDetectionRequired(duplicates)
            .setDuplicateDetectionHistoryTimeWindow(Duration.ofMinutes(10)).setPartitioningEnabled(partitioned)
            .setDeadLetteringOnMessageExpiration(expiry).setDefaultMessageTimeToLive(Duration.ofMinutes(2))
            .setAutoDeleteOnIdle(Duration.ofMinutes(5));
    }

    static CreateQueueOptions forwardingOptions(String destination, boolean partitioned) {
        return queueOptions(false, false, partitioned, false).setForwardTo(destination);
    }

    private String create(String suffix, CreateQueueOptions options) {
        String name = prefix + "-" + suffix;
        createOwned(resources, new QueueResource(name, options.getForwardTo()), () ->
            Objects.requireNonNull(admin.createQueue(name, options).block(Duration.ofSeconds(45)), "Queue creation returned no data."));
        return name;
    }

    enum QueueState { UNCONFIRMED, CREATED, DELETED }

    static final class QueueResource {
        final String name;
        final String forwardTo;
        QueueState state = QueueState.UNCONFIRMED;

        QueueResource(String name, String forwardTo) {
            this.name = name;
            this.forwardTo = forwardTo;
        }
    }

    static void createOwned(List<QueueResource> resources, QueueResource queue, Runnable create) {
        resources.add(queue);
        System.out.println("Creating " + queue.name + "; retain this name if the response cannot be confirmed.");
        create.run();
        queue.state = QueueState.CREATED;
        System.out.println("Created " + queue.name);
    }

    static void cleanup(List<QueueResource> resources, BiConsumer<String, Duration> delete,
                        RuntimeException scenarioFailure, Duration budget) {
        long deadline = System.nanoTime() + budget.toNanos();
        List<QueueResource> reverse = new ArrayList<>(resources);
        Collections.reverse(reverse);
        List<RuntimeException> failures = new ArrayList<>();
        for (QueueResource queue : reverse) {
            if (queue.state == QueueState.DELETED) continue;
            try {
                if (queue.state == QueueState.UNCONFIRMED) {
                    throw new IllegalStateException("Creation outcome unconfirmed; inspect ownership before deletion: " + queue.name);
                }
                if (resources.stream().anyMatch(source -> source.state != QueueState.DELETED && queue.name.equals(source.forwardTo))) {
                    throw new IllegalStateException("Retaining forwarding destination because source deletion is unconfirmed: " + queue.name);
                }
                long remaining = deadline - System.nanoTime();
                if (remaining <= 0) throw new IllegalStateException("Cleanup deadline expired for " + queue.name);
                System.out.println("Deleting only this run's queue: " + queue.name);
                delete.accept(queue.name, Duration.ofNanos(remaining));
                queue.state = QueueState.DELETED;
            } catch (RuntimeException failure) {
                System.err.println("Cleanup not confirmed for " + queue.name + ": " + failure.getMessage());
                failures.add(failure);
            }
        }
        if (!failures.isEmpty()) {
            IllegalStateException incomplete = new IllegalStateException("Cleanup incomplete; inspect every reported queue.", scenarioFailure);
            failures.forEach(incomplete::addSuppressed);
            throw incomplete;
        }
    }

    static ServiceBusMessage request(String id, String replies, String session) {
        return new ServiceBusMessage("What is my order status?").setMessageId(id).setReplyTo(replies).setReplyToSessionId(session);
    }

    static ServiceBusMessage reply(String requestId, String session) {
        return new ServiceBusMessage("Order accepted").setMessageId("reply-" + requestId)
            .setCorrelationId(requestId).setSessionId(session);
    }

    static ServiceBusMessage businessMessage(String id) {
        return new ServiceBusMessage("Business operation").setMessageId(id);
    }

    static ServiceBusMessage expiringMessage() {
        return new ServiceBusMessage("Time-sensitive work").setMessageId(UUID.randomUUID().toString()).setTimeToLive(Duration.ofSeconds(3));
    }

    static ServiceBusMessage partitionMessage(String session, int index) {
        return new ServiceBusMessage("Session item " + index).setMessageId(session + "-" + index)
            .setSessionId(session).setPartitionKey(session);
    }

    private void requestReply(String requests, String replies) {
        String id = UUID.randomUUID().toString();
        String session = UUID.randomUUID().toString();
        try (ServiceBusSenderClient requester = clients.sender().queueName(requests).buildClient();
             ServiceBusReceiverClient responder = clients.receiver().queueName(requests).buildClient();
             ServiceBusSenderClient replySender = clients.sender().queueName(replies).buildClient()) {
            requester.sendMessage(request(id, replies, session));
            ServiceBusReceivedMessage received = receiveRequired(responder);
            check(replies.equals(received.getReplyTo()) && session.equals(received.getReplyToSessionId()), "Unexpected reply destination/session.");
            replyAndComplete(received.getMessageId(), received.getReplyToSessionId(), replySender::sendMessage,
                () -> responder.complete(received));
            try (ServiceBusSessionReceiverClient sessions = clients.sessionReceiver().queueName(replies).buildClient();
                 ServiceBusReceiverClient receiver = sessions.acceptSession(session)) {
                ServiceBusReceivedMessage response = receiveRequired(receiver);
                validateReply(id, session, response.getCorrelationId(), response.getSessionId());
                System.out.println("Matched response to " + id + ": " + response.getBody());
                receiver.complete(response);
            }
        }
    }

    static void replyAndComplete(String requestId, String session, Consumer<ServiceBusMessage> send, Runnable complete) {
        send.accept(reply(requestId, session));
        complete.run();
    }

    static void validateReply(String expectedId, String expectedSession, String correlationId, String session) {
        check(expectedId.equals(correlationId) && expectedSession.equals(session), "Reply correlation/session mismatch.");
    }

    private void duplicateDetection(String queue) {
        try (ServiceBusSenderClient sender = clients.sender().queueName(queue).buildClient();
             ServiceBusReceiverClient receiver = clients.receiver().queueName(queue).buildClient()) {
            sender.sendMessage(businessMessage("order-42"));
            sender.sendMessage(businessMessage("order-42"));
            ServiceBusReceivedMessage first = receiveRequired(receiver);
            check("order-42".equals(first.getMessageId()), "Unexpected first ID.");
            receiver.complete(first);
            check(!receiver.receiveMessages(1, Duration.ofSeconds(10)).iterator().hasNext(), "Duplicate send produced another message.");
            sender.sendMessage(businessMessage("order-43"));
            ServiceBusReceivedMessage different = receiveRequired(receiver);
            check("order-43".equals(different.getMessageId()), "A different business ID should be accepted.");
            receiver.complete(different);
            System.out.println("One order-42 observed; order-43 accepted. Receive redelivery still requires idempotent processing.");
        }
    }

    private void expiry(String queue) {
        try (ServiceBusSenderClient sender = clients.sender().queueName(queue).buildClient();
             ServiceBusReceiverClient active = clients.receiver().queueName(queue).buildClient();
             ServiceBusReceiverClient deadLetters = clients.receiver().queueName(queue).subQueue(SubQueue.DEAD_LETTER_QUEUE).buildClient()) {
            ServiceBusMessage message = expiringMessage();
            sender.sendMessage(message);
            sleep(message.getTimeToLive().plusSeconds(2));
            check(!active.receiveMessages(1, Duration.ofSeconds(2)).iterator().hasNext(), "Expired work was delivered on the active queue.");
            ServiceBusReceivedMessage expired = receiveRequired(deadLetters);
            check(message.getMessageId().equals(expired.getMessageId()) && "TTLExpiredException".equals(expired.getDeadLetterReason()),
                "Expected this run's expired message in the DLQ.");
            System.out.println("Expired: " + expired.getMessageId() + "; " + expired.getDeadLetterReason() + "; " + expired.getDeadLetterErrorDescription());
            System.out.println("Explicit demo decision: discard this expired test work, do not replay it.");
            deadLetters.complete(expired);
        }
    }

    private void autoForward(String source, String destination) {
        try (ServiceBusSenderClient sender = clients.sender().queueName(source).buildClient();
             ServiceBusReceiverClient receiver = clients.receiver().queueName(destination).buildClient()) {
            sender.sendMessage(businessMessage("forwarded-order"));
            ServiceBusReceivedMessage forwarded = receiveRequired(receiver);
            check("forwarded-order".equals(forwarded.getMessageId()), "Unexpected forwarded message.");
            receiver.complete(forwarded);
            System.out.println("Received at destination without creating a receiver on the forwarding source.");
        }
    }

    private void partitioning(String queue) {
        String session = "order-session";
        try (ServiceBusSenderClient sender = clients.sender().queueName(queue).buildClient()) {
            ServiceBusTransactionContext transaction = sender.createTransaction();
            transact(() -> sender.sendMessage(partitionMessage(session, 1), transaction),
                () -> sender.sendMessage(partitionMessage(session, 2), transaction),
                () -> sender.commitTransaction(transaction), () -> sender.rollbackTransaction(transaction));
        }
        try (ServiceBusSessionReceiverClient sessions = clients.sessionReceiver().queueName(queue).buildClient();
             ServiceBusReceiverClient receiver = sessions.acceptSession(session)) {
            for (int index = 1; index <= 2; index++) {
                ServiceBusReceivedMessage message = receiveRequired(receiver);
                check(session.equals(message.getSessionId()) && (session + "-" + index).equals(message.getMessageId()), "Session ordering/key mismatch.");
                receiver.complete(message);
            }
        }
        System.out.println("Received both committed messages in session order. Transaction sends used one matching SessionId/PartitionKey.");
    }

    static void transact(Runnable first, Runnable second, Runnable commit, Runnable rollback) {
        try {
            first.run();
            second.run();
            commit.run();
        } catch (RuntimeException failure) {
            try { rollback.run(); }
            catch (RuntimeException rollbackFailure) { failure.addSuppressed(rollbackFailure); }
            throw failure;
        }
    }

    private static ServiceBusReceivedMessage receiveRequired(ServiceBusReceiverClient receiver) {
        return receiveRequired(() -> receiver.receiveMessages(1, Duration.ofSeconds(5)), Duration.ofMinutes(2));
    }

    static ServiceBusReceivedMessage receiveRequired(Supplier<Iterable<ServiceBusReceivedMessage>> receive, Duration budget) {
        long deadline = System.nanoTime() + budget.toNanos();
        while (System.nanoTime() < deadline) {
            for (ServiceBusReceivedMessage message : receive.get()) {
                return message;
            }
        }
        throw new IllegalStateException("Timed out waiting for the expected message.");
    }

    private static void sleep(Duration duration) {
        try {
            Thread.sleep(duration.toMillis());
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("Interrupted while waiting for message expiry.", e);
        }
    }

    private static void check(boolean condition, String message) {
        if (!condition) {
            throw new IllegalStateException(message);
        }
    }
}
