package com.microsoft.azure.servicebus.samples.jmstopicquickstart;

import com.azure.core.credential.AccessToken;
import com.azure.core.credential.TokenCredential;
import com.microsoft.azure.servicebus.jms.ServiceBusJmsConnectionFactory;
import java.lang.reflect.Proxy;
import java.time.OffsetDateTime;
import java.util.concurrent.atomic.AtomicInteger;
import javax.jms.ConnectionFactory;
import org.apache.qpid.jms.JmsConnectionFactory;
import org.junit.Test;
import reactor.core.publisher.Mono;
import static org.junit.Assert.*;

public class JmsAuthenticationTest {
    @Test
    public void defaultsToEntraAndRejectsInvalidModes() {
        assertEquals("entra", JmsTopicQuickstart.authenticationMode(null));
        assertEquals("entra", JmsTopicQuickstart.authenticationMode("entra"));
        assertEquals("sas", JmsTopicQuickstart.authenticationMode("sas"));
        for (String mode : new String[] {"", "SAS", "auto", " "}) {
            assertThrows(IllegalArgumentException.class, () -> JmsTopicQuickstart.authenticationMode(mode));
        }
    }

    @Test
    public void entraFactoryRequestsServiceBusTokenWithoutSas() {
        AtomicInteger requests = new AtomicInteger();
        TokenCredential credential = context -> {
            assertEquals(java.util.List.of("https://servicebus.azure.net/.default"), context.getScopes());
            requests.incrementAndGet();
            return Mono.just(new AccessToken("offline-token", OffsetDateTime.now().plusHours(1)));
        };
        ConnectionFactory factory = JmsTopicQuickstart.connectionFactory("entra",
            "example.servicebus.windows.net", null, null, credential);
        assertTrue(factory instanceof ServiceBusJmsConnectionFactory);
        assertFalse(((ServiceBusJmsConnectionFactory) factory).getSettings().shouldReconnect());
        assertEquals(120000, ((ServiceBusJmsConnectionFactory) factory).getSettings().getConnectionIdleTimeoutMS());
        assertTrue(requests.get() > 0);
    }

    @Test
    public void identityAndHttpProvidersLoadWithoutAuthentication() {
        assertNotNull(new com.azure.identity.DefaultAzureCredentialBuilder().build());
        assertNotNull(com.azure.core.http.HttpClient.createDefault());
    }

    @Test
    public void sasModeUsesDirectQpidAndDoesNotRequestTokens() {
        TokenCredential unused = context -> { throw new AssertionError("SAS must not acquire Entra tokens"); };
        JmsConnectionFactory factory = (JmsConnectionFactory) JmsTopicQuickstart.connectionFactory("sas",
            "example.servicebus.windows.net", "SampleSendListen", "offline-key", unused);
        assertEquals("SampleSendListen", factory.getUsername());
        assertEquals("offline-key", factory.getPassword());
        assertThrows(IllegalArgumentException.class, () -> JmsTopicQuickstart.connectionFactory("sas",
            "example.servicebus.windows.net", " ", "offline-key", unused));
        assertThrows(IllegalArgumentException.class, () -> JmsTopicQuickstart.connectionFactory("sas",
            "example.servicebus.windows.net", "SampleSendListen", null, unused));
    }

    @Test
    public void entraFailureDoesNotFallBackToSuppliedSasKeys() {
        TokenCredential failure = context -> Mono.error(new IllegalStateException("offline-token-failure"));
        RuntimeException error = assertThrows(RuntimeException.class, () -> JmsTopicQuickstart.connectionFactory(
            "entra", "example.servicebus.windows.net", "SampleSendListen", "offline-key", failure));
        assertTrue(error.toString().contains("offline-token-failure"));
    }

    @Test
    public void connectionOverloadMatchesSelectedAuthentication() throws Exception {
        AtomicInteger calls = new AtomicInteger();
        ConnectionFactory factory = (ConnectionFactory) Proxy.newProxyInstance(ConnectionFactory.class.getClassLoader(),
            new Class<?>[] {ConnectionFactory.class}, (proxy, method, args) -> {
                assertEquals("createConnection", method.getName());
                if (calls.getAndIncrement() == 0) {
                    assertNull(args);
                } else {
                    assertArrayEquals(new Object[] {"SampleSendListen", "offline-key"}, args);
                }
                return null;
            });
        JmsTopicQuickstart.createConnection(factory, null, null);
        JmsTopicQuickstart.createConnection(factory, "SampleSendListen", "offline-key");
        assertEquals(2, calls.get());
    }
}
