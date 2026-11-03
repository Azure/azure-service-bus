// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

package com.microsoft.azure.servicebus.samples.jmsqueuequickstart;

import org.apache.commons.cli.*;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.apache.qpid.jms.JmsConnectionFactory;
import com.azure.core.credential.TokenCredential;
import com.azure.identity.DefaultAzureCredentialBuilder;
import com.microsoft.azure.servicebus.jms.ServiceBusJmsConnectionFactory;
import com.microsoft.azure.servicebus.jms.ServiceBusJmsConnectionFactorySettings;

import javax.jms.*;
import javax.naming.Context;
import javax.naming.InitialContext;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.HashSet;
import java.util.Hashtable;
import java.util.Set;
import java.util.UUID;
import java.util.function.Function;

/**
 * This sample demonstrates how to send messages from a JMS Queue producer into
 * an Azure Service Bus Queue, and receive them with a JMS message consumer.
 * JMS Queue. 
 */
public class JmsQueueQuickstart {

    private static final int TOTAL_SEND = 10;
    private static final Logger logger = LoggerFactory.getLogger(JmsQueueQuickstart.class);

    public void run(String namespaceHost) throws Exception {
        String mode = authenticationMode(System.getenv("SB_SAMPLES_AUTH"));
        boolean sas = "sas".equals(mode);
        String sasKeyName = sas ? System.getenv("SB_SAMPLES_SAS_KEY_NAME") : null;
        String sasKey = sas ? System.getenv("SB_SAMPLES_SAS_KEY") : null;
        ConnectionFactory factory = connectionFactory(mode, namespaceHost, sasKeyName, sasKey,
            sas ? null : new DefaultAzureCredentialBuilder().build());
        
        // set up JNDI context
        Hashtable<String, String> hashtable = new Hashtable<>();
        hashtable.put("queue.QUEUE", "BasicQueue");
        hashtable.put(Context.INITIAL_CONTEXT_FACTORY, "org.apache.qpid.jms.jndi.JmsInitialContextFactory");
        Context context = new InitialContext(hashtable);
        run(context, factory, sasKeyName, sasKey);
    }

    static String authenticationMode(String configured) {
        if (configured == null) return "entra";
        if (!"entra".equals(configured) && !"sas".equals(configured)) {
            throw new IllegalArgumentException("SB_SAMPLES_AUTH must be entra (Premium) or sas (Standard/Premium). No automatic SAS fallback.");
        }
        return configured;
    }

    static ConnectionFactory connectionFactory(String mode, String host, String keyName, String key,
        TokenCredential credential) {
        authenticationMode(mode);
        if ("sas".equals(mode)) {
            if (keyName == null || keyName.isBlank() || key == null || key.isBlank()) {
                throw new IllegalArgumentException("SAS mode requires SB_SAMPLES_SAS_KEY_NAME and SB_SAMPLES_SAS_KEY.");
            }
            return new JmsConnectionFactory(keyName, key, "amqps://" + host + "?amqp.idleTimeout=120000");
        }
        System.out.println("Using Microsoft Entra ID. This JMS mode requires a Premium namespace and Service Bus data roles.");
        ServiceBusJmsConnectionFactorySettings settings = new ServiceBusJmsConnectionFactorySettings(120000, false);
        // Keep the quickstart's direct-connection behavior, not the wrapper's unlimited failover retries.
        settings.setShouldReconnect(false);
        return new ServiceBusJmsConnectionFactory(credential, host, settings);
    }

    void run(Context context, String sasKeyName, String sasKey) throws Exception {
        run(context, null, sasKeyName, sasKey);
    }

    void run(Context context, ConnectionFactory factory, String sasKeyName, String sasKey) throws Exception {
        try (AutoCloseable closeContext = context::close) {
            ConnectionFactory cf = factory == null ? (ConnectionFactory) context.lookup("SBCF") : factory;
            Destination queue = (Destination) context.lookup("QUEUE");
            String runId = UUID.randomUUID().toString();
            sendMessages(cf, queue, sasKeyName, sasKey, runId);
            receiveMessages(cf, queue, sasKeyName, sasKey, runId);
        }
        System.out.println("Received and acknowledged this run's ten messages; all clients closed.");
    }

    static void sendMessages(ConnectionFactory factory, Destination queue, String keyName, String key, String runId) throws JMSException {
        try (Connection connection = createConnection(factory, keyName, key);
             Session session = connection.createSession(false, Session.CLIENT_ACKNOWLEDGE);
             MessageProducer producer = session.createProducer(queue)) {
            for (int i = 0; i < TOTAL_SEND; i++) {
                BytesMessage message = session.createBytesMessage();
                message.writeBytes(String.valueOf(i).getBytes(StandardCharsets.UTF_8));
                message.setStringProperty("SampleRunId", runId);
                message.setIntProperty("SampleSequence", i);
                producer.send(message);
                System.out.printf("Sent message %d.\n", i + 1);
            }
        }
    }

    static void receiveMessages(ConnectionFactory factory, Destination queue, String keyName, String key, String runId) throws JMSException {
        Set<Integer> received = new HashSet<>();
        try (Connection connection = createConnection(factory, keyName, key);
             Session session = connection.createSession(false, Session.CLIENT_ACKNOWLEDGE);
             MessageConsumer consumer = session.createConsumer(queue)) {
            connection.start();
            long deadline = System.nanoTime() + Duration.ofSeconds(90).toNanos();
            while (received.size() < TOTAL_SEND) {
                long remaining = deadline - System.nanoTime();
                if (remaining <= 0) throw new JMSException("Timed out waiting for this run's ten messages.");
                Message message = consumer.receive(Math.max(1, Duration.ofNanos(remaining).toMillis()));
                if (message == null) throw new JMSException("Timed out waiting for this run's ten messages.");
                validateAndAcknowledge(message, runId, received);
                System.out.printf("Received message %d with ID %s.\n", received.size(), message.getJMSMessageID());
            }
        }
    }

    static void validateAndAcknowledge(Message message, String runId, Set<Integer> received) throws JMSException {
        if (!(message instanceof BytesMessage) || !runId.equals(message.getStringProperty("SampleRunId")) ||
            !message.propertyExists("SampleSequence")) {
            throw new JMSException("Unexpected message; leaving it unsettled. Use an empty disposable queue with no competing consumer.");
        }
        int sequence = message.getIntProperty("SampleSequence");
        if (sequence < 0 || sequence >= TOTAL_SEND || received.contains(sequence)) {
            throw new JMSException("Unexpected or duplicate sample sequence; leaving it unsettled.");
        }
        byte[] expected = String.valueOf(sequence).getBytes(StandardCharsets.UTF_8);
        BytesMessage bytes = (BytesMessage) message;
        byte[] actual = new byte[expected.length];
        if (bytes.getBodyLength() != expected.length || bytes.readBytes(actual) != expected.length ||
            !java.util.Arrays.equals(expected, actual)) {
            throw new JMSException("Unexpected sample payload; leaving it unsettled.");
        }
        message.acknowledge();
        received.add(sequence);
    }

    static Connection createConnection(ConnectionFactory factory, String keyName, String key) throws JMSException {
        return keyName == null ? factory.createConnection() : factory.createConnection(keyName, key);
    }

    public static void main(String[] args) {

        System.exit(runApp(args, (namespaceHost) -> {
            JmsQueueQuickstart app = new JmsQueueQuickstart();
            try {
                app.run(namespaceHost);
                return 0;
            } catch (Exception e) {
                System.out.printf("%s", e.toString());
                return 1;
            }
        }));
    }

    static final String SB_SAMPLES_NAMESPACE = "SB_SAMPLES_NAMESPACE";

    public static int runApp(String[] args, Function<String, Integer> run) {
        try {

            String namespaceHost = null;

            Options options = new Options();
            options.addOption(new Option("n", true, "Namespace host, such as example.servicebus.windows.net"));
            CommandLineParser clp = new DefaultParser();
            CommandLine cl = clp.parse(options, args);
            if (cl.getOptionValue("n") != null) {
                namespaceHost = cl.getOptionValue("n");
            }

            // get overrides from the environment
            String env = System.getenv(SB_SAMPLES_NAMESPACE);
            if (env != null) {
                namespaceHost = env;
            }

            if (namespaceHost == null || namespaceHost.isBlank()) {
                HelpFormatter formatter = new HelpFormatter();
                formatter.printHelp("run jar with", "", options, "", true);
                return 2;
            }
            return run.apply(namespaceHost);
        } catch (Exception e) {
            System.out.printf("%s", e.toString());
            return 3;
        }
    }
}
