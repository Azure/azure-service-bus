// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

package com.microsoft.azure.servicebus.samples.queueswithproxy;

import com.google.gson.reflect.TypeToken;
import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;
import com.google.gson.Gson;

import static java.nio.charset.StandardCharsets.*;

import java.io.IOException;
import java.net.*;
import java.time.Duration;
import java.util.*;
import java.util.concurrent.*;
import java.util.function.Function;

import com.microsoft.azure.servicebus.primitives.StringUtil;
import com.microsoft.azure.servicebus.primitives.TransportType;
import org.apache.commons.cli.*;
import org.apache.commons.lang3.math.NumberUtils;

public class QueuesWithProxy {

    static final Gson GSON = new Gson();

    public void run(String connectionString) throws Exception {
        // Set the transport type to AmqpWithWebsockets
        ConnectionStringBuilder connStrBuilder = new ConnectionStringBuilder(connectionString, "BasicQueue");
        connStrBuilder.setTransportType(TransportType.AMQP_WEB_SOCKETS);

        // Create a QueueClient instance for receiving using the connection string builder
        // We set the receive mode to "PeekLock", meaning the message is delivered
        // under a lock and must be acknowledged ("completed") to be removed from the queue
        QueueClient receiveClient = new QueueClient(connStrBuilder, ReceiveMode.PEEKLOCK);
        // We are using single thread executor as we are only processing one message at a time
    	ExecutorService executorService = Executors.newSingleThreadExecutor();
        this.registerReceiver(receiveClient, executorService);

        // Create a QueueClient instance for sending and then asynchronously send messages.
        // Close the sender once the send operation is complete.
        QueueClient sendClient = new QueueClient(connStrBuilder, ReceiveMode.PEEKLOCK);
        this.sendMessagesAsync(sendClient).thenRunAsync(() -> sendClient.closeAsync());

        // wait for ENTER or 10 seconds elapsing
        waitForEnter(10);

        // shut down receiver to close the receive loop
        receiveClient.close();
        executorService.shutdown();
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
                        new TypeToken<List<HashMap<String, String>>>() {}.getType());

        List<CompletableFuture> tasks = new ArrayList<>();
        for (int i = 0; i < data.size(); i++) {
            final String messageId = Integer.toString(i);
            Message message = new Message(GSON.toJson(data.get(i), Map.class).getBytes(UTF_8));
            message.setContentType("application/json");
            message.setLabel("Scientist");
            message.setMessageId(messageId);
            message.setTimeToLive(Duration.ofMinutes(2));
            System.out.printf("\nMessage sending: Id = %s", message.getMessageId());
            tasks.add(
                    sendClient.sendAsync(message).thenRunAsync(() -> {
                        System.out.printf("\n\tMessage acknowledged: Id = %s", message.getMessageId());
                    }));
        }
        return CompletableFuture.allOf(tasks.toArray(new CompletableFuture<?>[tasks.size()]));
    }

    void registerReceiver(QueueClient queueClient, ExecutorService executorService) throws Exception {

    	
        // register the RegisterMessageHandler callback with executor service
        queueClient.registerMessageHandler(new IMessageHandler() {
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
                                                   }
                                                   return CompletableFuture.completedFuture(null);
                                               }

                                               // callback invoked when the message handler has an exception to report
                                               public void notifyException(Throwable throwable, ExceptionPhase exceptionPhase) {
                                                   System.out.printf(exceptionPhase + "-" + throwable.getMessage());
                                               }
                                           },
                // 1 concurrent call, messages are auto-completed, auto-renew duration
                new MessageHandlerOptions(1, true, Duration.ofMinutes(1)),
                executorService);

    }

    public static void main(String[] args) {

        System.exit(runApp(args, (connectionString) -> {
            QueuesWithProxy app = new QueuesWithProxy();
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
    static final String SB_SAMPLES_PROXY_HOSTNAME = "SB_SAMPLES_PROXY_HOSTNAME";
    static final String SB_SAMPLES_PROXY_PORT = "SB_SAMPLES_PROXY_PORT";

    public static int runApp(String[] args, Function<String, Integer> run) {
        try {

            String connectionString;
            String proxyHostName;
            String proxyPortString;
            int proxyPort;

            // Add command line options and create parser
            Options options = new Options();
            options.addOption(new Option("c", true, "Connection string"));
            options.addOption(new Option("n", true, "Proxy hostname"));
            options.addOption(new Option("p", true, "Proxy port"));

            CommandLineParser clp = new DefaultParser();
            CommandLine cl = clp.parse(options, args);

            // Pull variables from command line options or environment variables
            connectionString = getOptionOrEnv(cl, "c", SB_SAMPLES_CONNECTIONSTRING);
            proxyHostName = getOptionOrEnv(cl, "n", SB_SAMPLES_PROXY_HOSTNAME);
            proxyPortString = getOptionOrEnv(cl, "p", SB_SAMPLES_PROXY_PORT);

            // Check for bad input
            if (StringUtil.isNullOrEmpty(connectionString) ||
                    StringUtil.isNullOrEmpty(proxyHostName) ||
                    StringUtil.isNullOrEmpty(proxyPortString) )
            {
                HelpFormatter formatter = new HelpFormatter();
                formatter.printHelp("run jar with", "", options, "", true);
                return 2;
            }

            if (!NumberUtils.isCreatable(proxyPortString)) {
                System.err.println("Please provide a numerical value for the port");
            }
            proxyPort = Integer.parseInt(proxyPortString);

            // ProxySelector set up for an HTTP proxy
            final ProxySelector systemDefaultSelector = ProxySelector.getDefault();
            ProxySelector.setDefault(new ProxySelector() {
                @Override
                public List<Proxy> select(URI uri) {
                    if (uri != null
                            && uri.getHost() != null
                            && uri.getHost().equalsIgnoreCase(proxyHostName)) {
                        List<Proxy> proxies = new LinkedList<>();
                        proxies.add(new Proxy(Proxy.Type.HTTP, new InetSocketAddress(proxyHostName, proxyPort)));
                        return proxies;
                    }
                    return systemDefaultSelector.select(uri);
                }

                @Override
                public void connectFailed(URI uri, SocketAddress sa, IOException ioe){
                    if (uri == null || sa == null || ioe == null) {
                        throw new IllegalArgumentException("Arguments can't be null.");
                    }
                    systemDefaultSelector.connectFailed(uri, sa, ioe);
                }
            });

            return run.apply(connectionString);
        } catch (Exception e) {
            System.out.printf("%s", e.toString());
            return 3;
        }
    }

    static private String getOptionOrEnv(CommandLine cl, String optionValue, String envName)
    {
        String output = null;

        if (cl.getOptionValue(optionValue) != null) {
            output = cl.getOptionValue("c");
        }

        // get overrides from the environment
        String env = System.getenv(envName);
        if (env != null) {
            output = env;
        }

        return output;
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
