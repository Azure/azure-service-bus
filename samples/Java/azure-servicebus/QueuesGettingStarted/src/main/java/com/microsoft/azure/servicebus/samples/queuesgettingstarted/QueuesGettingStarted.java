// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

package com.microsoft.azure.servicebus.samples.queuesgettingstarted;

import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;
import com.google.gson.Gson;

import static java.nio.charset.StandardCharsets.*;

import java.time.Duration;
import java.util.*;
import java.util.concurrent.*;
import java.util.function.Function;

import org.apache.commons.cli.*;

public class QueuesGettingStarted {

    QueueClient sendClient;
    QueueClient receiveClient;

    public CompletableFuture<Void> Run(String connectionString) throws Exception {

        ScheduledExecutorService executor = Executors.newScheduledThreadPool(2);

        // Create a QueueClient instance using the connection string builder
        // We set the receive mode to "PeekLock", meaning the message is delivered
        // under a lock and must be acknowledged ("completed") to be removed from the queue
        this.receiveClient = new QueueClient(new ConnectionStringBuilder(connectionString, "BasicQueue"), ReceiveMode.PEEKLOCK);
        this.InitializeReceiver();

        this.sendClient = new QueueClient(new ConnectionStringBuilder(connectionString, "BasicQueue"), ReceiveMode.PEEKLOCK);
        CompletableFuture sendTask = this.SendMessagesAsync();

        // wait for ENTER or 10 seconds elapsing
        executor.invokeAny(Arrays.asList(() -> {
            System.in.read();
            return 0;
        }, () -> {
            Thread.sleep(10*000);
            return 0;
        }));

        return CompletableFuture.allOf(
                this.receiveClient.closeAsync(),
                sendTask.thenRun(() -> this.sendClient.closeAsync())
        );

    }

    CompletableFuture<Void> SendMessagesAsync() {
        Gson gson = new Gson();
        ArrayList<HashMap<String, String>> data = new ArrayList<HashMap<String, String>>() {{
            add(new HashMap<String, String>() {{
                put("name", "Heisenberg");
                put("firstName", "Werner");
            }});
            add(new HashMap<String, String>() {{
                put("name", "Curie");
                put("firstName", "Marie");
            }});
            add(new HashMap<String, String>() {{
                put("name", "Hawking");
                put("firstName", "Steven");
            }});
            add(new HashMap<String, String>() {{
                put("name", "Newton");
                put("firstName", "Isaac");
            }});
            add(new HashMap<String, String>() {{
                put("name", "Bohr");
                put("firstName", "Niels");
            }});
            add(new HashMap<String, String>() {{
                put("name", "Faraday");
                put("firstName", "Michael");
            }});
            add(new HashMap<String, String>() {{
                put("name", "Galilei");
                put("firstName", "Galileo");
            }});
            add(new HashMap<String, String>() {{
                put("name", "Kepler");
                put("firstName", "Johannes");
            }});
            add(new HashMap<String, String>() {{
                put("name", "Kopernikus");
                put("firstName", "Nikolaus");
            }});
        }};

        List<CompletableFuture> tasks = new ArrayList<>();
        for (int i = 0; i < data.size(); i++) {
            final String messageId = Integer.toString(i);
            Message message = new Message(gson.toJson(data.get(i), Map.class).getBytes(UTF_8));
            message.setContentType("application/json");
            message.setLabel("Scientist");
            message.setMessageId(messageId);
            message.setTimeToLive(Duration.ofMinutes(2));

            tasks.add(
                    this.sendClient.sendAsync(message).thenRunAsync(() -> {
                        System.out.printf("Message sent: Id = %s\n", message.getMessageId());
                    }));
        }
        return CompletableFuture.allOf(tasks.toArray(new CompletableFuture<?>[tasks.size()]));
    }

    void InitializeReceiver() throws Exception {
        Gson gson = new Gson();
        // register the RegisterMessageHandler callback
        this.receiveClient.registerMessageHandler(new IMessageHandler() {
                  // callback invoked when the message handler loop has obtained a message
                  public CompletableFuture<Void> onMessageAsync(IMessage message) {
                      // receives message is passed to callback
                      if (message.getLabel() != null &&
                              message.getContentType() != null &&
                              message.getLabel().contentEquals("Scientist") &&
                              message.getContentType().contentEquals("application/json")) {

                          byte[] body = message.getBody();
                          Map scientist = gson.fromJson(new String(body, UTF_8), Map.class);

                          System.out.printf(
                                  "\t\t\t\tMessage received: \n\t\t\t\t\t\tMessageId = %s, \n\t\t\t\t\t\tSequenceNumber = %s, \n\t\t\t\t\t\tEnqueuedTimeUtc = %s," +
                                          "\n\t\t\t\t\t\tExpiresAtUtc = %s, \n\t\t\t\t\t\tContentType = \"%s\",  \n\t\t\t\t\t\tContent: [ firstName = %s, name = %s ]",
                                  message.getMessageId(),
                                  message.getSequenceNumber(),
                                  message.getEnqueuedTimeUtc(),
                                  message.getExpiresAtUtc(),
                                  message.getContentType(),
                                  scientist != null ? scientist.get("firstName") : "",
                                  scientist != null ? scientist.get("name") : "");
                      }
                      return receiveClient.completeAsync(message.getLockToken());
                  }

                  // callback invoked when the message handler has an exception to report
                  public void notifyException(Throwable throwable, ExceptionPhase exceptionPhase) {
                      System.out.printf(exceptionPhase + "-" + throwable.getMessage());
                  }
              },
              // 1 concurrent call, messages are auto-completed, auto-renew duration
              new MessageHandlerOptions(1, false, Duration.ofMinutes(1)));

    }

    public static void main(String[] args) {

        System.exit(runApp(args, (connectionString) -> {
            QueuesGettingStarted app = new QueuesGettingStarted();
            try {
                app.Run(connectionString).join();
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
