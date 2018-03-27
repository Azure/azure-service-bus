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
            var tenantId = ""; // AAD Tenant
            var clientId = ""; // AAD Web App ID. Do not use a native app
            var secret = ""; // Your generated secret
            var subscriptionId = ""; // Your Azure subscription                       
            var resourceId = ""; // resourceId can be taken when you select the namespace you intend to use in the portal and copy the url. Then delete everything before "subscriptions" and after the namespace name.
            string resourceGroup = "";
            string namespaceName = "";
            string entityName = "";  // Queue or Topic name           
            string metricName = "ActiveMessages";  // Valid metrics "IncomingMessages,IncomingRequests,ActiveMessages,Messages,Size"            
            string aggregation = "Average"; // Valid aggregations: Total and Average

            // Create new Metrics token and Management client.
            var serviceCreds = await ApplicationTokenProvider.LoginSilentAsync(tenantId, clientId, secret);            
            MonitorManagementClient monitoringClient = new MonitorManagementClient(serviceCreds);

            // Multiply automatically based on if premium or standard and based on if partitioning on or not. (Workaround for metrics bug)
            ServiceBusManagementClient serviceBusClient = new ServiceBusManagementClient(serviceCreds) { SubscriptionId = subscriptionId };
            int multiplier = retMultiplier(serviceBusClient, aggregation,resourceGroup,namespaceName,entityName);

            var metricDefinitions = monitoringClient.MetricDefinitions.List(resourceId);
            if (metricDefinitions.FirstOrDefault(
                    metric => string.Equals(metric.Name.Value, metricName, StringComparison.InvariantCultureIgnoreCase)) == null)
            {
                Console.WriteLine("Invalid metric");
                return;
            }

            // Use this as quick and easy way to understand what metrics are emitted and what to query for. Some make more sense in total and some more in average.
            // Total together with count and size should be looked at in minute intervals.
            // Averages need to be used with above workaround for now.
            // Response metrics = monitoringClient.Metrics.List(resourceUri: resourceId, metricnames: "ActiveMessages", odataQuery: odataFilterMetrics, timespan: timeSpan, aggregation: "Total", interval: TimeSpan.FromMinutes(5));
            // Console.WriteLine(JsonConvert.SerializeObject(metrics, Newtonsoft.Json.Formatting.Indented));

            // Use this to get different time spans and intervals than 1 minute and e.g. observe averages. Note that averages are given on a partition level right now.
            // This means: Standard with partitioning the average needs to be multiplied by 16. Premium with partitioning (currently always enabled) multiplied 2.
            // The timespan is the concatenation of the start and end date/times separated by "/"
            string startDate = DateTime.Now.AddHours(-1).ToString("o");
            //string startDate = DateTime.Now.AddMinutes(-1).ToString("o");
            string endDate = DateTime.Now.ToString("o");
            string timeSpan = startDate + "/" + endDate;
            ODataQuery<MetadataValue> odataFilterMetrics = new ODataQuery<MetadataValue>($"EntityName eq '{entityName}'");

            // odataFilterMetrics = null will give everything on NS.
            var metrics = monitoringClient.Metrics.List(resourceId, odataFilterMetrics, timeSpan, TimeSpan.FromMinutes(5), metricName, aggregation);
            EnumerateMetrics(metrics, resourceId, entityName, multiplier);

            Console.WriteLine("Press any key to exit.");
            Console.ReadLine();
        }       
        private static void EnumerateMetrics(Response metrics, string armId, string entityName, int multiplier, int maxRecords = 60)
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
                            data.Average*multiplier);
                    }
                }
                numRecords++;
                if (numRecords >= maxRecords)
                {
                    break;
                }
            }
        }

        private static int retMultiplier(ServiceBusManagementClient serviceBusClient, string aggregation, string resourceGroup, string namespaceName, string entityName)
        {
            INamespacesOperations SKUlevel = serviceBusClient.Namespaces;
            var SKU = SKUlevel.Get(resourceGroup, namespaceName);
            var nsSKU = SKU.Sku.Name;

            SBQueue queueData = serviceBusClient.Queues.Get(resourceGroup, namespaceName, entityName);
            bool? PartitioningEnabled = queueData.EnablePartitioning;

            int multiplier = 1;

            if (nsSKU == SkuName.Premium && PartitioningEnabled == true)
            {
                multiplier = 2;
            }
            else if ((nsSKU == SkuName.Standard || nsSKU == SkuName.Basic) && PartitioningEnabled == true)
            {
                multiplier = 16;
            }

            if (aggregation != "Average")
            {
                multiplier = 1;
            }

            return multiplier;
        }
    }
}
