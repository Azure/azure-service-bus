// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

package com.microsoft.azure.servicebus.samples.topicfilters;

import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;
import com.google.gson.Gson;

import static java.nio.charset.StandardCharsets.*;

import java.time.Duration;
import java.util.*;
import java.util.concurrent.*;
import java.util.function.Function;

import org.apache.commons.cli.*;

public class TopicFilters {

    static final Gson GSON = new Gson();

    static final String TopicName = "TopicFilterSampleTopic";
    static final String SubscriptionAllMessages = "AllOrders";
    static final String SubscriptionColorBlueSize10Orders = "ColorBlueSize10Orders";
    static final String SubscriptionColorRed = "ColorRed";
    static final String SubscriptionHighPriorityOrders = "HighPriorityOrders";

    void run(String connectionString) throws Exception {
        // Send sample messages.
        this.sendMessagesToTopic(connectionString);

        // Receive messages from subscriptions.
        this.receiveAllMessageFromSubscription(connectionString, SubscriptionAllMessages);
        this.receiveAllMessageFromSubscription(connectionString, SubscriptionColorBlueSize10Orders);
        this.receiveAllMessageFromSubscription(connectionString, SubscriptionColorRed);
        this.receiveAllMessageFromSubscription(connectionString, SubscriptionHighPriorityOrders);
    }

    void sendMessagesToTopic(String connectionString) throws Exception {
        // Create client for the topic.
        TopicClient topicClient = new TopicClient(new ConnectionStringBuilder(connectionString, TopicName));

        // Create a message sender from the topic client.

        System.out.printf("\nSending orders to topic.\n");

        // Now we can start sending orders.
        CompletableFuture.allOf(
                SendOrder(topicClient, new Order()),
                SendOrder(topicClient, new Order("blue", 5, "low")),
                SendOrder(topicClient, new Order("red", 10, "high")),
                SendOrder(topicClient, new Order("yellow", 5, "low")),
                SendOrder(topicClient, new Order("blue", 10, "low")),
                SendOrder(topicClient, new Order("blue", 5, "high")),
                SendOrder(topicClient, new Order("blue", 10, "low")),
                SendOrder(topicClient, new Order("red", 5, "low")),
                SendOrder(topicClient, new Order("red", 10, "low")),
                SendOrder(topicClient, new Order("red", 5, "low")),
                SendOrder(topicClient, new Order("yellow", 10, "high")),
                SendOrder(topicClient, new Order("yellow", 5, "low")),
                SendOrder(topicClient, new Order("yellow", 10, "low"))
        ).join();

        System.out.printf("All messages sent.\n");
    }

    CompletableFuture<Void> SendOrder(TopicClient topicClient, Order order) throws Exception {

        IMessage message = new Message(GSON.toJson(order, Order.class).getBytes(UTF_8));
        message.setCorrelationId(order.getPriority());
        message.setLabel(order.getColor());
        message.setProperties(new HashMap<String, String>() {{
            put("Color", order.getColor());
            put("Quantity", Integer.toString(order.getQuantity()));
            put("Priority", order.getPriority());
        }});

        System.out.printf("Sent order with Color=%s, Quantity=%d, Priority=%s\n", order.getColor(), order.getQuantity(), order.getPriority());
        return topicClient.sendAsync(message);
    }

    void receiveAllMessageFromSubscription(String connectionString, String subsName) throws Exception
    {
        int receivedMessages = 0;

        // Create subscription client.
        IMessageReceiver subscriptionClient = ClientFactory.createMessageReceiverFromConnectionStringBuilder(new ConnectionStringBuilder(connectionString, TopicName+"/subscriptions/"+ subsName), ReceiveMode.RECEIVEANDDELETE);

        // Create a receiver from the subscription client and receive all messages.
        System.out.printf("\nReceiving messages from subscription %s.\n", subsName);

        while (true)
        {
            IMessage receivedMessage = subscriptionClient.receive(Duration.ofSeconds(10));
            if (receivedMessage != null)
            {
                if ( receivedMessage.getProperties() != null ) {
                    for (String prop : receivedMessage.getProperties().keySet()) {
                        System.out.printf("%s=%s, ", prop, receivedMessage.getProperties().get(prop));
                    }
                }
                System.out.printf("CorrelationId=%s\n", receivedMessage.getCorrelationId());
                receivedMessages++;
            }
            else
            {
                // No more messages to receive.
                break;
            }
        }
        System.out.printf("Received %s messages from subscription %s.\n", receivedMessages, subsName);
    }

    public static void main(String[] args) {

        System.exit(runApp(args, (connectionString) -> {
            TopicFilters app = new TopicFilters();
            try {
                app.run(connectionString);
                return 0;
            } catch (Exception e) {
                System.out.printf("%s", e.toString());
                return 1;
            }
        }));
    }

    static final String SB_SAMPLES_CONNECTIONSTRING = "SB_SAMPLES_CONNECTIONSTRING";

    public static int runApp(String[] args, Function<String, Integer> run) {
        try {

            String connectionString = null;

            // parse connection string from command line
            Options options = new Options();
            options.addOption(new Option("c", true, "Connection string"));
            CommandLineParser clp = new DefaultParser();
            CommandLine cl = clp.parse(options, args);
            if (cl.getOptionValue("c") != null) {
                connectionString = cl.getOptionValue("c");
            }

            // get overrides from the environment
            String env = System.getenv(SB_SAMPLES_CONNECTIONSTRING);
            if (env != null) {
                connectionString = env;
            }

            if (connectionString == null) {
                HelpFormatter formatter = new HelpFormatter();
                formatter.printHelp("run jar with", "", options, "", true);
                return 2;
            }
            return run.apply(connectionString);
        } catch (Exception e) {
            System.out.printf("%s", e.toString());
            return 3;
        }
    }
}
