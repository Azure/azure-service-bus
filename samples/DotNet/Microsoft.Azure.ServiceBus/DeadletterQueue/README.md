# Dead-Letter Queues 

This sample shows how to move messages to the Dead-letter queue, how to retrieve
messages from it, and resubmit corrected message back into the main queue. 

For setup instructions, please refer back to the main [README](../README.md) file.

## What is a Dead-Letter Queue? 

All Service Bus Queues and Subscriptions have a secondary sub-queue, called the
*dead-letter queue* (DLQ). 

This sub-queue does not need to be explicitly created and cannot be deleted or
otherwise managed independent of the main entity. The purpose of the Dead-Letter
Queue (DLQ) is accept and hold messages that cannot be delivered to any receiver
or messages that could not be processed. Read more about Dead-Letter Queues [in
the product documentation.][1]

## Sample Code 

The sample implements two scenarios:

* Send a message and then retrierve and abandon the message until the maximum
delivery count is exhausted and the message is automatically dead-lettered. 

* Send a set of messages, and explicitly dead-letter messages that do not match
a certain criterion and would therefore not be processed correctly. The messages
are then picked up from the dead-letter queue, are automatically corrected, and
resubmitted.  

The sample code is further documented inline in the [Program.cs](Program.cs) C# file.

[1]: https://docs.microsoft.com/azure/service-bus-messaging/service-bus-dead-letter-queues