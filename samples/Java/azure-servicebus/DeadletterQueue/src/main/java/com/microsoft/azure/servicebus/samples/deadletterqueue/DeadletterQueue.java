// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

package com.microsoft.azure.servicebus.samples.deadletterqueue;

import com.google.gson.reflect.TypeToken;
import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;
import com.google.gson.Gson;

import static java.nio.charset.StandardCharsets.*;

import java.time.Duration;
import java.util.*;
import java.util.concurrent.*;
import java.util.function.Function;

import com.microsoft.azure.servicebus.primitives.ServiceBusException;
import org.apache.commons.cli.*;

public class DeadletterQueue {

    static final Gson GSON = new Gson();

    public void run(String connectionString) throws Exception {

        CompletableFuture<Void> receiveTask;
        CompletableFuture<Void> fixUpTask;
        IMessageSender sendClient;

        sendClient = ClientFactory.createMessageSenderFromConnectionStringBuilder(new ConnectionStringBuilder(connectionString, "BasicQueue"));

        // max delivery-count scenario
        this.sendMessagesAsync(sendClient, 1).join();
        this.exceedMaxDelivery(connectionString, "BasicQueue").join();

        // fix-up scenario
        this.sendMessagesAsync(sendClient, Integer.MAX_VALUE);
        receiveTask = this.receiveMessagesAsync(connectionString, "BasicQueue");
        fixUpTask = this.PickUpAndFixDeadletters(connectionString, "BasicQueue", sendClient);


        // wait for ENTER or 10 seconds elapsing
        waitForEnter(10);

        receiveTask.cancel(true);
        fixUpTask.cancel(true);

        CompletableFuture.allOf(
                sendClient.closeAsync(),
                receiveTask.exceptionally(t -> {if (t instanceof CancellationException) {return null;} throw new RuntimeException((Throwable) t);}),
                fixUpTask.exceptionally(t -> {if (t instanceof CancellationException) {return null;} throw new RuntimeException((Throwable) t);})
        ).join();
    }

    CompletableFuture<Void> sendMessagesAsync(IMessageSender sendClient, int maxMessages) {
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
        for (int i = 0; i < Math.min(data.size(), maxMessages); i++) {
            final String messageId = Integer.toString(i);
            Message message = new Message(GSON.toJson(data.get(i), Map.class).getBytes(UTF_8));
            message.setContentType("application/json");
            message.setLabel(i % 2 == 0 ? "Scientist" : "Physicist");
            message.setMessageId(messageId);
            message.setTimeToLive(Duration.ofMinutes(2));
            System.out.printf("Message sending: Id = %s\n", message.getMessageId());
            tasks.add(
                    sendClient.sendAsync(message).thenRunAsync(() -> {
                        System.out.printf("\tMessage acknowledged: Id = %s\n", message.getMessageId());
                    }));
        }
        return CompletableFuture.allOf(tasks.toArray(new CompletableFuture<?>[tasks.size()]));
    }

    CompletableFuture<Void> exceedMaxDelivery(String connectionString, String queueName) throws Exception {
        IMessageReceiver receiver = ClientFactory.createMessageReceiverFromConnectionStringBuilder(new ConnectionStringBuilder(connectionString, "BasicQueue"), ReceiveMode.PEEKLOCK);

        while (true) {
            IMessage msg = receiver.receive(Duration.ofSeconds(2));
            if (msg != null) {
                System.out.printf("Picked up message; DeliveryCount %d\n", msg.getDeliveryCount());
                receiver.abandon(msg.getLockToken());
            } else {
                break;
            }
        }
        receiver.close();

        IMessageReceiver deadletterReceiver = ClientFactory.createMessageReceiverFromConnectionStringBuilder(new ConnectionStringBuilder(connectionString, "BasicQueue/$deadletterqueue"), ReceiveMode.PEEKLOCK);
        while (true) {
            IMessage msg = deadletterReceiver.receive(Duration.ofSeconds(2));
            if (msg != null) {
                System.out.printf("\nDeadletter message:\n");
                if (msg.getProperties() != null) {
                    for (String prop : msg.getProperties().keySet()) {
                        System.out.printf("\t%s=%s\n", prop, msg.getProperties().get(prop));
                    }
                }
                deadletterReceiver.complete(msg.getLockToken());
            } else {
                break;
            }
        }
        deadletterReceiver.close();
        return CompletableFuture.completedFuture(null);
    }

    CompletableFuture receiveMessagesAsync(String connectionString, String queueName) throws Exception {

        CompletableFuture running = new CompletableFuture();
        QueueClient receiver = new QueueClient(new ConnectionStringBuilder(connectionString, "BasicQueue"), ReceiveMode.PEEKLOCK);

        running.whenComplete((r, t) -> {
            try {
                receiver.close();
            } catch (ServiceBusException e) {
                System.out.printf(e.getMessage());
            }
        });


        // register the RegisterMessageHandler callback
        receiver.registerMessageHandler(
                new IMessageHandler() {
                    // callback invoked when the message handler loop has obtained a message
                    public CompletableFuture<Void> onMessageAsync(IMessage message) {
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
                        } else {
                            return receiver.deadLetterAsync(message.getLockToken());
                        }
                        return receiver.completeAsync(message.getLockToken());
                    }

                    // callback invoked when the message handler has an exception to report
                    public void notifyException(Throwable throwable, ExceptionPhase exceptionPhase) {
                        System.out.printf(exceptionPhase + "-" + throwable.getMessage());
                    }
                },
                // 1 concurrent call, messages are auto-completed, auto-renew duration
                new MessageHandlerOptions(1, false, Duration.ofMinutes(1)));

        return running;
    }

    CompletableFuture PickUpAndFixDeadletters(String connectionString, String queueName, IMessageSender resubmitSender) throws Exception {
        CompletableFuture running = new CompletableFuture();
        QueueClient receiver = new QueueClient(new ConnectionStringBuilder(connectionString, "BasicQueue/$deadletterqueue"), ReceiveMode.PEEKLOCK);

        running.whenComplete((r, t) -> {
            try {
                receiver.close();
            } catch (ServiceBusException e) {
                System.out.printf(e.getMessage());
            }
        });

        // register the RegisterMessageHandler callback
        receiver.registerMessageHandler(
                new IMessageHandler() {
                    // callback invoked when the message handler loop has obtained a message
                    public CompletableFuture<Void> onMessageAsync(IMessage message) {
                        try {
                            IMessage resubmitMessage = new Message(message.getBody());
                            if (message.getLabel() != null && message.getLabel().contentEquals("Physicist")) {
                                System.out.printf(
                                        "\n\t\tFixing: \n\t\t\tMessageId = %s, \n\t\t\tSequenceNumber = %s, \n\t\t\tLabel = %s\n",
                                        message.getMessageId(),
                                        message.getSequenceNumber(),
                                        message.getLabel());
                                resubmitMessage.setMessageId(message.getMessageId());
                                resubmitMessage.setLabel("Scientist");
                                resubmitMessage.setContentType(message.getContentType());
                                resubmitMessage.setTimeToLive(Duration.ofMinutes(2));
                                resubmitSender.send(resubmitMessage);
                            }
                            return receiver.completeAsync(message.getLockToken());
                        } catch (Exception e) {
                            CompletableFuture failure = new CompletableFuture();
                            failure.completeExceptionally(e);
                            return failure;
                        }
                    }

                    // callback invoked when the message handler has an exception to report
                    public void notifyException(Throwable throwable, ExceptionPhase exceptionPhase) {
                        System.out.printf(exceptionPhase + "-" + throwable.getMessage());
                    }
                },
                // 1 concurrent call, messages are auto-completed, auto-renew duration
                new MessageHandlerOptions(1, false, Duration.ofMinutes(1)));

        return running;
    }

    public static void main(String[] args) {

        System.exit(runApp(args, (connectionString) -> {
            DeadletterQueue app = new DeadletterQueue();
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
