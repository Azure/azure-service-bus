# Query Service Bus metrics

This .NET 10 example uses `Azure.ResourceManager.Monitor` 1.3.1 and
`Azure.Identity` 1.21.0 instead of the retired-generation management/ADAL
authentication stack. It performs a read-only query of an existing namespace.

Sign in with `az login` or configure `DefaultAzureCredential` for managed
identity/service principal use. Grant **Monitoring Reader** on the namespace
(or equivalent metric-definition and metric-read permissions). No messaging or
Owner permissions are required.

From `AccessServiceBusMetricsViaCode`:

```powershell
dotnet build
dotnet run -- --help
$env:SERVICEBUS_RESOURCE_ID = "/subscriptions/<id>/resourceGroups/<group>/providers/Microsoft.ServiceBus/namespaces/<namespace>"
$env:SERVICEBUS_ENTITY = "<queue-or-topic-name>"
dotnet run -- query
```

The default is `IncomingMessages`, aggregation `Total`, over the last hour in
one-minute intervals. Both request timestamps use UTC with the `Z` suffix.
To query another metric, set `SERVICEBUS_METRIC` and
`SERVICEBUS_AGGREGATION`. The program checks live metric definitions and supported
aggregations before querying; gauges such as `ActiveMessages` must not be assumed
to support the same aggregations as counters. See the
[metric reference](https://learn.microsoft.com/azure/azure-monitor/reference/supported-metrics/microsoft-servicebus-namespaces-metrics).

Null points are printed as `no data`, not silently converted to zero. Numeric
zero remains zero: Azure Monitor can return zero-valued points even for an entity
name that does not exist. Neither a zero nor an empty result proves entity
existence or that a queue is empty. Metric ingestion also has latency.
The query has a two-minute deadline. Help/build commands do not authenticate;
`query` does contact Azure.
