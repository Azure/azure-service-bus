# Message Senders and Receivers with Service Bus Queues

This sample shows interacting with Service Bus Queues using the MessageReceiver
and MessageSender classes.

If you initially choose a Queue for a communication path, but later decide to
switch to a Topic with multiple subscriptions to allow further consumers to get
the sender's messages, ```MessageSender``` and ```MessageReceiver``` based code
can be used with that new topology without changes except for configuring
different paths.     

Refer to the main [README](../README.md) document for setup instructions. 

## Sample Code

The sample is documented inline in the [Program.cs](Program.cs) C# file.

To keep things reasonably simple, the sample program keeps message sender and
message receiver code within a single hosting application, even though these
roles are often spread across applications, services, or at least across
independently deployed and run tiers of applications or services. For clarity,
the send and receive activities are kept as separate as if they were different
apps and share no API object instances.

