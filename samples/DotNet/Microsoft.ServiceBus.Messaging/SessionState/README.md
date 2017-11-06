# Session State

This sample illustrates the Session state handling feature of Azure Service Bus. 

Session state allows keeping track of the processing state a handler has related
to a session, so that clients can be agile between processing nodes (including
failover) during session processing.

[Read more about sessions in the documentation][1]

Refer to the main [README](../README.md) document for setup instructions. 

## Sample Code 

This sample combines the Deferral and Session features such that the session state 
facility is being used to keep track of the procesing state of a workflow where
input for the respective steps arrives out of the expected order.

While sessions assure ordered delivery in the exact enqueue order, they
obviously can't detect when messages are intentionally sent in an order other
than expected by the receiver.

The sample is documented inline in the [Program.cs](Program.cs) C# file.

[1]: https://docs.microsoft.com/azure/service-bus-messaging/message-sessions