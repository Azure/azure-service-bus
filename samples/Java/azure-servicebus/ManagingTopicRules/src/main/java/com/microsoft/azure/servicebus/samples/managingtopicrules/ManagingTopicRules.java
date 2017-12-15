// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

package com.microsoft.azure.servicebus.samples.managingtopicrules;

import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;
import com.microsoft.azure.servicebus.primitives.ServiceBusException;
import com.microsoft.azure.servicebus.rules.CorrelationFilter;
import com.microsoft.azure.servicebus.rules.RuleDescription;
import com.microsoft.azure.servicebus.rules.SqlFilter;
import com.microsoft.azure.servicebus.rules.SqlRuleAction;
import com.microsoft.azure.servicebus.rules.TrueFilter;
import org.apache.commons.cli.*;
import org.apache.log4j.*;

import java.time.Duration;
import java.util.Collection;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.CompletableFuture;

public class ManagingTopicRules {
    // Connection String for the namespace can be obtained from the Azure portal under the
    // 'Shared Access policies' section.
    private static String connectionString = System.getenv("SB_SAMPLES_CONNECTIONSTRING");
    // Name of an existing Topic within the Service Bus namespace
    private static String topicName = System.getenv("SB_SAMPLES_TOPICNAME");
    // The following 4 subscriptions witgh these exact names are expect to exist on 
    // the given topic:
    private static final String allMessagesSubscriptionName = "allMessages";
    private static final String sqlFilterOnlySubscriptionName = "sqlFilterOnly";
    private static final String sqlFilterWithActionSubscriptionName = "sqlFilterWithAction";
    private static final String correlationFilterSubscriptionName = "correlationFilter";
    // topic client
    private static ITopicClient topicClient;
    // log4j logger 
    private static Logger logger = Logger.getRootLogger();

    public static void main(String[] args) throws Exception {

        if (!parseCommandLine(args)) {
            return;
        }

        logger.info("Starting TopicSubscriptionWithRuleOperations sample.");

        // create client
        logger.info("Create topic client.");
        topicClient = new TopicClient(new ConnectionStringBuilder(connectionString, topicName));
        logger.info("Create subscription client.");
        ISubscriptionClient allMessagessubscriptionClient = new SubscriptionClient(new ConnectionStringBuilder(
                connectionString, topicName + "/subscriptions/" + allMessagesSubscriptionName), ReceiveMode.PEEKLOCK);
        ISubscriptionClient sqlFilterOnlySubscriptionClient = new SubscriptionClient(new ConnectionStringBuilder(
                connectionString, topicName + "/subscriptions/" + sqlFilterOnlySubscriptionName), ReceiveMode.PEEKLOCK);
        ISubscriptionClient sqlFilterWithActionSubscriptionClient = new SubscriptionClient(
                new ConnectionStringBuilder(connectionString,
                        topicName + "/subscriptions/" + sqlFilterWithActionSubscriptionName),
                ReceiveMode.PEEKLOCK);
        ISubscriptionClient correlationFilterSubscriptionClient = new SubscriptionClient(
                new ConnectionStringBuilder(connectionString,
                        topicName + "/subscriptions/" + correlationFilterSubscriptionName),
                ReceiveMode.PEEKLOCK);

        // Drop existing rules and add a TrueFilter
        for (RuleDescription rd : allMessagessubscriptionClient.getRules()) {
            allMessagessubscriptionClient.removeRule(rd.getName());
        }

        allMessagessubscriptionClient.addRule(new RuleDescription("MatchAll", new TrueFilter()));

        // Drop existing rules and add a SQL filter
        for (RuleDescription rd : sqlFilterOnlySubscriptionClient.getRules()) {
            sqlFilterOnlySubscriptionClient.removeRule(rd.getName());
        }
        sqlFilterOnlySubscriptionClient.addRule(new RuleDescription("RedSqlRule", new SqlFilter("Color = 'Red'")));

        // Drop existing rules and add a SQL filter with a subsequent action
        for (RuleDescription rd : sqlFilterWithActionSubscriptionClient.getRules()) {
            sqlFilterWithActionSubscriptionClient.removeRule(rd.getName());
        }
        RuleDescription sqlRuleWithAction = new RuleDescription("BlueSqlRule", new SqlFilter("Color = 'Blue'"));
        sqlRuleWithAction.setAction(new SqlRuleAction("SET Color = 'BlueProcessed'"));
        sqlFilterWithActionSubscriptionClient.addRule(sqlRuleWithAction);

        // Drop existing rules and add a Correlationfilter
        logger.info(String.format("SubscriptionName: %s, Removing Default Rule and Adding CorrelationFilter",
                sqlFilterWithActionSubscriptionName));
        for (RuleDescription rd : correlationFilterSubscriptionClient.getRules()) {
            correlationFilterSubscriptionClient.removeRule(rd.getName());
        }
        // this correlation filter 
        CorrelationFilter correlationFilter = new CorrelationFilter();
        correlationFilter.setCorrelationId("important");
        correlationFilter.setLabel("Red");
        correlationFilterSubscriptionClient.addRule(new RuleDescription("ImportantCorrelationRule", correlationFilter));

        // Get Rules on Subscription, called here only for one subscription as example
        RuleDescription[] rules = correlationFilterSubscriptionClient.getRules().toArray(new RuleDescription[0]);
        logger.info(String.format("GetRules:: SubscriptionName: %s, CorrelationFilter Name: %s, Rule: %s",
                correlationFilterSubscriptionName, rules[0].getName(), rules[0].getFilter()));

        // Send messages to Topic
        sendMessages();

        // Receive messages from 'allMessagesSubscriptionName'. Should receive all 9 messages
        receiveMessages(allMessagesSubscriptionName);

        // Receive messages from 'sqlFilterOnlySubscriptionName'. Should receive all messages with Color = 'Red' i.e 3 messages
        receiveMessages(sqlFilterOnlySubscriptionName);

        // Receive messages from 'sqlFilterWithActionSubscriptionClient'. Should receive all messages with Color = 'Blue'
        // i.e 3 messages AND all messages should have color set to 'BlueProcessed'
        receiveMessages(sqlFilterWithActionSubscriptionName);

        // Receive messages from 'correlationFilterSubscriptionName'. Should receive all messages  with Color = 'Red' and CorrelationId = "important"
        // i.e 1 message
        receiveMessages(correlationFilterSubscriptionName);

        logger.info("Completed Receiving all messages...");
        logger.info("=========================================================");

        allMessagessubscriptionClient.close();
        sqlFilterOnlySubscriptionClient.close();
        sqlFilterWithActionSubscriptionClient.close();
        correlationFilterSubscriptionClient.close();
        topicClient.close();
    }

    private static void sendMessages() {
        logger.info("Sending Messages to Topic");
        try {
            CompletableFuture.allOf(sendMessageAsync("Red", null), sendMessageAsync("Blue", null),
                    sendMessageAsync("Red", "important"), sendMessageAsync("Blue", "important"),
                    sendMessageAsync("Red", "notimportant"), sendMessageAsync("Blue", "notimportant"),
                    sendMessageAsync("Green", null), sendMessageAsync("Green", "important"),
                    sendMessageAsync("Green", "notimportant")).get();
        } catch (Exception exception) {
            logger.info(String.format("Exception: %s", exception.getMessage()));
        }
    }

    /*
    *  Sends message with the subject, a custom property the correlation-Id property set
    */
    private static CompletableFuture<Void> sendMessageAsync(String label, String correlationId)
            throws ServiceBusException, InterruptedException {
        // create a new message
        Message message = new Message();
        // set the label
        message.setLabel(label);
        // create a Hashmap for custom properties
        Map<String, String> properties = new HashMap<>();
        properties.put("Color", label);
        // set the custom properties
        message.setProperties(properties);

        if (correlationId != null) {
            message.setCorrelationId(correlationId);
        }
        // send the message async; when the send operation hads completed, log that fact  
        return topicClient.sendAsync(message)
                .thenRunAsync(() -> logger.info(String.format("Sent Message:: Label: %s, CorrelationId: %s",
                        message.getLabel(), message.getCorrelationId() == null ? "" : message.getCorrelationId())));
    }

    /*
    *  receive sent messages from a given subscription
    */
    private static void receiveMessages(String subscriptionName) throws ServiceBusException, InterruptedException {
        IMessageReceiver subscriptionReceiver = ClientFactory.createMessageReceiverFromConnectionStringBuilder(
                new ConnectionStringBuilder(connectionString, topicName + "/subscriptions/" + subscriptionName),
                ReceiveMode.RECEIVEANDDELETE);

        logger.info(String.format("Receiving Messages From Subscription: %s", subscriptionName));
        int receivedMessageCount = 0;
        while (true) {
            IMessage receivedMessage = subscriptionReceiver.receive(Duration.ofSeconds(5));
            if (receivedMessage != null) {
                String colorProperty = receivedMessage.getProperties().get("Color");
                logger.info(String.format("Color Property = %s, CorrelationId = %s", colorProperty,
                        receivedMessage.getCorrelationId()));
                receivedMessageCount++;
            } else {
                break;
            }
        }

        logger.info(
                String.format("Received '%d' Messages From Subscription: %s", receivedMessageCount, subscriptionName));
        subscriptionReceiver.close();
    }

    static boolean parseCommandLine(String[] args) throws Exception {
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

        if (connectionString == null || topicName == null) {
            HelpFormatter formatter = new HelpFormatter();
            formatter.printHelp("run jar with", "", options, "", true);
            return false;
        }
        return true;
    }
}
