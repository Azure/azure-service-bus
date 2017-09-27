# Message Receiver Quickstart

This sample demonstrates how to use Azure Service Bus Queues with the Azure Service Bus SDK for Java.

You will learn how to set up a QueueClient, send messages, and receive those messages by explicitly pulling 
from the queue with an asynchronous receive gesture. The callback model shown in the [QueueClientQuickstart](../QueueClientQuickstart) 
sample is, however, the recommended method because the receive loop implemented by the SDK library 
transparently handles common issues like occasional network issues or transient errors, and also allows 
for parallel message handling on multiple worker threads. 

## Prerequisites

Please refer to the [overview README](../../readme.md) for prerequisites and setting up the samples 
environment, including creating a Service Bus cloud namespace. 

## Build and run

The sample can be built independently with 

```bash
mvn clean package 
```

and then run with (or just from VS Code or another Java IDE)

```bash
java -jar ./target/azure-servicebus-samples-messagereceiverquickstart-1.0.0-jar-with-dependencies.jar
```

The sample accepts two arguments that can either be supplied on the command line or via environment
variables. The setup script discussed in the overview readme sets the environment variables for you.

* -c (env: SB_SAMPLES_CONNECTIONSTRING) - Service Bus connection string with credentials or 
                                          token granting send and listen rights for the namespace
* -q (env: SB_SAMPLES_QUEUENAME) - Name of an existing queue within the namespace

## Sample Code Explained

For a discussion of the sample code, review the inline comments in [MessageReceiverQuickstart.java](./src/main/java/com/microsoft/azure/servicebus/samples/messagereceiverquickstart/MessageReceiverQuickstart.java)

