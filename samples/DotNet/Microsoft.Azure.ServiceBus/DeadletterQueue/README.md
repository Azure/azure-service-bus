# Dead-Letter Queues

This sample shows how to move messages to the Dead-letter queue, how to retrieve messages from it, and resubmit corrected message back into the main queue.

## What is a Dead-Letter Queue?

All Service Bus Queues and Subscriptions have a secondary sub-queue, called the *dead-letter queue* (DLQ). This sub-queue does not need to be explicitly 
created and cannot be deleted or otherwise managed independent of the main entity. The purpose of the Dead-Letter Queue (DLQ) is accept and hold messages 
that cannot be delivered to any receiver or messages that could not be processed. 

Read more about Dead-Letter Queues [in the product documentation.](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dead-letter-queues)

## Sample Code 

The sample is documented inline in the [Program.cs](Program.cs) C# file.