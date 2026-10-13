# Current Java Service Bus feature scenarios

These examples use `com.azure:azure-messaging-servicebus` **7.18.1**,
`azure-identity` **1.18.7**, Java 11 or later, and Maven 3.9 or later.
They do not use the retired `com.microsoft.azure:azure-servicebus` library.

## Build without Azure

```powershell
mvn clean package
java -jar .\target\servicebus-feature-scenarios-1.0.0.jar --help
```

The twenty unit tests exercise message properties, entity settings, proxy
configuration and failure paths without authenticating. They cover ownership
and forwarding-safe cleanup, ambiguous creation, reply-before-settlement,
correlation/session rejection, receive deadlines, transaction rollback and
explicit Standard/Premium partition configuration.
Packaging merges service-provider metadata
so Azure SDK HTTP/serialization providers remain available in the executable JAR.
These are not broker integration tests.

## Run one scenario

Use a disposable **Standard or Premium** namespace, sign in with `az login`,
and grant your identity **Azure Service Bus Data Owner** on that namespace.
`DefaultAzureCredential` also supports managed identity and standard service
principal environment configuration.

```powershell
$env:SERVICEBUS_NAMESPACE = "<namespace>.servicebus.windows.net"
java -jar .\target\servicebus-feature-scenarios-1.0.0.jar request-reply --allow-entity-management
```

For **Premium**, also set `SERVICEBUS_PREMIUM_PARTITIONS` to the namespace's
verified partition count (`1`, `2` or `4`) before running any scenario. For
example, use `$env:SERVICEBUS_PREMIUM_PARTITIONS = "2"` for a namespace with two
partitions. Leave this variable unset for Standard. The administration SDK
reports the tier and messaging units, but not the Premium partition count;
messaging units are not a substitute for that count.

Replace `request-reply` with one of:

| Scenario | Expected behavior |
| --- | --- |
| `request-reply` | Request carries `MessageId`, `ReplyTo` and `ReplyToSessionId`; reply has matching `CorrelationId` and is read from the caller's session |
| `duplicate-detection` | Two sends of `order-42` within ten minutes yield one observed copy; a different `order-43` is accepted |
| `ttl` | The three-second message expires, active receive triggers expiry processing, and its `TTLExpiredException` DLQ record is inspected and explicitly discarded |
| `autoforward` | A queue forwards to another queue in the same namespace; the receiver reads only the destination |
| `partitioning` | Two same-key messages are sent in a transaction and received in session order |

Each invocation creates uniquely named `sample-<random-id>-*` queues, sends and
consumes messages, and deletes only queues it successfully created. A forwarding
source is deleted before its destination. If source creation or deletion cannot
be confirmed, its destination is retained for manual inspection. Planned names
are printed before creation, and unconfirmed creation never grants automatic
deletion ownership. Cleanup attempts independent queues within a separate
two-minute budget and preserves the scenario error if cleanup also fails.
Namespace lookup and creation each have a 45-second wait; receive waits have
two-minute deadlines. Errors exit nonzero; timed-out requests can still take
effect in Azure. The printed names and five-minute auto-delete-on-idle setting
aid cleanup, but do not prove it completed. Do not run against production.

Request completion and reply send are separate operations: a crash can cause
another response on redelivery. Correlation/session isolation does not replace
idempotent business processing. Duplicate detection likewise does not provide
exactly-once processing or an unlimited detection window.

Expiry processing is lazy, not a precise timer. In real applications, decide
whether work is still valid before replaying a DLQ message. If approved, create
a new message with deliberate TTL/ID values and settle the old one; never
automatically replay expired business actions.

Autoforwarding is **same-namespace routing**, not cross-region replication.
Its source must not have a competing receiver. Observe the
[forwarding limits](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-auto-forwarding),
including the four-hop maximum and unsupported session-enabled forwarding sources.

## Tier-correct partitioning

Standard enables partitioning **when creating the queue** for the `partitioning`
scenario. Premium chooses the topology when creating the **namespace**, but
queue-create requests must still match it: `setPartitioningEnabled(true)` for two
or four namespace partitions, and `false` for one. This applies to every scenario
and every queue, including both autoforwarding queues. A false flag in a
partitioned Premium namespace is rejected by the service.

The program reads the live tier and uses the explicit
`SERVICEBUS_PREMIUM_PARTITIONS` setting; it does not enable or resize namespace
partitioning. Verify that count in Azure before supplying the setting or claiming
physical-partition coverage. Basic is not supported by these combined
session/transaction demonstrations.

`SessionId` and `PartitionKey` must match when both are supplied. All sends in
this transaction use the same key. Session ordering is not global ordering
across partitions. See [partitioning](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-partitioning)
and [transactions](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-transactions).

## HTTP proxy / AMQP over WebSockets

All scenarios can use an unauthenticated HTTP CONNECT proxy:

```powershell
$env:SERVICEBUS_PROXY_HOST = "proxy.example.com"
$env:SERVICEBUS_PROXY_PORT = "8080"
```

The compiled `clientBuilder` recipe sets **both**
`AmqpTransportType.AMQP_WEB_SOCKETS` and `ProxyOptions`; setting only a proxy is
insufficient. The destination Service Bus WebSocket connection uses TLS port 443.
Proxy ports outside 1-65535 and incomplete settings are rejected.

These options configure the **AMQP messaging connection only**. Identity token
acquisition and administration use separate HTTPS clients; configure their
network/proxy access independently in a restricted environment. For authenticated
proxies, use the appropriate `ProxyAuthenticationType` and credentials obtained
securely at runtime, not credentials embedded in source or URLs. See
[Service Bus network requirements](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-faq#what-ports-do-i-need-to-open-on-the-firewall).
