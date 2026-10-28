// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import com.azure.core.amqp.exception.AmqpException;
import com.azure.core.exception.HttpResponseException;
import com.azure.messaging.servicebus.ServiceBusException;
import java.io.PrintStream;
import java.time.Duration;
import java.util.Collections;
import java.util.IdentityHashMap;
import java.util.Map;
import java.util.Set;
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
        UpdateResults updates();
    }

    enum UpdateState { NOT_RUN, PASS, NOTPASS, FAIL }

    static final class UpdateResults {
        final QueueUpdate duplicates = new QueueUpdate(false);
        final QueueUpdate sessions = new QueueUpdate(true);

        void reportTo(PrintStream out) {
            duplicates.reportTo(out);
            sessions.reportTo(out);
        }

        void requireComplete() {
            if (!duplicates.complete() || !sessions.complete()) {
                throw new AzureQueueWork.SafetyFailure("Required queue update observation missing or failed");
            }
        }

        void seal() {
            duplicates.seal();
            sessions.seal();
        }

        void report(PrintStream out) {
            out.println("Final update evidence: " + duplicates.summary());
            out.println("Final update evidence: " + sessions.summary());
        }

        boolean immediatelyConfirmed() {
            return duplicates.confirmed() && sessions.confirmed();
        }
    }

    static final class QueueUpdate {
        private final boolean session;
        private UpdateState response = UpdateState.NOT_RUN;
        private UpdateState readback = UpdateState.NOT_RUN;
        private String actual;
        private PrintStream output;
        private boolean sealed;

        private QueueUpdate(boolean session) {
            this.session = session;
        }

        synchronized void reportTo(PrintStream out) {
            output = out;
        }

        synchronized void acknowledge() {
            if (sealed || response != UpdateState.NOT_RUN) {
                throw new AzureQueueWork.SafetyFailure("Queue update response already finalized");
            }
            response = UpdateState.PASS;
        }

        synchronized void observe(Duration idle) {
            if (session || idle == null || idle.compareTo(Duration.ofMinutes(5)) < 0) {
                throw new AzureQueueWork.SafetyFailure("Invalid queue readback; property=autoDeleteOnIdle");
            }
            observe(idle.toString(), Duration.ofMinutes(5).equals(idle));
        }

        synchronized void observe(Boolean deadLetterExpired) {
            if (!session || deadLetterExpired == null) {
                throw new AzureQueueWork.SafetyFailure(
                    "Invalid queue readback; property=deadLetteringOnMessageExpiration");
            }
            observe(deadLetterExpired.toString(), !deadLetterExpired);
        }

        private void observe(String value, boolean matches) {
            if (sealed || response != UpdateState.PASS || readback != UpdateState.NOT_RUN) {
                throw new AzureQueueWork.SafetyFailure("Queue update readback out of order or finalized");
            }
            actual = value;
            readback = matches ? UpdateState.PASS : UpdateState.NOTPASS;
            if (output != null) {
                output.println(summary());
            }
        }

        synchronized void failResponse() {
            if (!sealed && response == UpdateState.NOT_RUN) {
                response = UpdateState.FAIL;
            }
        }

        synchronized void failReadback() {
            if (!sealed && readback == UpdateState.NOT_RUN) {
                readback = UpdateState.FAIL;
            }
        }

        synchronized boolean complete() {
            return response == UpdateState.PASS
                && (readback == UpdateState.PASS || readback == UpdateState.NOTPASS);
        }

        synchronized boolean confirmed() {
            return response == UpdateState.PASS && readback == UpdateState.PASS;
        }

        synchronized void seal() {
            sealed = true;
            output = null;
        }

        synchronized String summary() {
            return (session ? "sessions" : "duplicates") + ": update-response=" + response
                + "; immediate-get-readback=" + readback
                + "; property=" + (session ? "deadLetteringOnMessageExpiration" : "autoDeleteOnIdle")
                + "; expected=" + (session ? "false" : "PT5M")
                + "; actual=" + (actual == null ? "NOT_OBSERVED" : actual)
                + (readback == UpdateState.NOTPASS ? "; reason=MISMATCH" : "");
        }
    }

    static int run(QueueWork work, PrintStream out, PrintStream err, Duration runLimit, Duration cleanupLimit) {
        boolean scenariosCompleted = false;
        boolean cleanupCompleted = false;
        UpdateResults updates = new UpdateResults();
        try {
            UpdateResults evidence = work.updates();
            if (evidence == null) {
                throw new AzureQueueWork.SafetyFailure("Queue update evidence missing");
            }
            updates = evidence;
            updates.reportTo(out);
            Mono.defer(work::provision).then(Mono.defer(work::scenarios))
                .then(Mono.fromRunnable(evidence::requireComplete)).timeout(runLimit).block();
            scenariosCompleted = true;
        } catch (RuntimeException e) {
            report(err, "Scenario failed; outstanding client work cancelled", e);
        } finally {
            updates.seal();
            out.println("Cleanup starting; deletion is not yet confirmed.");
            try {
                Mono.defer(work::cleanup).timeout(cleanupLimit).block();
                cleanupCompleted = true;
                out.println("Cleanup verified: no resources owned by this run remain.");
            } catch (RuntimeException e) {
                report(err, "Cleanup NOT verified; retain the printed resource IDs for manual inspection", e);
            }
        }
        boolean successful = scenariosCompleted && cleanupCompleted;
        updates.report(out);
        out.println("Lifecycle=" + (successful ? "PASS" : "FAIL")
            + "; cleanup=" + (cleanupCompleted ? "PASS" : "FAIL"));
        if (successful) {
            out.println("Lifecycle completed: acknowledged configuration updates, messaging/settlement"
                + " and owned cleanup. Immediate update confirmation: "
                + (updates.immediatelyConfirmed() ? "PASS" : "NOTPASS") + ".");
        }
        return successful ? 0 : 1;
    }

    static void report(PrintStream err, String operation, Throwable error) {
        // Only sample-owned safety text is trusted; SDK diagnostics use typed fields, never messages or payloads.
        String step = error instanceof AzureQueueWork.StepFailure
            ? "; step=" + ((AzureQueueWork.StepFailure) error).step : "";
        Throwable detail = error instanceof AzureQueueWork.StepFailure ? error.getCause() : error;
        StringBuilder reason = new StringBuilder();
        Set<Throwable> seen = Collections.newSetFromMap(new IdentityHashMap<>());
        int[] remaining = {8};
        appendFailure(reason, detail, seen, remaining);
        if (error instanceof AzureQueueWork.StepFailure) {
            appendSuppressed(reason, error, seen, remaining);
        }
        err.println(operation + " (" + safeType(error) + step + reason + ").");
    }

    private static void appendFailure(StringBuilder reason, Throwable detail, Set<Throwable> seen, int[] remaining) {
        if (detail == null) {
            return;
        }
        if (remaining[0] == 0 || !seen.add(detail)) {
            reason.append("; diagnostics-truncated=true");
            return;
        }
        remaining[0]--;
        reason.append("; cause=").append(safeType(detail));
        if (detail instanceof AzureQueueWork.StepFailure) {
            reason.append("; operation=").append(((AzureQueueWork.StepFailure) detail).step);
        }
        appendFrame(reason, detail);
        if (detail instanceof AzureQueueWork.SafetyFailure) {
            reason.append("; ").append(detail.getMessage());
        }
        if (detail instanceof ServiceBusException) {
            ServiceBusException serviceBus = (ServiceBusException) detail;
            reason.append("; serviceBusReason=").append(serviceBus.getReason())
                .append("; transient=").append(serviceBus.isTransient());
        }
        if (detail instanceof AmqpException) {
            AmqpException amqp = (AmqpException) detail;
            if (amqp.getErrorCondition() != null) {
                reason.append("; amqpCondition=").append(amqp.getErrorCondition().name());
            }
            reason.append("; transient=").append(amqp.isTransient());
        }
        if (detail instanceof HttpResponseException) {
            HttpResponseException http = (HttpResponseException) detail;
            if (http.getResponse() != null) {
                reason.append("; httpStatus=").append(http.getResponse().getStatusCode());
            }
        }
        appendFailure(reason, detail.getCause(), seen, remaining);
        appendSuppressed(reason, detail, seen, remaining);
    }

    private static void appendSuppressed(StringBuilder reason, Throwable detail, Set<Throwable> seen, int[] remaining) {
        Throwable[] suppressed = detail.getSuppressed();
        for (int i = 0; i < suppressed.length; i++) {
            if (remaining[0] == 0 || i == 8) {
                reason.append("; suppressed-truncated=true");
                break;
            }
            reason.append("; suppressed={");
            appendFailure(reason, suppressed[i], seen, remaining);
            reason.append("}");
        }
    }

    private static String safeType(Throwable error) {
        String type = error.getClass().getSimpleName();
        return type.matches("[A-Za-z_$][A-Za-z0-9_$]{0,127}") ? type : "RedactedType";
    }

    private static void appendFrame(StringBuilder reason, Throwable error) {
        StackTraceElement[] frames = error.getStackTrace();
        for (int i = 0; i < Math.min(frames.length, 16); i++) {
            StackTraceElement frame = frames[i];
            String owner = frame.getClassName();
            String method = frame.getMethodName();
            if ((owner.startsWith("com.azure.") || owner.startsWith("reactor.core."))
                && owner.length() <= 192 && owner.matches("[A-Za-z_$][A-Za-z0-9_$.]*")
                && method.length() <= 96
                && method.matches("(?:[A-Za-z_$][A-Za-z0-9_$]*|<init>|<clinit>)")) {
                reason.append("; frame=").append(owner).append(".").append(method);
                if (frame.getLineNumber() > 0) {
                    reason.append(":").append(frame.getLineNumber());
                }
                break;
            }
        }
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
