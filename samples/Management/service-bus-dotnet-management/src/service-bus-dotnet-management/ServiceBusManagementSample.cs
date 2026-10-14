// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace AzureServiceBusDotNetManagement;

public enum ResourceKind { ResourceGroup, Namespace, Queue, Topic, Subscription }

public sealed record ResourceStep(ResourceKind Kind, string Id, string? ParentId);

public sealed class ResourcePlan
{
    public const string OwnershipTag = "service-bus-management-run";
    public const string QueueName = "queue1";
    public const string TopicName = "topic1";
    public const string SubscriptionName = "topic1";
    public const string RuleName = "rule1";
    public const string SqlExpression = "myproperty='test'";
    public string RunId { get; } = Guid.NewGuid().ToString("N");
    public string ResourceGroupName { get; }
    public string NamespaceName { get; }
    public string FullyQualifiedNamespace => $"{NamespaceName}.servicebus.windows.net";
    public IReadOnlyList<ResourceStep> Steps { get; }

    public ResourcePlan(AppOptions options)
    {
        options.Validate();
        ResourceGroupName = $"{options.ResourceGroupPrefix}-{RunId}";
        NamespaceName = $"{options.NamespacePrefix}-{RunId}";
        var group = $"/subscriptions/{options.SubscriptionId}/resourceGroups/{ResourceGroupName}";
        var ns = $"{group}/providers/Microsoft.ServiceBus/namespaces/{NamespaceName}";
        var topic = $"{ns}/topics/{TopicName}";
        Steps =
        [
            new(ResourceKind.ResourceGroup, group, null),
            new(ResourceKind.Namespace, ns, group),
            new(ResourceKind.Queue, $"{ns}/queues/{QueueName}", ns),
            new(ResourceKind.Topic, topic, ns),
            new(ResourceKind.Subscription, $"{topic}/subscriptions/{SubscriptionName}", topic)
        ];
    }

    public CreateQueueOptions QueueOptions(AppOptions options) => new(QueueName)
    {
        EnablePartitioning = options.EnablePartitioning,
        UserMetadata = RunId
    };
    public CreateTopicOptions TopicOptions(AppOptions options) => new(TopicName)
    {
        EnablePartitioning = options.EnablePartitioning,
        UserMetadata = RunId
    };
    public CreateSubscriptionOptions SubscriptionOptions() => new(TopicName, SubscriptionName)
    {
        MaxDeliveryCount = 10,
        UserMetadata = RunId
    };
    public static CreateRuleOptions InitialRule() => new(RuleName, new SqlRuleFilter(SqlExpression));

    public static void VerifyRules(IReadOnlyList<RuleProperties> rules)
    {
        if (rules.Count != 1 || rules[0].Name != RuleName ||
            rules[0].Filter is not SqlRuleFilter sql || sql.SqlExpression != SqlExpression ||
            rules[0].Action is not null)
            throw new SampleException("Rule readback must contain only rule1 with the exact SQL filter, no action and no $Default rule.");
    }
}

public sealed class ResourceRecord(ResourceStep step)
{
    public ResourceStep Resource { get; } = step;
    public string State { get; set; } = "planned";
    public bool Owned { get; set; }
    public string? Error { get; set; }
}

public sealed class RunReport(ResourcePlan plan)
{
    public string RunId { get; } = plan.RunId;
    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = "planned";
    public string RuleId { get; } = $"{plan.Steps[^1].Id}/rules/{ResourcePlan.RuleName}";
    public string RuleCreation { get; } = "Atomic initial rule of the subscription; ownership follows confirmed subscription creation.";
    public List<ResourceRecord> Resources { get; } = plan.Steps.Select(step => new ResourceRecord(step)).ToList();
    public List<string> Failures { get; } = [];
    public bool Success => Failures.Count == 0 && Status is ("created-and-verified" or "created-verified-and-deleted");
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public interface IManagementOperations
{
    Task<bool> ExistsAsync(ResourceStep resource, CancellationToken cancellationToken);
    Task CreateAsync(ResourceStep resource, CancellationToken cancellationToken);
    Task VerifyAsync(ResourceStep resource, CancellationToken cancellationToken);
    Task DeleteOwnedAsync(ResourceStep resource, CancellationToken cancellationToken);
    Task VerifyRoutingAsync(CancellationToken cancellationToken);
}

public static class SafeError
{
    // SDK exception messages can contain headers, claims, tokens or external command output.
    public static string Describe(Exception exception) => exception switch
    {
        SampleException => exception.Message,
        OperationCanceledException => "Operation cancelled or timed out; service state may be unknown.",
        AuthenticationFailedException => "Authentication failed for the explicitly selected credential.",
        RequestFailedException request => $"Azure request failed (HTTP {request.Status}).",
        ServiceBusException bus => $"Service Bus request failed ({bus.Reason}).",
        _ => $"Operation failed ({exception.GetType().Name}); exception text suppressed."
    };

    public static bool IsCollision(Exception exception) =>
        exception is ServiceBusException { Reason: ServiceBusFailureReason.MessagingEntityAlreadyExists } ||
        exception is RequestFailedException { Status: 409 or 412 };
}

public static class ServiceBusManagementSample
{
    public static async Task<RunReport> RunAsync(
        ResourcePlan plan, IManagementOperations operations, TimeSpan operationTimeout, TimeSpan cleanupTimeout,
        bool cleanupOwned, bool verifyRouting, Action<RunReport> persist, CancellationToken cancellationToken = default,
        Func<ResourceStep, CancellationToken, Task>? beforeCreate = null)
    {
        var report = new RunReport(plan);
        var persistenceFailed = false;
        bool Checkpoint()
        {
            if (persistenceFailed)
                return false;
            try
            {
                persist(report);
                return true;
            }
            catch (Exception exception)
            {
                persistenceFailed = true;
                report.Failures.Add($"Resource inventory write failed: {SafeError.Describe(exception)}");
                report.Status = "failed";
                return false;
            }
        }
        if (!Checkpoint())
            return report;
        report.Status = "creating";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(operationTimeout);
        try
        {
            foreach (var record in report.Resources)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (record.Resource.ParentId is { } parent &&
                    !report.Resources.Any(item => item.Resource.Id == parent && item.Owned))
                    throw new SampleException("Creation refused: parent ownership is not confirmed.");
                if (beforeCreate is not null)
                    await beforeCreate(record.Resource, deadline.Token);
                record.State = "checking";
                if (!Checkpoint())
                    throw new SampleException("Cannot persist resource inventory; no further creation is allowed.");
                if (await operations.ExistsAsync(record.Resource, deadline.Token))
                {
                    record.State = "collision";
                    throw new SampleException("Creation refused: a resource already exists at the generated ID.");
                }
                record.State = "attempted";
                if (!Checkpoint())
                    throw new SampleException("Cannot persist resource inventory; no further creation is allowed.");
                try
                {
                    await operations.CreateAsync(record.Resource, deadline.Token);
                    record.Owned = true;
                    record.State = "owned";
                }
                catch (Exception exception)
                {
                    record.State = SafeError.IsCollision(exception) ? "collision" : "ambiguous";
                    record.Error = SafeError.Describe(exception);
                    throw;
                }
                if (!Checkpoint())
                    throw new SampleException("Cannot persist resource inventory; no further creation is allowed.");
            }
            report.Status = "verifying";
            foreach (var record in report.Resources)
                await operations.VerifyAsync(record.Resource, deadline.Token);
            if (verifyRouting)
                await operations.VerifyRoutingAsync(deadline.Token);
            report.Status = "created-and-verified";
        }
        catch (Exception exception)
        {
            report.Failures.Add(SafeError.Describe(exception));
            report.Status = "failed";
        }

        // Cleanup gets independent budgets even if provisioning or the caller was cancelled.
        if (cleanupOwned)
        {
            report.Status = "cleaning";
            foreach (var record in report.Resources.AsEnumerable().Reverse().Where(item => item.Owned))
            {
                if (report.Resources.Any(child => child.Resource.ParentId == record.Resource.Id &&
                    child.State is not ("planned" or "deleted")))
                {
                    record.State = "retained";
                    record.Error = "Cleanup refused: a descendant is retained, unowned or ambiguous.";
                    report.Failures.Add($"{record.Resource.Kind}: {record.Error}");
                    Checkpoint();
                    continue;
                }
                using var cleanupDeadline = new CancellationTokenSource(cleanupTimeout);
                try
                {
                    record.State = "deleting";
                    Checkpoint();
                    await operations.DeleteOwnedAsync(record.Resource, cleanupDeadline.Token);
                    record.State = "deleted";
                }
                catch (Exception exception)
                {
                    record.State = "cleanup-failed";
                    record.Error = SafeError.Describe(exception);
                    report.Failures.Add($"{record.Resource.Kind}: {record.Error}");
                }
                Checkpoint();
            }
            if (report.Failures.Count == 0)
                report.Status = "created-verified-and-deleted";
        }
        if (!report.Success)
            report.Status = "failed";
        Checkpoint();
        return report;
    }
}
