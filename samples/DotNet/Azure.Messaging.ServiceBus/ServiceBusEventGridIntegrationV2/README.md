# Service Bus Event Grid integration (.NET isolated worker)

This Functions v4 / .NET 10 example receives an
`Microsoft.ServiceBus.ActiveMessagesAvailableWithNoListeners` Event Grid event for
a **topic subscription**, then receives and completes up to 100 messages.
It preserves the sample's single-batch behavior; it is not a continuously running
consumer or a guarantee to drain a backlog. For ordinary continuous processing,
use the [Service Bus trigger](https://learn.microsoft.com/azure/azure-functions/functions-bindings-service-bus-trigger).
Processing must be idempotent: Event Grid notifications and Service Bus messages
can be delivered more than once.

## Build and run locally

Run `dotnet run --project .\tests\FunctionSmokeTests.csproj` from this folder
for nine offline checks covering the isolated binding, real Event Grid payload
decoding, mocked receive/settlement/disposal, malformed/unsupported events and
cancellation, empty batches, receive/settlement failures and mid-batch
cancellation. These do not start a Functions host or connect to Azure.

Install the .NET 10 SDK, Azure Functions Core Tools v4 and Azurite. Use a
disposable **Premium** Service Bus namespace with a topic and subscription
(Service Bus Event Grid integration requires Premium).

From `ReceiveMessagesFunctionApp`:

```powershell
dotnet build
Copy-Item local.settings.example.json local.settings.json
az login
dotnet run
```

Before running, set `ServiceBusFullyQualifiedNamespace` in the local settings
file to your namespace's fully qualified hostname. Start Azurite for
`UseDevelopmentStorage=true`. `DefaultAzureCredential` uses your developer
identity locally; it needs **Azure Service Bus Data Receiver** access to the
subscription. Do not commit `local.settings.json` or credentials.

To exercise the complete integration, create an Event Grid subscription for
`Microsoft.ServiceBus.ActiveMessagesAvailableWithNoListeners`, filtered to the
test topic subscription. Target `EventGridTriggerFunction`. A local endpoint
requires secure public forwarding and Event Grid endpoint validation; running
the host alone does not test delivery. Use the separate MessageSender application
to enqueue messages while no receiver is attached.

## Deploy

Create a Functions v4 app configured for **.NET 10 isolated**. Use Flex
Consumption or another plan supporting .NET 10; **Linux Consumption does not
support .NET 10**. Configure `FUNCTIONS_WORKER_RUNTIME=dotnet-isolated` where the
hosting plan uses that setting, `ServiceBusFullyQualifiedNamespace`, and real
host storage (`AzureWebJobsStorage` or its identity-based equivalent).

Enable the app's managed identity and grant it Azure Service Bus Data Receiver
on the target subscription. Host storage needs its own permissions. Publish the
function, then connect the Event Grid subscription to the Azure Function
endpoint. Do not reuse machine-specific Visual Studio deployment profiles.
The old fixed-target generated dependency templates and their Visual Studio
metadata have been removed; they are not a supported deployment path. Provision
host storage and optional monitoring explicitly for your own app and scope.
Deployment creates resources and can incur charges; it is a separate operation
from building this project.

See the [isolated worker guide](https://learn.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide)
and [Service Bus Event Grid overview](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-to-event-grid-integration-concept).
