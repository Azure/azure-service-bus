# Time-to-Live sample

This sample illustrates the "time-to-live" feature of Azure Service Bus.

For setup instructions, please refer back to the main [README](../README.md) file.

## What is Time-to-Live?

The payload inside of a message, or a command or inquiry that a message conveys
to a receiver, is almost always subject to some form of application-level
expiration deadline. After such a deadline, the content shall no longer be
delivered, or the requested operation shall no longer be executed. 

The Time-to-Live feature helps witzh this by dropping such expired messages
inside the broker. The expiration for any individual message can be controlled
by setting the ```TimeToLive``` system-defined property, which specifies a
relative duration. The expiration becomes an absolute instant when the message
is enqueued into the entity. At that time, the ```ExpiresAtUtc``` property takes on
the value ```EnqueuedTimeUtc + TimeToLive```.

[Read more about time-to-live in the documentation.][1]

## Sample Code 

The sample sends a set of messages with a short time-to-live into the queue
and then waits to let them expire. The default behavior in Service Bus is that 
expired messages are [moved into the dead-letter queue][2], and therefore the "fix" 
loop picks up those expired messages from there and resubmits them for processing.

The sample is further documented inline in the [TimeToLive.java](.\src\main\java\com\microsoft\azure\servicebus\samples\timetolive\TimeToLive.java) file.


[1]: https://docs.microsoft.com/azure/service-bus-messaging/message-expiration
[2]: https://docs.microsoft.com/azure/service-bus-messaging/service-bus-dead-letter-queues#moving-messages-to-the-dlq