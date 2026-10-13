# Azure Service Bus JMS Queues Quickstart

This sample demonstrates how to use Azure Service Bus Queues with the Java Message Service (JMS) API, 
using Microsoft Entra ID through the Service Bus JMS connection factory on
Premium by default. Explicit `SB_SAMPLES_AUTH=sas` mode uses Apache Qpid JMS
directly for the limited JMS 1.1 mapping on Standard or Premium.

Using Azure Service Bus queues through the JMS API provides basic send and receive capabilities and is 
a convenient choice when porting applications from other message brokers with JMS compliant APIs.

For cloud-native applications and applications that want to take advantage of more advanced Service Bus 
capabilities, the native Service Bus API provides deeper and more direct access to the service 
capabilities. 

Both modes use a pre-created queue. The following limitations apply only to
the explicit direct-Qpid SAS mode, not the full JMS 2.0 feature set available with the
[Service Bus JMS library on Premium](https://learn.microsoft.com/azure/service-bus-messaging/how-to-use-java-message-service-20):

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


## Prerequisites

Please refer to the [overview README](../../readme.md) for prerequisites and setting up the samples 
environment, including creating a Service Bus cloud namespace. 

## Build and run

The sample can be built independently with offline tests (live tests require
`-Dservicebus.integration=true`) using

```bash
mvn clean package 
```

and then run with

```bash
java -jar ./target/jmsqueuequickstart-1.0.0-jar-with-dependencies.jar
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

Create an empty, disposable `BasicQueue` before running, with no competing
consumer or autoforwarding. Do not use a production queue or one with a backlog.
The sample sends ten messages tagged with a unique run ID and sequence, then
receives synchronously until those ten distinct messages have been acknowledged.
Unexpected, duplicate or malformed messages fail before acknowledgment; a
missing expected message fails after a 90-second receive deadline. Clients close
on success and failure. Inspect any remaining messages after a failed run, and
delete only the disposable resources you created when finished.

For VS Code, open this quickstart folder, install the current Java language and
debugger extensions and satisfy their JDK requirements, and make Maven available
on `PATH`. Set the environment variables before starting VS Code. The supplied
launch configuration runs its `verify` task and uses the resulting executable
JAR. Configuration and Maven/JAR checks do not constitute an exercised IDE debug
session or a live Azure run.

## Sample Code Explained

For a discussion of the sample code, review the inline comments in [JmsQueueQuickstart.java](./src/main/java/com/microsoft/azure/servicebus/samples/jmsqueuequickstart/JmsQueueQuickstart.java)
