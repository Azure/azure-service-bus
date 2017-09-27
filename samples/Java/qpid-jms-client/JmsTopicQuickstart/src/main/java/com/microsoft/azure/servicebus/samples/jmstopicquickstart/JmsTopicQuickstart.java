// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

package com.microsoft.azure.servicebus.samples.jmstopicquickstart;

import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;
import org.apache.commons.cli.*;
import org.apache.log4j.*;

import javax.jms.*;
import javax.naming.Context;
import javax.naming.InitialContext;
import java.util.Hashtable;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * This sample demonstrates how to send messages from a JMS Topic producer into
 * an Azure Service Bus Topic, and receive the message from a Service Bus topic 
 * subscription using a message consumer that treats the subscription as a 
 * JMS Queue. 
 */
public class JmsTopicQuickstart {

    // Azure Service Bus connection string. 
    private static String connectionString = System.getenv("SB_SAMPLES_CONNECTIONSTRING");
    // Name of an existing topic in the Service Bus namespace
    private static String topicName = System.getenv("SB_SAMPLES_TOPICNAME");
    // Name of an existing subscription on that topic
    private static String subscriptionName = System.getenv("SB_SAMPLES_SUBSCRIPTIONNAME");
    // number of messages to send 
    private static int totalSend = 10;
    // tracking counter for how many messages have been received; used as termination condition
    private static AtomicInteger totalReceived = new AtomicInteger(0);
    // log4j logger 
    private static Logger logger = Logger.getRootLogger();

    public static void main(String[] args) throws Exception {

        // verify whether we have all input required to run
        if (!parseCommandLine(args)) {
            return;
        }

        // The connection string builder is the only part of the azure-servicebus SDK library 
        // we use in this JMS sample and for the purpose of robustly parsing the Service Bus 
        // connection string. 
        ConnectionStringBuilder csb = new ConnectionStringBuilder(connectionString);
        
        // set up the JNDI context 
        Hashtable<String, String> hashtable = new Hashtable<>();
        hashtable.put("connectionfactory.SBCF", "amqps://" + csb.getEndpoint().getHost() + "?amqp.idleTimeout=120000&amqp.traceFrames=true");
        hashtable.put("topic.TOPIC", topicName);
        hashtable.put("queue.SUBSCRIPTION", topicName + "/Subscriptions/" + subscriptionName);
        hashtable.put(Context.INITIAL_CONTEXT_FACTORY, "org.apache.qpid.jms.jndi.JmsInitialContextFactory");
        Context context = new InitialContext(hashtable);

        ConnectionFactory cf = (ConnectionFactory) context.lookup("SBCF");

        // Look up the topic
        Destination topic = (Destination) context.lookup("TOPIC");
               
        // we create a scope here so we can use the same set of local variables cleanly 
        // again to show the receive side seperately with minimal clutter
        {
            // Create Connection
            Connection connection = cf.createConnection(csb.getSasKeyName(), csb.getSasKey());
            connection.start();
            // Create Session, no transaction, client ack
            Session session = connection.createSession(false, Session.CLIENT_ACKNOWLEDGE);

            // Create producer
            MessageProducer producer = session.createProducer(topic);

            // Send messaGES
            for (int i = 0; i < totalSend; i++) {
                BytesMessage message = session.createBytesMessage();
                message.writeBytes(String.valueOf(i).getBytes());
                producer.send(message);
                logger.info(String.format("Sent message %d.", i + 1));
            }

            producer.close();
            session.close();
            connection.stop();
            connection.close();
        }

        // Look up the subscription (pretending it's a queue)
        Destination subscription = (Destination) context.lookup("SUBSCRIPTION");
        {
            // Create Connection
            Connection connection = cf.createConnection(csb.getSasKeyName(), csb.getSasKey());
            connection.start();
            // Create Session, no transaction, client ack
            Session session = connection.createSession(false, Session.CLIENT_ACKNOWLEDGE);
            // Create consumer
            MessageConsumer consumer = session.createConsumer(subscription);
            // Set callback listener. Gets called for each received message.
            consumer.setMessageListener(message -> {
                try {
                    logger.info(String.format("Received message %d with sq#: %s", 
                            totalReceived.incrementAndGet(),  // increments the counter
                            message.getJMSMessageID()));
                    message.acknowledge();
                } catch (Exception e) {
                    logger.error(e);
                }
            });

            // wait on the main thread until all sent messages have been received
            while (totalReceived.get() < totalSend) {
                Thread.sleep(1000);
            }
            consumer.close();
            session.close();
            connection.stop();
            connection.close();
        }

        logger.info("Received all messages, exiting the sample.");
        logger.info("Closing queue client.");
    }

    static boolean parseCommandLine(String[] args) throws Exception {
        Options options = new Options();
        options.addOption(new Option("c", true, "Connection string"));
        options.addOption(new Option("t", true, "Topic name"));
        options.addOption(new Option("s", true, "Subscription name"));
        CommandLineParser clp = new DefaultParser();
        CommandLine cl = clp.parse(options, args);
        if (cl.getOptionValue("c") != null) {
            connectionString = cl.getOptionValue("c");
        }
        if (cl.hasOption("t")) {
            topicName = cl.getOptionValue("t");
        }
        if (cl.hasOption("s")) {
            subscriptionName = cl.getOptionValue("s");
        }

        if (connectionString == null || topicName == null || subscriptionName == null) {
            HelpFormatter formatter = new HelpFormatter();
            formatter.printHelp("run jar with", "", options, "", true);
            return false;
        }
        return true;
    }
}
