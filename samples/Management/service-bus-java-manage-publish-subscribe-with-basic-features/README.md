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

# Service Bus publish/subscribe management in Java

This sample uses the supported Azure SDK for Java management and messaging libraries.
It creates a new resource group and namespace, exercises topic/subscription management,
verifies message delivery to both subscriptions, and waits for resource deletion.
**A live run creates billable Azure resources and deletes resources created by that run.**
No Azure operations occur without the explicit `--run` argument and valid configuration.

## Provenance

This sample originates from
[Azure-Samples/service-bus-java-manage-publish-subscribe-with-basic-features](https://github.com/Azure-Samples/service-bus-java-manage-publish-subscribe-with-basic-features),
baseline commit [`110b4c20064213337f846253c426dd782f2db236`](https://github.com/Azure-Samples/service-bus-java-manage-publish-subscribe-with-basic-features/commit/110b4c20064213337f846253c426dd782f2db236),
with subsequent modernization. The original repository is archived.
Microsoft copyright and the [MIT license](LICENSE) are retained. This sample
is maintained in `Azure/azure-service-bus`; follow the parent repository's
[contributing guide](../../../.github/CONTRIBUTING.md).

## Scenarios and options

Every live run performs the same management and messaging sequence:

1. Generate fresh resource group and namespace names, refuse collisions, and create
   a tagged namespace. Standard is the default; `--sku=premium` selects Premium with
   one messaging unit. Both require available regional quota.
2. Create a 2048 MB topic, read it back, then resize it to 3072 MB while adding the
   first subscription through the topic update. Create a second subscription with
   a 10-minute auto-delete-on-idle interval.
3. List the topic and both subscriptions, verifying their names and each subscription's
   single match-all `$Default` rule. Rules on a subscription are OR-combined: adding
   a restrictive rule without removing `$Default` would **not** restrict delivery.
   This sample does not add custom filters or rule actions.
4. List namespace authorization-rule metadata. With `--rotate-keys`, also retrieve
   the explicitly named `RootManageSharedAccessKey` keys and regenerate its secondary
   key. This optional administrative demonstration never prints or uses those keys.
5. Send `Hello World` with a unique message ID using Microsoft Entra authentication.
   The management SDK returns an HTTPS namespace endpoint; the sample passes only
   its hostname (without scheme, port, or trailing slash) to the messaging clients.
   Receive the matching ID and body from **each** subscription in PeekLock mode,
   explicitly complete both copies, and check a five-second quiet window for unexpected
   additional deliveries. Success requires readback and settlement, not just a send.
6. Remove the first subscription through a topic update, remove the second directly,
   and delete the topic and namespace. Verify removal, then delete the owned resource
   group and poll until it is absent.

| Option | Behavior |
| --- | --- |
| `--help` | Print usage without constructing clients or contacting Azure. |
| `--run` | Opt into provisioning, live messaging, and cleanup. Required for a live run. |
| `--sku=standard` | Standard namespace (default). Basic does not support topics and is rejected. |
| `--sku=premium` | Premium namespace, one messaging unit; higher charges may apply. |
| `--rotate-keys` | Opt into key retrieval and secondary-key rotation on this run's namespace only. |
| `--access-checkpoint=<new-absolute-directory>` | Optionally coordinate namespace-only access setup and role removal with an external administrator; each acknowledgment wait is limited to five minutes. Does not grant permission to change roles. |

## Build and offline checks

Use **JDK 11 or later** and **Maven 3.9.16 or later**. This project compiles with
`--release 11` and is exercised on Microsoft OpenJDK 11.0.32. The dedicated management
SDKs publish Java 8 base bytecode; Java 11 is this sample's minimum, not a claim that
the management SDK requires a newer JDK. In an IDE, import `pom.xml`, choose JDK 11+
for both the project and Maven runner, and use the main class below.

Stable versions verified in [Maven Central](https://repo.maven.apache.org/maven2/)
on 2026-10-09:

| Dependency/tool | Version |
| --- | --- |
| `azure-resourcemanager-servicebus` | 2.53.12 |
| `azure-resourcemanager-resources` | 2.55.0 (also required by the Service Bus management SDK) |
| `azure-messaging-servicebus` | 7.18.1 |
| `azure-identity` | 1.18.7 |
| Maven compiler / Shade plugins | 3.16.0 / 3.6.2 |
| Maven Surefire / Failsafe plugins | 3.6.0 / 3.6.0 |
| Maven exec plugin / JUnit | 3.6.4 / 4.13.2 |

```powershell
git clone https://github.com/Azure/azure-service-bus.git
cd azure-service-bus
cd .\samples\Management\service-bus-java-manage-publish-subscribe-with-basic-features
mvn clean verify
mvn exec:java "-Dexec.args=--help"
java -jar .\target\service-bus-java-manage-publish-subscribe-with-basic-features-0.0.1-SNAPSHOT-jar-with-dependencies.jar --help
```

The JAR main class is
`com.azure.resourcemanager.servicebus.samples.ServiceBusPublishSubscribeBasic`
(no `.java` suffix). The pinned Shade `shade` goal replaces the obsolete assembly
`attached` goal. It merges service-provider descriptors, Netty version metadata,
and license/notice files, and retains multi-release dependency classes.

Offline tests use fake operations and an in-memory HTTP transport for the actual
management SDK. The `verify` phase also launches isolated JVMs against the packaged
JAR, checks dependency/provider discovery and Netty metadata, and exercises help and
configuration-error exits. These tests do not acquire credentials, create resources,
send messages, or incur Azure charges. Missing-configuration launch checks prove the entry point
and nonzero exit behavior only; they are **not** live authentication or messaging tests.
Maven may download dependencies when they are not already cached.

## Configure and run against Azure

Use a dedicated, approved test subscription. Before running, arrange:

- A [DefaultAzureCredential](https://learn.microsoft.com/java/api/overview/azure/identity-readme#defaultazurecredential)
  identity (for example an already authenticated Azure CLI user, managed identity,
  or environment-configured service principal). `AZURE_TENANT_ID` and
  `AZURE_SUBSCRIPTION_ID` select the intended tenant and subscription; they are not
  sufficient credentials by themselves. Secrets, if needed by your chosen identity,
  must stay outside source control and command-line arguments.
- ARM permission to create/delete resource groups and manage Service Bus resources
  in that subscription, such as Contributor in an isolated lab subscription or a
  tailored role. `--rotate-keys` additionally needs key-list and regeneration actions.
- **Azure Service Bus Data Sender** and **Azure Service Bus Data Receiver** permissions
  inherited by the newly created namespace, or an administrator coordinating new
  namespace-only assignments through the optional checkpoint below. ARM Owner or
  Contributor alone does not grant messaging access. This program never creates
  or changes role assignments. Role propagation can take time.
- Access to Azure Resource Manager/identity HTTPS endpoints and Service Bus AMQP
  over TLS (port 5671). This sample targets public Azure.

Do not enable SDK/HTTP body, header, or verbose diagnostic logging for this sample.
`AZURE_LOG_LEVEL` must be unset or `5` (including a Java system-property override).
The pinned Azure Core SDK maps `5` to `NOT_SET`, which disables its default logger;
`none`, `off`, and blank values are not supported. The sample rejects other values
before client creation, disables management HTTP
logging, and prints only operation names and exception types on failure. Do not add
a logging backend that overrides this policy or share credential-bearing traces.

In PowerShell, replace the UUID placeholders with your approved configuration:

```powershell
$env:AZURE_TENANT_ID = "<tenant-uuid>"
$env:AZURE_SUBSCRIPTION_ID = "<subscription-uuid>"
$env:AZURE_REGION = "westus" # Optional; defaults to westus
$env:AZURE_LOG_LEVEL = "5"

# Choose one entry point, not both (each invocation creates a separate environment).
mvn compile exec:java "-Dexec.args=--run"
# Or, after mvn verify:
java -jar .\target\service-bus-java-manage-publish-subscribe-with-basic-features-0.0.1-SNAPSHOT-jar-with-dependencies.jar --run
```

For the Premium variant, append `--sku=premium` to the application's arguments.
For the key-management variant, append `--rotate-keys`; it can be combined with
either SKU. These options execute real Azure operations and require separate approval
in environments where provisioning or key rotation is restricted.

Exit codes are `0` for help or a fully successful run, `2` for configuration errors,
and `1` for an operation or cleanup failure. `exec:java` turns a nonzero application
exit into a failed Maven invocation (Maven's process exit code need not equal the
application's exit code).

### Optional namespace-only access coordination

The default remains noninteractive and requires inherited data access. If that
access is unavailable, an approved external coordinator can use
`--access-checkpoint=<new-absolute-directory>` with either entry point, SKU, and key
option. The directory must not exist; its parent must already exist. The sample
creates it before initializing Azure clients and refuses to reuse it. Use a private
local directory writable only by the sample operator and trusted coordinator.

Immediately after confirmed namespace creation, the sample writes `access-request.txt`
and waits up to five minutes for `access-ready.txt`. Each file contains exactly the
new namespace resource ID followed by one LF, in UTF-8 without a BOM. The coordinator
must verify the run's confirmed creation and ownership, arrange and read back only
the approved new namespace-scoped Data Sender and Data Receiver assignments for the
sample identity, then atomically publish the matching acknowledgment. Do not use a
fixed delay as permission evidence or grant subscription-wide access to avoid this
checkpoint. An acknowledgment permits the messaging attempt; it is not proof of
broker authorization or successful delivery. The existing bounded send/readback/
settlement operations still determine success, including propagation failures.

Before namespace deletion, or before resource-group cleanup following a failure,
the sample writes `cleanup-request.txt` and waits up to five minutes for
`roles-removed.txt`, with the same exact namespace-ID content. The coordinator must
remove and confirm absence of only the assignments it created for this run before
publishing that acknowledgment. Never alter existing assignments. A failed or timed-out
cleanup checkpoint blocks both namespace and resource-group deletion and is not
retried by the `finally` cleanup path. If creation was never confirmed and no access
request was attempted, no access/role-removal handshake is needed.

The sample publishes each request using a same-directory hard link after writing
all bytes to a temporary file, without replacing an existing request. The checkpoint
filesystem must support hard links; unsupported publication fails closed.

Publish acknowledgments by writing a temporary file in the same directory and
atomically renaming it to the expected filename; partial, mismatched, or non-regular
files fail closed. Keep the directory as evidence until cleanup is independently
confirmed. A coordinator failure or terminated JVM can leave billable resources
and assignments; inspect the exact IDs and obtain approved recovery authority.
The checkpoint itself grants no Azure or key-rotation permission, makes no role
changes, and does not expand the original scenarios.

## Ownership, limits, and cleanup failures

The program does not accept existing resource names and never deliberately adopts
resources. Each invocation generates UUID-based names, checks for collisions before
creation, and verifies its ownership tag. A failed or timed-out resource-group create
does not grant cleanup ownership. Names and resource IDs are printed before creation
so that ambiguous operations can be investigated without rerunning the sample.

Before destructive operations the resource-group and namespace tags must still match this run.
Resource-group cleanup also refuses unexpected resources or a namespace with a
different ownership tag. Use an isolated test environment: Azure create/update APIs
are not a cross-client locking protocol. Do not have other actors mutate these
resources concurrently, even if names or tags are known.

Management operations (including paginated listing and creation polling) use
five-minute reactive waits; deletion plus absence polling uses ten-minute reactive
waits. HTTP attempts have 10-second connect and 30-second response/read/write
timeouts, with at most two retries. Send and each subscription's
receive/settlement/quiet-window sequence use 60-second waits; AMQP attempts use
15 seconds with two retries. The receive subscription stays open through settlement
and the five-second duplicate quiet window.

These settings are not hard whole-process or credential-subprocess deadlines.
In particular, Azure Identity 1.18.7's CLI credential reads process output before its
timed process wait; synchronous credential work can outlive a reactive wait.
An external runner's root-process return alone does not prove that descendants,
SDK background activity or service-side work have stopped.
The optional access and role-removal acknowledgment waits are each limited to
five minutes.

Cleanup runs after failure as well as success. A delete request being accepted is
not completion: `Confirmed deleted` is printed only after absence is observed.
Any failed or timed-out deletion makes the run fail. Cancellation cannot roll back an
Azure request already accepted by the service; resources can remain billable.
If cleanup cannot verify ownership, encounters unrelated resources, or times out,
use the printed IDs to inspect the state and arrange approved manual cleanup.
Do not assume a nonzero exit means no resources exist, and do not rerun to clean
up a previous invocation (each invocation uses new names).

## More information

- [Azure management libraries for Java](https://aka.ms/azsdk/java/mgmt)
- [Service Bus messaging for Java](https://learn.microsoft.com/java/api/overview/azure/messaging-servicebus-readme)
- [Service Bus authentication and authorization](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-authentication-and-authorization)
- [Report a sample issue](https://github.com/Azure/azure-service-bus/issues)

---

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.
