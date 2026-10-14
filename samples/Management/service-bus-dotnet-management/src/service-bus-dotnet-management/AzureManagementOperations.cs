// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Azure;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.ServiceBus;

namespace AzureServiceBusDotNetManagement;

public sealed class AzureManagementOperations : IManagementOperations
{
    private readonly AppOptions options;
    private readonly ResourcePlan plan;
    private readonly TokenCredential credential;
    private readonly SubscriptionResource subscription;
    private readonly ResourceGroupResource group;
    private readonly ServiceBusNamespaceResource serviceBusNamespace;
    private readonly ServiceBusAdministrationClient administration;

    public AzureManagementOperations(AppOptions options, ResourcePlan plan, TokenCredential credential,
        ArmClientOptions? armOptions = null, ServiceBusAdministrationClientOptions? administrationOptions = null)
    {
        this.options = options;
        this.plan = plan;
        this.credential = credential;
        armOptions ??= new ArmClientOptions();
        administrationOptions ??= new ServiceBusAdministrationClientOptions();
        // An automatic retry of a PUT could obscure whether this process created the resource.
        armOptions.Retry.MaxRetries = 0;
        administrationOptions.Retry.MaxRetries = 0;
        armOptions.Diagnostics.IsLoggingContentEnabled = false;
        administrationOptions.Diagnostics.IsLoggingContentEnabled = false;
        var arm = new ArmClient(credential, options.SubscriptionId, armOptions);
        subscription = arm.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(options.SubscriptionId));
        group = arm.GetResourceGroupResource(new ResourceIdentifier(plan.Steps[0].Id));
        serviceBusNamespace = arm.GetServiceBusNamespaceResource(new ResourceIdentifier(plan.Steps[1].Id));
        administration = new ServiceBusAdministrationClient(plan.FullyQualifiedNamespace, credential, administrationOptions);
    }

    public async Task<bool> ExistsAsync(ResourceStep resource, CancellationToken cancellationToken) =>
        resource.Kind switch
        {
            ResourceKind.ResourceGroup => (await subscription.GetResourceGroups().ExistsAsync(plan.ResourceGroupName, cancellationToken)).Value,
            ResourceKind.Namespace => (await group.GetServiceBusNamespaces().ExistsAsync(plan.NamespaceName, cancellationToken)).Value,
            ResourceKind.Queue => (await administration.QueueExistsAsync(ResourcePlan.QueueName, cancellationToken)).Value,
            ResourceKind.Topic => (await administration.TopicExistsAsync(ResourcePlan.TopicName, cancellationToken)).Value,
            ResourceKind.Subscription => (await administration.SubscriptionExistsAsync(
                ResourcePlan.TopicName, ResourcePlan.SubscriptionName, cancellationToken)).Value,
            _ => throw new SampleException("Unknown resource kind.")
        };

    public async Task CreateAsync(ResourceStep resource, CancellationToken cancellationToken)
    {
        switch (resource.Kind)
        {
            case ResourceKind.ResourceGroup:
                var data = new ResourceGroupData(new AzureLocation(options.DataCenterLocation));
                data.Tags.Add(ResourcePlan.OwnershipTag, plan.RunId);
                var groupOperation = await subscription.GetResourceGroups().CreateOrUpdateAsync(
                    WaitUntil.Started, plan.ResourceGroupName, data, cancellationToken);
                RequireCreated(groupOperation.GetRawResponse());
                var createdGroup = (await groupOperation.WaitForCompletionAsync(cancellationToken)).Value;
                RequireTag(createdGroup.Data.Tags);
                break;
            case ResourceKind.Namespace:
                var namespaceOperation = await group.GetServiceBusNamespaces().CreateOrUpdateAsync(
                    WaitUntil.Started, plan.NamespaceName, options.CreateNamespaceData(plan.RunId), cancellationToken);
                RequireCreated(namespaceOperation.GetRawResponse());
                var createdNamespace = (await namespaceOperation.WaitForCompletionAsync(cancellationToken)).Value;
                RequireTag(createdNamespace.Data.Tags);
                break;
            case ResourceKind.Queue:
                var queue = await administration.CreateQueueAsync(plan.QueueOptions(options), cancellationToken);
                RequireCreated(queue.GetRawResponse());
                RequireMetadata(queue.Value.UserMetadata);
                break;
            case ResourceKind.Topic:
                var topic = await administration.CreateTopicAsync(plan.TopicOptions(options), cancellationToken);
                RequireCreated(topic.GetRawResponse());
                RequireMetadata(topic.Value.UserMetadata);
                break;
            case ResourceKind.Subscription:
                var created = await administration.CreateSubscriptionAsync(
                    plan.SubscriptionOptions(), ResourcePlan.InitialRule(), cancellationToken);
                RequireCreated(created.GetRawResponse());
                RequireMetadata(created.Value.UserMetadata);
                break;
            default:
                throw new SampleException("Unknown resource kind.");
        }
    }

    public async Task VerifyAsync(ResourceStep resource, CancellationToken cancellationToken)
    {
        switch (resource.Kind)
        {
            case ResourceKind.ResourceGroup:
                var groupData = (await group.GetAsync(cancellationToken)).Value.Data;
                RequireTag(groupData.Tags);
                if (!groupData.Location.Equals(new AzureLocation(options.DataCenterLocation)))
                    throw new SampleException("Resource-group location readback does not match the configured location.");
                break;
            case ResourceKind.Namespace:
                var ns = (await serviceBusNamespace.GetAsync(cancellationToken)).Value.Data;
                RequireTag(ns.Tags);
                if (!ns.Location.Equals(new AzureLocation(options.DataCenterLocation)) ||
                    ns.Sku.Name.ToString() != options.ServiceBusSku ||
                    ns.Sku.Tier?.ToString() != options.ServiceBusSku || ns.DisableLocalAuth != true ||
                    (options.ServiceBusSku == "Premium" &&
                     (ns.Sku.Capacity != options.PremiumCapacity || ns.PremiumMessagingPartitions != options.PremiumMessagingPartitions)))
                    throw new SampleException("Namespace readback does not match the configured location, tier, capacity, partitions or local-auth setting.");
                break;
            case ResourceKind.Queue:
                var queue = (await administration.GetQueueAsync(ResourcePlan.QueueName, cancellationToken)).Value;
                RequireMetadata(queue.UserMetadata);
                if (queue.EnablePartitioning != options.EnablePartitioning)
                    throw new SampleException("Queue partitioning readback does not match the tier.");
                break;
            case ResourceKind.Topic:
                var topic = (await administration.GetTopicAsync(ResourcePlan.TopicName, cancellationToken)).Value;
                RequireMetadata(topic.UserMetadata);
                if (topic.EnablePartitioning != options.EnablePartitioning)
                    throw new SampleException("Topic partitioning readback does not match the tier.");
                break;
            case ResourceKind.Subscription:
                var sub = (await administration.GetSubscriptionAsync(
                    ResourcePlan.TopicName, ResourcePlan.SubscriptionName, cancellationToken)).Value;
                RequireMetadata(sub.UserMetadata);
                if (sub.MaxDeliveryCount != 10)
                    throw new SampleException("Subscription MaxDeliveryCount readback must be 10.");
                ResourcePlan.VerifyRules(await GetRulesAsync(cancellationToken));
                break;
        }
    }

    public async Task DeleteOwnedAsync(ResourceStep resource, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(resource, cancellationToken))
            return; // A successful absence read confirms deletion; a failed read is never treated as absence.
        await VerifyAsync(resource, cancellationToken);
        switch (resource.Kind)
        {
            case ResourceKind.Subscription:
                // VerifyAsync proved this is the sole initial rule of our owned subscription.
                await administration.DeleteRuleAsync(ResourcePlan.TopicName, ResourcePlan.SubscriptionName,
                    ResourcePlan.RuleName, cancellationToken);
                if ((await GetRulesAsync(cancellationToken)).Count != 0)
                    throw new SampleException("Subscription cleanup refused: unexpected rules remain.");
                await administration.DeleteSubscriptionAsync(ResourcePlan.TopicName, ResourcePlan.SubscriptionName, cancellationToken);
                break;
            case ResourceKind.Topic:
                await foreach (var _ in administration.GetSubscriptionsAsync(ResourcePlan.TopicName, cancellationToken))
                    throw new SampleException("Topic cleanup refused: unexpected subscriptions remain.");
                await administration.DeleteTopicAsync(ResourcePlan.TopicName, cancellationToken);
                break;
            case ResourceKind.Queue:
                await administration.DeleteQueueAsync(ResourcePlan.QueueName, cancellationToken);
                break;
            case ResourceKind.Namespace:
                await foreach (var _ in administration.GetQueuesAsync(cancellationToken))
                    throw new SampleException("Namespace cleanup refused: unexpected queues remain.");
                await foreach (var _ in administration.GetTopicsAsync(cancellationToken))
                    throw new SampleException("Namespace cleanup refused: unexpected topics remain.");
                await serviceBusNamespace.DeleteAsync(WaitUntil.Completed, cancellationToken);
                break;
            case ResourceKind.ResourceGroup:
                await foreach (var _ in group.GetGenericResourcesAsync(cancellationToken: cancellationToken))
                    throw new SampleException("Resource-group cleanup refused: unexpected resources remain.");
                await group.DeleteAsync(WaitUntil.Completed, cancellationToken: cancellationToken);
                break;
        }
        if (await ExistsAsync(resource, cancellationToken))
            throw new SampleException("Deletion is not confirmed by an absence read; resource remains in the inventory.");
    }

    public async Task VerifyRoutingAsync(CancellationToken cancellationToken)
    {
        await using var client = new ServiceBusClient(plan.FullyQualifiedNamespace, credential,
            new ServiceBusClientOptions { RetryOptions = new ServiceBusRetryOptions { MaxRetries = 0, TryTimeout = TimeSpan.FromSeconds(30) } });
        await using var sender = client.CreateSender(ResourcePlan.TopicName);
        await using var receiver = client.CreateReceiver(ResourcePlan.TopicName, ResourcePlan.SubscriptionName);
        var messages = RoutingMessages(plan.RunId);
        await sender.SendMessagesAsync(messages, cancellationToken);
        var received = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(30), cancellationToken);
        if (received?.MessageId != messages[0].MessageId)
            throw new SampleException("Routing verification failed: expected the matching message.");
        await receiver.CompleteMessageAsync(received, cancellationToken);
        if (await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10), cancellationToken) is not null)
            throw new SampleException("Routing verification failed: an extra, nonmatching or missing-property message was delivered.");
    }

    public static ServiceBusMessage[] RoutingMessages(string runId)
    {
        var matching = new ServiceBusMessage("matching") { MessageId = $"{runId}-match", TimeToLive = TimeSpan.FromMinutes(2) };
        matching.ApplicationProperties["myproperty"] = "test";
        var nonmatching = new ServiceBusMessage("nonmatching") { MessageId = $"{runId}-nonmatch", TimeToLive = TimeSpan.FromMinutes(2) };
        nonmatching.ApplicationProperties["myproperty"] = "different";
        var missing = new ServiceBusMessage("missing property") { MessageId = $"{runId}-missing", TimeToLive = TimeSpan.FromMinutes(2) };
        return [matching, nonmatching, missing];
    }

    private async Task<List<RuleProperties>> GetRulesAsync(CancellationToken cancellationToken)
    {
        var rules = new List<RuleProperties>();
        await foreach (var rule in administration.GetRulesAsync(ResourcePlan.TopicName, ResourcePlan.SubscriptionName, cancellationToken))
            rules.Add(rule);
        return rules;
    }

    private static void RequireCreated(Response response)
    {
        if (response.Status != 201)
            throw new SampleException("Creation is ambiguous: expected an initial HTTP 201. No ownership or cleanup authority was granted.");
    }

    private void RequireTag(IDictionary<string, string> tags)
    {
        if (!tags.TryGetValue(ResourcePlan.OwnershipTag, out var value) || value != plan.RunId)
            throw new SampleException("Ownership tag does not match this run; refusing adoption or deletion.");
    }

    private void RequireMetadata(string metadata)
    {
        if (metadata != plan.RunId)
            throw new SampleException("Entity metadata does not match this run; refusing adoption or deletion.");
    }
}
