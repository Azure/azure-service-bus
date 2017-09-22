// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

package com.microsoft.azure.servicebus.samples.queueclientquickstart;

import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;
import org.apache.commons.cli.*;
import org.apache.log4j.*;

import java.time.Duration;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.atomic.AtomicInteger;

public class QueueClientQuickstart {
    // Connection String for the namespace can be obtained from the Azure portal under the
    // 'Shared Access policies' section.

    // connection string can be initialized via environment variable
    private static String connectionString = System.getenv("SB_SAMPLES_CONNECTIONSTRING");
    // queue name can be initialiyzed via environment variable
    private static String queueName = System.getenv("SB_SAMPLES_QUEUENAME");
    // queue client instance
    private static IQueueClient queueClient;
    // message send/receive counters
    private static int totalToSend = 100;
    private static AtomicInteger totalReceived = new AtomicInteger(0);
    // log4j logger 
    private static Logger logger = Logger.getRootLogger();

    public static void main(String[] args) throws Exception {
        // Parse and evaluate command line. Exit when there's not
        // enough configuration information in environment and to 
        // command line to continue
        if (!parseCommandLine(args)) {
            return;
        }

        logger.info("Starting BasicSendReceiveWithQueueClient sample");

        // Create a QueueClient instance using the connection string builder
        logger.info("Create queue client.");
        queueClient = new QueueClient(new ConnectionStringBuilder(connectionString, queueName), ReceiveMode.PEEKLOCK);

        // send messages in a loop
        for (int i = 0; i < totalToSend; i++) {
            int currentMessageCounter = i;
            queueClient.sendAsync(new Message("" + i)).thenRunAsync(() -> {
                logger.info(String.format("Sent message #%d.", currentMessageCounter));
            });
        }

        // register the anonymous message handler which receives/handles the messages. 
        queueClient.registerMessageHandler(new IMessageHandler() {
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

        // wait on the main thread until all sent messages have been received
        while (totalReceived.get() < totalToSend) {
            Thread.sleep(1000);
        }

        logger.info("Received all messages, exiting the sample.");
        logger.info("Closing queue client.");
        queueClient.close();
    }

    static boolean parseCommandLine(String[] args) throws Exception {
        Options options = new Options();
        options.addOption(new Option("c", true, "Connection string"));
        options.addOption(new Option("q", true, "Queue name"));
        CommandLineParser clp = new DefaultParser();
        CommandLine cl = clp.parse(options, args);
        if (cl.getOptionValue("c") != null) {
            connectionString = cl.getOptionValue("c");
        }
        if (cl.getOptionValue("q") != null) {
            queueName = cl.getOptionValue("q");
        }

        if (connectionString == null || queueName == null) {
            HelpFormatter formatter = new HelpFormatter();
            formatter.printHelp("run jar with", "", options, "", true);
            return false;
        }
        return true;
    }
}
