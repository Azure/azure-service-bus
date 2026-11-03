// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import java.io.PrintStream;
import java.time.Duration;
import java.util.function.Supplier;
import reactor.core.publisher.Mono;

final class ResourceLifecycle {
    static final String OWNER_TAG = "sample-run-id";
    static final Duration OPERATION_TIMEOUT = Duration.ofMinutes(5);
    static final Duration CLEANUP_TIMEOUT = Duration.ofMinutes(10);
    private static final Duration POLL_INTERVAL = Duration.ofSeconds(2);

    private final String resourceId;
    private final String owner;
    private final PrintStream out;
    private boolean created;

    ResourceLifecycle(String resourceId, String owner, PrintStream out) {
        this.resourceId = resourceId;
        this.owner = owner;
        this.out = out;
    }

    void create(Supplier<Mono<Boolean>> exists, Supplier<Mono<String>> create,
        Supplier<Mono<String>> readOwner) {
        out.println("Planned resource group: " + resourceId);
        require(Boolean.FALSE.equals(await(exists.get())), "Resource group absence not confirmed; refusing adoption");
        // A failed/ambiguous PUT never grants ownership, even if Azure eventually finishes it.
        require(owner.equals(await(create.get())), "Creation did not confirm the run ownership tag");
        created = true;
        require(owner.equals(await(readOwner.get())), "Resource group ownership readback failed");
        out.println("Created resource group: " + resourceId);
    }

    void requireOwnership(Supplier<Mono<String>> readOwner) {
        require(created && owner.equals(await(readOwner.get())), "Resource ownership changed; refusing mutation");
    }

    void cleanup(Supplier<Mono<String>> readOwner, Supplier<Mono<Void>> delete,
        Supplier<Mono<Boolean>> exists, Duration timeout) {
        if (!created) {
            out.println("No confirmed resource-group ownership; not deleting: " + resourceId
                + ". If creation was attempted, inspect this identifier manually.");
            return;
        }
        requireOwnership(readOwner);
        deleteAndConfirm(resourceId, delete, exists, timeout, POLL_INTERVAL, out);
        created = false;
    }

    static void deleteAndConfirm(String id, Supplier<Mono<Void>> delete, Supplier<Mono<Boolean>> exists,
        Duration timeout, Duration pollInterval, PrintStream out) {
        out.println("Deleting (completion pending): " + id);
        Mono.defer(delete)
            .then(Mono.defer(exists)
                .repeatWhen(repeats -> repeats.delayElements(pollInterval))
                .filter(present -> !present)
                .next()
                .switchIfEmpty(Mono.error(new IllegalStateException("Deletion was not confirmed"))))
            .block(timeout);
        out.println("Confirmed deleted: " + id);
    }

    static <T> T await(Mono<T> operation) {
        return operation.block(OPERATION_TIMEOUT);
    }

    static void require(boolean condition, String message) {
        if (!condition) {
            throw new IllegalStateException(message);
        }
    }
}
