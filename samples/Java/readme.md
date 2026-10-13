# Java samples for Azure Service Bus

For the supported Java client library, use the [azure-messaging-servicebus samples](https://github.com/Azure/azure-sdk-for-java/tree/main/sdk/servicebus/azure-messaging-servicebus/src/samples/java/com/azure/messaging/servicebus)
and the local [feature scenarios](azure-messaging-servicebus): request/reply,
duplicate detection, TTL/DLQ, autoforwarding, partitioning and WebSockets/proxy
configuration. The local native Java examples and the JMS default use Azure
Identity.

The `com.microsoft.azure:azure-servicebus` library was retired on September 30, 2026. Its samples have been removed from this directory. Use the [migration guide](https://github.com/Azure/azure-sdk-for-java/blob/main/sdk/servicebus/azure-messaging-servicebus/migration-guide.md) to update existing applications.

## JMS samples

These samples default to Microsoft Entra ID on **Service Bus Premium**, using
the Service Bus JMS `TokenCredential` factory. Explicit SAS mode preserves
direct Apache Qpid JMS coverage on **Standard or Premium**. Neither mode uses
the retired native Java client library:

- [Queues](qpid-jms-client/JmsQueueQuickstart)
- [Topics and subscriptions](qpid-jms-client/JmsTopicQuickstart)

For JMS 2.0 with Service Bus Premium, see the [Azure Service Bus JMS samples](https://github.com/Azure/azure-servicebus-jms-samples).

## Prerequisites

All Java projects require JDK 11 or later and Maven 3.9 or later. For the JMS
examples, `com.microsoft.azure:azure-servicebus-jms` 1.0.2 and Qpid JMS 1.17.0
preserve the `javax.jms` API; this JMS artifact is distinct from the retired
`com.microsoft.azure:azure-servicebus` native library. Azure Identity 1.18.7
provides `DefaultAzureCredential`. The `com.azure:azure-servicebus-jms` 2.x
artifact and Qpid 2.x use `jakarta.jms`, which is not a drop-in upgrade.
The Netty 4.1.138.Final BOM aligns Qpid and Azure Identity's HTTP transport
dependencies. Executable JARs merge service-provider metadata for Azure Identity.
Logging uses SLF4J 2.0.20 with its simple provider, not end-of-life Log4j 1.x.
Create a [Service Bus namespace](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-quickstart-portal) and the entities used by your sample:

- Queue sample: `BasicQueue`.
- Topic sample: `BasicTopic` with subscriptions `Subscription1`, `Subscription2`, and `Subscription3`.

For both standalone runs and integration tests, use disposable, empty entities
with no competing consumers or autoforwarding. Each topic subscription must have
one match-all rule so it receives all ten sample messages. The queue run expects
ten distinct messages; the topic run expects ten per subscription. Both validate
the run ID, sequence and body before acknowledgment. Inspect leftovers after any
failure, and delete only resources you created for the test.

### Microsoft Entra ID (default, Premium)

Sign in locally with `az login`, or configure a managed identity when running in
Azure. Set `SB_SAMPLES_NAMESPACE` to the Premium namespace host, such as
`example.servicebus.windows.net`. Leave `SB_SAMPLES_AUTH` unset, or set it to
`entra`. No SAS keys are required or read in this mode.

An administrator with role-assignment permission must grant **Azure Service Bus
Data Sender** and **Azure Service Bus Data Receiver** to the identity selected by
`DefaultAzureCredential`. Use the queue resource scope for `BasicQueue`, or the
topic scope for `BasicTopic` and its three subscriptions:

```powershell
$scope = "<resource ID of BasicQueue or BasicTopic>"
$principalId = "<object ID of the user, service principal, or managed identity>"
az role assignment create --assignee-object-id $principalId --role "Azure Service Bus Data Sender" --scope $scope
az role assignment create --assignee-object-id $principalId --role "Azure Service Bus Data Receiver" --scope $scope
```

Azure subscription Owner or Contributor alone does not grant messaging access.
[Allow up to five minutes for RBAC propagation](https://learn.microsoft.com/azure/service-bus-messaging/authenticate-application).
The applications do not assign roles. Authentication failures are reported,
not retried through SAS. The [JMS developer guide](https://learn.microsoft.com/azure/service-bus-messaging/jms-developer-guide#connection-factory)
describes the Premium `TokenCredential` factory and managed-identity options.

### Explicit SAS compatibility mode (Standard or Premium)

Use this mode only to demonstrate direct-Qpid JMS interoperability with
pre-created entities, including Standard's limited JMS 1.1 mapping. Set
`SB_SAMPLES_AUTH=sas` and create a shared access policy with Send and Listen
permissions scoped to the test queue or topic, not RootManageSharedAccessKey.
Local authentication must be enabled. Set these variables before running the
samples or their opt-in integration tests:

| Variable | Value |
| --- | --- |
| `SB_SAMPLES_NAMESPACE` | Namespace host, such as `example.servicebus.windows.net`. |
| `SB_SAMPLES_AUTH` | `sas` for explicit compatibility mode; default is `entra`. |
| `SB_SAMPLES_SAS_KEY_NAME` | Shared access policy name. |
| `SB_SAMPLES_SAS_KEY` | Shared access policy key. Keep this value out of source control. |

The `-n` argument can supply the namespace host instead of `SB_SAMPLES_NAMESPACE`.
The environment variable takes precedence. SAS credentials are read only from
the environment in explicit SAS mode. An empty or unknown `SB_SAMPLES_AUTH`
value fails; there is no automatic mode detection.

## Build

From this directory, run `mvn clean package` to build all three projects and run offline tests.
JMS live tests are **opt-in**: after setting the variables and preparing disposable,
empty entities, run `mvn -Dservicebus.integration=true test`. This sends and
consumes messages. JMS receive loops time out after 90 seconds per queue or
subscription instead of waiting indefinitely. Each sample README gives its run
command. `mvn -DskipTests package` skips all tests, not just live tests.

## Optional deployment template

The [template](scripts/azuredeploy.json) creates a billable **Standard** namespace
with the JMS entities for **explicit SAS compatibility mode** and additional filter, duplicate-detection, partitioning
and autoforwarding examples. It is not a Premium or Geo-DR deployment. Use only
an approved disposable resource group and a new globally unique namespace name;
verify that the namespace does not already exist before deploying. Replace
`GEN-UNIQUE` in [the parameters file](scripts/azuredeploy.parameters.json).
ARM deployment can update existing resources, so this template is not an
ownership guard. For the default Entra JMS mode, create a Premium namespace
and the required empty entities separately; changing the template's SKU alone
is not sufficient because its partitioned queue demonstrates Standard entity
partitioning.

The filter subscriptions replace `$Default` instead of leaving a match-all
rule alongside the intended filter. The `SendListen` policy grants messaging,
not management. Outputs contain only the namespace name, never keys or
connection strings. Obtain test credentials through an authorized secure path;
do not capture them in deployment logs. Inspect deployed entity/rule settings
and test routing before treating deployment as successful. Remove only the
owned disposable resources after testing. Local JSON checks are not Azure
template validation or deployment evidence.
