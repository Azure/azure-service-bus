# Azure Service Bus JMS Queues Quickstart

This sample demonstrates how to use Azure Service Bus Queues with the Java Message Service (JMS) API, 
implemented via the AMQP 1.0 to JMS mapping provcided by the Apache Qpid JMS client. The Apache Qpid 
JMS client is maintained by the Apache Foundation's Qpid Proton project.

Using Azure Service Bus queues through the JMS API provides basic send and receive capabilities and is 
a convenient choice when porting applications from other message brokers with JMS compliant APIs.

For cloud-native applications and applications that want to take advantage of more advanced Service Bus 
capabilities, the native Service Bus API provides deeper and more direct access to the service 
capabilities. 

Azure Service Bus splits the control plane from the data plane and therefore does not support several of
JMS's dynamic topology functions:

| Unsupported method          | Replace with                                                                             |
|-----------------------------|------------------------------------------------------------------------------------------|
| createDurableSubscriber     | create a Topic subscription porting the message selector                                 |
| createDurableConsumer       | create a Topic subscription porting the message selector                                 |
| createSharedConsumer        | Service Bus topics are always shareable, see above                                       |
| createSharedDurableConsumer | Service Bus topics are always shareable, see above                                       |
| createTemporaryTopic        | create a topic via management API/tools/portal with *AutoDeleteOnIdle* set to an expiration period |
| createTopic                 | create a topic via management API/tools/portal                                           |
| unsubscribe                 | delete the topic management API/tools/portal                                             |
| createBrowser               | unsupported. Use the Peek() functionality of the Service Bus API                         |
| createQueue                 | create a queue via management API/tools/portal                                           | 
| createTemporaryQueue        | create a queue via management API/tools/portal with *AutoDeleteOnIdle* set to an expiration period |


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
java -jar ./target/azure-servicebus-samples-jmsqueuequickstart-1.0.0-jar-with-dependencies.jar
```

The sample accept the connection string as an argument that can either be supplied on the command line or via environment
variables. The setup script discussed in the overview readme sets the environment variables for you.

* -c (env: SB_SAMPLES_CONNECTIONSTRING) - Service Bus connection string with credentials or 
                                          token granting send and listen rights for the namespace

The example assumes that the "BasicQueue" exists on the namespace. Please ensure that is created before running the sample.

## Sample Code Explained

For a discussion of the sample code, review the inline comments in [JmsQueueQuickstart.java](./src/main/java/com/microsoft/azure/servicebus/samples/jmsqueuequickstart/JmsQueueQuickstart.java)
