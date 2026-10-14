// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import com.azure.resourcemanager.samples.Utils;
import com.azure.resourcemanager.servicebus.models.AuthorizationKeys;
import java.io.ByteArrayOutputStream;
import java.io.PrintStream;
import java.lang.reflect.Proxy;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.Test;

import static org.junit.Assert.*;

public class ServiceBusPublishSubscribeBasicTest {
    private static final String ID = "11111111-2222-3333-4444-555555555555";
    private final ByteArrayOutputStream output = new ByteArrayOutputStream();
    private final PrintStream log = new PrintStream(output, true, StandardCharsets.UTF_8);

    static Map<String, String> environment() {
        Map<String, String> environment = new HashMap<>();
        environment.put("AZURE_TENANT_ID", ID);
        environment.put("AZURE_SUBSCRIPTION_ID", ID);
        return environment;
    }

    @Test
    public void helpDoesNotConstructClientsOrRequireCredentials() {
        assertEquals(0, ServiceBusPublishSubscribeBasic.execute(new String[] {"--help"}, Collections.emptyMap(),
            log, log, config -> { throw new AssertionError("Client initialization"); }));
        assertTrue(output.toString().contains("--sku=standard|premium"));
    }

    @Test
    public void noConsentOrInvalidConfigurationNeverConstructsClients() {
        for (String[] args : new String[][] {{}, {"--sku=premium"}, {"--run", "--sku=basic"},
            {"--run", "--run"}, {"--run", "--sku=premium", "--sku=standard"}, {"--run", "--unknown"}}) {
            assertEquals(2, ServiceBusPublishSubscribeBasic.execute(args, environment(), log, log,
                config -> { throw new AssertionError("Client initialization"); }));
        }
        assertEquals(2, ServiceBusPublishSubscribeBasic.execute(new String[] {"--run"}, Collections.emptyMap(),
            log, log, config -> { throw new AssertionError("Client initialization"); }));
        Map<String, String> invalid = environment();
        invalid.put("AZURE_SUBSCRIPTION_ID", "secret-invalid-id");
        assertEquals(2, ServiceBusPublishSubscribeBasic.execute(new String[] {"--run"}, invalid, log, log,
            config -> { throw new AssertionError("Client initialization"); }));
        assertFalse(output.toString().contains("secret-invalid-id"));
    }

    @Test
    public void allSkuAndKeyVariantsPreserveTheFullScenario() {
        for (String sku : Arrays.asList("standard", "premium")) {
            for (boolean keys : Arrays.asList(false, true)) {
                FakeOperations operations = new FakeOperations();
                List<String> args = new ArrayList<>(Arrays.asList("--run", "--sku=" + sku));
                if (keys) {
                    args.add("--rotate-keys");
                }
                assertEquals(0, ServiceBusPublishSubscribeBasic.execute(args.toArray(new String[0]),
                    environment(), log, log, config -> {
                        assertEquals("premium".equals(sku), config.premium);
                        assertEquals(keys, config.rotateKeys);
                        assertEquals("westus", config.region);
                        return operations;
                    }));
                assertEquals(Arrays.asList("group", "namespace", "topic-resize-two-subscriptions",
                    "list-default-rules", "authorization-" + keys, "send-verify-settle",
                    "delete-subscriptions-topic", "delete-namespace", "cleanup"), operations.calls);
            }
        }
        assertFalse(ServiceBusPublishSubscribeBasic.Configuration.parse(new String[] {"--run"},
            environment()).premium);
    }

    @Test
    public void configuredSdkLoggingIsRejectedBeforeClientInitialization() {
        Map<String, String> env = environment();
        env.put("AZURE_LOG_LEVEL", "verbose");
        assertEquals(2, ServiceBusPublishSubscribeBasic.execute(new String[] {"--run"}, env,
            log, log, config -> { throw new AssertionError("Client initialization"); }));
        assertThrows(IllegalArgumentException.class,
            () -> ServiceBusPublishSubscribeBasic.Configuration.requireLoggingDisabled("error"));
        ServiceBusPublishSubscribeBasic.Configuration.requireLoggingDisabled("none");
    }

    @Test
    public void provisioningSendReadbackAndRemovalFailuresAreNonzeroAndStillCleanUp() {
        for (String step : Arrays.asList("group", "namespace", "topic-resize-two-subscriptions",
            "list-default-rules", "authorization-false", "send-verify-settle", "delete-subscriptions-topic",
            "delete-namespace")) {
            output.reset();
            FakeOperations operations = new FakeOperations();
            operations.fail = step;
            assertEquals(1, ServiceBusPublishSubscribeBasic.execute(new String[] {"--run"},
                environment(), log, log, config -> operations));
            assertEquals("cleanup", operations.calls.get(operations.calls.size() - 1));
            assertFalse(output.toString().contains("Sample completed"));
            assertFalse(output.toString().contains("sentinel-secret"));
        }
    }

    @Test
    public void cleanupFailureOverridesSuccessfulMessaging() {
        FakeOperations operations = new FakeOperations();
        operations.fail = "cleanup";
        assertEquals(1, ServiceBusPublishSubscribeBasic.execute(new String[] {"--run"},
            environment(), log, log, config -> operations));
        assertTrue(output.toString().contains("Resource group cleanup failed"));
        assertFalse(output.toString().contains("Sample completed"));
    }

    @Test
    public void clientInitializationFailureIsNonzeroAndRedacted() {
        assertEquals(1, ServiceBusPublishSubscribeBasic.execute(new String[] {"--run"},
            environment(), log, log, config -> { throw new IllegalStateException("sentinel-secret"); }));
        assertFalse(output.toString().contains("sentinel-secret"));
    }

    @Test
    public void authorizationLoggingNeverReadsOrPrintsSecrets() {
        AtomicInteger reads = new AtomicInteger();
        AuthorizationKeys keys = (AuthorizationKeys) Proxy.newProxyInstance(AuthorizationKeys.class.getClassLoader(),
            new Class<?>[] {AuthorizationKeys.class}, (proxy, method, args) -> {
                reads.incrementAndGet();
                return "Endpoint=sb://sentinel;SharedAccessKey=sentinel-secret";
            });
        Utils.print(keys, log);
        Utils.printFailure("Sample", new IllegalStateException("sentinel-secret",
            new RuntimeException("Endpoint=sb://sentinel")), log);
        assertEquals(0, reads.get());
        assertFalse(output.toString().contains("sentinel"));
        assertTrue(output.toString().contains("[REDACTED]"));
    }

    @Test
    public void actualMainProcessExitsNonzeroBeforeNetworkWithoutConfiguration() throws Exception {
        ProcessBuilder builder = new ProcessBuilder(Path.of(System.getProperty("java.home"), "bin", "java").toString(),
            "-cp", System.getProperty("java.class.path"), ServiceBusPublishSubscribeBasic.class.getName(), "--run");
        builder.environment().remove("AZURE_TENANT_ID");
        builder.environment().remove("AZURE_SUBSCRIPTION_ID");
        builder.redirectErrorStream(true);
        Process process = builder.start();
        try {
            assertTrue("Main process must be bounded", process.waitFor(20, TimeUnit.SECONDS));
            String text = new String(process.getInputStream().readAllBytes(), StandardCharsets.UTF_8);
            assertEquals(text, 2, process.exitValue());
            assertTrue(text.contains("No Azure operations started"));
        } finally {
            process.destroyForcibly();
        }
    }

    private static final class FakeOperations implements ServiceBusPublishSubscribeBasic.Operations {
        final List<String> calls = new ArrayList<>();
        String fail;

        private void step(String name) {
            calls.add(name);
            if (name.equals(fail)) {
                throw new IllegalStateException("sentinel-secret");
            }
        }

        public void createResourceGroup() { step("group"); }
        public void createNamespace() { step("namespace"); }
        public void createTopicAndSubscriptions() { step("topic-resize-two-subscriptions"); }
        public void listAndVerifyEntities() { step("list-default-rules"); }
        public void inspectAuthorization(boolean rotateKeys) { step("authorization-" + rotateKeys); }
        public void sendAndVerifyFanOut() { step("send-verify-settle"); }
        public void deleteSubscriptionsAndTopic() { step("delete-subscriptions-topic"); }
        public void deleteNamespace() { step("delete-namespace"); }
        public void cleanupResourceGroup() { step("cleanup"); }
    }
}
