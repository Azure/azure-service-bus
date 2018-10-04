# Managing Azure Service Bus Queues

This sample shows the essential API elements for managing a
Service Bus Queue. Topics and Subscriptions can be managed similarly.

You will learn how to create a new queue, retrieve/get an existing queue,
update an existing queue, or delete the queue.

You will also learn about manipulating properties of the queue
so as to optimize to your scenario.

Refer to the main [README](../README.md) document for setup instructions.

## Sample Code

The sample is documented inline in the [ManagingEntity.java](./src/main/java/com/microsoft/azure/servicebus/samples/managingentity/ManagingEntity.java) file.

This sample creates a new Queue, retrieves the same, updates few properties and finally deletes it.
We also retrieve the runtime information of the queue which shows the current status on
number of messages, size of the queue, last accessed / updated time etc.
It also shows details on how the messages are spread between
active messages and deadlettered / scheduled messages.
