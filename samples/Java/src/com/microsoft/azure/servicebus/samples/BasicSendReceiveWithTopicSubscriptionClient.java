// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

package com.microsoft.azure.servicebus.samples;

import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;

import java.time.Duration;
import java.util.concurrent.CompletableFuture;

public class BasicSendReceiveWithTopicSubscriptionClient {
    // Connection String for the namespace can be obtained from the Azure portal under the
    // 'Shared Access policies' section.
    private static final String connectionString = "{connection string}";
    private static final String topicName = "{topic name}";
    private static final String subscriptionName = "{subscription name}";
    private static ITopicClient topicClient;
    private static ISubscriptionClient subscriptionClient;
    private static int totalSend = 100;
    private static int totalReceived = 0;

    public static void main(String[] args) throws Exception {

        Log.log("Starting BasicSendReceiveWithTopicSubscriptionClient sample.");

        // create client
        Log.log("Create topic client.");
        topicClient = new TopicClient(new ConnectionStringBuilder(connectionString, topicName));
        Log.log("Create subscription client.");
        subscriptionClient = new SubscriptionClient(new ConnectionStringBuilder(connectionString, topicName + "/subscriptions/" + subscriptionName), ReceiveMode.PeekLock);

        // send and receive
        subscriptionClient.registerMessageHandler(new MessageHandler(subscriptionClient), new MessageHandlerOptions(1, false, Duration.ofMinutes(1)));
        for (int i = 0; i < totalSend; i++) {
            int j = i;
            Log.log("Sending message #%d.", j);
            topicClient.sendAsync(new Message("" + i)).thenRunAsync(() -> { Log.log("Sent message #%d.", j);});
        }

        while(totalReceived != totalSend) {
            Thread.sleep(1000);
        }

        Log.log("Received all messages, exiting the sample.");
        Log.log("Closing subscription client.");
        subscriptionClient.close();
        Log.log("Closing topic client.");
        topicClient.close();
    }

    static class MessageHandler implements IMessageHandler {
        private ISubscriptionClient subscriptionClient;

        public MessageHandler(ISubscriptionClient subscriptionClient) {
            this.subscriptionClient = subscriptionClient;
        }

        @Override
        public CompletableFuture<Void> onMessageAsync(IMessage iMessage) {
            Log.log("Received message with sq#: %d and lock token: %s.", iMessage.getSequenceNumber(), iMessage.getLockToken());
            return this.subscriptionClient.completeAsync(iMessage.getLockToken()).thenRunAsync(() -> {
                Log.log("Completed message sq#: %d and locktoken: %s", iMessage.getSequenceNumber(), iMessage.getLockToken());
                totalReceived++;
            });
        }

        @Override
        public void notifyException(Throwable throwable, ExceptionPhase exceptionPhase) {
            Log.log(exceptionPhase + "-" + throwable.getMessage());
        }
    }
}
