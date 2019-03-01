# Using a Proxy with Service Bus Queues

This sample builds off the QueuesGettingStarted sample to show use of a proxy 
with Azure Service Bus.

You will learn how to configure a proxy for use with Service Bus Queues. This
use can be generalized to subscriptions and topics as well.

Refer to the main [README](../README.md) document for setup instructions. 

## Sample Code 

The sample is documented inline in the [QueuesWithProxy.java](src/main/java/com/microsoft/azure/servicebus/samples/queueswithproxy/QueuesWithProxy.java) file.

### Note from [QueuesGettingStarted.java](../QueuesGettingStarted/src/main/java/com/microsoft/azure/servicebus/samples/queuesgettingstarted/QueuesGettingStarted.java) sample:
To keep things reasonably simple, the sample program keeps message sender and
message receiver code within a single hosting application, even though these
roles are often spread across applications, services, or at least across
independently deployed and run tiers of applications or services. For clarity,
the send and receive activities are kept as separate as if they were different
apps and share no API object instances.

