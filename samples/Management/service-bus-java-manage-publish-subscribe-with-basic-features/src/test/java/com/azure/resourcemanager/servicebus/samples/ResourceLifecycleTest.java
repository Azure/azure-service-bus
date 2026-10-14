// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import java.io.ByteArrayOutputStream;
import java.io.PrintStream;
import java.time.Duration;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.Test;
import reactor.core.publisher.Mono;

import static org.junit.Assert.*;

public class ResourceLifecycleTest {
    private final ByteArrayOutputStream output = new ByteArrayOutputStream();
    private final PrintStream log = new PrintStream(output);
    private final ResourceLifecycle lifecycle = new ResourceLifecycle("/resourceGroups/owned", "owner", log);
    private final AtomicInteger deletes = new AtomicInteger();

    @Test
    public void collisionDoesNotCreateAdoptOrDelete() {
        AtomicInteger creates = new AtomicInteger();
        assertThrows(IllegalStateException.class, () -> lifecycle.create(() -> Mono.just(true),
            () -> { creates.incrementAndGet(); return Mono.just("owner"); }, () -> Mono.just("owner")));
        cleanup("owner");
        assertEquals(0, creates.get());
        assertEquals(0, deletes.get());
    }

    @Test
    public void failedOrUnconfirmedCreationDoesNotGrantOwnership() {
        for (Mono<String> create : java.util.Arrays.asList(Mono.<String>error(new IllegalStateException()),
            Mono.just("different-owner"), Mono.<String>empty())) {
            assertThrows(IllegalStateException.class, () -> lifecycle.create(() -> Mono.just(false),
                () -> create, () -> Mono.just("owner")));
            cleanup("owner");
        }
        assertEquals(0, deletes.get());
        assertTrue(output.toString().contains("/resourceGroups/owned"));
    }

    @Test
    public void emptyOrFailedPreflightDoesNotCreate() {
        AtomicInteger creates = new AtomicInteger();
        for (Mono<Boolean> preflight : java.util.Arrays.asList(Mono.<Boolean>empty(),
            Mono.<Boolean>error(new IllegalStateException()))) {
            assertThrows(IllegalStateException.class, () -> lifecycle.create(() -> preflight,
                () -> { creates.incrementAndGet(); return Mono.just("owner"); }, () -> Mono.just("owner")));
        }
        assertEquals(0, creates.get());
    }

    @Test
    public void changedOwnershipPreventsDeletionAndMutation() {
        createOwned();
        assertThrows(IllegalStateException.class, () -> lifecycle.requireOwnership(() -> Mono.just("other")));
        assertThrows(IllegalStateException.class, () -> cleanup("other"));
        assertEquals(0, deletes.get());
        assertFalse(output.toString().contains("Confirmed deleted"));
    }

    @Test
    public void ownedGroupIsDeletedOnlyOnceAfterConfirmedAbsence() {
        createOwned();
        cleanup("owner");
        cleanup("owner");
        assertEquals(1, deletes.get());
        assertTrue(output.toString().contains("Confirmed deleted: /resourceGroups/owned"));
    }

    @Test
    public void acceptedButPendingDeleteWaitsForAbsence() {
        AtomicInteger reads = new AtomicInteger();
        ResourceLifecycle.deleteAndConfirm("owned", Mono::empty,
            () -> Mono.just(reads.incrementAndGet() < 3), Duration.ofSeconds(1), Duration.ofMillis(5), log);
        assertEquals(3, reads.get());
        assertTrue(output.toString().contains("Confirmed deleted"));
    }

    @Test
    public void pendingDeletionTimesOutWithoutClaimingCompletion() {
        long start = System.nanoTime();
        assertThrows(IllegalStateException.class, () -> ResourceLifecycle.deleteAndConfirm("owned", Mono::empty,
            () -> Mono.just(true), Duration.ofMillis(80), Duration.ofMillis(5), log));
        assertTrue(Duration.ofNanos(System.nanoTime() - start).compareTo(Duration.ofSeconds(2)) < 0);
        assertTrue(output.toString().contains("completion pending"));
        assertFalse(output.toString().contains("Confirmed deleted"));
    }

    @Test
    public void deadlineIncludesDeleteRequestAndReadback() {
        for (boolean stuckDelete : java.util.Arrays.asList(true, false)) {
            assertThrows(IllegalStateException.class, () -> ResourceLifecycle.deleteAndConfirm("owned",
                () -> stuckDelete ? Mono.never() : Mono.empty(), Mono::never,
                Duration.ofMillis(50), Duration.ofMillis(5), log));
        }
        assertFalse(output.toString().contains("Confirmed deleted"));
    }

    @Test
    public void deletionAndReadbackErrorsPropagateAndRetainIdentifier() {
        for (boolean failDelete : java.util.Arrays.asList(true, false)) {
            assertThrows(IllegalArgumentException.class, () -> ResourceLifecycle.deleteAndConfirm("owned",
                () -> failDelete ? Mono.error(new IllegalArgumentException()) : Mono.empty(),
                () -> Mono.error(new IllegalArgumentException()), Duration.ofSeconds(1), Duration.ofMillis(5), log));
        }
        assertTrue(output.toString().contains("owned"));
        assertFalse(output.toString().contains("Confirmed deleted"));
    }

    private void createOwned() {
        lifecycle.create(() -> Mono.just(false), () -> Mono.just("owner"), () -> Mono.just("owner"));
    }

    private void cleanup(String owner) {
        lifecycle.cleanup(() -> Mono.just(owner), () -> {
            deletes.incrementAndGet();
            return Mono.empty();
        }, () -> Mono.just(false), Duration.ofSeconds(1));
    }
}
