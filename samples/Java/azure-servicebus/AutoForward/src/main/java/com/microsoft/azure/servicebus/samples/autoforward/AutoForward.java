// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

package com.microsoft.azure.servicebus.samples.autoforward;

import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;

import static java.nio.charset.StandardCharsets.*;

import java.time.Duration;
import java.util.*;
import java.util.function.Function;

import org.apache.commons.cli.*;

public class AutoForward {


    public void run(String connectionString) throws Exception
    {
        IMessageSender topicSender;
        IMessageSender queueSender;
        IMessageReceiver targetQueueReceiver;

        System.out.printf("\nSending messages\n");
        topicSender = ClientFactory.createMessageSenderFromConnectionStringBuilder(
                new ConnectionStringBuilder(connectionString, "AutoForwardSourceTopic"));
        topicSender.send(createMessage("M1"));

        queueSender = ClientFactory.createMessageSenderFromConnectionStringBuilder(
                new ConnectionStringBuilder(connectionString, "AutoForwardTargetQueue"));
        queueSender.send(createMessage("M2"));

        System.out.printf("\nReceiving messages\n");
        targetQueueReceiver = ClientFactory.createMessageReceiverFromConnectionStringBuilder(
                new ConnectionStringBuilder(connectionString, "AutoForwardTargetQueue"), ReceiveMode.PEEKLOCK);
        for (int i = 0; i < 2; i++)
        {
            IMessage message = targetQueueReceiver.receive(Duration.ofSeconds(10));
            if (message != null)
            {
                this.printReceivedMessage(message);
                targetQueueReceiver.complete(message.getLockToken());
            }
            else
            {
                throw new Exception("Expected message not receive\n");
            }
        }
        targetQueueReceiver.close();
    }

    void printReceivedMessage(IMessage receivedMessage) {
        System.out.printf("Received message:\n" + "\tLabel:\t%s\n" + "\tBody:\t%s\n",
                receivedMessage.getLabel(), new String(receivedMessage.getBody(), UTF_8));
        if (receivedMessage.getProperties() != null)
            for (String p : receivedMessage.getProperties().keySet()) {
                System.out.printf("\tProperty:\t%s = %s\n", p, receivedMessage.getProperties().get(p));
            }
    }

    // Create a new Service Bus message.
    IMessage createMessage(String label)
    {
        // Create a Service Bus message.
        IMessage msg = new Message(("This is the body of message \"" + label + "\".").getBytes(UTF_8));
        msg.setProperties(new HashMap<String, String>(){{
            put("Priority", "1");
            put("Importance", "High");
        }});
        msg.setLabel(label);
        msg.setTimeToLive(Duration.ofSeconds(90));
        return msg;
    }

    public static void main(String[] args) {

        System.exit(runApp(args, (connectionString) -> {
            AutoForward app = new AutoForward();
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
