---
page_type: sample
languages:
- java
products:
- azure
extensions:
  services: Servicebus
  platforms: java
---

# Service Bus advanced queues in Java

Provenance: adapted from
[Azure-Samples/service-bus-java-manage-queue-with-advanced-features](https://github.com/Azure-Samples/service-bus-java-manage-queue-with-advanced-features)
at baseline `665f98b321657133a2d6686136b0461fea2b1748`, with local modernization
changes. The original repository is archived. The original Microsoft copyright
and [MIT license](LICENSE) are retained. Contributions to this sample follow the parent
repository's [contribution guidance](../../../.github/CONTRIBUTING.md).

This sample uses the supported `com.azure` messaging and resource-manager SDKs.
It creates its own resource group and Service Bus namespace, demonstrates advanced
queue configuration and messaging, and verifies deletion before reporting success.
**Live execution creates billable Azure resources. It is never the default.**

## Scenarios

| Scenario | Behavior |
| --- | --- |
| Namespace | Standard by default; `--sku=premium` selects Premium with one messaging unit. |
| Session queue | Requires sessions, default TTL of 10 minutes, expiration dead-lettering enabled, maximum delivery count 40. |
| Duplicate-detection queue | 2,048 MB, 10-minute duplicate-detection window, initially 10-minute auto-delete-on-idle. |
| Queue updates | Changes auto-delete-on-idle to 5 minutes; disables expiration dead-lettering on the session queue while retaining its TTL and delivery limit. Reads back both configurations. |
| Authorization | Lists namespace rules without retrieving namespace-wide keys. Adds temporary, separate queue-scoped Send and Listen rules, retrieves their keys in memory, and removes both rules after verification. |
| Sessions and ordering | Sends `Hello` and `Hello 1` sequentially with session ID `23424`, distinct message IDs and a per-run correlation ID. Accepts that session after sending, checks body/identity/order, and explicitly completes both messages in PeekLock mode. |
| Duplicate detection | Sends the same message ID twice to the second queue. Requires one matching received message, completes it, then checks for unexpected messages during a bounded five-second observation window. |
| Cleanup | Deletes each created queue, then the namespace, then the empty resource group. Awaits deletion and polls for resource-specific not-found responses. |

TTL and dead-lettering are **configuration demonstrations**, not a timed
expiration/max-delivery experiment. The second queue can auto-delete after five
idle minutes. The duplicate quiet window is a bounded observation, not proof of
indefinite absence. The old README's auto-forwarding and dead-letter-forwarding
entries marked `[Remove]` had no implementation; changing expiration
dead-lettering is not forwarding. Batching, scheduling and transactions were not
implemented in this sample and are not advertised here.

## Build and offline checks

Use **JDK 11 or later** and **Maven 3.9 or later**. Set both your IDE's project SDK
and Maven runner JDK to 11 or later; the POM compiles with `--release 11`.
The sample deliberately targets Java 11 even though the management SDK's base
classes support Java 8.

Pinned stable versions, checked against Maven Central on October 9, 2026:
`azure-messaging-servicebus` 7.18.1, `azure-identity` 1.18.7,
`azure-resourcemanager-servicebus` 2.53.12 (which depends on
`azure-resourcemanager-resources` 2.55.0). The dedicated management packages
replace the aggregate management dependency and unused multi-service utility.

Clone the parent repository and enter the nested sample
directory in PowerShell. For an existing parent checkout, start at its root and
use only the second `Set-Location` command.

```powershell
git clone https://github.com/Azure/azure-service-bus.git
Set-Location .\azure-service-bus
Set-Location .\samples\Management\service-bus-java-manage-queue-with-advanced-features

# Set JAVA_HOME to your installed JDK 11 (or later) directory.
$env:PATH = "$env:JAVA_HOME\bin;$env:PATH"
java -version
mvn -version
mvn --batch-mode --no-transfer-progress clean verify

$jar = ".\target\service-bus-java-manage-queue-with-advanced-features-0.0.1-SNAPSHOT-jar-with-dependencies.jar"
java -jar $jar --help
mvn --batch-mode --no-transfer-progress exec:java "-Dexec.args=--help"

# Intentional configuration failure: exits 2 without constructing Azure clients.
java -jar $jar
```

Tests use local SDK models, fake management interfaces and reactive message
streams; they do not authenticate, retrieve real keys, provision Azure resources,
or send live messages. Maven may download dependencies on the initial build.
The shaded executable JAR merges service-provider metadata and names the actual
main class (without a `.java` suffix). `exec:java` also preserves failure status.

## Opt-in live execution

Run this section only after separately approving the subscription, region, cost
and resource lifecycle. Offline checks do not establish live service correctness.

Use an isolated development subscription or otherwise ensure no other actor
modifies the generated group and namespace during a run. The authenticated
identity needs management permissions to create/read/delete resource groups and
Service Bus namespaces/queues, enumerate resources and authorization rules, and
create/list keys/delete queue authorization rules (for example, Contributor at
the chosen subscription). The sample does **not** assign roles or register
providers; `Microsoft.ServiceBus` must already be registered.

Configure an approved authentication method supported by
[DefaultAzureCredential](https://learn.microsoft.com/java/api/overview/azure/identity-readme#defaultazurecredential)
before running. Do not put credentials in source, command arguments or checked-in
configuration. The management credential is used only for control-plane calls.
Messaging uses a Send-only key and a separate Listen-only key for each created
queue, not the namespace's RootManageSharedAccessKey. Consequently local/SAS
authentication must be permitted by policy. No keys or connection strings are
printed; do not enable SDK HTTP body/header, AMQP trace or credential logging.

```powershell
$env:AZURE_TENANT_ID = "<tenant UUID>"
$env:AZURE_SUBSCRIPTION_ID = "<subscription UUID>"
$env:AZURE_LOCATION = "westus"

# Choose one; these commands create resources and send messages.
java -jar $jar --run
java -jar $jar --run --sku=premium
# Equivalent Maven entry point, after building:
mvn --batch-mode --no-transfer-progress exec:java "-Dexec.args=--run"
```

Only Azure public cloud is configured by this sample. Subscription and tenant
must be explicit UUIDs; no default subscription is selected. Resource names are
fresh UUID-based names, not caller-supplied existing names.

## Ownership, deadlines and failures

The sample checks group existence and global namespace availability before
creation, rejects a group create response that indicates an update, and tracks
only confirmed creations. It stamps the group and namespace with a per-run
ownership tag, rechecks identities/tags before mutations, refuses unexpected
queues/topics, and refuses to delete a nonempty resource group. A collision never
authorizes adoption or deletion of an existing resource.

Azure Resource Manager create-or-update operations are not atomic name
reservations. Random names, preflight checks and ownership readback reduce
collision risk but do not protect against a concurrent actor deliberately
replacing resources. Keep the generated resources exclusive to the run.

The provisioning/messaging budget is 15 minutes; cleanup has a separate 10-minute
budget. Each management step is capped at five minutes, including deletion
polling. Receive/validation is capped at 45 seconds. AMQP attempts are capped at
20 seconds with at most two retries; HTTP connection timeout is 10 seconds and
read/write/response timeouts are 30 seconds. Expiry cancels the reactive
subscription and messaging clients close on completion, failure or cancellation.
Cancellation does **not** roll back an Azure operation already accepted by the
service.

Exit 0 means all required scenarios and cleanup completed; exit 1 means an
operation or cleanup failed; exit 2 means invalid/missing configuration.
Diagnostics include safe step/type information, never SDK exception text or raw
response bodies. Cleanup failure overrides scenario success.

Keep the resource IDs and ownership token printed at startup until cleanup is
verified. If creation times out before ownership is confirmed, deletion is
uncertain, or ownership/contents changed, the sample fails rather than adopting
or deleting resources speculatively. A terminated process cannot guarantee
cleanup. Inspect the exact printed IDs and tags in Azure, verify that the
resources belong solely to this run and that no operation is still in flight,
then perform separately authorized cleanup. Do not delete unrelated contents.

## More information

- [Azure management libraries for Java](https://aka.ms/azsdk/java/mgmt)
- [Azure Service Bus messaging library for Java](https://learn.microsoft.com/java/api/overview/azure/messaging-servicebus-readme)
- [Report a sample issue](https://github.com/Azure/azure-service-bus/issues)

---

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.
