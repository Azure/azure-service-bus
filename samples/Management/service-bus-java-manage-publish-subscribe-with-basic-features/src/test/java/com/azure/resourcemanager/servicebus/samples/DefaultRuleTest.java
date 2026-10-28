// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import com.azure.core.exception.HttpResponseException;
import com.azure.core.amqp.models.AmqpMessageId;
import com.azure.core.util.BinaryData;
import com.azure.json.JsonProviders;
import com.azure.json.JsonReader;
import com.azure.messaging.servicebus.ServiceBusReceivedMessage;
import com.azure.resourcemanager.servicebus.fluent.models.RuleInner;
import java.io.IOException;
import java.lang.reflect.Constructor;
import java.time.Duration;
import java.util.Arrays;
import java.util.Collections;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.Test;
import reactor.core.publisher.Flux;
import reactor.core.publisher.Mono;

import static org.junit.Assert.*;

public class DefaultRuleTest {
    private static RuleInner rule(String name, String expression, String action) throws IOException {
        try (JsonReader reader = JsonProviders.createReader("{\"name\":\"" + name
            + "\",\"properties\":{\"filterType\":\"SqlFilter\",\"sqlFilter\":{\"sqlExpression\":\""
            + expression + "\"}" + action + "}}")) {
            return RuleInner.fromJson(reader);
        }
    }

    @Test
    public void defaultRuleAllowsEveryMessageWithoutAnAction() throws IOException {
        AzureOperations.verifyDefaultRule(Collections.singletonList(rule("$Default", "1 = 1", "")));
        AzureOperations.verifyDefaultRule(Collections.singletonList(rule("$Default", "1=1", ",\"action\":{}")));
    }

    @Test
    public void restrictiveAdditionalOrTransformingRulesFailTheDefaultFanOutContract() throws IOException {
        RuleInner defaultRule = rule("$Default", "1=1", "");
        assertThrows(IllegalStateException.class, () -> AzureOperations.verifyDefaultRule(Collections.emptyList()));
        assertThrows(IllegalStateException.class, () -> AzureOperations.verifyDefaultRule(
            Arrays.asList(defaultRule, rule("custom", "1=0", ""))));
        assertThrows(IllegalStateException.class, () -> AzureOperations.verifyDefaultRule(
            Collections.singletonList(rule("$Default", "1=0", ""))));
        assertThrows(IllegalStateException.class, () -> AzureOperations.verifyDefaultRule(
            Collections.singletonList(rule("$Default", "1=1", ",\"action\":{\"sqlExpression\":\"SET a=1\"}"))));
    }

    @Test
    public void absentMessageCannotBeClaimedAsReadbackSuccess() {
        assertThrows(IllegalStateException.class, () -> AzureOperations.verifyMessage(null, "expected-id"));
    }

    @Test
    public void matchingMessageIsSettledAndExtraCopyIsRejected() throws Exception {
        ServiceBusReceivedMessage message = message("Hello World", "id");
        AtomicInteger receives = new AtomicInteger();
        AtomicInteger completions = new AtomicInteger();
        AzureOperations.verifyDelivery(() -> {
            receives.incrementAndGet();
            return Flux.concat(Flux.just(message), Flux.never());
        },
            received -> { completions.incrementAndGet(); return Mono.empty(); }, "id",
            Duration.ofSeconds(1), Duration.ofMillis(10));
        assertEquals(1, completions.get());
        assertEquals(1, receives.get());
        assertThrows(IllegalStateException.class, () -> AzureOperations.verifyDelivery(() -> Flux.just(message, message),
            received -> Mono.empty(), "id", Duration.ofSeconds(1), Duration.ofMillis(10)));
    }

    @Test
    public void mismatchingBodyOrIdNeverSettles() throws Exception {
        for (ServiceBusReceivedMessage message : Arrays.asList(message("wrong", "id"),
            message("Hello World", "wrong"))) {
            assertThrows(IllegalStateException.class, () -> AzureOperations.verifyDelivery(() -> Flux.just(message),
                received -> { throw new AssertionError("Must not settle a mismatching message"); }, "id",
                Duration.ofSeconds(1), Duration.ofMillis(10)));
        }
    }

    @Test
    public void receiveAndSettlementFailuresPropagate() throws Exception {
        ServiceBusReceivedMessage message = message("Hello World", "id");
        assertThrows(IllegalArgumentException.class, () -> AzureOperations.verifyDelivery(
            () -> Flux.error(new IllegalArgumentException()), received -> Mono.empty(), "id",
            Duration.ofSeconds(1), Duration.ofMillis(10)));
        assertThrows(IllegalArgumentException.class, () -> AzureOperations.verifyDelivery(() -> Flux.just(message),
            received -> Mono.error(new IllegalArgumentException()), "id",
            Duration.ofSeconds(1), Duration.ofMillis(10)));
    }

    @Test
    public void stalledReceiveAndSettlementAreBounded() throws Exception {
        ServiceBusReceivedMessage message = message("Hello World", "id");
        for (boolean stallReceive : new boolean[] {false, true}) {
            assertThrows(IllegalStateException.class, () -> AzureOperations.verifyDelivery(
                () -> stallReceive ? Flux.never() : Flux.just(message), received -> Mono.never(), "id",
                Duration.ofMillis(50), Duration.ofMillis(10)));
        }
    }

    private static ServiceBusReceivedMessage message(String body, String id) throws Exception {
        // The SDK has no public received-message constructor; no client or broker is involved.
        Constructor<ServiceBusReceivedMessage> constructor
            = ServiceBusReceivedMessage.class.getDeclaredConstructor(BinaryData.class);
        constructor.setAccessible(true);
        ServiceBusReceivedMessage message = constructor.newInstance(BinaryData.fromString(body));
        message.getRawAmqpMessage().getProperties().setMessageId(new AmqpMessageId(id));
        return message;
    }

    @Test
    public void unknownHttpFailureIsNotMistakenForAbsence() {
        HttpResponseException failure = new HttpResponseException("sensitive details", null);
        assertSame(failure, assertThrows(HttpResponseException.class,
            () -> AzureOperations.present(Mono.error(failure)).block()));
        assertEquals(Boolean.TRUE, AzureOperations.present(Mono.just("resource")).block());
        assertEquals(Boolean.FALSE, AzureOperations.present(Mono.empty()).block());
    }
}
