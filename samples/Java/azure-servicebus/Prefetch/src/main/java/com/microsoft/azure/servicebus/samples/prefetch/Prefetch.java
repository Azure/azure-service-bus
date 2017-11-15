// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

package com.microsoft.azure.servicebus.samples.prefetch;

import com.google.common.base.Stopwatch;
import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;

import java.time.Duration;
import java.util.*;
import java.util.concurrent.*;
import java.util.function.Function;

import org.apache.commons.cli.*;

public class Prefetch {

    public void run(String connectionString) throws Exception
    {
        IMessageSender sender;
        IMessageReceiver receiver;

        // Create communication objects to send and receive on the queue
        sender = ClientFactory.createMessageSenderFromConnectionStringBuilder(new ConnectionStringBuilder(connectionString, "BasicQueue"));
        // run 1
        receiver = ClientFactory.createMessageReceiverFromConnectionStringBuilder(new ConnectionStringBuilder(connectionString, "BasicQueue"), ReceiveMode.PEEKLOCK);
        receiver.setPrefetchCount(0);
        // Send and Receive messages with prefetch OFF
        long timeTaken1 = this.sendAndReceiveMessages(sender, receiver, 100);
        receiver.close();

        // run 2
        receiver = ClientFactory.createMessageReceiverFromConnectionStringBuilder(new ConnectionStringBuilder(connectionString, "BasicQueue"), ReceiveMode.PEEKLOCK);
        receiver.setPrefetchCount(50);
        // Send and Receive messages with prefetch ON
        long timeTaken2 = this.sendAndReceiveMessages(sender, receiver, 100);

        receiver.close();

        // Calculate the time difference
        long timeDifference = timeTaken1 - timeTaken2;

        System.out.printf("\nTime difference = %d milliseconds\n", timeDifference);
    }

    long sendAndReceiveMessages(IMessageSender sender, IMessageReceiver receiver, int messageCount) throws Exception
    {
        // Now we can start sending messages.
        Random rnd = new Random();
        byte[] mockPayload = new byte[100]; // 100 random-byte payload

        rnd.nextBytes(mockPayload);

        System.out.printf("\nSending %d messages to the queue\n", messageCount);
        ArrayList<CompletableFuture<Void>> sendOps = new ArrayList<>();
        for (int i = 0; i < messageCount; i++)
        {
            IMessage message = new Message(mockPayload);
            message.setTimeToLive(Duration.ofMinutes(5));
            sendOps.add(sender.sendAsync(message));
        }
        CompletableFuture.allOf(sendOps.toArray(new CompletableFuture<?>[sendOps.size()])).join();

        System.out.printf("Send completed\n");

        // Receive the messages
        System.out.printf("Receiving messages...\n");

        // Start stopwatch
        Stopwatch stopWatch = Stopwatch.createStarted();

        IMessage receivedMessage = receiver.receive(Duration.ofSeconds(5));
        while (receivedMessage != null) {
            // here's where you'd do any work

            // complete (round trips)
            receiver.complete(receivedMessage.getLockToken());

            if (--messageCount <= 0)
                break;

            // now get the next message
            receivedMessage = receiver.receive(Duration.ofSeconds(5));
        }
        // Stop the stopwatch
        stopWatch.stop();

        System.out.printf("Receive completed\n");

        long timeTaken = stopWatch.elapsed(TimeUnit.MILLISECONDS);
        System.out.printf("Time to receive and complete all messages = %d milliseconds\n", timeTaken);

        return timeTaken;
    }


    public static void main(String[] args) {

        System.exit(runApp(args, (connectionString) -> {
            Prefetch app = new Prefetch();
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
