# Migrate applications using NetMessagingBinding

`NetMessagingBinding` sends one-way WCF operations through a Service Bus queue. Its messaging transport uses SBMP, which [was retired on 30 September 2026](https://techcommunity.microsoft.com/t5/messaging-on-azure-blog/some-azure-service-bus-sdk-libraries-will-be-retired-on-30/ba-p/3917853). To remove this dependency, replace the binding on both sides of the application. First identify the queued endpoint, then decide whether work must wait for an offline receiver or whether the receiver can be available when each request is sent. Migrate both the sending and receiving sides.

This guide covers three migration options:

| Option | Choose it when |
| --- | --- |
| Azure Service Bus with the current .NET client | Work must remain queued while the receiver is offline, or needs broker redelivery and settlement. |
| Azure Relay Hybrid Connections | A live listener can handle each request, and the application can adopt an HTTP or WebSocket contract. This is the preferred Relay path for a new design. |
| WCF Relay | A live listener can handle each request, but the application must retain a WCF service contract. |

**Keep Service Bus if messages must wait while the receiver is offline.** Neither Relay option stores requests for later delivery or preserves queue settlement. For either Relay option, [create a separate Azure Relay namespace](https://learn.microsoft.com/azure/azure-relay/relay-create-namespace-portal) and its endpoint. Keep the existing Service Bus namespace and queue until the queued backlog is drained. A new Relay namespace can be created and managed in the Azure portal; a Relay namespace does not supply a queue.

## 1. Identify each endpoint and its delivery requirements

Inspect the deployed configuration **and** the client and service call sites. For example, this simplified excerpt from the [NetMessagingBinding WCF sample](Readme.md) shows both endpoints targeting one queue. A working WCF configuration also needs the binding extensions and authentication behaviors shown in that sample:

```xml
<!-- Sender's App.config -->
<client>
  <endpoint name="pingClient"
            address="sb://<namespace>.servicebus.windows.net/PingQueue"
            binding="netMessagingBinding"
            contract="Example.IPingService" />
</client>

<!-- Receiver's App.config -->
<services>
  <service name="Example.PingService">
    <endpoint address="sb://<namespace>.servicebus.windows.net/PingQueue"
              binding="netMessagingBinding"
              contract="Example.IPingService" />
  </service>
</services>
```

The old client invokes an operation through a WCF `ChannelFactory`; the receiving `ServiceHost` dispatches it from the queue. The sample's operation uses `[OperationContract(IsOneWay = true)]` and calls `ReceiveContext.Complete` after processing. In your application, record the actual queue, contract, encoder, credentials, message shape, headers, sessions, settlement behavior, dead-letter policy, retry and concurrency settings, and every producer and consumer. Do not assume that the sample's settings are yours.

To confirm that an endpoint needs this migration, find `netMessagingBinding` in its deployed configuration, identify the queue it addresses, and trace both the sender and receiver. Do not classify it from an `sb://` address, a package reference, or namespace-level SBMP activity alone: none identifies the binding used by a particular process. An endpoint configured with `netTcpRelayBinding` uses WCF Relay, not this queued binding.

Before selecting a destination, answer these questions for each operation:

| Question | If the answer is yes |
| --- | --- |
| Must sends succeed when no receiver is listening? | Keep Service Bus. |
| Must the broker retain, redeliver, dead-letter, or order messages? | Keep Service Bus and map each behavior explicitly. |
| Can the caller handle a failed or timed-out request when no Relay listener is available? | Consider Relay Hybrid Connections. |
| Must existing .NET Framework WCF service contracts remain in use? | Assess WCF Relay with its package and lifecycle caveats. |

## 2. Migrate using Azure Service Bus

**Use this path for queued delivery.** The modern [`Azure.Messaging.ServiceBus` client](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/servicebus/Azure.Messaging.ServiceBus/MigrationGuide_WindowsAzureServiceBus.md) uses AMQP and does not support SBMP. Keep the existing queue when the namespace and its AMQP endpoint support the new client; test that path in the actual deployment before committing to an in-place cutover. A new queue or namespace is a separate migration decision, not a prerequisite merely because the customer also uses Relay.

| Today: WCF over a queue | Replacement: explicit messaging |
| --- | --- |
| `binding="netMessagingBinding"` and a queue `sb://` address | `ServiceBusClient` and `CreateSender(queueName)` / `CreateProcessor(queueName)` |
| `channel.Ping(pingData)` | Serialize an application-defined payload and call `ServiceBusSender.SendMessageAsync` |
| `ServiceHost` dispatches `Ping(PingData)` | A message handler deserializes the payload and calls the application operation |
| `ReceiveContext.Complete()` | `ProcessMessageEventArgs.CompleteMessageAsync(message)` |
| WCF encoder, contract and behaviors | Explicit, versioned payload, metadata, authentication and failure policy |

The following example illustrates **a new JSON contract**, not a decoder for messages already encoded by WCF. Add `Azure.Messaging.ServiceBus`, `Azure.Identity` and, where needed, `System.Text.Json` to the applications. Grant the sending and receiving identities the appropriate Service Bus data roles. Replace the example payload and the consumer's business operation with your own contract and processing code.

```csharp
// Shared by the new sender and consumer.
public sealed class PingData
{
    public string Message { get; set; }
    public string SenderId { get; set; }
}
```

On the sending side, replace the WCF channel call with:

```csharp
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using System.Text.Json;

string namespaceHost = "<namespace>.servicebus.windows.net";
string queueName = "PingQueue";
string operationId = "<stable-application-operation-id>";

await using var client =
    new ServiceBusClient(namespaceHost, new DefaultAzureCredential());
await using var sender = client.CreateSender(queueName);

var ping = new PingData { Message = "Hello", SenderId = "sender-1" };
var message = new ServiceBusMessage(JsonSerializer.Serialize(ping))
{
    ContentType = "application/json",
    MessageId = operationId
};
await sender.SendMessageAsync(message);
```

Use an application-defined `MessageId` that stays the same if this operation is resubmitted; generating a fresh ID on every retry defeats broker duplicate detection. On the receiving side, replace the queued WCF `ServiceHost` with a processor. The example handles a malformed JSON body separately from an application failure:

```csharp
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using System;
using System.Text.Json;
using System.Threading.Tasks;

string namespaceHost = "<namespace>.servicebus.windows.net";
string queueName = "PingQueue";

await using var client =
    new ServiceBusClient(namespaceHost, new DefaultAzureCredential());
await using var processor = client.CreateProcessor(queueName,
    new ServiceBusProcessorOptions { AutoCompleteMessages = false });

async Task OnMessage(ProcessMessageEventArgs args)
{
    PingData ping;
    try
    {
        ping = JsonSerializer.Deserialize<PingData>(args.Message.Body.ToString())
            ?? throw new JsonException("The message body is empty.");
    }
    catch (JsonException error)
    {
        await args.DeadLetterMessageAsync(
            args.Message, "InvalidPayload", error.Message);
        return;
    }

    Console.WriteLine($"{ping.SenderId}: {ping.Message}");
    // Make the real business operation idempotent: lock loss can redeliver
    // a processed message. Use a deduplication check or an upsert.
    await args.CompleteMessageAsync(args.Message);
}

Task OnError(ProcessErrorEventArgs args)
{
    Console.Error.WriteLine(args.Exception);
    return Task.CompletedTask;
}

processor.ProcessMessageAsync += OnMessage;
processor.ProcessErrorAsync += OnError;
try
{
    await processor.StartProcessingAsync();
    Console.ReadLine();
    await processor.StopProcessingAsync();
}
finally
{
    processor.ProcessMessageAsync -= OnMessage;
    processor.ProcessErrorAsync -= OnError;
}
```

This illustrates a non-session queue. If the entity requires sessions, use `ServiceBusSessionProcessor` and preserve the session identifier. Configure concurrency, lock renewal, settlement and dead-letter handling to match the application's needs. Limiting concurrent calls to one does not guarantee ordering; validate ordering requirements with sessions. A handler failure without settlement is not a successful processing result. Follow the [Service Bus processor examples](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/servicebus/Azure.Messaging.ServiceBus/samples/Sample04_Processor.md) and [message settlement guidance](https://learn.microsoft.com/azure/service-bus-messaging/message-transfers-locks-settlement) when adapting the code.

### Move existing messages and cut over

The new JSON consumer cannot assume it can deserialize messages that the WCF binding encoded. Before changing the consumer, inspect representative messages and decide whether to **drain the old queue with the old WCF receiver** or build and test a converter that understands their actual format. Do not delete a queue while it contains unprocessed work.

1. Test the new sender and consumer together on a separate test queue with representative successful, malformed, duplicate and failed operations.
2. If old and new applications must share a queue temporarily, test all four producer/consumer combinations. Otherwise separate formats by queue and switch both sides together.
3. Stop new writes through `NetMessagingBinding`. Drain or explicitly account for in-flight and dead-lettered old-format messages, then start the new sender and consumer. Include rollback and duplicate-processing handling in the cutover plan.
4. Confirm that processing succeeds through AMQP and that no application process still opens an SBMP session. Ask your support or account team to confirm service-side protocol activity for the namespace; queue metrics alone do not identify the binding.

Appending `;TransportType=Amqp` is **not** a binding-level fix. [Legacy AMQP guidance](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-amqp-dotnet) applies to legacy messaging libraries; the traced `NetMessagingBinding` transport constructs an SBMP factory. The [WindowsAzure.ServiceBus client migration guide](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/servicebus/Azure.Messaging.ServiceBus/MigrationGuide_WindowsAzureServiceBus.md) compares messaging APIs, not WCF binding behavior.

## 3. Migrate using Azure Relay Hybrid Connections

**Prefer this Relay path when the receiver can be online for each request.** Hybrid Connections connects a sender to a listener through Azure Relay; it does not require a permanent, direct connection between the applications. An HTTP request can be short-lived, while a WebSocket connection can remain open for a longer exchange. It does not host a WCF contract or retain requests for an offline listener. Choose it only if the application can change its call and hosting model and does not require queued delivery.

| Today: queued WCF operation | After redesign: Hybrid Connection |
| --- | --- |
| `channel.Ping(pingData)` enqueues a one-way operation | Send an HTTP request or WebSocket payload with an explicit contract |
| A queued `ServiceHost` dispatches the operation | A `HybridConnectionListener` handles live requests or accepts connections |
| Queue and lock settlement protect offline work | The application handles listener availability, timeouts, retries and any required durability |

For an HTTP request/response design, create a Hybrid Connection resource in the new Relay namespace. Prefer Microsoft Entra ID: assign the `Azure Relay Listener` role to the listener's identity and the `Azure Relay Sender` role to the sender's identity at the narrowest practical scope. Deploy the listener before changing the sender. The [.NET HTTP quickstart](https://learn.microsoft.com/azure/azure-relay/relay-hybrid-connections-http-requests-dotnet-get-started) demonstrates `HybridConnectionListener.RequestHandler` for receiving and `HttpClient` with a `ServiceBusAuthorization` token for sending. The [WebSockets quickstart](https://learn.microsoft.com/azure/azure-relay/relay-hybrid-connections-dotnet-get-started) demonstrates a bidirectional stream when HTTP request/response is not the right interaction. Define the HTTP method, request and response payloads, authentication and timeout behavior for each former WCF operation; do not send a WCF binary envelope and assume the listener will interpret it.

For example, a listener handling the new `PingData` JSON contract can replace the queued `ServiceHost`. The following uses a system-assigned managed identity on an Azure host:

```csharp
using Microsoft.Azure.Relay;
using System;
using System.IO;
using System.Net;
using System.Text.Json;

string relayHost = "<relay-namespace>.servicebus.windows.net";
string connectionName = "PingRelay";
var tokenProvider = TokenProvider.CreateManagedIdentityTokenProvider();
var listener = new HybridConnectionListener(
    new Uri($"sb://{relayHost}/{connectionName}"), tokenProvider);

listener.RequestHandler = context =>
{
    try
    {
        using var input = new StreamReader(context.Request.InputStream);
        PingData ping = JsonSerializer.Deserialize<PingData>(input.ReadToEnd())
            ?? throw new JsonException("Empty request.");
        Console.WriteLine($"{ping.SenderId}: {ping.Message}");
        context.Response.StatusCode = HttpStatusCode.OK;
    }
    catch (JsonException error)
    {
        Console.Error.WriteLine(error);
        context.Response.StatusCode = HttpStatusCode.BadRequest;
    }
    finally
    {
        context.Response.Close();
    }
};

await listener.OpenAsync();
try
{
    Console.ReadLine();
}
finally
{
    await listener.CloseAsync();
}
```

On the sending side, replace `channel.Ping(pingData)` with an HTTP request. The sender runs under its own identity with the Sender role:

```csharp
using Microsoft.Azure.Relay;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;

string relayHost = "<relay-namespace>.servicebus.windows.net";
string connectionName = "PingRelay";
var tokenProvider = TokenProvider.CreateManagedIdentityTokenProvider();
var address = new Uri($"https://{relayHost}/{connectionName}");
string token = (await tokenProvider.GetTokenAsync(
    address.AbsoluteUri, TimeSpan.FromHours(1))).TokenString;

using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
using var request = new HttpRequestMessage(HttpMethod.Post, address);
request.Headers.TryAddWithoutValidation("ServiceBusAuthorization", token);
var ping = new PingData { Message = "Hello", SenderId = "sender-1" };
request.Content = new StringContent(
    JsonSerializer.Serialize(ping), Encoding.UTF8, "application/json");
using var response = await client.SendAsync(request);
response.EnsureSuccessStatusCode();
```

For a listener outside Azure without managed identity, use an application identity with `TokenProvider.CreateAzureActiveDirectoryTokenProvider` as described in [Relay application authentication](https://learn.microsoft.com/azure/azure-relay/authenticate-application). If identity authentication is unavailable, the [HTTP quickstart](https://learn.microsoft.com/azure/azure-relay/relay-hybrid-connections-http-requests-dotnet-get-started) also shows SAS-token authentication; protect its keys and scope policies to the required permissions. The managed-identity setup and roles are described in [Relay managed-identity guidance](https://learn.microsoft.com/azure/azure-relay/authenticate-managed-identity). This example confirms that the **live listener** accepted a request; it does not provide queue storage, redelivery or broker-managed settlement. Replace the console output with the actual business operation and define its failure responses before cutover.

Test the live listener being unavailable, restarting and losing its connection. Unlike a successful queue send, a live call depends on the listener being reachable; if work cannot be lost during those outages, retain Service Bus or add a separately designed durable store. Stop the old queued sender and drain its remaining messages before retiring its receiver and queue.

## 4. Migrate using WCF Relay when WCF must remain

WCF Relay connects a live WCF listener and client through Azure Relay. It can preserve a WCF-style contract, but it is **not** a way to keep the old queue while changing only `netMessagingBinding` to `netTcpRelayBinding`. The listener must be running, the Relay resource and credentials must be configured, and the contract and error handling must be validated for live calls. It provides no queue drain, stored backlog or broker settlement.

For example, the [WCF Relay tutorial](https://learn.microsoft.com/azure/azure-relay/service-bus-relay-tutorial) uses a `netTcpRelayBinding` endpoint on both client and service:

```xml
<!-- Illustrative endpoints; also configure the binding extension,
     Relay access policy and endpoint authentication. -->
<endpoint address="sb://<namespace>.servicebus.windows.net/PingRelay"
          binding="netTcpRelayBinding"
          contract="Example.IPingService" />
```

Create the WCF Relay endpoint in the new Relay namespace, configure authentication on both sides, host and open the service to register its listener, and point the client at that listener. Test the actual WCF contract, including one-way operations, faults and behavior when the listener is offline. Drain queued messages through the old receiver before removing the old queued endpoint.

The WCF Relay tutorial uses the legacy `WindowsAzure.ServiceBus` package. [Azure Relay continues to support WCF Relay](https://learn.microsoft.com/azure/azure-relay/relay-faq), and Microsoft [confirms support for this package with WCF Relay continues until further notice](https://learn.microsoft.com/answers/questions/1822432/retiring-of-the-windowsazure-servicebus-nuget-pack). That is distinct from retiring the queued Service Bus messaging path that uses SBMP. Moving a queued workload to WCF Relay is not a migration to the current Service Bus client library. For a new live-connection design, prefer Hybrid Connections. Use WCF Relay when the WCF service contract must remain.

## 5. Validate the chosen destination

| Check | Service Bus | Hybrid Connections or WCF Relay |
| --- | --- | --- |
| Receiver offline | Message remains queued for later processing. | No request is queued for later delivery; handle a failed or timed-out call. |
| Duplicate or failed work | Exercise redelivery, idempotency, lock loss and dead-letter handling. | Define application retries and idempotency; there is no queue settlement. |
| Contract | Verify new payloads, headers, sessions and old-format backlog. | Verify the live request or WCF contract and listener lifecycle. |
| SBMP exit | Confirm the old sender and receiver are stopped and AMQP processing works. | Confirm old queued traffic is drained and no queued WCF endpoint still connects. |

Run the failure tests against the actual namespace and applications before cutover. A namespace-level SBMP brownout may reveal another legacy client unrelated to this binding; correlate an affected process with its deployed configuration rather than inferring its identity from protocol telemetry.
