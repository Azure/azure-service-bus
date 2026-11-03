# Current Service Bus feature scenarios (.NET)

Small, runnable .NET 10 examples using `Azure.Messaging.ServiceBus` 7.21.0 and
Azure Identity. Each command focuses on a current feature rather than porting a
retired sample framework.

| Command | Demonstrates |
| --- | --- |
| `request-reply` | `MessageId`, `ReplyTo`, `ReplyToSessionId`, response `CorrelationId`, and accepting the requester's reply session |
| `duplicate-detection` | Stable business IDs, two sends within a ten-minute window, one observed copy, and acceptance of a different ID |
| `ttl` | Explicit message TTL, dead-letter-on-expiry, active receive to trigger expiry processing, DLQ reason inspection, and deliberate discard rather than automatic replay |
| `partitioning` | Standard entity-level partitioning versus Premium namespace-level partitioning; matching partition/session keys and same-key transactional sends |

## Local checks (no Azure)

Install the .NET 10 SDK. From this directory:

```powershell
dotnet build
dotnet run -- --self-test
dotnet run -- --help
```

The self-test checks message/configuration contracts, Standard and Premium
partition settings, and six lifecycle and failure-path categories: cleanup,
unconfirmed creation, reply-before-settlement, reply identity, receive
cancellation and transaction abort. It does not test broker behavior.

## Run against an isolated Azure namespace

Use an existing disposable **Standard or Premium** namespace and `az login`.
Grant your identity **Azure Service Bus Data Owner** on that test namespace for
entity administration and messaging. No namespace/resource-group creation or
ARM Owner permission is needed. Basic is rejected because the full set of
scenarios uses sessions, duplicate detection and transactions.

```powershell
$env:SERVICEBUS_NAMESPACE = "<namespace>.servicebus.windows.net"
dotnet run -- request-reply --allow-entity-management
dotnet run -- duplicate-detection --allow-entity-management
dotnet run -- ttl --allow-entity-management
dotnet run -- partitioning --allow-entity-management
```

For **Premium**, also set `SERVICEBUS_PREMIUM_PARTITIONS` to the namespace's
verified partition count (`1`, `2` or `4`) before running any scenario. For
example, use `$env:SERVICEBUS_PREMIUM_PARTITIONS = "2"` for a namespace with two
partitions. Leave this variable unset for Standard. The administration SDK
reports the tier and messaging units, but not the Premium partition count;
messaging units are not a substitute for that count.

Each run creates uniquely named `sample-<random-id>-*` queues, sends/consumes test
messages and deletes **only queues successfully created by that run**. The
scenario has a five-minute deadline and cleanup a separate two-minute deadline.
Failures exit nonzero. Queue names are printed before creation. Cleanup attempts
all confirmed owned queues and preserves the original scenario error if cleanup
also fails. Unconfirmed creation does not authorize deletion: inspect the
reported names and ownership manually. Five-minute auto-delete-on-idle is a fallback,
not proof that cleanup completed. Do not use production namespaces.

## Important semantics

- **Request/reply:** the responder sends before completing the request. A crash
  between those operations can produce another response on redelivery. This is
  not an exactly-once application protocol: use idempotent business processing,
  correlation checks and an appropriate transaction/outbox design where needed.
  Real callers need timeout/cancellation handling and a reply-queue lifecycle.
- **Duplicate detection:** the broker tracks the application-supplied
  `MessageId` within the configured window. It does not prevent redelivery of an
  unsettled receive or duplicate side effects in an external database. On
  partitioned entities the partition key also participates in uniqueness.
- **Expiry:** expiry processing is not a precise timer. The example attempts an
  active receive and waits for the DLQ message. It intentionally discards its
  expired test work after inspection. In a real application, first decide whether
  the business operation is still valid; only then create a new message with an
  appropriate TTL/ID and settle the old DLQ message. Never blindly replay expired
  orders, reservations or authorizations.
- **Partitioning:** Basic/Standard configure partitioning when creating an
  entity; this sample supports Standard, not Basic. Premium chooses the topology
  when creating the **namespace**, but its queue-create request must still match
  that topology: `EnablePartitioning=true` for two or four namespace partitions,
  and `false` for one. This applies to **every queue in every scenario**, including
  request/reply queues. A false flag in a partitioned Premium namespace is rejected
  by the service. The setting does not enable or resize namespace partitioning.
  This example does not provision or change a Premium namespace: verify its
  partition count before supplying `SERVICEBUS_PREMIUM_PARTITIONS` or claiming
  physical-partition coverage.
  Sessions give ordering within a session, not across partitions. When both are
  present, `SessionId` and `PartitionKey` must match. A transaction's sends must
  use a consistent partition key; this example commits two sends to one queue.

See [sessions/request-response](https://learn.microsoft.com/azure/service-bus-messaging/message-sessions#request-response-pattern),
[duplicate detection](https://learn.microsoft.com/azure/service-bus-messaging/duplicate-detection),
[expiration](https://learn.microsoft.com/azure/service-bus-messaging/message-expiration),
[partitioning](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-partitioning),
and [transactions](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-transactions).
For .NET autoforwarding, reuse the
[SDK forwarding example](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/servicebus/Azure.Messaging.ServiceBus/samples/Sample21_AutoForwardIntoSession.md).
