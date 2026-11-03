// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import java.io.IOException;
import java.io.InputStream;
import java.io.PrintStream;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.LinkOption;
import java.nio.file.NoSuchFileException;
import java.nio.file.Path;
import java.nio.file.StandardOpenOption;
import java.nio.file.attribute.BasicFileAttributes;
import java.time.Duration;
import java.util.Arrays;
import reactor.core.publisher.Mono;

import static com.azure.resourcemanager.servicebus.samples.ResourceLifecycle.require;

final class AccessCheckpoint {
    private final Path directory;
    private final PrintStream out;
    private final Duration timeout;
    private final Duration pollInterval;
    private String namespaceId;
    private boolean cleanupAttempted;
    private boolean cleanupAcknowledged;

    AccessCheckpoint(Path directory, PrintStream out) {
        this(directory, out, ResourceLifecycle.OPERATION_TIMEOUT, Duration.ofMillis(200));
    }

    AccessCheckpoint(Path directory, PrintStream out, Duration timeout, Duration pollInterval) {
        this.directory = directory;
        this.out = out;
        this.timeout = timeout;
        this.pollInterval = pollInterval;
        if (directory != null) {
            try {
                Files.createDirectory(directory);
            } catch (IOException failure) {
                throw new UncheckedIOException("Cannot create a fresh checkpoint directory", failure);
            }
        }
    }

    void awaitAccess(String createdNamespaceId) {
        if (directory != null) {
            require(namespaceId == null, "Access checkpoint cannot be reused");
            namespaceId = createdNamespaceId;
            exchange("access-request.txt", "access-ready.txt");
        }
    }

    void beforeCleanup() {
        if (namespaceId == null || cleanupAcknowledged) {
            return;
        }
        require(!cleanupAttempted, "Role-removal coordination already failed; refusing resource deletion");
        cleanupAttempted = true;
        exchange("cleanup-request.txt", "roles-removed.txt");
        cleanupAcknowledged = true;
    }

    private void exchange(String request, String acknowledgment) {
        byte[] expected = (namespaceId + "\n").getBytes(StandardCharsets.UTF_8);
        try {
            publishRequest(directory.resolve(request), expected);
        } catch (IOException failure) {
            throw new UncheckedIOException("Cannot publish checkpoint request", failure);
        }
        out.println("Access checkpoint waiting: " + acknowledgment + " " + namespaceId);
        require(Boolean.TRUE.equals(Mono.fromCallable(() -> acknowledged(directory.resolve(acknowledgment), expected))
            .repeatWhen(repeats -> repeats.delayElements(pollInterval))
            .filter(Boolean::booleanValue).next().block(timeout)), "Checkpoint was not acknowledged");
        out.println("Access checkpoint acknowledged: " + acknowledgment + " " + namespaceId);
    }

    static void publishRequest(Path destination, byte[] content) throws IOException {
        Path temporary = Files.createTempFile(destination.getParent(), destination.getFileName() + ".", ".tmp");
        try {
            Files.write(temporary, content, StandardOpenOption.WRITE);
            // A hard link publishes complete bytes atomically and cannot replace an existing request.
            Files.createLink(destination, temporary);
        } finally {
            Files.delete(temporary);
        }
    }

    private static boolean acknowledged(Path file, byte[] expected) throws IOException {
        try {
            BasicFileAttributes attributes = Files.readAttributes(file, BasicFileAttributes.class,
                LinkOption.NOFOLLOW_LINKS);
            require(attributes.isRegularFile(), "Checkpoint acknowledgment must be a regular file");
            try (InputStream input = Files.newInputStream(file, StandardOpenOption.READ, LinkOption.NOFOLLOW_LINKS)) {
                require(Arrays.equals(expected, input.readNBytes(expected.length + 1)),
                    "Checkpoint acknowledgment does not match this namespace");
            }
            return true;
        } catch (NoSuchFileException absent) {
            return false;
        }
    }
}
