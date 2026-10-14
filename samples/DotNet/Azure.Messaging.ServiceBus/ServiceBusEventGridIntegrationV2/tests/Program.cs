using Azure.Messaging.EventGrid;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using ReceiveMessagesFunctionApp;
using System.Reflection;

const string eventType = "Microsoft.ServiceBus.ActiveMessagesAvailableWithNoListeners";
EventGridEvent Event(string type = eventType, string data = """{"topicName":"orders","subscriptionName":"processor"}""") =>
    new("/namespaces/example/topics/orders/subscriptions/processor", type, "1.0", BinaryData.FromString(data));
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var method = typeof(ReceiveMessagesOnEvent).GetMethod("Run")!;
Check(method.GetCustomAttribute<FunctionAttribute>()?.Name == "EventGridTriggerFunction", "Function identity changed.");
Check(method.GetParameters()[0].GetCustomAttribute<EventGridTriggerAttribute>() is not null, "Missing isolated Event Grid binding.");

var client = new RecordingClient();
var function = new ReceiveMessagesOnEvent(client, NullLogger<ReceiveMessagesOnEvent>.Instance);
await function.Run(Event(), CancellationToken.None);
Check(client.Topic == "orders" && client.Subscription == "processor", "Incorrect event routing.");
Check(client.Receiver.Completed.SequenceEqual(new[] { "one", "two" }), "Each message must be completed after processing.");
Check(client.Receiver.Disposed, "Receiver must be disposed.");

client = new RecordingClient();
function = new ReceiveMessagesOnEvent(client, NullLogger<ReceiveMessagesOnEvent>.Instance);
await function.Run(Event("Microsoft.Storage.BlobCreated"), CancellationToken.None);
Check(client.Topic is null, "Unsupported events must not access Service Bus.");

try
{
    await function.Run(Event(data: "{}"), CancellationToken.None);
    throw new Exception("Expected missing topic/subscription to fail.");
}
catch (InvalidOperationException)
{
    Check(client.Topic is null, "Malformed events must not access Service Bus.");
}

client = new RecordingClient();
function = new ReceiveMessagesOnEvent(client, NullLogger<ReceiveMessagesOnEvent>.Instance);
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try
{
    await function.Run(Event(), cancelled.Token);
    throw new Exception("Expected cancellation.");
}
catch (OperationCanceledException)
{
    Check(client.Receiver.Disposed && client.Receiver.Completed.Count == 0, "Cancellation must dispose without settlement.");
}

client = new RecordingClient();
client.Receiver.Empty = true;
function = new ReceiveMessagesOnEvent(client, NullLogger<ReceiveMessagesOnEvent>.Instance);
await function.Run(Event(), CancellationToken.None);
Check(client.Receiver.Disposed && client.Receiver.Completed.Count == 0, "Empty batch must dispose without settlement.");

client = new RecordingClient();
client.Receiver.ReceiveFailure = new InvalidOperationException("receive failed");
function = new ReceiveMessagesOnEvent(client, NullLogger<ReceiveMessagesOnEvent>.Instance);
try { await function.Run(Event(), CancellationToken.None); throw new Exception("Expected receive failure."); }
catch (InvalidOperationException e) when (ReferenceEquals(e, client.Receiver.ReceiveFailure))
{
    Check(client.Receiver.Disposed && client.Receiver.Completed.Count == 0, "Failed receive must dispose without settlement.");
}

client = new RecordingClient();
client.Receiver.FailSecondCompletion = true;
function = new ReceiveMessagesOnEvent(client, NullLogger<ReceiveMessagesOnEvent>.Instance);
try { await function.Run(Event(), CancellationToken.None); throw new Exception("Expected settlement failure."); }
catch (InvalidOperationException e) when (e.Message == "second completion failed")
{
    Check(client.Receiver.Disposed && client.Receiver.Completed.SequenceEqual(new[] { "one" }), "Failed settlement must not report completion.");
}

using var midBatch = new CancellationTokenSource();
client = new RecordingClient();
client.Receiver.CancelAfterFirst = midBatch;
function = new ReceiveMessagesOnEvent(client, NullLogger<ReceiveMessagesOnEvent>.Instance);
try { await function.Run(Event(), midBatch.Token); throw new Exception("Expected mid-batch cancellation."); }
catch (OperationCanceledException)
{
    Check(client.Receiver.Disposed && client.Receiver.Completed.SequenceEqual(new[] { "one" }), "Cancellation must not complete remaining messages.");
}
Console.WriteLine("PASS: 9 offline Function cases, including empty/failed receive, settlement failure and mid-batch cancellation. No Azure calls.");

sealed class RecordingClient : ServiceBusClient
{
    public string? Topic { get; private set; }
    public string? Subscription { get; private set; }
    public RecordingReceiver Receiver { get; } = new();
    public override ServiceBusReceiver CreateReceiver(string topicName, string subscriptionName, ServiceBusReceiverOptions? options = null)
    {
        Topic = topicName;
        Subscription = subscriptionName;
        return Receiver;
    }
}

sealed class RecordingReceiver : ServiceBusReceiver
{
    public List<string> Completed { get; } = [];
    public bool Disposed { get; private set; }
    public bool Empty { get; set; }
    public Exception? ReceiveFailure { get; set; }
    public bool FailSecondCompletion { get; set; }
    public CancellationTokenSource? CancelAfterFirst { get; set; }
    public override Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveMessagesAsync(
        int maxMessages, TimeSpan? maxWaitTime = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maxMessages != 100) throw new InvalidOperationException("Unexpected batch size.");
        if (maxWaitTime != TimeSpan.FromSeconds(5)) throw new InvalidOperationException("Unexpected receive wait.");
        if (ReceiveFailure is not null) throw ReceiveFailure;
        if (Empty) return Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>([]);
        return Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>(
            [ServiceBusModelFactory.ServiceBusReceivedMessage(messageId: "one"),
             ServiceBusModelFactory.ServiceBusReceivedMessage(messageId: "two")]);
    }
    public override Task CompleteMessageAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailSecondCompletion && Completed.Count == 1)
            throw new InvalidOperationException("second completion failed");
        Completed.Add(message.MessageId);
        if (Completed.Count == 1) CancelAfterFirst?.Cancel();
        return Task.CompletedTask;
    }
    public override ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
