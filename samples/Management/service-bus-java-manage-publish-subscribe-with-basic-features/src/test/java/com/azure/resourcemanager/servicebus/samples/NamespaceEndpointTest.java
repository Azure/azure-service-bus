// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import com.azure.core.credential.TokenCredential;
import com.azure.messaging.servicebus.ServiceBusReceiverAsyncClient;
import com.azure.messaging.servicebus.ServiceBusSenderAsyncClient;
import javax.net.ssl.SNIHostName;
import org.junit.Test;

import static org.junit.Assert.*;

public class NamespaceEndpointTest {
    private static final String HOST = "sample-namespace.servicebus.windows.net";
    private static final TokenCredential NO_CREDENTIAL = request -> {
        throw new AssertionError("Endpoint regression must not acquire credentials");
    };

    @Test
    public void armEndpointBecomesHostnameForSenderAndBothReceivers() {
        String endpoint = "https://" + HOST + ":443/";
        assertThrows(IllegalArgumentException.class, () -> new SNIHostName(endpoint));
        try (ServiceBusSenderAsyncClient sender = AzureOperations.messagingBuilder(endpoint, NO_CREDENTIAL)
                .sender().topicName(AzureOperations.TOPIC).buildAsyncClient();
            ServiceBusReceiverAsyncClient first = AzureOperations.messagingBuilder(endpoint, NO_CREDENTIAL)
                .receiver().topicName(AzureOperations.TOPIC).subscriptionName(AzureOperations.FIRST).buildAsyncClient();
            ServiceBusReceiverAsyncClient second = AzureOperations.messagingBuilder(endpoint, NO_CREDENTIAL)
                .receiver().topicName(AzureOperations.TOPIC).subscriptionName(AzureOperations.SECOND)
                .buildAsyncClient()) {
            assertEquals(HOST, sender.getFullyQualifiedNamespace());
            assertEquals(HOST, first.getFullyQualifiedNamespace());
            assertEquals(HOST, second.getFullyQualifiedNamespace());
            assertEquals(HOST, new SNIHostName(sender.getFullyQualifiedNamespace()).getAsciiName());
        }
    }

    @Test
    public void implicitHttpsPortAndCloudSuffixArePreserved() {
        for (String endpoint : new String[] {"https://" + HOST, "https://" + HOST + "/",
            "https://namespace.servicebus.usgovcloudapi.net:443/"}) {
            try (ServiceBusSenderAsyncClient sender = AzureOperations.messagingBuilder(endpoint, NO_CREDENTIAL)
                    .sender().topicName(AzureOperations.TOPIC).buildAsyncClient()) {
                assertEquals(java.net.URI.create(endpoint).getHost(), sender.getFullyQualifiedNamespace());
                new SNIHostName(sender.getFullyQualifiedNamespace());
            }
        }
    }

    @Test
    public void invalidOrAmbiguousEndpointsFailBeforeClientConstruction() {
        assertThrows(IllegalStateException.class, () -> AzureOperations.messagingBuilder(null, NO_CREDENTIAL));
        for (String endpoint : new String[] {"", HOST, "http://" + HOST, "https://" + HOST + ":5671/",
            "https://user@" + HOST, "https://" + HOST + "/orders", "https://" + HOST + "?key=value",
            "https://" + HOST + "#fragment", "https:///missing-host"}) {
            assertThrows(endpoint, IllegalStateException.class,
                () -> AzureOperations.messagingBuilder(endpoint, NO_CREDENTIAL));
        }
        assertThrows(IllegalArgumentException.class,
            () -> AzureOperations.messagingBuilder("https://invalid host/", NO_CREDENTIAL));
    }
}
