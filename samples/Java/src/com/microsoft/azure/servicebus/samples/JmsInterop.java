// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

package com.microsoft.azure.servicebus.samples;

import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;

import javax.jms.*;
import javax.naming.Context;
import javax.naming.InitialContext;
import java.time.Duration;
import java.util.Hashtable;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Currently, Service Bus Java Client only supports sending and receiving AMQP Data Section.
 * In future, the client will support more interpreted types.
 * <p>
 * This sample shows how to send message with JmsBytesMessage to Azure Service Bus and receive with Azure Service Bus Java client.
 */
public class JmsInterop {

    // Connection String for the namespace can be obtained from the Azure portal under the
    // 'Shared Access policies' section.
    private static final String namespace = "{namespace]";
    private static final String sasKey = "{sas key}";
    private static final String sasKeyName = "{sas key name, e.g. RootManageSharedAccessKey}";
    private static final String queueName = "{queue name}";
    private static int totalSend = 100;
    private static AtomicInteger totalReceived = new AtomicInteger(0);

    public static void main(String[] args) throws Exception {

        // set up JMS
        Hashtable<String, String> hashtable = new Hashtable<>();
        hashtable.put("connectionfactory.SBCF", "amqps://" + namespace + ".servicebus.windows.net?amqp.idleTimeout=120000&amqp.traceFrames=true");
        hashtable.put("queue.QUEUE", queueName);

        hashtable.put(Context.INITIAL_CONTEXT_FACTORY, "org.apache.qpid.jms.jndi.JmsInitialContextFactory");
        Context context = new InitialContext(hashtable);

        // Look up ConnectionFactory and Queue
        ConnectionFactory cf = (ConnectionFactory) context.lookup("SBCF");
        Destination queue = (Destination) context.lookup("QUEUE");

        // Create Connection
        Connection connection = cf.createConnection(sasKeyName, sasKey);
        // Create Session, no transaction, client ack
        Session session = connection.createSession(false, Session.CLIENT_ACKNOWLEDGE);

        // Create producer
        MessageProducer producer = session.createProducer(queue);

        for (int i = 0; i < totalSend; i++) {
            BytesMessage message = session.createBytesMessage();
            message.writeBytes(String.valueOf(i).getBytes());
            producer.send(message);
        }

        producer.close();
        session.close();
        connection.stop();
        connection.close();

        ConnectionStringBuilder builder = new ConnectionStringBuilder(namespace, queueName, sasKeyName, sasKey);
        IQueueClient queueClient = new QueueClient(builder, ReceiveMode.PEEKLOCK);
        queueClient.registerMessageHandler(new MessageHandler(queueClient), new MessageHandlerOptions(1, false, Duration.ofMinutes(1)));

        while (totalReceived.get() != totalSend) {
            Thread.sleep(1000);
        }

        Log.log("Received all messages, exiting the sample.");
        Log.log("Closing queue client.");
        queueClient.close();
    }

    static class MessageHandler implements IMessageHandler {
        private IQueueClient client;

        public MessageHandler(IQueueClient client) {
            this.client = client;
        }

        @Override
        public CompletableFuture<Void> onMessageAsync(IMessage iMessage) {
            Log.log("Received message with sq#: %d, body '%s' and lock token: %s.", iMessage.getSequenceNumber(), new String(iMessage.getBody()), iMessage.getLockToken());
            return this.client.completeAsync(iMessage.getLockToken()).thenRunAsync(() -> {
                Log.log("Completed message sq#: %d and lock token: %s", iMessage.getSequenceNumber(), iMessage.getLockToken());
                totalReceived.incrementAndGet();
            });
        }

        @Override
        public void notifyException(Throwable throwable, ExceptionPhase exceptionPhase) {
            Log.log(exceptionPhase + "-" + throwable.getMessage());
        }
    }
}
