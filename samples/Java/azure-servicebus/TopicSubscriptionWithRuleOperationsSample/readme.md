# Get started configuring and managing rules for Subscriptions

In order to run the sample in this directory, replace the following bracketed values in the `TopicSubscriptionWithRuleOperationsSample.java` file.

```java
// Simply create 4 default subscriptions (no rules specified explicitly) and provide subscription names. 
// The Rule addition will be done as part of the sample depending on the subscription behavior expected.
static final String ALL_MESSAGES_SUBSCRIPTION_NAME = "{Subscription 1 Name}";
static final String SQL_FILTER_ONLY_SUBSCRIPTION_NAME = "{Subscription 2 Name}";
static final String SQL_FILTER_WITH_ACTION_SUBSCRIPTION_NAME = "{Subscription 3 Name}";
static final String CORRELATION_FILTER_SUBSCRIPTION_NAME = "{Subscription 4 Name}";
```

## The Sample Program
To keep things reasonably simple, the sample program keeps send and receive code within a single hosting application.
Typically in real world applications these roles are often spread across applications, services, or at least across 
independently deployed and run tiers of applications or services. For clarity, the send and receive activities are kept as 
separate methods as if they were different apps.

For further information on how to create this sample on your own, follow the rest of the tutorial.

## What will be accomplished
Topics are similar to Queues for the send side of the application. However unlike Queues, Topic can have zero or more subscriptions,
from which messages can be retrieved and each of subscription act like independent queues. Whether a message is selected into the
subscription is determined by the Filter condition for the subscription. Filters can be one of the following:

1. `TrueFilter` - Selects all messages to subscription, 
2. `FalseFilter` - Selects none of the messages to subscription, 
3. `SqlFilter` - Holds a SQL-like condition expression that is evaluated in the ServiceBus service against the arriving messages'
user-defined properties and system properties and if matched the message is selected for subscription.
4. `CorrelationFilter` - Holds a set of conditions that is evaluated in the ServiceBus service against the arriving messages'
user-defined properties and system properties. A match exists when an arriving message's value for a property is equal to the
value specified in the correlation filter.

In this tutorial, we will write a console application to manage rules on Subscription (`AddRule`, `GetRules`, `RemoveRules`).
We will also explore different forms of subscription filters. Refer to the 
link(https://github.com/Azure/azure-service-bus/tree/master/samples/Java/azure-servicebus/TopicFilters) for a more 
detailed explanation of filters.

## Prerequisites
1. [Java Core](https://docs.microsoft.com/azure/developer/java/fundamentals/?view=azure-java-stable)
1. An Azure subscription.
2. [A ServiceBus namespace](https://docs.microsoft.com/azure/service-bus-messaging/service-bus-create-namespace-portal) 
3. [A ServiceBus Topic](https://docs.microsoft.com/azure/service-bus-messaging/service-bus-java-how-to-use-topics-subscriptions#2-create-a-topic-using-the-azure-portal)
4. [ServiceBus Subscriptions](https://docs.microsoft.com/azure/service-bus-messaging/service-bus-java-how-to-use-topics-subscriptions)

### Create a console application

- Create a Java project using Eclipse or a tool of your choice.

### Configure your application to use Service Bus

1. Add the following to your pom.xml, making sure that the solution references the `azure-messaging-servicebus` project.

    ```xml
    <dependency>
        <groupId>com.azure</groupId>
        <artifactId>azure-messaging-servicebus</artifactId>
        <version>7.2.2</version>
    </dependency>
    ```

### Write some code to send messages to the topic, manage rules and receive messages from the subscription
1. Add the following using statement to the top of the TopicSubscriptionWithRuleOperationsSample.java file.
   
    ```java
    import com.azure.messaging.servicebus.*;
    import com.azure.messaging.servicebus.administration.*;
    import com.azure.messaging.servicebus.administration.models.*;
    import com.azure.messaging.servicebus.models.*;
    ```

1. Add the following variables to the `TopicSubscriptionWithRuleOperationsSample` class, and replace the placeholder values:
    
    ```java
    static final String ALL_MESSAGES_SUBSCRIPTION_NAME = "{Subscription 1 Name}";
    static final String SQL_FILTER_ONLY_SUBSCRIPTION_NAME = "{Subscription 2 Name}";
    static final String SQL_FILTER_WITH_ACTION_SUBSCRIPTION_NAME = "{Subscription 3 Name}";
    static final String CORRELATION_FILTER_SUBSCRIPTION_NAME = "{Subscription 4 Name}";
    ```

1. Create the following methods that will send messages with various combinations to the topic:

    ```java
    static void sendMessagesAsync() {
        List<HashMap<String, String>> messageDataList =
                GSON.fromJson("[{\"label\":\"Red\"}," +
                                "{\"label\":\"Blue\"}," +
                                "{\"label\":\"Red\", \"correlationId\":\"important\"}," +
                                "{\"label\":\"Blue\", \"correlationId\":\"important\"}," +
                                "{\"label\":\"Red\", \"correlationId\":\"notimportant\"}," +
                                "{\"label\":\"Blue\", \"correlationId\":\"notimportant\"}," +
                                "{\"label\":\"Green\"}," +
                                "{\"label\":\"Green\", \"correlationId\":\"important\"}," +
                                "{\"label\":\"Green\", \"correlationId\":\"notimportant\"}]",
                        new TypeToken<ArrayList<HashMap<String, String>>>() {
                        }.getType());
        System.out.println("==========================================================================");
        System.out.println("Sending Messages to Topic");
        ServiceBusMessageBatch batchMessage = topicSenderAsyncClient.createMessageBatch()
                .doOnSuccess(messageBatch -> {
                    messageDataList.forEach(messageMap -> {
                        ServiceBusMessage message = new ServiceBusMessage(BinaryData.fromString(messageMap.get(MESSAGE_JSON_ITEM_NAME_LABEL)));
                        message.addContext("Color", messageMap.get(MESSAGE_JSON_ITEM_NAME_LABEL));
                        if (messageMap.containsKey(MESSAGE_JSON_ITEM_NAME_CORRELATION_ID) && messageMap.get(MESSAGE_JSON_ITEM_NAME_CORRELATION_ID) != null) {
                            message.setCorrelationId(messageMap.get(MESSAGE_JSON_ITEM_NAME_CORRELATION_ID));
                        }
                        messageBatch.tryAddMessage(message);
                        System.out.println(String.format("Sent Message:: Label: %s, CorrelationId: %s",
                                messageMap.get(MESSAGE_JSON_ITEM_NAME_LABEL), !messageMap.containsKey(MESSAGE_JSON_ITEM_NAME_CORRELATION_ID)
                                        || messageMap.get(MESSAGE_JSON_ITEM_NAME_CORRELATION_ID) == null ? "" : messageMap.get(MESSAGE_JSON_ITEM_NAME_CORRELATION_ID)));
                    });
                }).block();
        topicSenderAsyncClient.sendMessages(batchMessage)
                .doOnError(onError -> System.out.println(String.format("%s :: Exception: %s", OffsetDateTime.now(), onError.getMessage())))
                .doOnSuccess(onSuccess -> System.out.println(String.format("%s :: Sent message success to topic: %s", OffsetDateTime.now(), topicName)))
                .block();
    }
    ```

1. Create a new method `receiveMessagesAsync` with the following code to process messages from a given subscription:
	```java
    static void receiveMessagesAsync(String subscriptionName) throws InterruptedException {
        AtomicReference<ServiceBusReceiverAsyncClient> receiverAsyncClient = new AtomicReference<>();
        AtomicReference<Disposable> subscription = new AtomicReference<>();
        AtomicReference<CountDownLatch> countdownLatch = new AtomicReference<>();
        countdownLatch.set(new CountDownLatch(1));
        receiverAsyncClient.set(new ServiceBusClientBuilder()
                .connectionString(connectionString)
                .receiver()
                .topicName(topicName)
                .subscriptionName(subscriptionName)
                .receiveMode(ServiceBusReceiveMode.RECEIVE_AND_DELETE)
                .disableAutoComplete()
                .buildAsyncClient());
        System.out.println("==========================================================================");
        System.out.println(String.format("%s :: Receiving Messages From Subscription: %s", OffsetDateTime.now(), subscriptionName));
        AtomicLong receiveMessagesCount = new AtomicLong(0L);
        subscription.set(receiverAsyncClient.get().receiveMessages()
                .flatMap(receivedMessage -> {
                    receiveMessagesCount.incrementAndGet();
                    System.out.println(String.format("EntityPath = %s, Label = %s, CorrelationId = %s",
                            receiverAsyncClient.get().getEntityPath(), receivedMessage.getBody().toString(), subscriptionName));
                    return receiverAsyncClient.get().complete(receivedMessage);
                }).subscribe()
        );
        countdownLatch.get().await(10, TimeUnit.SECONDS);
        System.out.println(String.format("%s :: Received '%s' Messages From Subscription: %s", OffsetDateTime.now(), receiveMessagesCount.get(), subscriptionName));
        System.out.println("==========================================================================");
        subscription.get().dispose();
        receiverAsyncClient.get().close();
    }
	```

1. Create a new method called `run` with the following code:
	```java
    void run(String parametersJson) throws InterruptedException {
        HashMap<String, String> parameters =
                GSON.fromJson(parametersJson, new TypeToken<HashMap<String, String>>() {
                }.getType());
        connectionString = parameters.get(SERVICE_BUS_CONNECTION_STRING);
        topicName = parameters.get(SERVICE_BUS_TOPIC_NAME);

        topicSenderAsyncClient = new ServiceBusClientBuilder()
                .connectionString(connectionString)
                .sender()
                .topicName(topicName)
                .buildAsyncClient();

        administrationAsyncClient = new ServiceBusAdministrationClientBuilder()
                .connectionString(connectionString)
                .serviceVersion(ServiceBusServiceVersion.getLatest())
                .buildAsyncClient();

       // First Subscription is already created with default rule. Leave as is.

       // 2nd Subscription: Add SqlFilter on Subscription 2
       // Delete Default Rule.
       // Add the required SqlFilter Rule
       // Note: Does not apply to this sample but if there are multiple rules configured for a
       // single subscription, then one message is delivered to the subscription when any of the
       // rule matches. If more than one rules match and if there is no `SqlRuleAction` set for the
       // rule, then only one message will be delivered to the subscription. If more than one rules
       // match and there is a `SqlRuleAction` specified for the rule, then one message per `SqlRuleAction`
       // is delivered to the subscription.
       System.out.println(String.format("SubscriptionName: %s, Removing Default Rule and Adding SqlFilter", SQL_FILTER_ONLY_SUBSCRIPTION_NAME));
       administrationAsyncClient.deleteRuleWithResponse(topicName, SQL_FILTER_ONLY_SUBSCRIPTION_NAME, DEFAULT_SUBSCRIPTION_RULE_NAME).block();
       administrationAsyncClient.createRuleWithResponse(topicName, SQL_FILTER_ONLY_SUBSCRIPTION_NAME, SQL_FILTER_ONLY_SUBSCRIPTION_RULE_NAME,
               new CreateRuleOptions(new SqlRuleFilter("Color = 'Red'"))).block();

       // 3rd Subscription: Add SqlFilter and SqlRuleAction on Subscription 3
       // Delete Default Rule
       // Add the required SqlFilter Rule and Action
       System.out.println(String.format("SubscriptionName: %s, Removing Default Rule and Adding CorrelationFilter", SQL_FILTER_WITH_ACTION_SUBSCRIPTION_NAME));
       administrationAsyncClient.deleteRuleWithResponse(topicName, SQL_FILTER_WITH_ACTION_SUBSCRIPTION_NAME, DEFAULT_SUBSCRIPTION_RULE_NAME).block();
       administrationAsyncClient.createRuleWithResponse(topicName, SQL_FILTER_WITH_ACTION_SUBSCRIPTION_NAME, SQL_FILTER_WITH_ACTION_SUBSCRIPTION_RULE_NAME,
               new CreateRuleOptions().setFilter(new SqlRuleFilter("Color = 'Blue'"))
                       .setAction(new SqlRuleAction("SET Color = 'BlueProcessed'"))).block();

       // 4th Subscription: Add Correlation Filter on Subscription 4
       System.out.println(String.format("SubscriptionName: %s, Removing Default Rule and Adding CorrelationFilter", CORRELATION_FILTER_SUBSCRIPTION_NAME));
       administrationAsyncClient.deleteRuleWithResponse(topicName, CORRELATION_FILTER_SUBSCRIPTION_NAME, DEFAULT_SUBSCRIPTION_RULE_NAME).block();
       administrationAsyncClient.createRuleWithResponse(topicName, CORRELATION_FILTER_SUBSCRIPTION_NAME, CORRELATION_FILTER_SUBSCRIPTION_RULE_NAME,
               new CreateRuleOptions(new CorrelationRuleFilter().setCorrelationId("important").setLabel("Red"))).block();

        // Get Rules on Subscription, called here only for one subscription as example
        administrationAsyncClient.listRules(topicName, CORRELATION_FILTER_SUBSCRIPTION_NAME).collectList()
                .doOnSuccess(listRuleProperties -> {
                    listRuleProperties.forEach(ruleProperties -> {
                        System.out.println(String.format("GetRules:: SubscriptionName: %s, CorrelationFilter Name: %s, Rule: %s", CORRELATION_FILTER_SUBSCRIPTION_NAME, ruleProperties.getName(), ruleProperties.getFilter()));
                    });
                }).block();

        // Send messages to Topic
        sendMessagesAsync();

        // Receive messages from 'allMessagesSubscriptionName'. Should receive all 9 messages
        receiveMessagesAsync(ALL_MESSAGES_SUBSCRIPTION_NAME);

        // Receive messages from 'sqlFilterOnlySubscriptionName'. Should receive all messages with Color = 'Red' i.e 3 messages
        receiveMessagesAsync(SQL_FILTER_ONLY_SUBSCRIPTION_NAME);

        // Receive messages from 'sqlFilterWithActionSubscriptionName'. Should receive all messages with Color = 'Blue'
        // i.e 3 messages AND all messages should have color set to 'BlueProcessed'
        receiveMessagesAsync(SQL_FILTER_WITH_ACTION_SUBSCRIPTION_NAME);

        // Receive messages from 'correlationFilterSubscriptionName'. Should receive all messages  with Color = 'Red' and CorrelationId = "important"
        // i.e 1 message
        receiveMessagesAsync(CORRELATION_FILTER_SUBSCRIPTION_NAME);

        System.out.println("=========================================================");
        System.out.println("Completed Receiving all messages... Press any key to exit");
        System.out.println("=========================================================");

        try {
            int read = System.in.read(new byte[1]);
        } catch (IOException e) {
            e.printStackTrace();
        }

        topicSenderAsyncClient.close();
    }
    ```

1. Add the following code to the `runApp` method:

    ```java
    static int runApp(String[] args, Function<String, Integer> run) {
        try {
            // parse connection string from command line
            Options options = new Options();
            options.addOption(new Option("c", true, "Connection string"));
            options.addOption(new Option("t", true, "Topic name"));
            CommandLineParser clp = new DefaultParser();
            CommandLine cl = clp.parse(options, args);
            if (cl.getOptionValue("c") != null) {
                connectionString = cl.getOptionValue("c");
            }
            if (cl.getOptionValue("t") != null) {
                topicName = cl.getOptionValue("t");
            }

            // get overrides from the environment
             String connectionEnv = System.getenv(SERVICE_BUS_CONNECTION_STRING);
            if (connectionEnv != null) {
                connectionString = connectionEnv;
            }

             String topicEnv = System.getenv(SERVICE_BUS_TOPIC_NAME);
            if (topicEnv != null) {
                topicName = topicEnv;
            }

            if (connectionString == null || topicName == null) {
                HelpFormatter formatter = new HelpFormatter();
                formatter.printHelp("run jar with", "", options, "", true);
                return 2;
            }

            return run.apply("{\"" + SERVICE_BUS_CONNECTION_STRING + "\":\"" + connectionString + "\", \"" + SERVICE_BUS_TOPIC_NAME + "\":\"" + topicName + "\"}");
        } catch (Exception e) {
            System.out.printf("%s", e.toString());
            return 3;
        }
    }
    ```
   
1. Add the following code to the `main` method:
    
    ```java
    public static void main(String[] args) {
        System.exit(runApp(args, (parametersJson) -> {
            TopicSubscriptionWithRuleOperationsSample app = new TopicSubscriptionWithRuleOperationsSample();
            try {
                app.run(parametersJson);
                return 0;
            } catch (Exception e) {
                System.out.printf("%s", e.getMessage());
                return 1;
            }
        }));
    }
    ```

Congratulations! You have now learnt to configure and manage rules for a ServiceBus Topic Subscription.
