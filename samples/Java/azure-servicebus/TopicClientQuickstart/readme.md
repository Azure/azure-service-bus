# Topic Client Quickstart

This sample demonstrates how to use Azure Service Bus Topics with the Azure Service Bus SDK for Java.

You will learn how to set up a TopicClient and send messages, and how to set up a SubscriptionClient and 
receive those messages into a callback handler. The [MessageReceiverQuickstart](../MessageReceiverQuickStart) 
sample demonstrates how to receive messages by explicitly pulling from a queue, and equivalently applies to a 
SubscriptionClient. 

The callback model shown in this sample is the recommended method because the receive loop implemented by 
the SDK library transparently handles common issues like occasional network issues or transient errors, and 
also allows for parallel message handling on multiple worker threads. 

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
java -jar ./target/azure-servicebus-samples-topicclientquickstart-1.0.0-jar-with-dependencies.jar
```

The sample accepts two arguments that can either be supplied on the command line or via environment
variables. The setup script discussed in the overview readme sets the environment variables for you.

* -c (env: SB_SAMPLES_CONNECTIONSTRING) - Service Bus connection string with credentials or 
                                          token granting send and listen rights for the namespace
* -t (env: SB_SAMPLES_TOPICNAME)        - Name of an existing topic within the namespace
* -s (env: SB_SAMPLES_SUBSCRIPTIONNAME) - Name of an existing subscription on the given topic

## Sample Code Explained

For a discussion of the sample code, review the inline comments in [TopicClientQuickstart.java](./src/main/java/com/microsoft/azure/servicebus/samples/topicclientquickstart/TopicClientQuickstart.java)

