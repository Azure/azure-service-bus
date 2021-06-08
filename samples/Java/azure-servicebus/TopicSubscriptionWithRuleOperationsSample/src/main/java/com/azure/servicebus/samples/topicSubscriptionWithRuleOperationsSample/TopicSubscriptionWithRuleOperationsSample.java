package com.azure.servicebus.samples.topicSubscriptionWithRuleOperationsSample;

import com.azure.core.util.BinaryData;
import com.azure.messaging.servicebus.ServiceBusClientBuilder;
import com.azure.messaging.servicebus.ServiceBusMessage;
import com.azure.messaging.servicebus.ServiceBusMessageBatch;
import com.azure.messaging.servicebus.ServiceBusReceiverAsyncClient;
import com.azure.messaging.servicebus.ServiceBusSenderAsyncClient;
import com.azure.messaging.servicebus.ServiceBusServiceVersion;
import com.azure.messaging.servicebus.administration.ServiceBusAdministrationAsyncClient;
import com.azure.messaging.servicebus.administration.ServiceBusAdministrationClientBuilder;
import com.azure.messaging.servicebus.administration.models.CorrelationRuleFilter;
import com.azure.messaging.servicebus.administration.models.CreateRuleOptions;
import com.azure.messaging.servicebus.administration.models.SqlRuleAction;
import com.azure.messaging.servicebus.administration.models.SqlRuleFilter;
import com.azure.messaging.servicebus.administration.models.TrueRuleFilter;
import com.azure.messaging.servicebus.models.ServiceBusReceiveMode;
import com.google.gson.Gson;
import com.google.gson.reflect.TypeToken;

import org.apache.commons.cli.CommandLine;
import org.apache.commons.cli.CommandLineParser;
import org.apache.commons.cli.DefaultParser;
import org.apache.commons.cli.HelpFormatter;
import org.apache.commons.cli.Option;
import org.apache.commons.cli.Options;

import reactor.core.Disposable;

import java.io.IOException;
import java.time.OffsetDateTime;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicLong;
import java.util.concurrent.atomic.AtomicReference;
import java.util.function.Function;

public class TopicSubscriptionWithRuleOperationsSample {

    static final String SERVICE_BUS_CONNECTION_STRING = "SERVICE_BUS_CONNECTION_STRING";
    static final String SERVICE_BUS_TOPIC_NAME = "SERVICE_BUS_TOPIC_NAME";
    static final String ALL_MESSAGES_SUBSCRIPTION_NAME = "{Subscription 1 Name}";
    static final String SQL_FILTER_ONLY_SUBSCRIPTION_NAME = "{Subscription 2 Name}";
    static final String SQL_FILTER_WITH_ACTION_SUBSCRIPTION_NAME = "{Subscription 3 Name}";
    static final String CORRELATION_FILTER_SUBSCRIPTION_NAME = "{Subscription 4 Name}";
    static final String DEFAULT_SUBSCRIPTION_RULE_NAME = "$Default";
    static final String SQL_FILTER_ONLY_SUBSCRIPTION_RULE_NAME = "RedSqlRule";
    static final String SQL_FILTER_WITH_ACTION_SUBSCRIPTION_RULE_NAME = "BlueSqlRule";
    static final String CORRELATION_FILTER_SUBSCRIPTION_RULE_NAME = "ImportantCorrelationRule";
    static final String MESSAGE_JSON_ITEM_NAME_LABEL = "label";
    static final String MESSAGE_JSON_ITEM_NAME_CORRELATION_ID = "correlationId";
    static final Gson GSON = new Gson();

    static ServiceBusSenderAsyncClient topicSenderAsyncClient;
    static ServiceBusAdministrationAsyncClient administrationAsyncClient;
    static String connectionString = null;
    static String topicName = null;

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
       System.out.println(String.format("SubscriptionName: %s, Removing and re-adding Default Rule", ALL_MESSAGES_SUBSCRIPTION_NAME));
       administrationAsyncClient.deleteRuleWithResponse(topicName, ALL_MESSAGES_SUBSCRIPTION_NAME, DEFAULT_SUBSCRIPTION_RULE_NAME).block();
       administrationAsyncClient.createRuleWithResponse(topicName, ALL_MESSAGES_SUBSCRIPTION_NAME, DEFAULT_SUBSCRIPTION_RULE_NAME,
               new CreateRuleOptions(new TrueRuleFilter())).block();

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

}
