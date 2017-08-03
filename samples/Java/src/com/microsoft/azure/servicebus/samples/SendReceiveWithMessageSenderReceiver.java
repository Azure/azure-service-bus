// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
package com.microsoft.azure.servicebus.samples;

import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;
import com.microsoft.azure.servicebus.primitives.ServiceBusException;

import java.util.concurrent.ExecutionException;
import java.util.concurrent.atomic.AtomicInteger;

public class SendReceiveWithMessageSenderReceiver {
    private static final String connectionString = "{connection string}";
    private static final String queueName = "{queue name}";
    private static IMessageReceiver messageReceiver;
    private static IMessageSender messageSender;
    private static int totalSend = 100;
    private static AtomicInteger totalReceived = new AtomicInteger(0);

    public static void main(String[] args) throws Exception {
        Log.log("Starting SendReceiveWithMessageSenderReceiver sample.");

        Log.log("Create message receiver.");
        messageReceiver = ClientFactory.createMessageReceiverFromConnectionStringBuilder(new ConnectionStringBuilder(connectionString, queueName), ReceiveMode.PEEKLOCK);
        Log.log("Create message sender.");
        messageSender = ClientFactory.createMessageSenderFromConnectionStringBuilder(new ConnectionStringBuilder(connectionString, queueName));

        send(messageSender);

        receive(messageReceiver);

        Log.log("Received all messages, exiting the sample.");

        Log.log("Closing message receiver.");
        messageReceiver.close();
        Log.log("Closing message sender.");
        messageSender.close();
    }

    static void send(IMessageSender sender) {
        for (int i = 0; i < totalSend; i++) {
            int j = i;
            Log.log("Sending message #%d.", j);
            sender.sendAsync(new Message("" + i)).thenRunAsync(() -> {
                Log.log("Sent message #%d.", j);
            });
        }
    }

    static void receive(IMessageReceiver receiver) throws InterruptedException, ExecutionException, ServiceBusException {
        while (totalReceived.get() != totalSend) {
            receiver.receiveAsync().thenAcceptAsync(m -> {
                if (m != null) {
                    Log.log("Received message with sq#: %d and lock token: %s.", m.getSequenceNumber(), m.getLockToken());
                    receiver.completeAsync(m.getLockToken()).thenRunAsync(() -> {
                        Log.log("Completed message sq#: %d and lock token: %s", m.getSequenceNumber(), m.getLockToken());
                        totalReceived.incrementAndGet();
                    });
                }
            });
        }
    }
}
