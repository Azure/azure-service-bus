// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import com.azure.core.http.HttpClient;
import com.azure.identity.DefaultAzureCredentialBuilder;
import com.azure.json.JsonProviders;
import com.azure.json.JsonReader;
import com.azure.messaging.servicebus.ServiceBusClientBuilder;
import com.azure.messaging.servicebus.ServiceBusMessage;
import com.azure.resourcemanager.resources.ResourceManager;
import com.azure.resourcemanager.servicebus.ServiceBusManager;
import com.azure.resourcemanager.servicebus.fluent.models.SBTopicInner;
import io.netty.util.Version;
import java.io.File;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Properties;
import java.util.concurrent.TimeUnit;
import java.util.jar.JarFile;
import org.junit.Test;

import static org.junit.Assert.*;

public class PackagedJarIT {
    private final Path jar = Path.of(System.getProperty("packaged.jar"));

    @Test
    public void manifestMetadataAndProviderDiscoverySurvivePackaging() throws Exception {
        try (JarFile archive = new JarFile(jar.toFile())) {
            assertEquals(ServiceBusPublishSubscribeBasic.class.getName(),
                archive.getManifest().getMainAttributes().getValue("Main-Class"));
            assertEquals("true", archive.getManifest().getMainAttributes().getValue("Multi-Release"));
            assertNotNull(archive.getJarEntry("META-INF/LICENSE"));
            assertNotNull(archive.getJarEntry("META-INF/NOTICE"));
            Properties packaged = new Properties();
            try (InputStream input = archive.getInputStream(archive.getJarEntry("META-INF/io.netty.versions.properties"))) {
                packaged.load(input);
            }
            java.util.Enumeration<java.net.URL> inputs = getClass().getClassLoader()
                .getResources("META-INF/io.netty.versions.properties");
            while (inputs.hasMoreElements()) {
                Properties dependency = new Properties();
                try (InputStream input = inputs.nextElement().openStream()) {
                    dependency.load(input);
                }
                for (String key : dependency.stringPropertyNames()) {
                    assertEquals("Lost Netty metadata: " + key, dependency.getProperty(key), packaged.getProperty(key));
                }
            }
        }
        run(0, "-cp", jar + File.pathSeparator + Path.of("target", "test-classes"), Smoke.class.getName());
    }

    @Test
    public void packagedHelpNoConsentAndAllMissingConfigurationVariantsHaveCorrectExits() throws Exception {
        assertTrue(run(0, "-jar", jar.toString(), "--help").contains("Usage:"));
        assertTrue(run(2, "-jar", jar.toString()).contains("No Azure operations started"));
        for (String sku : Arrays.asList("standard", "premium")) {
            assertTrue(run(2, "-jar", jar.toString(), "--run", "--sku=" + sku)
                .contains("No Azure operations started"));
            assertTrue(run(2, "-jar", jar.toString(), "--run", "--sku=" + sku, "--rotate-keys")
                .contains("No Azure operations started"));
        }
    }

    private static String run(int expected, String... args) throws Exception {
        List<String> command = new ArrayList<>();
        command.add(Path.of(System.getProperty("java.home"), "bin", "java").toString());
        command.addAll(Arrays.asList(args));
        ProcessBuilder builder = new ProcessBuilder(command).redirectErrorStream(true);
        builder.environment().remove("AZURE_TENANT_ID");
        builder.environment().remove("AZURE_SUBSCRIPTION_ID");
        builder.environment().remove("AZURE_LOG_LEVEL");
        Process process = builder.start();
        try {
            assertTrue("Packaged JVM must finish without network", process.waitFor(30, TimeUnit.SECONDS));
            String text = new String(process.getInputStream().readAllBytes(), StandardCharsets.UTF_8);
            assertEquals(text, expected, process.exitValue());
            return text;
        } finally {
            process.destroyForcibly();
        }
    }

    public static final class Smoke {
        public static void main(String[] args) throws Exception {
            if (!HttpClient.createDefault().getClass().getName().contains("Netty")) {
                throw new IllegalStateException("HTTP provider not found");
            }
            try (JsonReader reader = JsonProviders.createReader("{\"name\":\"topic\",\"properties\":{\"maxSizeInMegabytes\":2048}}")) {
                if (SBTopicInner.fromJson(reader).maxSizeInMegabytes() != 2048) {
                    throw new IllegalStateException("JSON provider failed");
                }
            }
            new ServiceBusMessage("Hello World");
            new ServiceBusClientBuilder();
            new DefaultAzureCredentialBuilder();
            Class.forName(ServiceBusManager.class.getName());
            Class.forName(ResourceManager.class.getName());
            if (!Version.identify().keySet().containsAll(Arrays.asList("netty-handler", "netty-codec-dns"))) {
                throw new IllegalStateException("Netty metadata missing");
            }
            System.out.println("Packaged provider discovery passed; no credentials or network operations performed.");
        }
    }
}
