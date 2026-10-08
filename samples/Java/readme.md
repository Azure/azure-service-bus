# Java samples for Azure Service Bus

For the supported Java client library, use the [azure-messaging-servicebus samples](https://github.com/Azure/azure-sdk-for-java/tree/main/sdk/servicebus/azure-messaging-servicebus/src/samples/java/com/azure/messaging/servicebus).

The `com.microsoft.azure:azure-servicebus` library was retired on September 30, 2026. Its samples have been removed from this directory. Use the [migration guide](https://github.com/Azure/azure-sdk-for-java/blob/main/sdk/servicebus/azure-messaging-servicebus/migration-guide.md) to update existing applications.

## Apache Qpid JMS samples

These samples use Apache Qpid JMS directly, without a dependency on the retired Java library:

- [Queues](qpid-jms-client/JmsQueueQuickstart)
- [Topics and subscriptions](qpid-jms-client/JmsTopicQuickstart)

For JMS 2.0 with Service Bus Premium, see the [Azure Service Bus JMS samples](https://github.com/Azure/azure-servicebus-jms-samples).

## Prerequisites

Install JDK 11 or later and Maven. Create a [Service Bus namespace](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-quickstart-portal) and the entities used by your sample:

- Queue sample: `BasicQueue`.
- Topic sample: `BasicTopic` with subscriptions `Subscription1`, `Subscription2`, and `Subscription3`.

Create a shared access policy with Send and Listen permissions. Set these environment variables before running the samples or their integration tests:

| Variable | Value |
| --- | --- |
| `SB_SAMPLES_NAMESPACE` | Namespace host, such as `example.servicebus.windows.net`. |
| `SB_SAMPLES_SAS_KEY_NAME` | Shared access policy name. |
| `SB_SAMPLES_SAS_KEY` | Shared access policy key. Keep this value out of source control. |

The `-n` argument can supply the namespace host instead of `SB_SAMPLES_NAMESPACE`. The environment variable takes precedence. Credentials are read only from the environment.

## Build

From this directory, run `mvn package` to build both samples and run their integration tests against the configured namespace. To compile and package without connecting to Service Bus, run `mvn -DskipTests package`. Each sample README gives its run command.
