// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import java.io.PrintStream;
import java.time.Duration;
import java.util.Map;
import java.util.UUID;
import java.util.function.Function;
import reactor.core.publisher.Mono;

/** Advanced queues with explicit live opt-in, bounded execution and verified cleanup. */
public final class ServiceBusQueueAdvanceFeatures {
    static final Duration RUN_LIMIT = Duration.ofMinutes(15);
    static final Duration CLEANUP_LIMIT = Duration.ofMinutes(10);

    private ServiceBusQueueAdvanceFeatures() {
    }

    public static void main(String[] args) {
        int result = execute(args, System.getenv(), System.out, System.err, AzureQueueWork::new);
        if (result != 0) {
            System.exit(result);
        }
    }

    static int execute(String[] args, Map<String, String> environment, PrintStream out, PrintStream err,
        Function<Config, QueueWork> factory) {
        if (args.length == 1 && "--help".equals(args[0])) {
            out.println("Offline help. Live execution: --run [--sku=standard|--sku=premium]");
            out.println("Requires AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID and AZURE_LOCATION.");
            out.println("Creates billable resources and temporary queue-scoped SAS rules; see README.md.");
            return 0;
        }
        try {
            Config config = Config.parse(args, environment);
            out.println("Resource group: " + config.groupId());
            out.println("Namespace: " + config.namespaceId());
            out.println("Ownership token: " + config.runId);
            return run(factory.apply(config), out, err, RUN_LIMIT, CLEANUP_LIMIT);
        } catch (IllegalArgumentException e) {
            err.println("Configuration invalid. Use --help; no Azure operation was started.");
            return 2;
        } catch (RuntimeException e) {
            report(err, "Client initialization failed", e);
            return 1;
        }
    }

    interface QueueWork {
        Mono<Void> provision();
        Mono<Void> scenarios();
        Mono<Void> cleanup();
    }

    static int run(QueueWork work, PrintStream out, PrintStream err, Duration runLimit, Duration cleanupLimit) {
        boolean successful = true;
        try {
            Mono.defer(work::provision).then(Mono.defer(work::scenarios)).timeout(runLimit).block();
        } catch (RuntimeException e) {
            successful = false;
            report(err, "Scenario failed; outstanding client work cancelled", e);
        } finally {
            out.println("Cleanup starting; deletion is not yet confirmed.");
            try {
                Mono.defer(work::cleanup).timeout(cleanupLimit).block();
                out.println("Cleanup verified: no resources owned by this run remain.");
            } catch (RuntimeException e) {
                successful = false;
                report(err, "Cleanup NOT verified; retain the printed resource IDs for manual inspection", e);
            }
        }
        if (successful) {
            out.println("All queue scenarios and cleanup completed.");
        }
        return successful ? 0 : 1;
    }

    static void report(PrintStream err, String operation, RuntimeException error) {
        // SDK exception messages, causes and request bodies can contain SAS credentials.
        String step = error instanceof AzureQueueWork.StepFailure
            ? "; step=" + ((AzureQueueWork.StepFailure) error).step : "";
        Throwable detail = error instanceof AzureQueueWork.StepFailure ? error.getCause() : error;
        String reason = detail instanceof AzureQueueWork.SafetyFailure ? "; " + detail.getMessage()
            : "; cause=" + detail.getClass().getSimpleName();
        err.println(operation + " (" + error.getClass().getSimpleName() + step + reason + ").");
    }

    static final class Config {
        final String tenant;
        final String subscription;
        final String location;
        final boolean premium;
        final String runId = UUID.randomUUID().toString();
        final String group = "rg-sb-advanced-" + runId;
        final String namespace = "sb-advanced-" + runId.replace("-", "");

        Config(String tenant, String subscription, String location, boolean premium) {
            this.tenant = tenant;
            this.subscription = subscription;
            this.location = location;
            this.premium = premium;
        }

        static Config parse(String[] args, Map<String, String> environment) {
            if (args.length < 1 || args.length > 2 || !"--run".equals(args[0])
                || (args.length == 2 && !"--sku=standard".equals(args[1]) && !"--sku=premium".equals(args[1]))) {
                throw new IllegalArgumentException("Explicit run consent required");
            }
            String tenant = uuid(environment.get("AZURE_TENANT_ID"));
            String subscription = uuid(environment.get("AZURE_SUBSCRIPTION_ID"));
            String location = environment.get("AZURE_LOCATION");
            if (location == null || !location.matches("[a-z][a-z0-9]{1,39}")) {
                throw new IllegalArgumentException("Location required");
            }
            return new Config(tenant, subscription, location,
                args.length == 2 && "--sku=premium".equals(args[1]));
        }

        private static String uuid(String value) {
            if (value == null || !value.matches("[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}")) {
                throw new IllegalArgumentException("UUID required");
            }
            return UUID.fromString(value).toString();
        }

        String groupId() {
            return "/subscriptions/" + subscription + "/resourceGroups/" + group;
        }

        String namespaceId() {
            return groupId() + "/providers/Microsoft.ServiceBus/namespaces/" + namespace;
        }
    }
}
