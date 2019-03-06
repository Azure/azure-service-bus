# Receive Loop

This sample shows how to implement an explicit receive loop using the
```MessageReceiver``` client, instead of the callback-based model that
```QueueClient```and ```SubscriptionClient``` provide. Most application should
use the callback-based programming model.

Refer to the main [README](../README.md) document for setup instructions. 


## Sample Code 

The sample is documented inline in the [ReceiveLoop.java](./src/main/java/com/microsoft/azure/servicebus/samples/receiveloop/ReceiveLoop.java) file.

To keep things reasonably simple, the sample program keeps message sender and
message receiver code within a single hosting application, even though these
roles are often spread across applications, services, or at least across
independently deployed and run tiers of applications or services. For clarity,
the send and receive activities are kept as separate as if they were different
apps and share no API object instances.
