// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.servicebus.samples;

import com.azure.core.credential.TokenCredential;
import com.azure.core.http.HttpClient;
import com.azure.core.http.HttpHeaders;
import com.azure.core.http.HttpPipeline;
import com.azure.core.http.HttpPipelineBuilder;
import com.azure.core.http.HttpRequest;
import com.azure.core.http.HttpResponse;
import com.azure.core.management.AzureEnvironment;
import com.azure.core.management.profile.AzureProfile;
import com.azure.resourcemanager.resources.ResourceManager;
import com.azure.resourcemanager.servicebus.ServiceBusManager;
import java.io.ByteArrayOutputStream;
import java.io.PrintStream;
import java.nio.ByteBuffer;
import java.nio.charset.Charset;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.TreeMap;
import java.util.stream.Collectors;
import org.junit.Test;
import reactor.core.publisher.Flux;
import reactor.core.publisher.Mono;

import static org.junit.Assert.*;

/**
 * Executes the actual management adapter against an in-memory HTTP transport.
 * No bearer-token policy, socket transport, or credential acquisition is installed.
 */
public class AzureOperationsTest {
    private final ByteArrayOutputStream output = new ByteArrayOutputStream();
    private final PrintStream log = new PrintStream(output);
    private final ArmTransport transport = new ArmTransport();

    private AzureOperations operations(boolean premium) {
        ServiceBusPublishSubscribeBasic.Configuration config = ServiceBusPublishSubscribeBasic.Configuration.parse(
            new String[] {"--run", "--sku=" + (premium ? "premium" : "standard")},
            ServiceBusPublishSubscribeBasicTest.environment());
        AzureProfile profile = new AzureProfile(config.tenantId, config.subscriptionId, AzureEnvironment.AZURE);
        HttpPipeline pipeline = new HttpPipelineBuilder().httpClient(transport).build();
        TokenCredential forbidden = request -> Mono.error(new AssertionError("No credentials in offline tests"));
        return new AzureOperations(config, ResourceManager.authenticate(pipeline, profile)
            .withSubscription(config.subscriptionId), ServiceBusManager.authenticate(pipeline, profile),
            forbidden, log);
    }

    @Test
    public void actualSdkManagementContractsWorkForBothSkus() {
        for (boolean premium : new boolean[] {false, true}) {
            transport.reset();
            AzureOperations operations = operations(premium);
            operations.createResourceGroup();
            operations.createNamespace();
            assertTrue(transport.bodies.stream().anyMatch(body -> body.contains(premium ? "\"Premium\"" : "\"Standard\"")));
            operations.createTopicAndSubscriptions();
            assertTrue(transport.bodies.stream().anyMatch(body -> body.contains("\"maxSizeInMegabytes\":2048")));
            assertTrue(transport.bodies.stream().anyMatch(body -> body.contains("\"maxSizeInMegabytes\":3072")));
            assertTrue(transport.bodies.stream().anyMatch(body -> body.contains("\"autoDeleteOnIdle\":\"PT10M\"")));
            operations.listAndVerifyEntities();
            operations.inspectAuthorization(false);
            assertFalse(transport.requests.stream().anyMatch(path -> path.contains("listKeys")));
            operations.inspectAuthorization(true);
            assertTrue(transport.requests.stream().anyMatch(path ->
                path.toLowerCase(Locale.ROOT).endsWith("/authorizationrules/rootmanagesharedaccesskey/listkeys")));
            assertTrue(transport.requests.stream().anyMatch(path ->
                path.toLowerCase(Locale.ROOT).endsWith("/authorizationrules/rootmanagesharedaccesskey/regeneratekeys")));
            assertTrue(transport.bodies.stream().anyMatch(body -> body.contains("\"keyType\":\"SecondaryKey\"")));
            assertFalse(output.toString().contains("sentinel-secret"));
            operations.deleteSubscriptionsAndTopic();
            operations.deleteNamespace();
            operations.cleanupResourceGroup();
            assertTrue(transport.stored.isEmpty());
        }
    }

    @Test
    public void actualSdkNamespaceCollisionNeverCreatesOrDeletesTheExistingNamespace() {
        AzureOperations operations = operations(false);
        operations.createResourceGroup();
        transport.nameAvailable = false;
        assertThrows(IllegalStateException.class, operations::createNamespace);
        operations.cleanupResourceGroup();
        assertFalse(transport.requests.stream().anyMatch(request ->
            (request.startsWith("PUT ") || request.startsWith("DELETE ")) && request.contains("/namespaces/")));
        assertTrue(transport.stored.isEmpty());
    }

    @Test
    public void foreignResourcesPreventResourceGroupCleanup() {
        AzureOperations operations = operations(false);
        operations.createResourceGroup();
        transport.foreignResource = true;
        assertThrows(IllegalStateException.class, operations::cleanupResourceGroup);
        assertFalse(transport.requests.stream().anyMatch(request -> request.startsWith("DELETE ")));
        assertFalse(output.toString().contains("Confirmed deleted"));
    }

    @Test
    public void replacedNamespaceOwnershipPreventsGroupCleanup() {
        AzureOperations operations = operations(false);
        operations.createResourceGroup();
        operations.createNamespace();
        transport.stored.replaceAll((path, body) -> path.contains("/namespaces/")
            ? body.replaceFirst("\"sample-run-id\":\"[^\"]+\"", "\"sample-run-id\":\"foreign\"") : body);
        assertThrows(IllegalStateException.class, operations::deleteSubscriptionsAndTopic);
        assertThrows(IllegalStateException.class, operations::deleteNamespace);
        assertThrows(IllegalStateException.class, operations::cleanupResourceGroup);
        assertFalse(transport.requests.stream().anyMatch(request -> request.startsWith("DELETE ")));
    }

    @Test
    public void permissionErrorsNeverLookLikeResourceAbsence() {
        assertThrows(com.azure.core.exception.HttpResponseException.class,
            () -> AzureOperations.present(Mono.error(new com.azure.core.exception.HttpResponseException(
                "sentinel-secret", new Response(new HttpRequest(com.azure.core.http.HttpMethod.GET,
                    "https://management.azure.com/test"), 403, "{}")))).block());
        assertEquals(Boolean.FALSE, AzureOperations.present(Mono.error(
            new com.azure.core.exception.HttpResponseException("not found",
                new Response(new HttpRequest(com.azure.core.http.HttpMethod.GET,
                    "https://management.azure.com/test"), 404, "{}")))).block());
    }

    private static final class ArmTransport implements HttpClient {
        final Map<String, String> stored = new TreeMap<>(String.CASE_INSENSITIVE_ORDER);
        final List<String> requests = new ArrayList<>();
        final List<String> bodies = new ArrayList<>();
        boolean nameAvailable = true;
        boolean foreignResource;

        void reset() {
            stored.clear();
            requests.clear();
            bodies.clear();
            nameAvailable = true;
            foreignResource = false;
        }

        @Override
        public Mono<HttpResponse> send(HttpRequest request) {
            return Mono.fromSupplier(() -> respond(request));
        }

        private synchronized HttpResponse respond(HttpRequest request) {
            String path = request.getUrl().getPath();
            String lowerPath = path.toLowerCase(Locale.ROOT);
            String method = request.getHttpMethod().toString();
            requests.add(method + " " + path);
            String body = request.getBodyAsBinaryData() == null ? "{}" : request.getBodyAsBinaryData().toString();
            bodies.add(body);
            if (path.endsWith("/CheckNameAvailability") || path.endsWith("/checkNameAvailability")) {
                return new Response(request, 200, "{\"nameAvailable\":" + nameAvailable + "}");
            }
            if ("PUT".equals(method)) {
                String name = path.substring(path.lastIndexOf('/') + 1);
                String resource = "{\"id\":\"" + path + "\",\"name\":\"" + name + "\""
                    + ("{}".equals(body) ? "}" : "," + body.substring(1));
                stored.put(path, resource);
                return new Response(request, 200, resource);
            }
            if ("DELETE".equals(method)) {
                stored.keySet().removeIf(key -> key.equalsIgnoreCase(path)
                    || key.toLowerCase(Locale.ROOT).startsWith(path.toLowerCase(Locale.ROOT) + "/"));
                return new Response(request, path.toLowerCase(Locale.ROOT).contains("/providers/") ? 204 : 200, "");
            }
            if (lowerPath.endsWith("/authorizationrules")) {
                return new Response(request, 200, "{\"value\":[{\"id\":\"" + path
                    + "/RootManageSharedAccessKey\",\"name\":\"RootManageSharedAccessKey\","
                    + "\"properties\":{\"rights\":[\"Manage\",\"Send\",\"Listen\"]}}]}");
            }
            if (lowerPath.endsWith("/listkeys") || lowerPath.endsWith("/regeneratekeys")) {
                return new Response(request, 200, "{\"primaryKey\":\"sentinel-secret-primary\","
                    + "\"secondaryKey\":\"sentinel-secret-secondary\","
                    + "\"primaryConnectionString\":\"Endpoint=sb://sentinel-secret/;SharedAccessKey=secret\","
                    + "\"secondaryConnectionString\":\"Endpoint=sb://sentinel-secret/;SharedAccessKey=secret\"}");
            }
            if (path.endsWith("/rules")) {
                return new Response(request, 200, "{\"value\":[{\"name\":\"$Default\","
                    + "\"properties\":{\"filterType\":\"SqlFilter\",\"sqlFilter\":{\"sqlExpression\":\"1=1\"}}}]}");
            }
            if (path.endsWith("/resources")) {
                String groupPath = path.substring(0, path.length() - "/resources".length());
                String values = stored.entrySet().stream()
                    .filter(entry -> entry.getKey().toLowerCase(Locale.ROOT)
                        .startsWith(groupPath.toLowerCase(Locale.ROOT) + "/providers/")
                        && !entry.getKey().contains("/topics/"))
                    .map(Map.Entry::getValue).collect(Collectors.joining(","));
                if (foreignResource) {
                    values = "{\"id\":\"" + groupPath
                        + "/providers/Microsoft.Storage/storageAccounts/foreign\",\"name\":\"foreign\"}";
                }
                return new Response(request, 200, "{\"value\":[" + values + "]}");
            }
            if (path.endsWith("/topics") || path.endsWith("/subscriptions")) {
                String values = stored.entrySet().stream().filter(entry -> entry.getKey().startsWith(path + "/")
                    && !entry.getKey().substring(path.length() + 1).contains("/"))
                    .map(Map.Entry::getValue).collect(Collectors.joining(","));
                return new Response(request, 200, "{\"value\":[" + values + "]}");
            }
            if (stored.containsKey(path)) {
                return new Response(request, 200, stored.get(path));
            }
            return new Response(request, 404, "{\"error\":{\"code\":\"ResourceNotFound\",\"message\":\"not found\"}}");
        }
    }

    private static final class Response extends HttpResponse {
        private final int status;
        private final String body;
        private final HttpHeaders headers = new HttpHeaders().set("Content-Type", "application/json");

        Response(HttpRequest request, int status, String body) {
            super(request);
            this.status = status;
            this.body = body;
        }

        public int getStatusCode() { return status; }
        public String getHeaderValue(String name) { return headers.getValue(name); }
        public HttpHeaders getHeaders() { return headers; }
        public Flux<ByteBuffer> getBody() { return Flux.just(ByteBuffer.wrap(body.getBytes(StandardCharsets.UTF_8))); }
        public Mono<byte[]> getBodyAsByteArray() { return Mono.just(body.getBytes(StandardCharsets.UTF_8)); }
        public Mono<String> getBodyAsString() { return Mono.just(body); }
        public Mono<String> getBodyAsString(Charset charset) {
            return Mono.just(new String(body.getBytes(StandardCharsets.UTF_8), charset));
        }
    }
}
