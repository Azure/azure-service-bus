// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import java.io.ByteArrayOutputStream;
import java.io.PrintStream;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.util.concurrent.TimeUnit;
import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;

import static org.junit.Assert.*;

public class AccessCheckpointTest {
    private static final String NAMESPACE = "/subscriptions/11111111-2222-3333-4444-555555555555/"
        + "resourceGroups/owned/providers/Microsoft.ServiceBus/namespaces/owned";
    @Rule public TemporaryFolder temporary = new TemporaryFolder();
    private final ByteArrayOutputStream output = new ByteArrayOutputStream();
    private final PrintStream log = new PrintStream(output);

    private AccessCheckpoint checkpoint(Path directory) {
        return new AccessCheckpoint(directory, log, Duration.ofMillis(75), Duration.ofMillis(5));
    }

    @Test
    public void disabledCheckpointNeedsNoAcknowledgments() {
        AccessCheckpoint checkpoint = checkpoint(null);
        checkpoint.awaitAccess(NAMESPACE);
        checkpoint.beforeCleanup();
        assertEquals("", output.toString());
    }

    @Test
    public void existingDirectoryIsNeverAdopted() {
        assertThrows(UncheckedIOException.class, () -> checkpoint(temporary.getRoot().toPath()));
    }

    @Test
    public void bothAcknowledgmentsMustMatchAndCleanupIsNotRepeated() throws Exception {
        Path directory = temporary.getRoot().toPath().resolve("handshake");
        AccessCheckpoint checkpoint = checkpoint(directory);
        Files.writeString(directory.resolve("access-ready.txt"), NAMESPACE + "\n");
        checkpoint.awaitAccess(NAMESPACE);
        assertEquals(NAMESPACE + "\n", Files.readString(directory.resolve("access-request.txt")));
        Files.writeString(directory.resolve("roles-removed.txt"), NAMESPACE + "\n");
        checkpoint.beforeCleanup();
        checkpoint.beforeCleanup();
        assertEquals(NAMESPACE + "\n", Files.readString(directory.resolve("cleanup-request.txt")));
        assertTrue(output.toString().contains("acknowledged: roles-removed.txt"));
        assertThrows(IllegalStateException.class, () -> checkpoint.awaitAccess(NAMESPACE));
    }

    @Test
    public void accessTimeoutStillRequiresCleanupCoordination() throws Exception {
        Path directory = temporary.getRoot().toPath().resolve("timeout");
        AccessCheckpoint checkpoint = checkpoint(directory);
        long started = System.nanoTime();
        assertThrows(IllegalStateException.class, () -> checkpoint.awaitAccess(NAMESPACE));
        assertTrue(TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - started) < 2000);
        Files.writeString(directory.resolve("roles-removed.txt"), NAMESPACE + "\n");
        checkpoint.beforeCleanup();
        assertTrue(Files.exists(directory.resolve("cleanup-request.txt")));
    }

    @Test
    public void cleanupTimeoutFailsClosedAndCannotBeRetriedByFinally() throws Exception {
        Path directory = temporary.getRoot().toPath().resolve("cleanup-timeout");
        AccessCheckpoint checkpoint = checkpoint(directory);
        Files.writeString(directory.resolve("access-ready.txt"), NAMESPACE + "\n");
        checkpoint.awaitAccess(NAMESPACE);
        assertThrows(IllegalStateException.class, checkpoint::beforeCleanup);
        Files.writeString(directory.resolve("roles-removed.txt"), NAMESPACE + "\n");
        long started = System.nanoTime();
        assertThrows(IllegalStateException.class, checkpoint::beforeCleanup);
        assertTrue(TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - started) < 1000);
        assertFalse(output.toString().contains("acknowledged: roles-removed.txt"));
    }

    @Test
    public void mismatchedOversizedAndBomAcknowledgmentsFailWithoutLoggingContents() throws Exception {
        for (String value : new String[] {"sentinel-secret", NAMESPACE + "\nextra", "\ufeff" + NAMESPACE + "\n"}) {
            Path directory = temporary.getRoot().toPath().resolve("invalid-" + value.hashCode());
            AccessCheckpoint checkpoint = checkpoint(directory);
            Files.write(directory.resolve("access-ready.txt"), value.getBytes(StandardCharsets.UTF_8));
            assertThrows(IllegalStateException.class, () -> checkpoint.awaitAccess(NAMESPACE));
            assertFalse(output.toString().contains("sentinel-secret"));
            assertFalse(output.toString().contains("acknowledged: access-ready.txt"));
        }
    }

    @Test
    public void nonRegularAcknowledgmentFailsClosed() throws Exception {
        Path directory = temporary.getRoot().toPath().resolve("nonregular");
        AccessCheckpoint checkpoint = checkpoint(directory);
        Files.createDirectory(directory.resolve("access-ready.txt"));
        assertThrows(IllegalStateException.class, () -> checkpoint.awaitAccess(NAMESPACE));
    }
}
