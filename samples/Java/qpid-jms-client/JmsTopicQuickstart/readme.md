# Azure Service Bus JMS Topics Quickstart

This sample demonstrates how to use Azure Service Bus Topics with the Java Message Service (JMS) API, 
using Microsoft Entra ID through the Service Bus JMS connection factory on
Premium by default. Explicit `SB_SAMPLES_AUTH=sas` mode uses Apache Qpid JMS
directly for the limited JMS 1.1 mapping on Standard or Premium.

Using Azure Service Bus topics and subscriptions through the JMS API provides basic send and receive 
capabilities and is a convenient choice when porting applications from other message brokers with JMS 
compliant APIs, even though Service Bus Topics differ from JMS Topics and require a few adjustments 
discussed below.

For cloud-native applications and applications that want to take advantage of more advanced Service Bus 
capabilities, teh native Service Bus API provides deeper and more direct access to the service 
capabilities. 

## Prerequisites

Please refer to the [overview README](../../readme.md) for prerequisites and setting up the samples 
environment, including creating a Service Bus cloud namespace. 

## JMS Topics vs. Service Bus Topics

Azure Service Bus topics route messages into named, shared, durable subscriptions that are managed through
the Azure Resource Management interface, the Azure command line tools, or through the Azure portal. Each
subscription allows for up to 2000 selection rules, each of which may have a filter condition and, for SQL 
filters, also a metadata transformation action. Each filter condition match selects the input message to 
be copied into tehj subscription.  

Receiving messages from subscriptions is identical receiving messages from queues. Each subscription 
has an associated dead-letter queue as well as the ability to automatically forward messages to another 
queue or topics. 

JMS Topics allow clients to dynamically create nondurable and durable subscribers that optionally allow
filtering messages with message selectors. Both modes in this sample use
pre-created subscriptions. Explicit SAS mode demonstrates the JMS 1.1-style
AMQP mapping in Standard and Premium; the default Microsoft Entra ID factory uses the
[Service Bus JMS library on Premium](https://learn.microsoft.com/azure/service-bus-messaging/how-to-use-java-message-service-20).
The SQL filter rule syntax for Service Bus is, however, very similar to the message selector syntax 
supported by JMS. 

The JMS Topic publisher side is compatible with Service Bus, as shown in this sample, but 
dynamic subscribers are not part of the direct-Qpid mapping. The following table
describes only explicit direct-Qpid SAS mode, not all Service Bus JMS 2.0 capabilities.


| Unsupported method          | Replace with                                                                             |
|-----------------------------|------------------------------------------------------------------------------------------|
| createDurableSubscriber     | create a Topic subscription porting the message selector                                 |
| createDurableConsumer       | create a Topic subscription porting the message selector                                 |
| createSharedConsumer        | Service Bus topics are always shareable, see above                                       |
| createSharedDurableConsumer | Service Bus topics are always shareable, see above                                       |
| createTemporaryTopic        | create a topic via management API/tools/portal with *AutoDeleteOnIdle* set to an expiration period |
| createTopic                 | create a topic via management API/tools/portal                                           |
| unsubscribe                 | delete only the intended, owned subscription through management API/tools/portal after checking its backlog; never delete the parent topic |
| createBrowser               | unsupported. Use the Peek() functionality of the Service Bus API                         |
| createQueue                 | create a queue via management API/tools/portal                                           | 
| createTemporaryQueue        | create a queue via management API/tools/portal with *AutoDeleteOnIdle* set to an expiration period |


## Build and run

The sample can be built independently with offline tests (live tests require
`-Dservicebus.integration=true`) using

```bash
mvn clean package 
```

and then run with

```bash
java -jar ./target/jmstopicquickstart-1.0.0-jar-with-dependencies.jar
```

Set `SB_SAMPLES_NAMESPACE` to your **Premium** namespace host and configure
`DefaultAzureCredential` plus Data Sender and Data Receiver roles as described
in the [overview README](../../readme.md#microsoft-entra-id-default-premium).
No SAS variables are required by default. To exercise Standard-compatible
direct Qpid explicitly, set `SB_SAMPLES_AUTH=sas` and the two SAS variables
documented in the [compatibility setup](../../readme.md#explicit-sas-compatibility-mode-standard-or-premium).
You can also pass the namespace host with `-n example.servicebus.windows.net`;
the environment variable takes precedence. Authentication failure does not
switch modes.

Create a disposable `BasicTopic` with empty subscriptions `Subscription1`,
`Subscription2`, and `Subscription3` before running. Each subscription must
accept all sample messages (one match-all rule), have no competing consumer, and
not forward messages elsewhere. Do not use production entities or subscriptions
with a backlog.

The sample sends ten messages tagged with a unique run ID and sequence. It
receives synchronously and acknowledges ten distinct messages from each
subscription: thirty deliveries in total. Unexpected, duplicate or malformed
messages fail before acknowledgment. A missing message fails after a 90-second
receive deadline per subscription. Clients close on success and failure.
Inspect remaining messages after a failed run, and delete only the disposable
resources you created when finished.

For VS Code, open this quickstart folder, install the current Java language and
debugger extensions and satisfy their JDK requirements, and make Maven available
on `PATH`. Set the environment variables before starting VS Code. The supplied
launch configuration runs its `verify` task and uses the resulting executable
JAR. Configuration and Maven/JAR checks do not constitute an exercised IDE debug
session or a live Azure run.

## Sample Code Explained

For a discussion of the sample code, review the inline comments in [JmsTopicQuickstart.java](./src/main/java/com/microsoft/azure/servicebus/samples/jmstopicquickstart/JmsTopicQuickstart.java)
