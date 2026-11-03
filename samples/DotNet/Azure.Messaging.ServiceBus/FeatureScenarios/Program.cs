using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using System.Transactions;

await FeatureScenarios.RunAsync(args);

internal static class FeatureScenarios
{
    private static readonly string[] Scenarios = ["request-reply", "duplicate-detection", "ttl", "partitioning"];

    public static async Task RunAsync(string[] args)
    {
        if (args is ["--self-test"])
        {
            await SelfTestAsync();
            return;
        }
        if (args.Length == 0 || args is ["--help"])
        {
            Console.WriteLine($"Scenarios: {string.Join(", ", Scenarios)}");
            Console.WriteLine("Usage: dotnet run -- <scenario> --allow-entity-management");
            Console.WriteLine("Set SERVICEBUS_NAMESPACE to <namespace>.servicebus.windows.net. Use only a disposable Standard/Premium namespace.");
            Console.WriteLine("For Premium, set SERVICEBUS_PREMIUM_PARTITIONS to its verified namespace partition count (1, 2 or 4). Leave it unset for Standard.");
            Console.WriteLine("--self-test checks local message, configuration and failure-path contracts without Azure.");
            return;
        }
        if (args.Length != 2 || !Scenarios.Contains(args[0]) || args[1] != "--allow-entity-management")
            throw new ArgumentException("Choose a scenario and explicitly allow creation/deletion of its temporary queues. Use --help.");
        var host = Environment.GetEnvironmentVariable("SERVICEBUS_NAMESPACE");
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("Set SERVICEBUS_NAMESPACE to the fully qualified namespace hostname.");

        int? premiumPartitions = ParsePremiumPartitions(Environment.GetEnvironmentVariable("SERVICEBUS_PREMIUM_PARTITIONS"));
        var credential = new DefaultAzureCredential();
        var admin = new ServiceBusAdministrationClient(host, credential);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var token = timeout.Token;
        var sku = (await admin.GetNamespacePropertiesAsync(token)).Value.MessagingSku;
        bool partitionQueues = PartitionQueues(sku, args[0], premiumPartitions);
        await using var client = new ServiceBusClient(host, credential);
        string prefix = $"sample-{Guid.NewGuid():N}";
        var created = new List<string>();

        async Task<string> CreateAsync(string suffix, bool sessions = false, bool duplicates = false, bool expiry = false)
        {
            var options = QueueOptions($"{prefix}-{suffix}", sessions, duplicates, partitionQueues, expiry);
            await CreateOwnedAsync(options.Name, created, () => admin.CreateQueueAsync(options, token));
            return options.Name;
        }

        Exception? scenarioFailure = null;
        try
        {
            switch (args[0])
            {
                case "request-reply":
                    var requests = await CreateAsync("requests");
                    var replies = await CreateAsync("replies", sessions: true);
                    await RequestReplyAsync(client, requests, replies, token);
                    break;
                case "duplicate-detection":
                    var dedup = await CreateAsync("dedup", duplicates: true);
                    await DuplicateDetectionAsync(client, dedup, token);
                    break;
                case "ttl":
                    var expiring = await CreateAsync("expiry", expiry: true);
                    await ExpiryAsync(client, expiring, token);
                    break;
                case "partitioning":
                    var partitioned = await CreateAsync("partitioned", sessions: true);
                    Console.WriteLine(sku == MessagingSku.Standard
                        ? "Standard: entity-level partitioning enabled at creation."
                        : $"Premium: matching namespace partition count {premiumPartitions}; EnablePartitioning={partitionQueues.ToString().ToLowerInvariant()}. Namespace topology is unchanged.");
                    await PartitioningAsync(client, partitioned, token);
                    break;
            }
        }
        catch (Exception e)
        {
            scenarioFailure = e;
            throw;
        }
        finally
        {
            // A new cleanup deadline is independent of an expired scenario deadline.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await CleanupOwnedAsync(created, queue => admin.DeleteQueueAsync(queue, cleanup.Token), scenarioFailure);
        }
    }

    private static async Task CreateOwnedAsync(string name, List<string> created, Func<Task> create)
    {
        Console.WriteLine($"Creating {name}. Retain this name if the response cannot be confirmed.");
        try { await create(); }
        catch (Exception)
        {
            Console.Error.WriteLine($"Creation outcome unconfirmed for {name}; inspect it manually. It is not eligible for automatic deletion.");
            throw;
        }
        created.Add(name);
        Console.WriteLine($"Created {name}");
    }

    private static async Task CleanupOwnedAsync(List<string> created, Func<string, Task> delete, Exception? scenarioFailure)
    {
        var failures = new List<Exception>();
        foreach (var queue in created.AsEnumerable().Reverse().ToArray())
        {
            try
            {
                Console.WriteLine($"Deleting only this run's queue: {queue}");
                await delete(queue);
                created.Remove(queue);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Cleanup not confirmed for {queue}: {e.Message}");
                failures.Add(e);
            }
        }
        if (failures.Count != 0)
        {
            if (scenarioFailure is not null)
                failures.Insert(0, scenarioFailure);
            throw new AggregateException($"Cleanup incomplete. Inspect: {string.Join(", ", created)}", failures);
        }
    }

    private static int? ParsePremiumPartitions(string? value) => value switch
    {
        null => null,
        "1" => 1,
        "2" => 2,
        "4" => 4,
        _ => throw new ArgumentException("SERVICEBUS_PREMIUM_PARTITIONS must be 1, 2 or 4.")
    };

    private static bool PartitionQueues(MessagingSku sku, string scenario, int? premiumPartitions)
    {
        if (sku == MessagingSku.Premium)
            return premiumPartitions switch
            {
                1 => false,
                2 or 4 => true,
                _ => throw new InvalidOperationException("Set SERVICEBUS_PREMIUM_PARTITIONS to the Premium namespace's verified partition count (1, 2 or 4).")
            };
        if (sku != MessagingSku.Standard)
            throw new InvalidOperationException("These scenarios require Standard or Premium, not Basic.");
        if (premiumPartitions is not null)
            throw new InvalidOperationException("Leave SERVICEBUS_PREMIUM_PARTITIONS unset for Standard.");
        return scenario == "partitioning";
    }

    private static CreateQueueOptions QueueOptions(string name, bool sessions = false, bool duplicates = false, bool partitioned = false, bool expiry = false) =>
        new(name)
        {
            RequiresSession = sessions,
            RequiresDuplicateDetection = duplicates,
            DuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(10),
            EnablePartitioning = partitioned,
            DeadLetteringOnMessageExpiration = expiry,
            DefaultMessageTimeToLive = TimeSpan.FromMinutes(2),
            AutoDeleteOnIdle = TimeSpan.FromMinutes(5)
        };

    private static ServiceBusMessage Request(string id, string replies, string session) => new("What is my order status?")
    {
        MessageId = id, ReplyTo = replies, ReplyToSessionId = session
    };

    private static ServiceBusMessage Reply(ServiceBusReceivedMessage request) => new("Order accepted")
    {
        MessageId = $"reply-{request.MessageId}",
        CorrelationId = request.MessageId,
        SessionId = request.ReplyToSessionId
    };

    private static ServiceBusMessage BusinessMessage(string id) => new("Business operation") { MessageId = id };

    private static ServiceBusMessage ExpiringMessage() => new("Time-sensitive work")
    {
        MessageId = Guid.NewGuid().ToString("N"),
        TimeToLive = TimeSpan.FromSeconds(3)
    };

    private static ServiceBusMessage PartitionMessage(string session, int index) => new($"Session item {index}")
    {
        MessageId = $"{session}-{index}", SessionId = session, PartitionKey = session
    };

    private static async Task RequestReplyAsync(ServiceBusClient client, string requests, string replies, CancellationToken token)
    {
        string id = Guid.NewGuid().ToString("N");
        string session = Guid.NewGuid().ToString("N");
        await using var requester = client.CreateSender(requests);
        await requester.SendMessageAsync(Request(id, replies, session), token);

        await using var responder = client.CreateReceiver(requests);
        var request = await ReceiveRequiredAsync(responder, token);
        Check(request.ReplyTo == replies && request.ReplyToSessionId == session, "Unexpected reply destination/session.");
        await using var replySender = client.CreateSender(replies);
        await ReplyAndCompleteAsync(request,
            reply => replySender.SendMessageAsync(reply, token),
            () => responder.CompleteMessageAsync(request, token));

        await using var replyReceiver = await client.AcceptSessionAsync(replies, session, cancellationToken: token);
        var reply = await ReceiveRequiredAsync(replyReceiver, token);
        ValidateReply(reply, id, session);
        Console.WriteLine($"Matched response to request {id}: {reply.Body}");
        await replyReceiver.CompleteMessageAsync(reply, token);
    }

    private static async Task ReplyAndCompleteAsync(ServiceBusReceivedMessage request,
        Func<ServiceBusMessage, Task> send, Func<Task> complete)
    {
        await send(Reply(request));
        await complete();
    }

    private static void ValidateReply(ServiceBusReceivedMessage reply, string id, string session) =>
        Check(reply.CorrelationId == id && reply.SessionId == session, "Reply correlation/session mismatch.");

    private static async Task DuplicateDetectionAsync(ServiceBusClient client, string queue, CancellationToken token)
    {
        await using var sender = client.CreateSender(queue);
        string id = "order-42";
        await sender.SendMessageAsync(BusinessMessage(id), token);
        await sender.SendMessageAsync(BusinessMessage(id), token);
        await using var receiver = client.CreateReceiver(queue);
        var first = await ReceiveRequiredAsync(receiver, token);
        Check(first.MessageId == id, "Unexpected first message ID.");
        await receiver.CompleteMessageAsync(first, token);
        Check(await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10), token) is null,
            "The repeat send produced another message within the duplicate-detection window.");
        await sender.SendMessageAsync(BusinessMessage("order-43"), token);
        var different = await ReceiveRequiredAsync(receiver, token);
        Check(different.MessageId == "order-43", "A different business ID should be accepted.");
        await receiver.CompleteMessageAsync(different, token);
        Console.WriteLine("One copy of order-42 observed; order-43 accepted. Receive redelivery still requires idempotent processing.");
    }

    private static async Task ExpiryAsync(ServiceBusClient client, string queue, CancellationToken token)
    {
        await using var sender = client.CreateSender(queue);
        var expiring = ExpiringMessage();
        await sender.SendMessageAsync(expiring, token);
        await Task.Delay(expiring.TimeToLive + TimeSpan.FromSeconds(2), token);
        await using var active = client.CreateReceiver(queue);
        Check(await active.ReceiveMessageAsync(TimeSpan.FromSeconds(2), token) is null,
            "Expired work must not be delivered from the active queue.");
        await using var deadLetters = client.CreateReceiver(queue, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
        var expired = await ReceiveRequiredAsync(deadLetters, token);
        Check(expired.MessageId == expiring.MessageId && expired.DeadLetterReason == "TTLExpiredException",
            "Expected this run's expired message in the DLQ.");
        Console.WriteLine($"Expired message {expired.MessageId}: {expired.DeadLetterReason}; {expired.DeadLetterErrorDescription}");
        Console.WriteLine("Explicit demo decision: discard this expired test work; do not replay it.");
        await deadLetters.CompleteMessageAsync(expired, token);
    }

    private static async Task PartitioningAsync(ServiceBusClient client, string queue, CancellationToken token)
    {
        string session = "order-session";
        await using var sender = client.CreateSender(queue);
        await SendTransactionAsync(session, message => sender.SendMessageAsync(message, token));
        await using var receiver = await client.AcceptSessionAsync(queue, session, cancellationToken: token);
        for (int index = 1; index <= 2; index++)
        {
            var message = await ReceiveRequiredAsync(receiver, token);
            Check(message.SessionId == session && message.MessageId == $"{session}-{index}", "Session ordering/key mismatch.");
            await receiver.CompleteMessageAsync(message, token);
        }
        Console.WriteLine("Received the two committed messages in one session. SessionId and PartitionKey must match; transaction sends use one key.");
    }

    private static async Task SendTransactionAsync(string session, Func<ServiceBusMessage, Task> send)
    {
        using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        await send(PartitionMessage(session, 1));
        await send(PartitionMessage(session, 2));
        transaction.Complete();
    }

    private static async Task<ServiceBusReceivedMessage> ReceiveRequiredAsync(ServiceBusReceiver receiver, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5), token);
            if (message is not null)
                return message;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static async Task SelfTestAsync()
    {
        var request = Request("request-1", "replies", "session-1");
        Check(request.MessageId == "request-1" && request.ReplyTo == "replies" && request.ReplyToSessionId == "session-1", "Request routing contract.");
        var reply = Reply(ServiceBusModelFactory.ServiceBusReceivedMessage(messageId: "request-1", replyToSessionId: "session-1"));
        Check(reply.CorrelationId == request.MessageId && reply.SessionId == request.ReplyToSessionId, "Response correlation contract.");
        Check(BusinessMessage("stable-id").MessageId == BusinessMessage("stable-id").MessageId, "Stable retry ID.");
        Check(BusinessMessage("stable-id").MessageId != BusinessMessage("different-id").MessageId, "Distinct operation ID.");
        var dedup = QueueOptions("dedup", duplicates: true);
        Check(dedup.RequiresDuplicateDetection && dedup.DuplicateDetectionHistoryTimeWindow == TimeSpan.FromMinutes(10), "Deduplication configuration.");
        var expiry = QueueOptions("expiry", expiry: true);
        Check(expiry.DeadLetteringOnMessageExpiration && ExpiringMessage().TimeToLive < expiry.DefaultMessageTimeToLive, "Expiry configuration.");
        Check(QueueOptions("replies", sessions: true).RequiresSession, "Session-enabled reply queue.");
        foreach (var scenario in Scenarios)
        {
            Check(QueueOptions("standard", partitioned: PartitionQueues(MessagingSku.Standard, scenario, null)).EnablePartitioning ==
                (scenario == "partitioning"), "Standard retains per-scenario partitioning.");
            foreach (var count in new[] { 1, 2, 4 })
                Check(QueueOptions("premium", partitioned: PartitionQueues(MessagingSku.Premium, scenario, count)).EnablePartitioning ==
                    (count > 1), "Every Premium queue must match namespace partitioning.");
        }
        Check(ParsePremiumPartitions(null) is null, "Absent Premium partition setting.");
        foreach (var count in new[] { "1", "2", "4" })
            Check(ParsePremiumPartitions(count) == int.Parse(count), "Explicit Premium partition setting.");
        foreach (var invalid in new[] { "", "0", "3", "-1", "two", " 2 ", "2\n", "16" })
        {
            try { ParsePremiumPartitions(invalid); throw new Exception("Expected invalid partition setting rejection."); }
            catch (ArgumentException) { }
        }
        try { PartitionQueues(MessagingSku.Premium, "partitioning", null); throw new Exception("Expected missing Premium partition count rejection."); }
        catch (InvalidOperationException) { }
        foreach (var count in new[] { 1, 2, 4 })
        {
            try { PartitionQueues(MessagingSku.Standard, "partitioning", count); throw new Exception("Expected Premium-only configuration rejection."); }
            catch (InvalidOperationException) { }
        }
        try { PartitionQueues(MessagingSku.Basic, "partitioning", null); throw new Exception("Expected Basic tier rejection."); }
        catch (InvalidOperationException) { }
        var partition = PartitionMessage("key", 1);
        Check(partition.PartitionKey == partition.SessionId && partition.MessageId == "key-1", "Partition/session key contract.");
        var owned = new List<string> { "first", "second" };
        var attempts = new List<string>();
        var primary = new InvalidOperationException("scenario failed");
        try
        {
            await CleanupOwnedAsync(owned, name =>
            {
                attempts.Add(name);
                return name == "second" ? Task.FromException(new InvalidOperationException("delete failed")) : Task.CompletedTask;
            }, primary);
            throw new Exception("Expected cleanup failure.");
        }
        catch (AggregateException e)
        {
            Check(e.InnerExceptions.Count == 2 && ReferenceEquals(e.InnerExceptions[0], primary) &&
                attempts.SequenceEqual(new[] { "second", "first" }) && owned.SequenceEqual(new[] { "second" }),
                "Cleanup must attempt all owned queues and preserve both failures.");
        }
        var unconfirmed = new List<string>();
        try
        {
            await CreateOwnedAsync("unconfirmed-queue", unconfirmed, () => Task.FromException(new TimeoutException("create response lost")));
            throw new Exception("Expected unconfirmed create failure.");
        }
        catch (TimeoutException) { Check(unconfirmed.Count == 0, "Unconfirmed creation must not grant deletion ownership."); }
        bool settled = false;
        try
        {
            await ReplyAndCompleteAsync(ServiceBusModelFactory.ServiceBusReceivedMessage(messageId: "request-1", replyToSessionId: "session-1"),
                _ => Task.FromException(new InvalidOperationException("reply send failed")),
                () => { settled = true; return Task.CompletedTask; });
            throw new Exception("Expected reply send failure.");
        }
        catch (InvalidOperationException) { Check(!settled, "Failed replies must leave requests unsettled."); }
        foreach (var invalid in new[]
        {
            ServiceBusModelFactory.ServiceBusReceivedMessage(correlationId: "other-request", sessionId: "session-1"),
            ServiceBusModelFactory.ServiceBusReceivedMessage(correlationId: "request-1", sessionId: "other-session")
        })
        {
            try { ValidateReply(invalid, "request-1", "session-1"); throw new Exception("Expected correlation rejection."); }
            catch (InvalidOperationException) { }
        }
        using var deadline = new CancellationTokenSource();
        await using var empty = new EmptyReceiver(deadline);
        try { await ReceiveRequiredAsync(empty, deadline.Token); throw new Exception("Expected receive cancellation."); }
        catch (OperationCanceledException) { Check(empty.Calls == 1, "Receive deadline must terminate empty polling."); }
        TransactionStatus? transactionStatus = null;
        int sends = 0;
        try
        {
            await SendTransactionAsync("transaction-test", _ =>
            {
                if (++sends == 1)
                    Transaction.Current!.TransactionCompleted += (_, e) => transactionStatus = e.Transaction!.TransactionInformation.Status;
                return sends == 2 ? Task.FromException(new InvalidOperationException("second send failed")) : Task.CompletedTask;
            });
            throw new Exception("Expected transaction failure.");
        }
        catch (InvalidOperationException) { Check(transactionStatus == TransactionStatus.Aborted, "Failed transaction must not commit."); }
        Console.WriteLine("PASS: message/configuration contracts, Standard/Premium partition settings and 6 lifecycle/failure-path categories. No Azure calls; broker behavior is not tested.");
    }

    private sealed class EmptyReceiver(CancellationTokenSource deadline) : ServiceBusReceiver
    {
        public int Calls { get; private set; }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public override Task<ServiceBusReceivedMessage> ReceiveMessageAsync(TimeSpan? maxWaitTime = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            deadline.Cancel();
            return Task.FromResult<ServiceBusReceivedMessage>(null!);
        }
    }
}
