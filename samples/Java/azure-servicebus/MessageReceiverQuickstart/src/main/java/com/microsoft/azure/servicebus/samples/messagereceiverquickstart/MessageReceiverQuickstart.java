// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
package com.microsoft.azure.servicebus.samples.messagereceiverquickstart;

import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;
import com.microsoft.azure.servicebus.primitives.ServiceBusException;
import org.apache.commons.cli.*;
import org.apache.log4j.*;

import java.util.concurrent.ExecutionException;
import java.util.concurrent.atomic.AtomicInteger;

public class MessageReceiverQuickstart {
    // connection string can be initialized via environment variable
    private static String connectionString = System.getenv("SB_SAMPLES_CONNECTIONSTRING"); 
    // queue name can be initialiyzed via environment variable
    private static String queueName = System.getenv("SB_SAMPLES_QUEUENAME");

    private static IMessageReceiver messageReceiver;
    private static IMessageSender messageSender;
    // message send/receive counters
    private static int totalSend = 100;
    private static AtomicInteger totalReceived = new AtomicInteger(0);
    // log4j logger 
    private static Logger logger = Logger.getRootLogger();

    public static void main(String[] args) throws Exception {
        
        if ( !parseCommandLine(args) ) {
            return;
        }

        logger.info("Starting SendReceiveWithMessageSenderReceiver sample.");

        logger.info("Create message receiver.");
        messageReceiver = ClientFactory.createMessageReceiverFromConnectionStringBuilder(new ConnectionStringBuilder(connectionString, queueName), ReceiveMode.PEEKLOCK);
        logger.info("Create message sender.");
        messageSender = ClientFactory.createMessageSenderFromConnectionStringBuilder(new ConnectionStringBuilder(connectionString, queueName));

        send(messageSender);

        receive(messageReceiver);

        logger.info("Received all messages, exiting the sample.");

        logger.info("Closing message receiver.");
        messageReceiver.close();
        logger.info("Closing message sender.");
        messageSender.close();
    }

    static void send(IMessageSender sender) {
        for (int i = 0; i < totalSend; i++) {
            int currentMessageCounter = i;
            logger.info(String.format("Sending message #%d.", currentMessageCounter));
            sender.sendAsync(new Message("" + i)).thenRunAsync(() -> {
                logger.info(String.format("Sent message #%d.", currentMessageCounter));
            });
        }
    }

    static void receive(IMessageReceiver receiver) throws InterruptedException, ExecutionException, ServiceBusException {
        while (totalReceived.get() != totalSend) {
            receiver.receiveAsync().thenAcceptAsync(m -> {
                if (m != null) {
                    logger.info(String.format("Received message with sq#: %d and lock token: %s.", m.getSequenceNumber(), m.getLockToken()));
                    receiver.completeAsync(m.getLockToken()).thenRunAsync(() -> {
                        logger.info(String.format("Completed message %d sq#: %d and lock token: %s", totalReceived.incrementAndGet(), m.getSequenceNumber(), m.getLockToken()));
                    });
                }
            });
        }
    }

    static boolean parseCommandLine(String[] args) throws Exception{
        Options options = new Options();
        options.addOption(new Option("c", true, "Connection string"));
        options.addOption(new Option("t", true, "Queue name"));
        CommandLineParser clp = new DefaultParser();
        CommandLine cl = clp.parse(options, args);
        if ( cl.getOptionValue("c") != null) {
            connectionString = cl.getOptionValue("c");
        }
        if ( cl.getOptionValue("q") != null) {
            queueName = cl.getOptionValue("q");
        }
        
        if ( connectionString == null || queueName == null) {
            HelpFormatter formatter = new HelpFormatter();
            formatter.printHelp("run jar with", "", options, "", true);    
            return false;
        }
        return true;
    }
}
