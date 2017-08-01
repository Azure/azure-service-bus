# Service Bus Java Samples
In order to run the sample in this directory, replace the following bracketed values in the `[sample].java` file.

**For queue samples**
```java
    private static final String connectionString = "{connection string}";
    private static final String queueName = "{queue name}";
```

**For topic samples**
```java
    private static final String connectionString = "{connection string}";
    private static final String topicName = "{topic name}";
    private static final String subscriptionName = "{subscription name}";
```

## Prerequisites
1. Java 8
2. An Azure subscription.
3. [A ServiceBus namespace](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-create-namespace-portal)
4. [A ServiceBus queue](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dotnet-get-started-with-queues#2-create-a-queue-using-the-azure-portal)
5. Or [A ServiceBus topic](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dotnet-how-to-use-topics-subscriptions#1-create-a-namespace-using-the-azure-portal)

## Samples
There are currently three samples with Service Bus Java Client Library

#### Send and receive messages with Queue using QueueClient
[This sample](https://github.com/Azure/azure-service-bus/tree/master/samples/Java/src/com/microsoft/azure/servicebus/samples/BasicSendReceiveWithQueueClient.java) demonstrates how to use QueueClient to connect to a queue and then send and receive messages with this QueueClient. It uses [`MessageHandler`](https://docs.microsoft.com/en-us/java/api/com.microsoft.azure.servicebus._queue_client.registermessagehandler) (aka MessagePump) model which simplifies the processing model for messages.

#### Send and receive messages with Topic Subscription using TopicClient and Subscription Client
[This sample](https://github.com/Azure/azure-service-bus/tree/master/samples/Java/src/com/microsoft/azure/servicebus/samples/BasicSendReceiveWithTopicSubscriptionClient.java) demonstrates how to use TopicClient and SubscriptionClient to connect to a Topic and its Subscription and then send and receive messages. It uses [`MessageHandler`](https://docs.microsoft.com/en-us/java/api/com.microsoft.azure.servicebus._subscription_client.registermessagehandler) (aka MessagePump) model which simplifies the processing model for messages.

#### Send and receive messages with Queue using MessageSender and MessageReceiver
[This sample](https://github.com/Azure/azure-service-bus/tree/master/samples/Java/src/com/microsoft/azure/servicebus/samples/SendReceiveWithMessageSenderReceiver.java) shows how to use MessageSender and MessageReceiver to send and receive messages from a Service Bus Queue. With sender and receiveer, the client could have full control of how the messages are sent and processed.
