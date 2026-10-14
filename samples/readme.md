# Azure Service Bus samples

Use the current client libraries for new applications and when migrating existing applications.

## .NET

The [Azure.Messaging.ServiceBus samples](https://github.com/Azure/azure-sdk-for-net/tree/main/sdk/servicebus/Azure.Messaging.ServiceBus/samples) cover sending, receiving, sessions, transactions, and administration. Additional examples are in the [Azure.Messaging.ServiceBus folder](DotNet/Azure.Messaging.ServiceBus).

The local projects target .NET 10 with Service Bus 7.21.0, including an
isolated-worker Event Grid function. [Feature scenarios](DotNet/Azure.Messaging.ServiceBus/FeatureScenarios)
add request/reply, duplicate detection, TTL/DLQ and tier-correct partitioning.

## Java

Use the [azure-messaging-servicebus samples](https://github.com/Azure/azure-sdk-for-java/tree/main/sdk/servicebus/azure-messaging-servicebus/src/samples/java/com/azure/messaging/servicebus)
and the local [Java feature scenarios](Java/azure-messaging-servicebus) for the
current Java SDK. The latter covers request/reply, duplicate detection, TTL/DLQ,
autoforwarding, partitioning and WebSockets/proxy configuration. See
[Java samples](Java) for Qpid JMS 1.17.0 examples and Java 11 prerequisites.

## Management

The [management samples](Management) demonstrate namespace and entity management with PowerShell, Azure CLI, and .NET.

The in-tree .NET applications use .NET 10, Azure Identity and current ARM SDKs.
Management scripts use Az PowerShell or Azure CLI, explicit targets and
confirmation/preview guards rather than automatic failover or resource-group deletion.

## Scenario selection

These examples complement, rather than replace, the SDK catalogs:

| Scenario | Current guidance |
| --- | --- |
| Atomic transactions and sagas | [SDK transaction primitives](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/servicebus/Azure.Messaging.ServiceBus/samples/Sample06_Transactions.md) and [saga architecture](https://learn.microsoft.com/azure/architecture/patterns/saga); no recreation of the old bespoke saga framework |
| Offline durable buffering | A local durable outbox is a separate design requirement; a cloud-only outbox does not replace disconnected storage. A new MSMQ bridge is not included, and MSMQ is not labeled retired. |
| Disaster recovery | Prefer [managed Premium Geo-Replication](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-geo-replication) for eligible new designs; retain [metadata-only Geo-DR](Management/GeoDR) for existing deployments |
| Independent-namespace federation | [Federation guidance](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-federation-overview) remains distinct from single-primary managed DR; no claim of current runtime/package validation for external replication examples |
| Diagnostics | [Current SDK tracing](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/servicebus/Azure.Messaging.ServiceBus/samples/Sample19_DistributedTracing.md) and [Azure Monitor metrics](Management/Metrics), not the retired WAD collection path |

## Migrating retired libraries

`WindowsAzure.ServiceBus`, `Microsoft.Azure.ServiceBus`, and `com.microsoft.azure:azure-servicebus` were retired for Service Bus messaging on September 30, 2026. The ordinary samples for these libraries have been removed; their source remains in Git history.

Use the migration guide for your library:

- [WindowsAzure.ServiceBus](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/servicebus/Azure.Messaging.ServiceBus/MigrationGuide_WindowsAzureServiceBus.md)
- [NetMessagingBinding](DotNet/Microsoft.ServiceBus.Messaging/NetMessagingBinding/MigrationGuide.md)
- [Microsoft.Azure.ServiceBus](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/servicebus/Azure.Messaging.ServiceBus/MigrationGuide.md)
- [com.microsoft.azure:azure-servicebus](https://github.com/Azure/azure-sdk-for-java/blob/main/sdk/servicebus/azure-messaging-servicebus/migration-guide.md)

The messaging-library retirement does not include WCF Relay. Keep Relay
migration decisions separate from the removed queued WCF/SBMP examples.
