using System;
using System.Linq;
using Microsoft.Azure.Management.Monitor;
using Microsoft.Azure.Management.Monitor.Models;
using Microsoft.Azure.Management.ServiceBus;
using Microsoft.Azure.Management.ServiceBus.Models;
using Microsoft.Rest.Azure.OData;
using System.Threading.Tasks;
using Microsoft.Rest.Azure.Authentication;
using Newtonsoft.Json;

namespace AccessServiceBusMetricsViaCode
{
    class Program
    {
        public static void Main(String[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }
        static async Task MainAsync(string[] args)
        {
            var tenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47"; // AAD Tenant
            var clientId = "b96384a0-7a01-4f04-b290-4b48e117ee3c"; // AAD Web App ID. Do not use a native app
            var secret = "pnAY/v75vD3Q23pqM64duhp0laN7Ct25NsN6YaPQqk8="; // Your generated secret                              
            var resourceId = "subscriptions/326100e2-f69d-4268-8503-075374f62b6e/resourceGroups/DemoGroup/providers/Microsoft.ServiceBus/namespaces/DemoNamespaceSB"; // resourceId can be taken when you select the namespace you intend to use in the portal and copy the url. Then delete everything before "subscriptions" and after the namespace name.                        
            string entityName = "inbound";  // Queue or Topic name           
            string metricName = "ActiveMessages";  // Valid metrics "IncomingMessages,IncomingRequests,ActiveMessages,Messages,Size"            
            string aggregation = "Total"; // Valid aggregations: Total and Average

            // Create new Metrics token and Management client.
            var serviceCreds = await ApplicationTokenProvider.LoginSilentAsync(tenantId, clientId, secret);            
            MonitorManagementClient monitoringClient = new MonitorManagementClient(serviceCreds);           

            var metricDefinitions = monitoringClient.MetricDefinitions.List(resourceId);
            if (metricDefinitions.FirstOrDefault(
                    metric => string.Equals(metric.Name.Value, metricName, StringComparison.InvariantCultureIgnoreCase)) == null)
            {
                Console.WriteLine("Invalid metric");
                return;
            }

            string startDate = DateTime.Now.AddHours(-1).ToString("o");
            string endDate = DateTime.Now.ToString("o");
            string timeSpan = startDate + "/" + endDate;
            ODataQuery<MetadataValue> odataFilterMetrics = new ODataQuery<MetadataValue>($"EntityName eq '{entityName}'");

            // Use this as quick and easy way to understand what metrics are emitted and what to query for. 
            // When looking for the count and size of an entity the only supported way is using total and 1 minute time slices.
            // Accessing those metrics via code is mostly for auto scaling purposes on sender and receiver side.
            Response metrics1 = monitoringClient.Metrics.List(resourceUri: resourceId, metricnames: "ActiveMessages", odataQuery: odataFilterMetrics, timespan: timeSpan, aggregation: "Total", interval: TimeSpan.FromMinutes(1));
            Console.WriteLine(JsonConvert.SerializeObject(metrics1, Newtonsoft.Json.Formatting.Indented));

            // Use this to get a list output to your console                        
            var metrics = monitoringClient.Metrics.List(resourceId, odataFilterMetrics, timeSpan, TimeSpan.FromMinutes(1), metricName, aggregation);
            EnumerateMetrics(metrics, resourceId, entityName);

            Console.WriteLine("Press any key to exit.");
            Console.ReadLine();
        }       
        private static void EnumerateMetrics(Response metrics, string armId, string entityName, int maxRecords = 60)
        {
            Console.Write(
                  "Cost: {0}\r\nTimespan: {1}\r\nInterval: {2}\r\n",
                  metrics.Cost,
                  metrics.Timespan,
                  metrics.Interval);

            var numRecords = 0;
            Console.WriteLine("Printing metrics for Resource " + armId);
            foreach (var metric in metrics.Value)
            {
                foreach (var timeSeries in metric.Timeseries)
                {
                    // Use Average and multiplier for bigger time ranges than one minute and when observing bigger time ranges than 5 minutes.
                    // Use Total for short time ranges and 1 minute interval for observing e.g. one hour worth of data and decide to automatically scale receivers or senders.
                    foreach (var data in timeSeries.Data)
                    {
                        Console.WriteLine(
                            "{0}\t{1}\t{2}\t{3}", entityName,
                            metric.Name.Value,
                            metric.Name.LocalizedValue,
                            data.Total);
                    }
                }
                numRecords++;
                if (numRecords >= maxRecords)
                {
                    break;
                }
            }
        }        
    }
}
