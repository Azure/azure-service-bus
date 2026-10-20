using Azure.Messaging.EventGrid;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ReceiveMessagesFunctionApp;
using System.Reflection;
using System.Text.Json;

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
var context = new RecordingContext();
var logger = new RecordingLogger();
var function = new ReceiveMessagesOnEvent(client, logger);
var inputEvent = Event();
await function.Run(inputEvent, context, CancellationToken.None);
Check(client.Topic == "orders" && client.Subscription == "processor", "Incorrect event routing.");
Check(client.Receiver.Completed.SequenceEqual(new[] { "one", "two" }), "Each message must be completed after processing.");
Check(client.Receiver.Disposed, "Receiver must be disposed.");
var eventLine = logger.Lines.Single(line => line.Contains(" received event "));
using (var envelope = JsonDocument.Parse(eventLine[(eventLine.IndexOf(" received event ") + " received event ".Length)..]))
{
    Check(eventLine.Contains(context.InvocationId) && envelope.RootElement.GetProperty("id").GetString() == inputEvent.Id,
        "The event envelope must carry the actual event and invocation IDs.");
    Check(envelope.RootElement.GetProperty("data").GetProperty("topicName").GetString() == "orders",
        "The event envelope must preserve the trigger payload.");
}
Check(logger.Lines.Count(line => line.Contains(" completed message ")) == 2, "Each accepted settlement must be logged.");
Check(logger.Lines.Last() == $"Event Grid invocation {context.InvocationId} completed event {inputEvent.Id}.",
    "Event completion must follow receiver disposal.");

client = new RecordingClient();
logger = new RecordingLogger();
function = new ReceiveMessagesOnEvent(client, logger);
await function.Run(Event("Microsoft.Storage.BlobCreated"), context, CancellationToken.None);
Check(client.Topic is null, "Unsupported events must not access Service Bus.");

try
{
    await function.Run(Event(data: "{}"), context, CancellationToken.None);
    throw new Exception("Expected missing topic/subscription to fail.");
}
catch (InvalidOperationException)
{
    Check(client.Topic is null, "Malformed events must not access Service Bus.");
}

client = new RecordingClient();
function = new ReceiveMessagesOnEvent(client, new RecordingLogger());
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try
{
    await function.Run(Event(), context, cancelled.Token);
    throw new Exception("Expected cancellation.");
}
catch (OperationCanceledException)
{
    Check(client.Receiver.Disposed && client.Receiver.Completed.Count == 0, "Cancellation must dispose without settlement.");
}

client = new RecordingClient();
client.Receiver.Empty = true;
function = new ReceiveMessagesOnEvent(client, new RecordingLogger());
await function.Run(Event(), context, CancellationToken.None);
Check(client.Receiver.Disposed && client.Receiver.Completed.Count == 0, "Empty batch must dispose without settlement.");

client = new RecordingClient();
client.Receiver.ReceiveFailure = new InvalidOperationException("receive failed");
function = new ReceiveMessagesOnEvent(client, new RecordingLogger());
try { await function.Run(Event(), context, CancellationToken.None); throw new Exception("Expected receive failure."); }
catch (InvalidOperationException e) when (ReferenceEquals(e, client.Receiver.ReceiveFailure))
{
    Check(client.Receiver.Disposed && client.Receiver.Completed.Count == 0, "Failed receive must dispose without settlement.");
}

client = new RecordingClient();
client.Receiver.FailSecondCompletion = true;
logger = new RecordingLogger();
function = new ReceiveMessagesOnEvent(client, logger);
try { await function.Run(Event(), context, CancellationToken.None); throw new Exception("Expected settlement failure."); }
catch (InvalidOperationException e) when (e.Message == "second completion failed")
{
    Check(client.Receiver.Disposed && client.Receiver.Completed.SequenceEqual(new[] { "one" }), "Failed settlement must not report completion.");
    Check(logger.Lines.Count(line => line.Contains(" completed message ")) == 1 &&
        !logger.Lines.Any(line => line.Contains(" completed event ")), "Failed settlement must not log message or event success.");
}

using var midBatch = new CancellationTokenSource();
client = new RecordingClient();
client.Receiver.CancelAfterFirst = midBatch;
function = new ReceiveMessagesOnEvent(client, new RecordingLogger());
try { await function.Run(Event(), context, midBatch.Token); throw new Exception("Expected mid-batch cancellation."); }
catch (OperationCanceledException)
{
    Check(client.Receiver.Disposed && client.Receiver.Completed.SequenceEqual(new[] { "one" }), "Cancellation must not complete remaining messages.");
}
client = new RecordingClient();
client.Receiver.DisposeFailure = new InvalidOperationException("dispose failed");
logger = new RecordingLogger();
function = new ReceiveMessagesOnEvent(client, logger);
try { await function.Run(Event(), context, CancellationToken.None); throw new Exception("Expected disposal failure."); }
catch (InvalidOperationException e) when (ReferenceEquals(e, client.Receiver.DisposeFailure))
{
    Check(!logger.Lines.Any(line => line.Contains(" completed event ")), "Failed disposal must not log event completion.");
}
Console.WriteLine("PASS: 10 offline Function cases, including correlated event/settlement logs and disposal failure. No Azure calls.");

sealed class RecordingLogger : ILogger<ReceiveMessagesOnEvent>
{
    public List<string> Lines { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
}

sealed class RecordingContext : FunctionContext
{
    public override string InvocationId => "11111111-2222-3333-4444-555555555555";
    public override string FunctionId => throw new NotSupportedException();
    public override TraceContext TraceContext => throw new NotSupportedException();
    public override BindingContext BindingContext => throw new NotSupportedException();
    public override RetryContext RetryContext => throw new NotSupportedException();
    public override IServiceProvider InstanceServices
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override FunctionDefinition FunctionDefinition => throw new NotSupportedException();
    public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
    public override IInvocationFeatures Features => throw new NotSupportedException();
}

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
    public Exception? DisposeFailure { get; set; }
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
        if (DisposeFailure is not null) throw DisposeFailure;
        return ValueTask.CompletedTask;
    }
}
