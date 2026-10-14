# Service Bus management sample

Create an isolated resource group and Service Bus namespace, a queue, a topic,
a subscription, and a SQL rule using .NET 10 and the supported Azure SDKs.
Read back every resource's settings, optionally verify message routing, and
optionally delete resources that this invocation positively owns.

This sample uses **Azure Resource Manager for resource groups and namespaces**
and **`ServiceBusAdministrationClient` for create-only entity operations**.
It does not update or adopt an existing namespace. The original queue, topic,
subscription and rule demonstrations remain: `queue1`, `topic1`, the `topic1`
subscription with `MaxDeliveryCount = 10`, and `rule1` with `myproperty='test'`.
The rule is supplied atomically when creating the subscription, replacing the
normal `$Default` match-all rule rather than coexisting with it.

## Provenance

This sample originates from
[Azure-Samples/service-bus-dotnet-management](https://github.com/Azure-Samples/service-bus-dotnet-management),
baseline commit
[`a55185bef30d1763c1a8182a3361dbb548bad436`](https://github.com/Azure-Samples/service-bus-dotnet-management/tree/a55185bef30d1763c1a8182a3361dbb548bad436),
with subsequent local modernization. The original repository is archived.
The original Microsoft copyright and MIT license are retained in [LICENSE](LICENSE).
For contributions here, use the parent repository's
[contributing guide](../../../.github/CONTRIBUTING.md).

## Prerequisites and versions

Use the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
`global.json` selects **10.0.401**, allowing later stable patches in that feature
band. Both projects target `net10.0`; use an IDE that supports that SDK or the
commands below. No Azure account is required for builds, offline tests, help,
validation or preview.

Direct package versions, verified against the NuGet Gallery on October 9, 2026:

| Package | Version | Use |
| --- | --- | --- |
| [Azure.Identity](https://www.nuget.org/packages/Azure.Identity/1.21.0) | 1.21.0 | Explicit Azure CLI or managed-identity credentials |
| [Azure.ResourceManager.Resources](https://www.nuget.org/packages/Azure.ResourceManager.Resources/1.12.0) | 1.12.0 | Resource group lifecycle |
| [Azure.ResourceManager.ServiceBus](https://www.nuget.org/packages/Azure.ResourceManager.ServiceBus/1.2.0) | 1.2.0 | Namespace lifecycle and readback |
| [Azure.Messaging.ServiceBus](https://www.nuget.org/packages/Azure.Messaging.ServiceBus/7.21.0) | 7.21.0 | Entity administration and optional routing verification |

Package lock files record transitive dependencies. There is no ADAL token
cache/timer, fixed access token, legacy Fluent stack, secret-manager CLI tool
reference, or retired Service Bus client dependency.

## Build and offline checks

Clone [Azure/azure-service-bus](https://github.com/Azure/azure-service-bus) if
you do not already have a checkout, then enter this sample directory:

```powershell
git clone https://github.com/Azure/azure-service-bus.git
Set-Location .\azure-service-bus\samples\Management\service-bus-dotnet-management
```

For an existing checkout, start at the `Azure/azure-service-bus` repository
root and use:

```powershell
Set-Location .\samples\Management\service-bus-dotnet-management
```

Run all remaining commands from
`samples\Management\service-bus-dotnet-management`, **not** the parent repository
root. This working directory selects the sample's `global.json` and keeps
solution, project, configuration and inventory paths local to this sample.

```powershell
dotnet restore .\service-bus-dotnet-management.sln --locked-mode
dotnet build .\service-bus-dotnet-management.sln --configuration Release --no-restore
dotnet run --project .\tests\ManagementSample.Tests --configuration Release --no-build
dotnet run --project .\src\service-bus-dotnet-management --configuration Release --no-build -- --help
```

The focused test project is a console test harness: **use `dotnet run`, not
`dotnet test`**. It exits nonzero on any failure, reports passed/failed/skipped
counts, and uses fakes and in-process HTTP transports, not Azure endpoints.
It exercises the actual SDK request serializers and credential policy as well
as configuration, consent, ownership, collision, rule, failure and cleanup
contracts. These checks do not establish live Azure behavior.

## Configuration and authentication

Copy the checked-in configuration to an ignored local file:

```powershell
Copy-Item .\src\service-bus-dotnet-management\appsettings.json .\appsettings.local.json
```

Edit **nonsecret** settings in that file. The default file is loaded from beside
the executable, not the current directory; use `--config` for an explicit file.
Unknown JSON properties are errors, including the old `ClientSecret`,
`ClientId`, `ServiceBusSkuName` and `ServiceBusSkuTier` settings.

| Setting | Meaning |
| --- | --- |
| `SubscriptionId` | Required nonempty subscription GUID; never inferred from the active CLI subscription |
| `Credential` | Required explicit selection: `azure-cli` or `managed-identity` |
| `TenantId` | Required GUID for `azure-cli`; leave empty for managed identity |
| `ManagedIdentityClientId` | Optional user-assigned identity GUID for `managed-identity`; empty explicitly selects the system-assigned identity |
| `DataCenterLocation` | Azure location name, default `eastus`, not the display name `East US` |
| `ServiceBusSku` | `Standard` (default) or `Premium`; Basic is rejected before authentication |
| `PremiumCapacity` | Premium messaging units: 1, 2, 4, 8 or 16 |
| `PremiumMessagingPartitions` | Premium namespace partitions: 1, 2 or 4; capacity must be at least this value |
| `ResourceGroupPrefix`, `NamespacePrefix` | 3-16 lowercase letters/digits/hyphens, beginning with a letter; defaults `sb-management` |
| `OperationTimeoutSeconds` | 1-3600 seconds for creation, the access checkpoints, readback and optional messaging together; default 900 |
| `CleanupTimeoutSeconds` | 1-1800 seconds **per resource**, independent of the operation timeout; default 300 |

Standard uses partitioned queues/topics and leaves the Premium settings at
their default values of 1. Premium sets capacity and partition count on the
namespace. Queue and topic creation requests must match that topology:
`EnablePartitioning` is `false` for one namespace partition and `true` for two
or four. This flag does not change namespace partitioning; messaging units are
not a substitute for the partition count. Regional Premium availability and
quota must be checked before an authorized run.

These environment variables override their JSON counterparts:
`SB_SAMPLE_SUBSCRIPTION_ID`, `SB_SAMPLE_TENANT_ID`, `SB_SAMPLE_CREDENTIAL`,
`SB_SAMPLE_MANAGED_IDENTITY_CLIENT_ID`, `SB_SAMPLE_LOCATION`, `SB_SAMPLE_SKU`.
There is no unprefixed Azure environment credential fallback.

For `azure-cli`, arrange a sign-in to the intended tenant separately, then the
sample uses only `AzureCliCredential` pinned to `TenantId`. For
`managed-identity`, run on a supported Azure host with the chosen identity
already configured. Neither mode falls through to a different account,
developer tool or credential chain. One `TokenCredential` is shared across
the ARM, administration and messaging clients; Azure Identity and the SDK
authentication policies acquire/cache/refresh tokens. No application token
expiration arithmetic is used.

**Do not put passwords, connection strings, SAS keys, client secrets or tokens
in source, JSON, command arguments or logs.** The sample has no secret-valued
configuration inputs, disables namespace local/SAS authentication, never
retrieves keys, and suppresses arbitrary SDK exception text. It does not assign
roles, register providers or change your CLI subscription.

### Offline validation and preview

```powershell
dotnet run --project .\src\service-bus-dotnet-management --configuration Release --no-build -- --validate --config .\appsettings.local.json
dotnet run --project .\src\service-bus-dotnet-management --configuration Release --no-build -- --plan --config .\appsettings.local.json
```

Validation checks all settings without constructing a credential. Preview
prints IDs and the configured tier/partitioning/rule without calling Azure.
Each invocation generates new names; a preview's suffix is **not** reused by a
later run. No arguments displays help, including when launched from an IDE.

## Permissions and authorized live execution

**Running the sample provisions billable resources.** Obtain approval for a
disposable test subscription/scope, tier/region, cost ceiling, identity, entity
administration, optional message traffic and cleanup separately. Merely being
signed in is not authorization. This sample supports Azure public cloud only.
Live execution and independent review are not proven by a build or offline test.

Have an authorized administrator arrange the necessary access outside this
sample. Do not grant subscription-wide Owner or general administrator roles
for this demonstration.

* ARM resource-group creation needs
  `Microsoft.Resources/subscriptions/resourceGroups/read` and `write` at the
  disposable test subscription. Namespace creation/readback needs
  `Microsoft.ServiceBus/namespaces/read` and `write` at the generated group.
  The sample pauses after group creation so this group-scoped access can be
  arranged; enter `continue` only after it is ready.
  When cleanup is requested, allow the corresponding `delete` actions and
  `Microsoft.Resources/subscriptions/resourceGroups/resources/read` for the
  empty-group check. The `Microsoft.ServiceBus` provider must already be
  registered. These are management permissions, not role-assignment permission.
* After creating the namespace, the sample prints its exact resource ID and
  **pauses before entity creation**. Arrange entity-administration access for
  the selected identity at **that namespace**, wait for propagation, and enter
  `continue`. [Azure Service Bus Data Owner](https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/integration#azure-service-bus-data-owner)
  is the service-specific built-in role supporting administration of entities
  and rules. It also permits messaging: scope it only to the disposable
  namespace and remove temporary access after the exercise. Data Sender or
  Data Receiver alone cannot create entities. A custom role can be considered
  under your organization's access policy rather than widening the scope.
* Optional traffic uses the same identity, with send on the sample topic and
  receive/settlement on the sample subscription. The namespace-scoped
  administration role above already encompasses these operations, but traffic
  still requires the separate CLI consent flags.

Both checkpoints are inside the operation timeout. EOF, any input other than
`continue`, cancellation or timeout fails the run; the inventory still records
the resources already created. The process must have an interactive stdin or
an operator-controlled input stream. Do not pre-feed confirmation before
access and scope have actually been verified.

Once authorized:

```powershell
# Creates resources and reads back configuration; retains them for inspection.
dotnet run --project .\src\service-bus-dotnet-management --configuration Release --no-build -- --run --confirm-create --config .\appsettings.local.json

# Separate authorized invocation, with new names; cleanup runs even after failure.
dotnet run --project .\src\service-bus-dotnet-management --configuration Release --no-build -- --run --confirm-create --cleanup-owned --config .\appsettings.local.json

# Separate authorized invocation; includes traffic and cleanup.
dotnet run --project .\src\service-bus-dotnet-management --configuration Release --no-build -- --run --confirm-create --verify-routing --confirm-messaging --cleanup-owned --config .\appsettings.local.json
```

Omitting `--cleanup-owned` from the routing command retains that run's
resources. Routing sends exactly three short-lived messages: a matching
`myproperty = "test"`, a nonmatching value, and no `myproperty`. It expects the
matching message within 30 seconds, completes it, and observes no further
message for 10 seconds. This bounded observation complements the exact rule
readback; it is not proof that a later unexpected delivery is impossible.

## Ownership, failures and cleanup

Names combine the configured prefix with a fresh GUID suffix.
Every creation checks for an existing resource and refuses observed
collisions. Entity operations are create-only SDK calls, not upserts; a
service-side collision after preflight also fails without adoption.

ARM resource-group and namespace APIs are upserts and do **not** document an
atomic create-only precondition. Random non-reusable names plus preflight
remove the path where a user selects an existing scope, but are not a lock
against concurrent writers. **Reserve the generated scope for this invocation;
do not have other processes create, replace or modify resources inside it.**
The sample requires the initial HTTP 201, completed provisioning and the
matching run marker before granting ownership. An HTTP 200/202, lost response,
timeout or inconsistent marker is ambiguous: even if a resource is visible
later, the sample does not adopt or automatically delete it. This conservative
rule can leave a legitimate creation requiring manual reconciliation.

Before the first Azure request, a local `management-run-<id>.json` is created
in the current directory. It is updated before requests and after outcomes,
and printed at the end. Keep this nonsecret inventory for partial-resource
reconciliation. It records `planned`, `checking`, `attempted`, `owned`,
`collision`, `ambiguous`, `deleting`, `deleted`, `retained` and `cleanup-failed`
states. A run interrupted during `attempted` or `deleting` is not proof of
absence. The initial rule's ownership follows the confirmed subscription
creation; its full ID is included in the report.

`--cleanup-owned` deletes in reverse dependency order: initial SQL rule and
subscription, topic, queue, namespace, then resource group. It checks run
markers and current settings again, refuses unexpected children, and blocks
parent deletion after any retained/unknown child. Independent sibling cleanup
continues after a failure. Deletes are awaited, including ARM long-running
operations, and followed by an absence read. Ctrl+C cancels provisioning but
does not cancel already-consented cleanup; cleanup has up to five independent
per-resource budgets. A timeout or failure remains a failure and is recorded.
Parent emptiness checks are also not atomic against concurrent writers.

Without cleanup consent, inspect the report and later perform **manual**
cleanup with a separately authorized tool, after verifying exact IDs, markers,
descendants and current ownership. Do not delete a whole group simply because
its name appears in a report. Ambiguous and colliding resources need human
reconciliation; a report is not a transferable deletion capability. The sample
deliberately cannot reload an old report to resume, adopt or delete resources.

Exit codes: **0** means all requested operations completed, **1** means a
runtime, authentication, readback, routing or cleanup failure, and **2** means
invalid configuration/arguments. An unsuccessful create/readback still exits
nonzero even if cleanup succeeds. Automatic SDK mutation retries are disabled
to avoid hiding an uncertain create response. A second invocation always uses
a new scope, so inspect and reconcile the previous inventory first.

## Scenario and execution inventory

| Scenario | Local coverage | Required authorized service evidence |
| --- | --- | --- |
| Configuration binding (X02-V01) | Checked-in JSON, environment precedence, validation, serialized SKU | Intended tier/region/capacity used |
| Authentication (X02-V02) | Explicit CLI/system/user-managed identity selection; SDK refresh with expired/near-expiry tokens on fake transport | Both credential modes access the intended tenant/scope; managed identity host required |
| Resource group (X02-V03) | Ownership, collision, ambiguous HTTP response contracts | Fresh group, correct marker/location, no existing-scope adoption |
| Namespace (X02-V04) | Actual Standard/Premium options; initial response handling | Both tiers, correct capacity/partitions, local auth disabled |
| Queue (X02-V05) | Create-only contract, tier-specific partitioning | Queue settings match readback |
| Topic (X02-V06) | Create-only contract, tier-specific partitioning | Topic settings match readback |
| Subscription (X02-V07) | MaxDeliveryCount, atomic initial-rule request | Subscription and ownership marker match |
| Rule (X02-V08) | Exact rule set rejects `$Default`, wrong SQL and actions | Only `rule1`; matching/nonmatching/missing-property routing |
| All-resource readback (X02-V09) | Readback failures remain failures | All service settings independently read back |
| Cleanup (X02-V10) | Reverse order, consent, ownership, ambiguity, sibling failures, timeout and cancellation | Actual deletion completion/absence, failure inventories, manual reconciliation of retained resources |

Safe executable modes are no-argument help, `--help`, `--validate`, and `--plan`.
Live variants are creation/readback with or without cleanup, and
creation/readback/routing with or without cleanup, for Standard and Premium.
The access checkpoints and both explicit credential types are separate live
coverage obligations. None of these live variants is certified by the offline
harness, and local changes do not update any external repository's pinned
snapshot or waive live-run, independent-review or publication gates.
