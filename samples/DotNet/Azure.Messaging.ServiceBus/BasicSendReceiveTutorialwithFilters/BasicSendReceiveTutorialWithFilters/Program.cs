using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Azure.Identity;

namespace BasicSendReceiveTutorialWithFilters
{
    class Program
    {
        string FullyQualifiedNamespace;
        string TopicName;
        readonly string RulePrefix = $"sample-{Guid.NewGuid():N}";
        readonly List<(string Subscription, string Rule)> CreatedRules = new();

        static readonly string[] Subscriptions = { "S1", "S2", "S3" };
        static readonly IDictionary<string, string[]> SubscriptionFilters = new Dictionary<string, string[]> {
            { "S1", new[] { "StoreId IN('Store1', 'Store2', 'Store3')", "StoreId = 'Store4'"} },
            { "S2", new[] { "sys.To IN ('Store5','Store6','Store7') OR StoreId = 'Store8'" } },
            { "S3", new[] { "sys.To NOT IN ('Store1','Store2','Store3','Store4','Store5','Store6','Store7','Store8') OR StoreId NOT IN ('Store1','Store2','Store3','Store4','Store5','Store6','Store7','Store8')" } }
        };
        // You can have only have one action per rule and this sample code supports only one action for the first filter which is used to create the first rule. 
        static readonly IDictionary<string, string> SubscriptionAction = new Dictionary<string, string> {
            { "S1", "" },
            { "S2", "" },
            { "S3", "SET sys.Label = 'SalesEvent'"  }
        };
        static readonly string[] Store = { "Store1", "Store2", "Store3", "Store4", "Store5", "Store6", "Store7", "Store8", "Store9", "Store10" };
        //static string SysField = "sys.To";
        //static string CustomField = "StoreId";
        static readonly int NrOfMessagesPerStore = 1; // Send at least 1.

        static Program StartProgram(string FullyQualifiedNamespace, string TopicName)
        {
            var program = new Program
            {
                FullyQualifiedNamespace = FullyQualifiedNamespace,
                TopicName = TopicName
            };

            return program;
        }
        static void Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--self-test")
            {
                OfflineChecks.RunAsync().GetAwaiter().GetResult();
                return;
            }
            string FullyQualifiedNamespace = Environment.GetEnvironmentVariable("SERVICEBUS_FULLY_QUALIFIED_NAMESPACE") ?? "";
            string TopicName = Environment.GetEnvironmentVariable("SERVICEBUS_TOPIC") ?? "";

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-FullyQualifiedNamespace")
                {
                    if (++i >= args.Length)
                        throw new ArgumentException("-FullyQualifiedNamespace requires a value.");
                    FullyQualifiedNamespace = args[i];
                }
                else if (args[i] == "-TopicName")
                {
                    if (++i >= args.Length)
                        throw new ArgumentException("-TopicName requires a value.");
                    TopicName = args[i];
                }
                else
                {
                    throw new ArgumentException($"Unknown argument: {args[i]}. Use -FullyQualifiedNamespace and -TopicName.");
                }
            }

            if (!string.IsNullOrWhiteSpace(FullyQualifiedNamespace) && !string.IsNullOrWhiteSpace(TopicName))
            {
                Program P = StartProgram(FullyQualifiedNamespace, TopicName);
                P.PresentMenu().GetAwaiter().GetResult();
            }
            else
            {
                Console.Error.WriteLine("Set SERVICEBUS_FULLY_QUALIFIED_NAMESPACE (example.servicebus.windows.net) and SERVICEBUS_TOPIC, or specify -FullyQualifiedNamespace and -TopicName.");
                Environment.ExitCode = 1;
            }
        }

        public async Task PresentMenu()
        {
            Console.WriteLine("Choose an action:");
            Console.WriteLine("[1] Remove the default filters which accept all messages. DO THIS ALWAYS FIRST.");
            Console.WriteLine("[2] Create your own filters. DO THIS SECOND.");
            Console.WriteLine("[3] Remove only filters created by this running process. OPTIONAL");
            Console.WriteLine("[4] Send messages.");
            Console.WriteLine("[5] Receive messages.\n");

            Char key = Console.ReadKey(true).KeyChar;
            String keyPressed = key.ToString().ToUpper();

            switch (keyPressed)
            {
                case "1":
                    // This will remove the default filters, which you need to do always first
                    await RemoveDefaultFilters();
                    break;
                case "2":
                    // This will create the customer filters
                    await CreateCustomFilters();
                    break;
                case "3":
                    // Optionally with this you can remove the custom filters.
                    await CleanUpCustomFilters();
                    break;
                case "4":
                    // Use this to Send messages.
                    await SendMessages();
                    break;
                case "5":
                    // Use this to Receive messages.
                    await Receive();
                    break;
                default:
                    Console.WriteLine("Exiting.");
                    break;
            }
        }

        private async Task SendMessages()
        {
            SendReceive sr = new SendReceive
            {
                FullyQualifiedNamespace = FullyQualifiedNamespace,
                TopicName = TopicName,
                Subscriptions = Subscriptions,
                Store = Store,
                NrOfMessagesPerStore = NrOfMessagesPerStore
            };

            await sr.SendMessages();

            await PresentMenu();
        }

        private async Task Receive()
        {
            SendReceive sr = new SendReceive
            {
                FullyQualifiedNamespace = FullyQualifiedNamespace,
                TopicName = TopicName,
                Subscriptions = Subscriptions,
                Store = Store,
                NrOfMessagesPerStore = NrOfMessagesPerStore
            };
           
            Console.WriteLine("\nReceiving until the subscriptions are idle, with a two-minute deadline. After receiving, press M for the menu or another key to exit.\n");

            await sr.Receive();

            char key = Console.ReadKey(true).KeyChar;
            string keyPressed = key.ToString().ToUpper();

            switch (keyPressed)
            {
                case "M":
                    await PresentMenu();
                    break;
            }
        }

        private async Task RemoveDefaultFilters()
        {
            Console.WriteLine($"Starting to remove default filters.");

            var client = new ServiceBusAdministrationClient(FullyQualifiedNamespace, new DefaultAzureCredential());
            foreach (var subscription in Subscriptions)
            {
                await client.DeleteRuleAsync(TopicName, subscription, CreateRuleOptions.DefaultRuleName);
                Console.WriteLine($"Default filter for {subscription} has been removed.");
            }
            Console.WriteLine("All default rules have been removed.\n");
            await PresentMenu();
        }

        private async Task CreateCustomFilters()
        {
            var client = new ServiceBusAdministrationClient(FullyQualifiedNamespace, new DefaultAzureCredential());
            for (int i = 0; i < Subscriptions.Length; i++)
            {
                string[] filters = SubscriptionFilters[Subscriptions[i]];
                for (int filterIndex = 0; filterIndex < filters.Length; filterIndex++)
                {
                    if (string.IsNullOrEmpty(filters[filterIndex]))
                        continue;
                    string name = $"{RulePrefix}-{CreatedRules.Count}-{filterIndex}";
                    string action = SubscriptionAction[Subscriptions[i]];
                    var options = new CreateRuleOptions(name, new SqlRuleFilter(filters[filterIndex]));
                    if (filterIndex == 0 && !string.IsNullOrEmpty(action))
                        options.Action = new SqlRuleAction(action);
                    Console.WriteLine($"Creating rule {TopicName}/{Subscriptions[i]}/{name}. If creation fails, inspect this exact name before retrying.");
                    await client.CreateRuleAsync(TopicName, Subscriptions[i], options);
                    CreatedRules.Add((Subscriptions[i], name));
                    Console.WriteLine($"Created rule {name}.");
                }
            }
            Console.WriteLine("All filters and actions have been created.\n");
            await PresentMenu();
        }

        private async Task CleanUpCustomFilters()
        {
            var client = new ServiceBusAdministrationClient(FullyQualifiedNamespace, new DefaultAzureCredential());
            await CleanupRulesAsync(CreatedRules,
                owned => client.DeleteRuleAsync(TopicName, owned.Subscription, owned.Rule));
            Console.WriteLine("All rules recorded as created by this process have been removed. Other rules were not changed.\n");
            await PresentMenu();
        }

        internal static async Task CleanupRulesAsync(List<(string Subscription, string Rule)> createdRules,
            Func<(string Subscription, string Rule), Task> delete)
        {
            var failures = new List<Exception>();
            foreach (var owned in createdRules.ToArray())
            {
                try
                {
                    await delete(owned);
                    createdRules.Remove(owned);
                    Console.WriteLine($"Removed this run's rule {owned.Subscription}/{owned.Rule}.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Could not delete {owned.Subscription}/{owned.Rule}: {ex.Message}");
                    failures.Add(ex);
                }
            }
            if (failures.Count != 0)
                throw new AggregateException("Some owned rules remain; inspect the reported names.", failures);
        }
    }
}
