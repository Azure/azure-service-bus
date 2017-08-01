package com.microsoft.azure.servicebus.samples;

import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;
import com.microsoft.azure.servicebus.primitives.ServiceBusException;

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutionException;
import java.util.function.Consumer;

public class SendReceiveWithMessageSenderReceiver {
    private static final String connectionString = "Endpoint=sb://INT7-BN3-008-stresssuite25.servicebus.int7.windows-int.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=N46oKT+awT8vGu9gYFkAqeffA3hiVyVhMY78HPwv+/o=";
    private static final String queueName = "queue1";
    private static IMessageReceiver messageReceiver;
    private static IMessageSender messageSender;
    private static int totalSend = 100;
    private static int totalReceived = 0;

    public static void main(String[] args) throws Exception {
        Log.log("Starting SendReceiveWithMessageSenderReceiver sample.");

        Log.log("Create message receiver.");
        messageReceiver = ClientFactory.createMessageReceiverFromConnectionStringBuilder(new ConnectionStringBuilder(connectionString, queueName), ReceiveMode.PeekLock);
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
        while (totalReceived != totalSend) {
            receiver.receiveAsync().thenAcceptAsync(m -> {
                if (m != null) {
                    Log.log("Received message with sq#: %d and lock token: %s.", m.getSequenceNumber(), m.getLockToken());
                    receiver.completeAsync(m.getLockToken()).thenRunAsync(() -> {
                        Log.log("Completed message sq#: %d and lock token: %s", m.getSequenceNumber(), m.getLockToken());
                        totalReceived++;
                    });
                }
            });
        }
    }
}
