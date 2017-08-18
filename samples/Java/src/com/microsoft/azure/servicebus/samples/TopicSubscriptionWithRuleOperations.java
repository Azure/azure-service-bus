// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

package com.microsoft.azure.servicebus.samples;

import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;
import com.microsoft.azure.servicebus.primitives.ServiceBusException;
import com.microsoft.azure.servicebus.rules.CorrelationFilter;
import com.microsoft.azure.servicebus.rules.RuleDescription;
import com.microsoft.azure.servicebus.rules.SqlFilter;
import com.microsoft.azure.servicebus.rules.SqlRuleAction;

import java.time.Duration;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.CompletableFuture;

public class TopicSubscriptionWithRuleOperations {
    // Connection String for the namespace can be obtained from the Azure portal under the
    // 'Shared Access policies' section.
    private static final String connectionString = "{connection string}";
    private static final String topicName = "{topic name}";
    // Simply create 4 default subscriptions (no rules specified explicitly) and provide subscription names.
    // The Rule addition will be done as part of the sample depending on the subscription behavior expected.
    private static final String allMessagesSubscriptionName = "{subscription1}";
    private static final String sqlFilterOnlySubscriptionName = "{subscription2}";
    private static final String sqlFilterWithActionSubscriptionName = "{subscription3}";
    private static final String correlationFilterSubscriptionName = "{subscription4}";

    private static ITopicClient topicClient;

    public static void main(String[] args) throws Exception {

        Log.log("Starting TopicSubscriptionWithRuleOperations sample.");

        // create client
        Log.log("Create topic client.");
        topicClient = new TopicClient(new ConnectionStringBuilder(connectionString, topicName));
        Log.log("Create subscription client.");
        ISubscriptionClient allMessagessubscriptionClient = new SubscriptionClient(
                new ConnectionStringBuilder(connectionString, topicName + "/subscriptions/" + allMessagesSubscriptionName),
                ReceiveMode.PEEKLOCK);
        ISubscriptionClient sqlFilterOnlySubscriptionClient = new SubscriptionClient(
                new ConnectionStringBuilder(connectionString, topicName + "/subscriptions/" + sqlFilterOnlySubscriptionName),
                ReceiveMode.PEEKLOCK);
        ISubscriptionClient sqlFilterWithActionSubscriptionClient = new SubscriptionClient(
                new ConnectionStringBuilder(connectionString, topicName + "/subscriptions/" + sqlFilterWithActionSubscriptionName),
                ReceiveMode.PEEKLOCK);
        ISubscriptionClient correlationFilterSubscriptionClient = new SubscriptionClient(
                new ConnectionStringBuilder(connectionString, topicName + "/subscriptions/" + correlationFilterSubscriptionName),
                ReceiveMode.PEEKLOCK);

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

        Log.log("SubscriptionName: %s, Removing Default Rule and Adding SqlFilter", sqlFilterOnlySubscriptionName);
        sqlFilterOnlySubscriptionClient.removeRule(SubscriptionClient.DEFAULT_RULE_NAME);
        sqlFilterOnlySubscriptionClient.addRule(new RuleDescription
            (
                "RedSqlRule",
                new SqlFilter("Color = 'Red'")
            ));

        // 3rd Subscription: Add SqlFilter and SqlRuleAction on Subscription 3
        // Delete Default Rule
        // Add the required SqlFilter Rule and Action
        Log.log("SubscriptionName: %s, Removing Default Rule and Adding SqlFilter and SqlRuleAction", sqlFilterWithActionSubscriptionName);
        sqlFilterWithActionSubscriptionClient.removeRule(SubscriptionClient.DEFAULT_RULE_NAME);
        RuleDescription sqlRuleWithAction = new RuleDescription
            (
                "BlueSqlRule",
                new SqlFilter("Color = 'Blue'")
            );
        sqlRuleWithAction.setAction(new SqlRuleAction("SET Color = 'BlueProcessed'"));
        sqlFilterWithActionSubscriptionClient.addRule(sqlRuleWithAction);

        // 4th Subscription: Add Correlation Filter on Subscription 4
        Log.log("SubscriptionName: %s, Removing Default Rule and Adding CorrelationFilter", sqlFilterWithActionSubscriptionName);
        correlationFilterSubscriptionClient.removeRule(SubscriptionClient.DEFAULT_RULE_NAME);
        CorrelationFilter correlationFilter = new CorrelationFilter();
        correlationFilter.setCorrelationId("important");
        correlationFilter.setLabel("Red");
        correlationFilterSubscriptionClient.addRule(new RuleDescription
            (
                "ImportantCorrelationRule",
                correlationFilter
            ));

        // Get Rules on Subscription, called here only for one subscription as example
        RuleDescription[] rules = correlationFilterSubscriptionClient.getRules().toArray(new RuleDescription[0]);
        Log.log("GetRules:: SubscriptionName: %s, CorrelationFilter Name: %s, Rule: %s",
                correlationFilterSubscriptionName,
                rules[0].getName(),
                rules[0].getFilter());

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

        Log.log("Completed Receiving all messages...");
        Log.log("=========================================================");

        allMessagessubscriptionClient.close();
        sqlFilterOnlySubscriptionClient.close();
        sqlFilterWithActionSubscriptionClient.close();
        correlationFilterSubscriptionClient.close();
        topicClient.close();
    }

    private static void sendMessages()
    {
        Log.log("Sending Messages to Topic");
        try
        {
            CompletableFuture.allOf(
                    sendMessageAsync("Red", null),
                    sendMessageAsync("Blue", null),
                    sendMessageAsync("Red", "important"),
                    sendMessageAsync("Blue", "important"),
                    sendMessageAsync("Red", "notimportant"),
                    sendMessageAsync("Blue", "notimportant"),
                    sendMessageAsync("Green", null),
                    sendMessageAsync("Green", "important"),
                    sendMessageAsync("Green", "notimportant")
            ).get();
        }
        catch (Exception exception)
        {
            Log.log("Exception: %s", exception.getMessage());
        }
    }

    private static CompletableFuture<Void> sendMessageAsync(String label, String correlationId) throws ServiceBusException, InterruptedException {
        Message message = new Message();
        message.setLabel(label);
        Map<String, String > properties = new HashMap<>();
        properties.put("Color", label);
        message.setProperties(properties);

        if (correlationId != null)
        {
            message.setCorrelationId(correlationId);
        }

        return topicClient.sendAsync(message).thenRunAsync(() ->
                Log.log("Sent Message:: Label: %s, CorrelationId: %s", message.getLabel(), message.getCorrelationId() == null ? "" : message.getCorrelationId()));
    }

    private static void receiveMessages(String subscriptionName) throws ServiceBusException, InterruptedException {
        IMessageReceiver subscriptionReceiver = ClientFactory.createMessageReceiverFromConnectionStringBuilder(
                new ConnectionStringBuilder(
                        connectionString,
                        topicName + "/subscriptions/" + subscriptionName), ReceiveMode.RECEIVEANDDELETE);

        Log.log("Receiving Messages From Subscription: %s", subscriptionName);
        int receivedMessageCount = 0;
        while (true)
        {
            IMessage receivedMessage = subscriptionReceiver.receive(Duration.ofSeconds(5));
            if (receivedMessage != null)
            {
                String colorProperty = receivedMessage.getProperties().get("Color");
                Log.log("Color Property = %s, CorrelationId = %s", colorProperty, receivedMessage.getCorrelationId());
                receivedMessageCount++;
            }
            else
            {
                break;
            }
        }

        Log.log("Received '%d' Messages From Subscription: %s", receivedMessageCount, subscriptionName);
        subscriptionReceiver.close();
    }
}
