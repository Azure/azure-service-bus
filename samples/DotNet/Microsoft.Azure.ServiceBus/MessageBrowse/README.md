# Message Browsing (Peek)

This sample shows how to enumerate messages residing in a Queue or Topic
subscription without locking and/or deleting them. This feature is typically
used for diagnostic and troubleshooting purposes and/or for tooling built on top
of Service Bus. 

[Read more about message browsing in the documentation.][1]

Refer to the main [README](../README.md) document for setup instructions.

## Sample Code 

The sample sends a set of messages into a queue and then enumerates them. When
you run the sample repeatedly, you will see that messages accumulate in the log
as we don't receive and remove them. 

You will also observe that expired messages (we send with a 2 minute
time-to-live setting) may hang around past their expiration time, because
Service Bus lazily cleans up expired messages no longer available for regular
retrieval.

The sample is documented inline in the [Program.cs](Program.cs) C# file.


[1]: https://docs.microsoft.com/azure/service-bus-messaging/message-browsing