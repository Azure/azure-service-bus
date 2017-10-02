// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

package com.microsoft.azure.servicebus.samples.topicclientquickstart;

import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;

import java.time.Duration;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.atomic.AtomicInteger;
import org.apache.commons.cli.*;
import org.apache.log4j.*;

public class TopicClientQuickstart {
    // Connection String for the namespace can be obtained from the Azure portal under the
    // 'Shared Access policies' section.
    private static String connectionString = System.getenv("SB_SAMPLES_CONNECTIONSTRING");
    private static String topicName = System.getenv("SB_SAMPLES_TOPICNAME");
    private static String subscriptionName = System.getenv("SB_SAMPLES_SUBSCRIPTIONNAME");

    private static ITopicClient topicClient;
    private static ISubscriptionClient subscriptionClient;
    private static int totalSend = 10;
    private static AtomicInteger totalReceived = new AtomicInteger(0);

    // log4j logger 
    private static Logger logger = Logger.getRootLogger();

    public static void main(String[] args) throws Exception {

        if (!parseCommandLine(args)) {
            return;
        }

        logger.info("Starting BasicSendReceiveWithTopicSubscriptionClient sample.");

        // create client
        logger.info("Create topic client.");
        topicClient = new TopicClient(new ConnectionStringBuilder(connectionString, topicName));
        logger.info("Create subscription client.");
        subscriptionClient = new SubscriptionClient(
                new ConnectionStringBuilder(connectionString, topicName + "/subscriptions/" + subscriptionName),
                ReceiveMode.PEEKLOCK);

        // send messages in a loop
        for (int i = 0; i < totalSend; i++) {
            int currentMessageCounter = i;
            topicClient.sendAsync(new Message("" + i)).thenRunAsync(() -> {
                logger.info(String.format("Sent message #%d.", currentMessageCounter));
            });
        }

        // register message handler
        subscriptionClient.registerMessageHandler(new IMessageHandler() {
            // callback invoked when the message handler loop has obtained a message
            public CompletableFuture<Void> onMessageAsync(IMessage message) {
                // receives message is passed to callback
                logger.info(String.format("Received message %d with sq#: %d and lock token: %s.",
                        totalReceived.incrementAndGet(), message.getSequenceNumber(), message.getLockToken()));
                return CompletableFuture.completedFuture(null);
            }

            // callback invoked when the message handler has an exception to report
            public void notifyException(Throwable throwable, ExceptionPhase exceptionPhase) {
                logger.error(exceptionPhase + "-" + throwable.getMessage());
            }
        },
                // 1 concurrent call, messages are auto-completed, auto-renew duration
                new MessageHandlerOptions(1, true, Duration.ofMinutes(1)));

        while (totalReceived.get() < totalSend) {
            Thread.sleep(1000);
        }

        logger.info("Received all messages, exiting the sample.");
        logger.info("Closing subscription client.");
        subscriptionClient.close();
        logger.info("Closing topic client.");
        topicClient.close();
    }

    static boolean parseCommandLine(String[] args) throws Exception {
        Options options = new Options();
        options.addOption(new Option("c", true, "Connection string"));
        options.addOption(new Option("t", true, "Topic name"));
        options.addOption(new Option("s", true, "Subscription name"));
        CommandLineParser clp = new DefaultParser();
        CommandLine cl = clp.parse(options, args);
        if (cl.getOptionValue("c") != null) {
            connectionString = cl.getOptionValue("c");
        }
        if (cl.getOptionValue("t") != null) {
            topicName = cl.getOptionValue("t");
        }
        if (cl.getOptionValue("s") != null) {
            subscriptionName = cl.getOptionValue("s");
        }

        if (connectionString == null || topicName == null || subscriptionName == null) {
            HelpFormatter formatter = new HelpFormatter();
            formatter.printHelp("run jar with", "", options, "", true);
            return false;
        }
        return true;
    }
}
