// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

package com.microsoft.azure.servicebus.samples.duplicatedetection;

import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;

import java.time.Duration;
import java.util.*;
import java.util.function.Function;

import org.apache.commons.cli.*;

public class DuplicateDetection {


    void run(String connectionString) throws Exception {
        send(connectionString);
        receive(connectionString);
    }

    void send(String connectionString) throws Exception {
        IMessageSender sender = ClientFactory.createMessageSenderFromConnectionStringBuilder(new ConnectionStringBuilder(connectionString, "DupdetectQueue"));

        String messageId = UUID.randomUUID().toString();
        // Send messages to queue
        System.out.printf("\tSending messages to %s ...\n", sender.getEntityPath());
        IMessage message = new Message();
        message.setMessageId(messageId);
        message.setTimeToLive(Duration.ofMinutes(1));
        sender.send(message);
        System.out.printf("\t=> Sent a message with messageId %s\n", message.getMessageId());

        IMessage message2 = new Message();
        message2.setMessageId(messageId);
        message2.setTimeToLive(Duration.ofMinutes(1));
        sender.send(message2);
        System.out.printf("\t=> Sent a duplicate message with messageId %s\n", message.getMessageId());
        sender.close();
    }

    void receive(String connectionString) throws Exception {
        IMessageReceiver receiver = ClientFactory.createMessageReceiverFromConnectionStringBuilder(new ConnectionStringBuilder(connectionString, "DupdetectQueue"), ReceiveMode.PEEKLOCK);

        // receive messages from queue
        String receivedMessageId = "";

        System.out.printf("\n\tWaiting up to 5 seconds for messages from %s ...\n", receiver.getEntityPath());
        while (true) {
            IMessage receivedMessage = receiver.receive(Duration.ofSeconds(5));

            if (receivedMessage == null) {
                break;
            }
            System.out.printf("\t<= Received a message with messageId %s\n", receivedMessage.getMessageId());
            receiver.complete(receivedMessage.getLockToken());
            if (receivedMessageId.contentEquals(receivedMessage.getMessageId())) {
                throw new Exception("Received a duplicate message!");
            }
            receivedMessageId = receivedMessage.getMessageId();
        }
        System.out.printf("\tDone receiving messages from %s\n", receiver.getEntityPath());

        receiver.close();
    }

    public static void main(String[] args) {

        System.exit(runApp(args, (connectionString) -> {
            DuplicateDetection app = new DuplicateDetection();
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
