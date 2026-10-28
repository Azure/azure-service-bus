package com.microsoft.azure.servicebus.samples.jmsqueuequickstart;

import java.lang.reflect.InvocationHandler;
import java.lang.reflect.Proxy;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.atomic.AtomicInteger;
import javax.jms.*;
import javax.naming.Context;
import org.junit.Test;
import static org.junit.Assert.*;

public class JmsLifecycleTest {
    @Test
    public void entraRunKeepsOwnedMessageAndCleanupContracts() throws Exception {
        Fixture fixture = new Fixture();
        new JmsQueueQuickstart().run(fixture.context(), fixture.factory(), null, null);
        assertEquals(10, fixture.sends);
        assertEquals(10, fixture.deliveries);
        assertEquals(10, fixture.acknowledged.get());
        assertEquals(fixture.connections, Collections.frequency(fixture.closed, "connection"));
        assertEquals("context", fixture.closed.get(fixture.closed.size() - 1));
    }

    @Test
    public void repeatedRunsStopAtTenOwnedMessages() throws Exception {
        Fixture fixture = new Fixture();
        JmsQueueQuickstart app = new JmsQueueQuickstart();
        app.run(fixture.context(), "offline", "offline");
        app.run(fixture.context(), "offline", "offline");
        assertEquals(20, fixture.deliveries);
        assertEquals(20, fixture.sends);
        assertEquals(20, fixture.acknowledged.get());
        assertEquals(2, Collections.frequency(fixture.closed, "context"));
        assertEquals(fixture.connections, Collections.frequency(fixture.closed, "connection"));
    }

    @Test
    public void failuresCloseEveryAcquiredResource() {
        for (String stage : Arrays.asList("lookup", "connection", "session", "producer", "send", "start", "consumer", "receive")) {
            Fixture fixture = new Fixture();
            fixture.failure = stage;
            assertThrows(Exception.class, () -> new JmsQueueQuickstart().run(fixture.context(), "offline", "offline"));
            assertEquals(stage, fixture.connections, Collections.frequency(fixture.closed, "connection"));
            assertEquals(stage, fixture.sessions, Collections.frequency(fixture.closed, "session"));
            assertEquals(stage, fixture.producers, Collections.frequency(fixture.closed, "producer"));
            assertEquals(stage, fixture.consumers, Collections.frequency(fixture.closed, "consumer"));
            assertEquals("context", fixture.closed.get(fixture.closed.size() - 1));
        }
    }

    @Test
    public void closeFailureDoesNotMaskSendFailure() {
        Fixture fixture = new Fixture();
        fixture.failure = "send";
        fixture.closeFails = true;
        Exception failure = assertThrows(JMSException.class,
            () -> new JmsQueueQuickstart().run(fixture.context(), "offline", "offline"));
        assertEquals("send failed", failure.getMessage());
        assertEquals(1, failure.getSuppressed().length);
        assertEquals(Arrays.asList("producer", "session", "connection", "context"), fixture.closed);
    }

    @Test
    public void validatesProvenanceSequenceAndPayloadBeforeAcknowledgment() throws Exception {
        Fixture fixture = new Fixture();
        Set<Integer> received = new HashSet<>();
        assertThrows(JMSException.class, () -> JmsQueueQuickstart.validateAndAcknowledge(fixture.message("foreign", 0, "0"), "run", received));
        assertThrows(JMSException.class, () -> JmsQueueQuickstart.validateAndAcknowledge(fixture.message("run", -1, "-1"), "run", received));
        assertThrows(JMSException.class, () -> JmsQueueQuickstart.validateAndAcknowledge(fixture.message("run", 10, "10"), "run", received));
        assertThrows(JMSException.class, () -> JmsQueueQuickstart.validateAndAcknowledge(fixture.message("run", 0, "wrong"), "run", received));
        assertThrows(JMSException.class, () -> JmsQueueQuickstart.validateAndAcknowledge(fixture.message("run", 0, "x"), "run", received));
        assertThrows(JMSException.class, () -> JmsQueueQuickstart.validateAndAcknowledge(
            fake(Message.class, (proxy, method, args) -> { throw new AssertionError("Wrong message type must not be acknowledged"); }), "run", received));
        assertEquals(0, fixture.acknowledged.get());
        for (int index = 9; index >= 0; index--) {
            JmsQueueQuickstart.validateAndAcknowledge(fixture.message("run", index, String.valueOf(index)), "run", received);
        }
        assertEquals(10, received.size());
        assertThrows(JMSException.class, () -> JmsQueueQuickstart.validateAndAcknowledge(fixture.message("run", 0, "0"), "run", received));
        assertEquals(10, fixture.acknowledged.get());
    }

    @Test
    public void failedAcknowledgmentDoesNotAdvanceProgress() {
        Fixture fixture = new Fixture();
        fixture.failure = "acknowledge";
        Set<Integer> received = new HashSet<>();
        assertThrows(JMSException.class, () -> JmsQueueQuickstart.validateAndAcknowledge(fixture.message("run", 0, "0"), "run", received));
        assertTrue(received.isEmpty());
        assertEquals(0, fixture.acknowledged.get());
    }

    private static <T> T fake(Class<T> type, InvocationHandler handler) {
        return type.cast(Proxy.newProxyInstance(type.getClassLoader(), new Class<?>[] {type}, handler));
    }

    private static final class Fixture {
        final List<String> closed = new ArrayList<>();
        final AtomicInteger acknowledged = new AtomicInteger();
        String failure;
        String runId;
        boolean closeFails;
        int connections, sessions, producers, consumers, deliveries, sends;

        void check(String stage) throws JMSException {
            if (stage.equals(failure)) throw new JMSException(stage + " failed");
        }

        Context context() {
            return fake(Context.class, (proxy, method, args) -> {
                if ("close".equals(method.getName())) { closed.add("context"); return null; }
                if ("lookup".equals(method.getName())) {
                    if ("lookup".equals(failure)) throw new javax.naming.NamingException("lookup failed");
                    return "SBCF".equals(args[0]) ? factory() : null;
                }
                throw new UnsupportedOperationException(method.getName());
            });
        }

        ConnectionFactory factory() {
            return fake(ConnectionFactory.class, (proxy, method, args) -> {
                assertEquals("createConnection", method.getName());
                check("connection");
                connections++;
                return fake(Connection.class, (connection, operation, values) -> {
                    switch (operation.getName()) {
                        case "close": closed.add("connection"); return null;
                        case "start": check("start"); return null;
                        case "createSession": check("session"); sessions++; return session();
                        default: throw new UnsupportedOperationException(operation.getName());
                    }
                });
            });
        }

        Session session() {
            return fake(Session.class, (proxy, method, args) -> {
                switch (method.getName()) {
                    case "close": closed.add("session"); return null;
                    case "createBytesMessage": return message("", 0, "0");
                    case "createProducer":
                        check("producer"); producers++;
                        return fake(MessageProducer.class, (producer, operation, values) -> {
                            if ("send".equals(operation.getName())) { check("send"); sends++; return null; }
                            if ("close".equals(operation.getName())) {
                                closed.add("producer");
                                if (closeFails) throw new JMSException("close failed");
                                return null;
                            }
                            throw new UnsupportedOperationException(operation.getName());
                        });
                    case "createConsumer":
                        check("consumer"); consumers++;
                        AtomicInteger sequence = new AtomicInteger();
                        return fake(MessageConsumer.class, (consumer, operation, values) -> {
                            if ("close".equals(operation.getName())) { closed.add("consumer"); return null; }
                            if ("receive".equals(operation.getName())) {
                                check("receive");
                                int index = sequence.getAndIncrement();
                                deliveries++;
                                return message(runId, index, String.valueOf(index));
                            }
                            throw new UnsupportedOperationException(operation.getName());
                        });
                    default: throw new UnsupportedOperationException(method.getName());
                }
            });
        }

        BytesMessage message(String owner, int sequence, String body) {
            byte[] payload = body.getBytes(StandardCharsets.UTF_8);
            return fake(BytesMessage.class, (proxy, method, args) -> {
                switch (method.getName()) {
                    case "getStringProperty": return owner;
                    case "propertyExists": return true;
                    case "getIntProperty": return sequence;
                    case "getBodyLength": return (long) payload.length;
                    case "readBytes": System.arraycopy(payload, 0, args[0], 0, payload.length); return payload.length;
                    case "getJMSMessageID": return "offline-" + sequence;
                    case "acknowledge": check("acknowledge"); acknowledged.incrementAndGet(); return null;
                    case "setStringProperty": runId = (String) args[1]; return null;
                    case "setIntProperty":
                    case "writeBytes": return null;
                    default: throw new UnsupportedOperationException(method.getName());
                }
            });
        }
    }
}
