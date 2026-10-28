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
| Queue updates | Requests auto-delete-on-idle of 5 minutes and disables expiration dead-lettering on the session queue while retaining its TTL and delivery limit. Validates each update response, then independently reads and reports each updated field while checking queue identity and unchanged settings. |
| Authorization | Lists namespace rules without retrieving namespace-wide keys. Adds temporary, separate queue-scoped Send and Listen rules, retrieves their keys in memory, and removes both rules after verification. Confirms each removed rule is absent from a successful complete rule list on the verified owned queue. |
| Sessions and ordering | Sends `Hello` and `Hello 1` sequentially with session ID `23424`, distinct message IDs and a per-run correlation ID. Accepts that session after sending, checks body/identity/order, and explicitly completes both messages in PeekLock mode. |
| Duplicate detection | Sends the same message ID twice to the second queue. Requires one matching received message, completes it, then checks for unexpected messages during a bounded five-second observation window. |
| Cleanup | Deletes each created queue, then the namespace, then the empty resource group. Awaits deletion and polls for resource-specific not-found responses. |

TTL and dead-lettering are **configuration demonstrations**, not a timed
expiration/max-delivery experiment. The requested auto-delete interval is five
idle minutes; an update acknowledgment does not demonstrate timed deletion.
The duplicate quiet window is a bounded observation, not proof of
indefinite absence. The old README's auto-forwarding and dead-letter-forwarding
entries marked `[Remove]` had no implementation; changing expiration
dead-lettering is not forwarding. Batching, scheduling and transactions were not
implemented in this sample and are not advertised here.

Each queue update requires a nonempty PUT response with the expected queue
identity and all requested typed settings. One independent immediate GET must
then return a nonempty model with that identity and every inspected field.
Unchanged settings must still match: duplicate detection, size and detection
window on the duplicate queue; sessions, TTL and delivery count on the session
queue. HTTP/transport errors, missing fields, wrong identities and mismatching
unchanged settings fail before messaging and still trigger owned cleanup.
Creation readbacks retain their strict settings checks.

The deliberately updated field is reported separately. Auto-delete-on-idle must
be a duration of at least five minutes; expiration dead-lettering must be a
Boolean. A valid difference from the requested value records
`immediate-get-readback=NOTPASS` with its exact expected and actual values, while
allowing the remaining lifecycle to continue. For example, PUT `PT5M` followed
by GET `PT10M` retains both values and does not confirm the requested interval.
The same applies to a session update acknowledging `false` followed by GET
`true`. Matching values record `PASS` for that observation only.

Each observation is printed when received and retained in the final summary,
including on later failure. Unreached observations are `NOT_RUN`, not `PASS`.
This separates an acknowledged update from immediate confirmation; it establishes
neither a read-your-write guarantee nor eventual convergence or a cache cause.
It adds no GET retry, polling or wait.

## Build and offline checks

Use **JDK 11 or later** and **Maven 3.9 or later**. Set both your IDE's project SDK
and Maven runner JDK to 11 or later; the POM compiles with `--release 11`.
The sample deliberately targets Java 11 even though the management SDK's base
classes support Java 8.

Pinned stable versions:

Last verified: October 9, 2026 (Maven Central).
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
providers; `Microsoft.ServiceBus` must already be registered. Both management
clients explicitly omit the SDK's automatic provider-registration policy while
retaining its other policies. Missing registration fails rather than attempting
a subscription-wide registration or retrying after registration.

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

Exit 0 means both update responses were validated, both independent GET
observations were valid, and all required authorization, session/dedup messaging,
settlement and owned cleanup completed. `Lifecycle=PASS` can coexist with
immediate update confirmation `NOTPASS`; exit 0 does not certify immediate
equality. Exit 1 means a required operation, observation, deadline or cleanup
failed, or required update evidence is missing. Exit 2 means invalid/missing
configuration. Later failures retain earlier readback differences and make the
lifecycle fail. Final output reports both queue-update results and the lifecycle
and cleanup status.
Client close operations run on Reactor's blocking-capable bounded-elastic
scheduler for completion, error and cancellation, not on the receive or
quiet-window timer callback. Completion and error wait for client cleanup.
An original operation failure stays primary if close also fails, with the
close failure retained as suppressed. A close failure after cancellation is
reported explicitly because cancellation has no downstream error channel.

Each receiver has one continuous subscription through validation, explicit
settlement and the full post-settlement quiet window. Cancelling after the
expected messages would close the SDK receive link before settlement, and a
session receiver does not support resubscribing for the quiet check. The sample
keeps demand for unexpected messages while settling and waiting, buffers only
the expected one or two messages, and rejects early stream completion. The
45-second receive deadline covers arrival of the expected batch; settlement
retains the SDK and scenario deadlines, followed by the five-second quiet window.

Diagnostics include the safe step and at most eight exception nodes across
causes and suppressed failures, with explicit truncation for longer or cyclic
graphs. Nested sample-owned operation labels distinguish ownership GETs and
queue authorization-rule listing, creation, key lookup, deletion and absence
readback. Queue updates distinguish `<queue>-update-response` from
`<queue>-immediate-get-readback` for transport and validation failures. Both
phases share the existing five-minute update-step deadline; expiration of an
enclosing step or scenario deadline cancels outstanding work at that boundary.
These labels preserve the original cause; they do not retry operations or change
the deadlines or accepted absence responses.
Each node can include the first `com.azure.*` or `reactor.core.*` frame
among its first 16 frames: only bounded class/method identifiers and the line
number, never file paths, arguments or a full stack. Where present, typed
Service Bus reasons, AMQP conditions, transient flags and HTTP status codes
are retained. SDK exception messages, request/response payloads and credentials
are not printed. Queue-setting failures identify the mismatching field and its
expected/actual boolean, size, count or duration, without logging the entire
model or changing the expected settings or retry behavior. Valid differences in
the deliberately updated field use the separate observation result above;
invalid observations and unchanged-setting mismatches remain failures.
Missing or truncated diagnostics do not establish a cause
or completed message counts. Cleanup failure overrides scenario success.

Resource group, namespace and queue absence checks require HTTP 404 and a
recognized structured error code, including
the queue service's `EntityNotFound`. Namespace absence checks additionally accept
`NamespaceNotFound`, which the resource provider can return after the namespace's
DELETE operation has completed. This does not prove resource-group absence:
cleanup still verifies group ownership, an empty resource inventory, group
deletion and the group's own absence. Queue and group checks do not accept
`NamespaceNotFound`. Ownership reads, `ParentResourceNotFound`,
`MessagingGatewayNotFoundStatusCode`, unknown codes and non-404 responses remain
failures.

Temporary authorization-rule deletion uses positive list evidence instead:
the group/namespace ownership and queue identity must still match, every rule
page must succeed with HTTP 200 and an explicit value array, and the removed
rule must be absent after the terminal page. Present rules are polled within
the existing step deadline. Missing/malformed pages, foreign rule identities,
invalid or repeated continuation links, and parent/list/authorization/transport
errors fail rather than proving absence. The raw generated SDK list model and
the existing management HTTP pipeline are used because the SDK's paged wrapper
normalizes a missing value array to an empty collection. Authorization-rule
absence is established by the complete list, not a 404 response. Response bodies
and credentials are not logged.

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
