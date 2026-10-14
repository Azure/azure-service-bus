// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import com.azure.resourcemanager.samples.Utils;
import java.io.PrintStream;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.function.Function;

/**
 * Creates an isolated namespace, demonstrates topic/subscription management, verifies
 * default-rule fan-out with message settlement, and waits for owned-resource deletion.
 */
public final class ServiceBusPublishSubscribeBasic {
    private ServiceBusPublishSubscribeBasic() {
    }

    public static void main(String[] args) {
        int status = execute(args, System.getenv(), System.out, System.err, AzureOperations::new);
        if (status != 0) {
            System.exit(status);
        }
    }

    static int execute(String[] args, Map<String, String> environment, PrintStream out,
        PrintStream err, Function<Configuration, Operations> factory) {
        if (args.length == 1 && "--help".equals(args[0])) {
            out.println("Usage: --run [--sku=standard|premium] [--rotate-keys]");
            out.println("Requires AZURE_TENANT_ID and AZURE_SUBSCRIPTION_ID; optional AZURE_REGION (westus).");
            out.println("Creates billable resources, sends/receives messages, then deletes owned resources.");
            out.println("--rotate-keys also reads the default namespace keys and rotates its secondary key.");
            return 0;
        }
        final Configuration configuration;
        try {
            configuration = Configuration.parse(args, environment);
            Configuration.requireLoggingDisabled(System.getProperty("AZURE_LOG_LEVEL"));
        } catch (IllegalArgumentException failure) {
            err.println("Configuration error: use --help, explicitly pass --run, and set valid "
                + "AZURE_TENANT_ID / AZURE_SUBSCRIPTION_ID UUIDs. Disable AZURE_LOG_LEVEL logging. "
                + "No Azure operations started.");
            return 2;
        }
        try {
            return runSample(factory.apply(configuration), configuration.rotateKeys, out, err) ? 0 : 1;
        } catch (RuntimeException failure) {
            Utils.printFailure("Client initialization", failure, err);
            return 1;
        }
    }

    static boolean runSample(Operations operations, boolean rotateKeys, PrintStream out, PrintStream err) {
        boolean success = false;
        String stage = "Resource group creation";
        try {
            operations.createResourceGroup();
            stage = "Namespace creation";
            operations.createNamespace();
            stage = "Topic and subscription creation/update";
            operations.createTopicAndSubscriptions();
            stage = "Entity listing and default-rule verification";
            operations.listAndVerifyEntities();
            stage = "Authorization rule operations";
            operations.inspectAuthorization(rotateKeys);
            stage = "Message send/readback/settlement";
            operations.sendAndVerifyFanOut();
            stage = "Subscription and topic deletion";
            operations.deleteSubscriptionsAndTopic();
            stage = "Namespace deletion";
            operations.deleteNamespace();
            success = true;
        } catch (RuntimeException failure) {
            Utils.printFailure(stage, failure, err);
        } finally {
            try {
                operations.cleanupResourceGroup();
            } catch (RuntimeException failure) {
                Utils.printFailure("Resource group cleanup", failure, err);
                success = false;
            }
        }
        if (success) {
            out.println("Sample completed: fan-out verified and owned resources confirmed deleted.");
        } else {
            err.println("Sample failed. Consult the resource identifiers above; cleanup may require manual action.");
        }
        return success;
    }

    interface Operations {
        void createResourceGroup();
        void createNamespace();
        void createTopicAndSubscriptions();
        void listAndVerifyEntities();
        void inspectAuthorization(boolean rotateKeys);
        void sendAndVerifyFanOut();
        void deleteSubscriptionsAndTopic();
        void deleteNamespace();
        void cleanupResourceGroup();
    }

    static final class Configuration {
        final String tenantId;
        final String subscriptionId;
        final String region;
        final boolean premium;
        final boolean rotateKeys;

        private Configuration(String tenantId, String subscriptionId, String region, boolean premium,
            boolean rotateKeys) {
            this.tenantId = tenantId;
            this.subscriptionId = subscriptionId;
            this.region = region;
            this.premium = premium;
            this.rotateKeys = rotateKeys;
        }

        static Configuration parse(String[] args, Map<String, String> environment) {
            boolean run = false;
            boolean premium = false;
            boolean rotateKeys = false;
            Set<String> seen = new HashSet<>();
            for (String arg : args) {
                String name = arg.split("=", 2)[0];
                if (!seen.add(name)) {
                    throw new IllegalArgumentException("Duplicate option");
                }
                switch (arg) {
                    case "--run": run = true; break;
                    case "--sku=standard": break;
                    case "--sku=premium": premium = true; break;
                    case "--rotate-keys": rotateKeys = true; break;
                    default: throw new IllegalArgumentException("Unknown option");
                }
            }
            if (!run) {
                throw new IllegalArgumentException("Explicit live-operation consent required");
            }
            requireLoggingDisabled(environment.get("AZURE_LOG_LEVEL"));
            String tenant = requireUuid(environment.get("AZURE_TENANT_ID"));
            String subscription = requireUuid(environment.get("AZURE_SUBSCRIPTION_ID"));
            String region = environment.getOrDefault("AZURE_REGION", "westus");
            if (!region.matches("[a-z][a-z0-9]{1,40}")) {
                throw new IllegalArgumentException("Invalid region");
            }
            return new Configuration(tenant, subscription, region, premium, rotateKeys);
        }

        static void requireLoggingDisabled(String level) {
            if (level != null && !level.isBlank() && !"none".equalsIgnoreCase(level)) {
                throw new IllegalArgumentException("SDK logging must be disabled");
            }
        }

        private static String requireUuid(String value) {
            if (value == null || !value.matches("[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}")) {
                throw new IllegalArgumentException("Missing or invalid Azure identifier");
            }
            return UUID.fromString(value).toString();
        }
    }
}
