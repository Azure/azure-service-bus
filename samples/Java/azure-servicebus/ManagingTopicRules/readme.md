# Managing Topic Rules

This sample demonstrates how to manage Topic subscription rules with the Azure Service Bus SDK for Java.

Expanding from the [TopicClientQuickstart](../TopicClientQuickstart) base sample, you will learn how to 
modify the rules of existing subscriptions at runtime in order to change the conditions under which 
messages are received from these subscriptions. 

The sample demonstrates all available rule types:
* TrueFilter - matches all messages
* SqlFilter - matches messages with a message selector on message metadata 
* SqlFilter with SqlAction - additionally performs a transform on message metadata
* CorrelationFilter - performs a simple metadata property match 

Correlation filters yield significantly higher performance and therefore lower latency and 
throughput on a Topic than SQL filters and are therefore preferred.

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
java -jar ./target/azure-servicebus-samples-managingtopicrules-1.0.0-jar-with-dependencies.jar
```

The sample accepts two arguments that can either be supplied on the command line or via environment
variables. The setup script discussed in the overview readme sets the environment variables for you.

* -c (env: SB_SAMPLES_CONNECTIONSTRING) - Service Bus connection string with credentials or 
                                          token granting send and listen rights for the namespace
* -t (env: SB_SAMPLES_TOPICNAME)        - Name of an existing topic within the namespace
* -s (env: SB_SAMPLES_SUBSCRIPTIONNAME) - Name of an existing subscription on the given topic

## Sample Code

For a discussion of the sample code, review the inline comments in [ManagingTopicRules.java](./src/main/java/com/microsoft/azure/servicebus/samples/managingtopicrules/ManagingTopicRules.java)


