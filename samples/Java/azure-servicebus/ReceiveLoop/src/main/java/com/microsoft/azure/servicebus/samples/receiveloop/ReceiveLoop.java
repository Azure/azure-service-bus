// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

package com.microsoft.azure.servicebus.samples.receiveloop;

import com.google.gson.reflect.TypeToken;
import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;
import com.google.gson.Gson;

import static java.nio.charset.StandardCharsets.*;

import java.time.Duration;
import java.util.*;
import java.util.concurrent.*;
import java.util.function.Function;

import org.apache.commons.cli.*;

public class ReceiveLoop {

    static final Gson GSON = new Gson();


    public void run(String connectionString) throws Exception {

        QueueClient sendClient;
        IMessageReceiver receiver;
        CompletableFuture receiveTask;

        // Create a QueueClient instance using the connection string builder
        // We set the receive mode to "PeekLock", meaning the message is delivered
        // under a lock and must be acknowledged ("completed") to be removed from the queue

        sendClient = new QueueClient(new ConnectionStringBuilder(connectionString, "BasicQueue"), ReceiveMode.PEEKLOCK);
        this.sendMessagesAsync(sendClient).thenRunAsync(() -> sendClient.closeAsync());


        receiver = ClientFactory.createMessageReceiverFromConnectionStringBuilder(
                new ConnectionStringBuilder(connectionString, "BasicQueue"), ReceiveMode.PEEKLOCK);
        receiveTask = this.receiveMessagesAsync(receiver);

        waitForEnter(10);

        receiveTask.cancel(true);
        receiver.close();

        CompletableFuture.allOf( receiveTask.exceptionally(t -> { if (t instanceof CancellationException) { return null; } throw new RuntimeException((Throwable) t); })).join();

    }

    CompletableFuture<Void> sendMessagesAsync(QueueClient sendClient) {

        List<HashMap<String, String>> data =
                GSON.fromJson(
                        "[" +
                                "{'name' = 'Einstein', 'firstName' = 'Albert'}," +
                                "{'name' = 'Heisenberg', 'firstName' = 'Werner'}," +
                                "{'name' = 'Curie', 'firstName' = 'Marie'}," +
                                "{'name' = 'Hawking', 'firstName' = 'Steven'}," +
                                "{'name' = 'Newton', 'firstName' = 'Isaac'}," +
                                "{'name' = 'Bohr', 'firstName' = 'Niels'}," +
                                "{'name' = 'Faraday', 'firstName' = 'Michael'}," +
                                "{'name' = 'Galilei', 'firstName' = 'Galileo'}," +
                                "{'name' = 'Kepler', 'firstName' = 'Johannes'}," +
                                "{'name' = 'Kopernikus', 'firstName' = 'Nikolaus'}" +
                                "]",
                        new TypeToken<List<HashMap<String, String>>>() {
                        }.getType());

        List<CompletableFuture> tasks = new ArrayList<>();
        for (int i = 0; i < data.size(); i++) {
            final String messageId = Integer.toString(i);
            Message message = new Message(GSON.toJson(data.get(i), Map.class).getBytes(UTF_8));
            message.setContentType("application/json");
            message.setLabel("Scientist");
            message.setMessageId(messageId);
            message.setTimeToLive(Duration.ofMinutes(2));

            tasks.add(
                    sendClient.sendAsync(message).thenRunAsync(() -> {
                        System.out.printf("Message sent: Id = %s\n", message.getMessageId());
                    }));
        }
        return CompletableFuture.allOf(tasks.toArray(new CompletableFuture<?>[tasks.size()]));
    }

    CompletableFuture receiveMessagesAsync(IMessageReceiver receiver) {

        CompletableFuture currentTask = new CompletableFuture();

        try {
            CompletableFuture.runAsync(() -> {
                while (!currentTask.isCancelled()) {
                    try {
                        IMessage message = receiver.receive(Duration.ofSeconds(60));
                        if (message != null) {
                            // receives message is passed to callback
                            if (message.getLabel() != null &&
                                    message.getContentType() != null &&
                                    message.getLabel().contentEquals("Scientist") &&
                                    message.getContentType().contentEquals("application/json")) {

                                byte[] body = message.getBody();
                                Map scientist = GSON.fromJson(new String(body, UTF_8), Map.class);

                                System.out.printf(
                                        "\n\t\t\t\tMessage received: \n\t\t\t\t\t\tMessageId = %s, \n\t\t\t\t\t\tSequenceNumber = %s, \n\t\t\t\t\t\tEnqueuedTimeUtc = %s," +
                                                "\n\t\t\t\t\t\tExpiresAtUtc = %s, \n\t\t\t\t\t\tContentType = \"%s\",  \n\t\t\t\t\t\tContent: [ firstName = %s, name = %s ]\n",
                                        message.getMessageId(),
                                        message.getSequenceNumber(),
                                        message.getEnqueuedTimeUtc(),
                                        message.getExpiresAtUtc(),
                                        message.getContentType(),
                                        scientist != null ? scientist.get("firstName") : "",
                                        scientist != null ? scientist.get("name") : "");
                            }
                            receiver.completeAsync(message.getLockToken());
                        }
                    } catch (Exception e) {
                        currentTask.completeExceptionally(e);
                    }
                }
                currentTask.complete(null);
            });
            return currentTask;
        } catch (Exception e) {
            currentTask.completeExceptionally(e);
        }
        return currentTask;
    }


    public static void main(String[] args) {

        System.exit(runApp(args, (connectionString) -> {
            ReceiveLoop app = new ReceiveLoop();
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

    private void waitForEnter(int seconds) {
        ExecutorService executor = Executors.newCachedThreadPool();
        try {
            executor.invokeAny(Arrays.asList(() -> {
                System.in.read();
                return 0;
            }, () -> {
                Thread.sleep(seconds * 1000);
                return 0;
            }));
        } catch (Exception e) {
            // absorb
        }
    }
}
