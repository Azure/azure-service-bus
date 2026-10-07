# Azure Service Bus samples

Use the current client libraries for new applications and when migrating existing applications.

## .NET

The [Azure.Messaging.ServiceBus samples](https://github.com/Azure/azure-sdk-for-net/tree/main/sdk/servicebus/Azure.Messaging.ServiceBus/samples) cover sending, receiving, sessions, transactions, and administration. Additional examples are in the [Azure.Messaging.ServiceBus folder](DotNet/Azure.Messaging.ServiceBus).

## Java

Use the [azure-messaging-servicebus samples](https://github.com/Azure/azure-sdk-for-java/tree/main/sdk/servicebus/azure-messaging-servicebus/src/samples/java/com/azure/messaging/servicebus) for the Java client library. See [Java samples](Java) for JMS examples.

## Management

The [management samples](Management) demonstrate namespace and entity management with PowerShell, Azure CLI, and .NET.

## Migrating retired libraries

`WindowsAzure.ServiceBus`, `Microsoft.Azure.ServiceBus`, and `com.microsoft.azure:azure-servicebus` were retired for Service Bus messaging on September 30, 2026. The ordinary samples for these libraries have been removed; their source remains in Git history.

Use the migration guide for your library:

- [WindowsAzure.ServiceBus](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/servicebus/Azure.Messaging.ServiceBus/MigrationGuide_WindowsAzureServiceBus.md)
- [Microsoft.Azure.ServiceBus](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/servicebus/Azure.Messaging.ServiceBus/MigrationGuide.md)
- [com.microsoft.azure:azure-servicebus](https://github.com/Azure/azure-sdk-for-java/blob/main/sdk/servicebus/azure-messaging-servicebus/migration-guide.md)
