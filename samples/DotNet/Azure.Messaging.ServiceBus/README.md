# .NET Service Bus samples

All projects in this folder target **.NET 10** and use
`Azure.Messaging.ServiceBus` **7.21.0**. Install the
[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

| Sample | Run/setup |
| --- | --- |
| [FeatureScenarios](FeatureScenarios) | Request/reply, duplicate detection, TTL/DLQ, partitioning/session keys and transactions; Azure Identity and opt-in temporary entities |
| [BasicSendReceiveTutorialwithFilters](BasicSendReceiveTutorialwithFilters) | Interactive SQL filters/actions for subscriptions S1, S2 and S3 |
| [TopicSubscriptionFilters](TopicSubscriptionFilters) | Create filtered subscriptions, then send and receive sample orders |
| [MessageSender](MessageSender) | Send five messages to an existing topic |
| [ServiceBusEventGridIntegrationV2](ServiceBusEventGridIntegrationV2) | .NET 10 isolated-worker Event Grid function for a Premium topic subscription |

## Existing topic examples

Basic filters, TopicSubscriptionFilters and MessageSender use Microsoft Entra ID
through `DefaultAzureCredential`, as do the feature scenarios and Event Grid
consumer. Sign in locally with `az login`, or configure a managed identity when
running in Azure. Set the namespace host, not a connection string:

```powershell
$env:SERVICEBUS_FULLY_QUALIFIED_NAMESPACE = "example.servicebus.windows.net"
$env:SERVICEBUS_TOPIC = "<existing-topic>"
```

From this directory:

```powershell
dotnet run --project .\MessageSender
dotnet run --project .\BasicSendReceiveTutorialwithFilters\BasicSendReceiveTutorialWithFilters
```

MessageSender requires **Azure Service Bus Data Sender** on the topic. Basic
filters requires **Azure Service Bus Data Owner** on its disposable topic for
rule changes and messaging. TopicSubscriptionFilters setup requires Data Owner
on the namespace; its send/receive application requires Data Sender and
**Azure Service Bus Data Receiver** on the topic. Azure subscription Owner or
Contributor alone does not grant message access.

An administrator with role-assignment permission can grant the required role:

```powershell
$scope = "<Service Bus topic or namespace resource ID>"
$principalId = "<object ID of the user, service principal, or managed identity>"
az role assignment create --assignee-object-id $principalId --role "Azure Service Bus Data Sender" --scope $scope
```

Use the role and narrowest scope listed above for the application you run.
[Role assignments can take up to five minutes to propagate](https://learn.microsoft.com/azure/service-bus-messaging/authenticate-application).
The applications do not create role assignments or fall back to SAS.

For Basic filters, first create a topic
and subscriptions `S1`, `S2`, `S3` in a disposable **Standard or Premium**
namespace. Its menu removes the subscriptions' default rules before installing
the sample filters. Do not point it at production subscriptions. The existing
`-FullyQualifiedNamespace` / `-TopicName` arguments can override the environment.
`SERVICEBUS_CONNECTION_STRING` and `-ConnectionString` are no longer used.

The Basic filters cleanup option deletes only custom rules successfully created
by the current process; it does not restore default rules or remove other rules.
Inspect printed rule names after partial failures. Sends and receives have
two-minute deadlines and propagate failures. Run its project with `--self-test`
for six offline ownership, cleanup, send/receive, payload and cancellation checks;
these do not connect to Azure.

TopicSubscriptionFilters has its own setup/run instructions. Its send/receive
example uses ReceiveAndDelete for simplicity, which can lose a message if
processing fails; use PeekLock and explicit settlement for reliable processing.
These older teaching examples leave resources in place for manual cleanup.

## Reuse the SDK examples

The [current .NET SDK catalog](https://github.com/Azure/azure-sdk-for-net/tree/main/sdk/servicebus/Azure.Messaging.ServiceBus/samples)
also covers processors, sessions, scheduling, administration and diagnostics.
Use its [autoforward-to-session example](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/servicebus/Azure.Messaging.ServiceBus/samples/Sample21_AutoForwardIntoSession.md)
instead of duplicating that coverage here.
