using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Monitor;
using Azure.ResourceManager.Monitor.Models;
using System.Globalization;

if (args.Length == 0 || args is ["--help"])
{
    Console.WriteLine("Usage: dotnet run -- query");
    Console.WriteLine("Set SERVICEBUS_RESOURCE_ID to the namespace ARM ID and SERVICEBUS_ENTITY to a queue/topic.");
    Console.WriteLine("Optional SERVICEBUS_METRIC (default IncomingMessages) and SERVICEBUS_AGGREGATION (default Total).");
    return;
}
if (args is not ["query"])
    throw new ArgumentException("Unknown arguments. Use --help.");

string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
    ? value : throw new InvalidOperationException($"Set {name}.");

var resourceId = new ResourceIdentifier(Required("SERVICEBUS_RESOURCE_ID"));
if (resourceId.ResourceType != new ResourceType("Microsoft.ServiceBus/namespaces"))
    throw new ArgumentException("SERVICEBUS_RESOURCE_ID must identify a Service Bus namespace.");
var entity = Required("SERVICEBUS_ENTITY");
var metricName = Environment.GetEnvironmentVariable("SERVICEBUS_METRIC") ?? "IncomingMessages";
var aggregation = Environment.GetEnvironmentVariable("SERVICEBUS_AGGREGATION") ?? "Total";
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var token = timeout.Token;
var arm = new ArmClient(new DefaultAzureCredential());
MonitorMetricDefinition? definition = null;
await foreach (var candidate in arm.GetMonitorMetricDefinitionsAsync(resourceId, cancellationToken: token))
{
    if (candidate.Name.Value.Equals(metricName, StringComparison.OrdinalIgnoreCase))
        definition = candidate;
}
if (definition is null)
    throw new ArgumentException($"Metric {metricName} is not available on this namespace.");
if (!definition.SupportedAggregationTypes.Any(value => value.ToString().Equals(aggregation, StringComparison.OrdinalIgnoreCase)))
    throw new ArgumentException($"Aggregation {aggregation} is unsupported. Choose: {string.Join(", ", definition.SupportedAggregationTypes)}");
if (!definition.Dimensions.Any(value => value.Value.Equals("EntityName", StringComparison.OrdinalIgnoreCase)))
    throw new ArgumentException($"Metric {metricName} does not support filtering by EntityName.");

var end = DateTime.UtcNow;
var options = new ArmResourceGetMonitorMetricsOptions
{
    Metricnames = definition.Name.Value,
    Metricnamespace = "Microsoft.ServiceBus/namespaces",
    Aggregation = aggregation,
    Timespan = $"{end.AddHours(-1):O}/{end:O}",
    Interval = TimeSpan.FromMinutes(1),
    Filter = $"EntityName eq '{entity.Replace("'", "''")}'"
};
int records = 0;
await foreach (var metric in arm.GetMonitorMetricsAsync(resourceId, options, token))
{
    if (!string.IsNullOrEmpty(metric.ErrorCode) && metric.ErrorCode != "Success")
        throw new InvalidOperationException($"{metric.ErrorCode}: {metric.ErrorMessage}");
    foreach (var series in metric.Timeseries)
    {
        foreach (var point in series.Data)
        {
            double? value = aggregation.ToLowerInvariant() switch
            {
                "total" => point.Total, "average" => point.Average, "minimum" => point.Minimum,
                "maximum" => point.Maximum, "count" => point.Count,
                _ => throw new ArgumentException($"Unsupported aggregation output: {aggregation}")
            };
            Console.WriteLine($"{point.TimeStamp:O}\t{entity}\t{metric.Name.Value}\t{aggregation}={value?.ToString(CultureInfo.InvariantCulture) ?? "no data"}");
            records++;
        }
    }
}
Console.WriteLine($"Returned {records} metric points. Missing observations are not zero.");
