# Metadata-only Geo-disaster recovery

These .NET 10 examples use `Azure.ResourceManager.ServiceBus` 1.2.0 and
`Azure.Identity` 1.21.0. They cover **existing metadata-only Geo-DR deployments**,
not message replication. Prefer [managed Geo-Replication](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-geo-replication)
for new, eligible Premium DR designs. Neither feature is arbitrary active/active
application federation.

Choose the [separate alias](SBGeoDR2/SBGeoDR2) or
[existing namespace-name alias](SBGeoDR2/SBGeoDR_existing_namespace_name) variant.
Both compile the same management implementation; only the alias setup differs.

## Prerequisites and configuration

Install the .NET 10 SDK and sign in with `az login`. `DefaultAzureCredential`
also supports managed identity or the standard `AZURE_TENANT_ID`,
`AZURE_CLIENT_ID` and `AZURE_CLIENT_SECRET` environment variables for a service
principal. No ADAL or secrets in `App.config` are required.

Use disposable **Premium** namespaces in different regions, with an empty
secondary. Your identity needs management-plane permissions for the namespace
and disaster-recovery operations, for example Contributor scoped to the test
resource group. Owner and data-plane message permissions are not required merely
to run these management operations.

Existing namespaces must also meet the
[metadata Geo-DR pairing prerequisites](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-geo-dr),
including compatible partitioning settings and matching partition counts.
Do not assume any two Premium namespaces can be paired; verify their actual
configuration before `pair`.

Application recovery requires separate preparation. Microsoft Entra data-role
assignments are not replicated: grant the application's required Sender and/or
Receiver roles on the secondary, and configure its firewall, private endpoints
and DNS for the application's network. Test access to both namespaces before
failover and the alias endpoint after failover. Management Contributor access
or a successful `status` command does not prove messaging authorization,
network reachability or application recovery.

Set these environment variables in PowerShell:

```powershell
$env:AZURE_SUBSCRIPTION_ID = "<subscription-guid>"
$env:SERVICEBUS_RESOURCE_GROUP = "<existing-test-resource-group>"
$env:SERVICEBUS_PRIMARY_NAMESPACE = "<primary-name>"
$env:SERVICEBUS_SECONDARY_NAMESPACE = "<secondary-name>"
$env:SERVICEBUS_ALIAS = "<alias-name>"
```

Only `create-namespaces` also needs `SERVICEBUS_PRIMARY_LOCATION` and
`SERVICEBUS_SECONDARY_LOCATION` (for example `southcentralus` and `northcentralus`).
That command creates two billable Premium namespaces and refuses to overwrite
either existing namespace. It does not create a resource group. To pair existing
namespaces, skip it.

## Run one operation at a time

From either project directory:

```powershell
dotnet build
dotnet run -- --help
dotnet run -- --self-test
dotnet run -- status
# Each command below changes Azure. Run only the specific intended operation.
dotnet run -- create-namespaces --confirm-alias $env:SERVICEBUS_ALIAS
dotnet run -- pair --confirm-alias $env:SERVICEBUS_ALIAS
```

Create test queues/topics on the primary separately, then inspect `status` and
pending metadata replication before using the alias endpoint with a
[supported messaging client](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-dotnet-get-started-with-queues).

Other explicit commands are `failover`, `break-pairing`,
`delete-primary-alias`, and `delete-secondary-alias`. Every mutation requires
`--confirm-alias` with the exact configured alias. No command automatically
fails over, deletes namespaces or prints access keys.

**Failover is disruptive and one-way for the pairing.** It is sent to the
secondary; the program reports request acceptance, not an assumed completion
after a fixed delay. Inspect `status` and verify the alias endpoint before
resuming applications. Metadata-only Geo-DR does **not** copy messages: messages
remaining in the old primary need a separate recovery plan.

Break pairing before deleting a primary alias. Delete the secondary alias only
when the post-failover recovery plan permits it. If the alias matches the original
namespace name, breaking pairing without failover does not require deleting that
alias; the namespace-name variant rejects `delete-primary-alias`. After failover,
delete the alias before reusing that namespace outside the pairing.

`status` reads the primary and secondary independently, with a two-minute budget
for each, and reports failure after observing both outcomes. The two offline
`--self-test` cases cover a failed or timed-out primary without Azure calls.
Mutating commands have a 15-minute client deadline. Timeout is not rollback: an accepted
Azure operation may continue, and partially created namespaces can remain
billable. Check the portal before retrying. Resource deletion is deliberately a
separate manual step.

Read the [metadata-only Geo-DR guidance](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-geo-dr)
before making any changes. Local builds and help output do not validate pairing,
replication or failover in Azure.
