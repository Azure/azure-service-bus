using Microsoft.Azure.ServiceBus;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace BasicSendReceiveTutorialWithFilters
{
    class Program
    {
        string ServiceBusConnectionString;
        string TopicName;

        static string[] Subscriptions = { "S1", "S2", "S3" };
        static IDictionary<string, string[]> SubscriptionFilters = new Dictionary<string, string[]> {
            { "S1", new[] { "StoreId IN('Store1', 'Store2', 'Store3')", "StoreId = 'Store4'"} },
            { "S2", new[] { "sys.To IN ('Store5','Store6','Store7') OR StoreId = 'Store8'" } },
            { "S3", new[] { "sys.To NOT IN ('Store1','Store2','Store3','Store4','Store5','Store6','Store7','Store8') OR StoreId NOT IN ('Store1','Store2','Store3','Store4','Store5','Store6','Store7','Store8')" } }
        };
        // You can have only have one action per rule and this sample code supports only one action for the first filter which is used to create the first rule. 
        static IDictionary<string, string> SubscriptionAction = new Dictionary<string, string> {
            { "S1", "" },
            { "S2", "" },
            { "S3", "SET sys.Label = 'SalesEvent'"  }
        };
        static string[] Store = { "Store1", "Store2", "Store3", "Store4", "Store5", "Store6", "Store7", "Store8", "Store9", "Store10" };
        static string SysField = "sys.To";
        static string CustomField = "StoreId";
        static int NrOfMessagesPerStore = 1; // Send at least 1.

        static Program StartProgram(string ServiceBusConnectionString, string TopicName)
        {
            var program = new Program
            {
                ServiceBusConnectionString = ServiceBusConnectionString,
                TopicName = TopicName
            };

            return program;
        }
        static void Main(string[] args)
        {
            string ServiceBusConnectionString = "";
            string TopicName = "";

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-ConnectionString")
                {
                    Console.WriteLine($"ConnectionString: {args[i + 1]}");
                    ServiceBusConnectionString = args[i + 1]; // Alternatively enter your connection string here.
                }
                else if (args[i] == "-TopicName")
                {
                    Console.WriteLine($"TopicName: {args[i + 1]}");
                    TopicName = args[i + 1]; // Alternatively enter your queue name here.
                }
            }

            if (ServiceBusConnectionString != "" && TopicName != "")
            {
                Program P = StartProgram(ServiceBusConnectionString, TopicName);
                P.PresentMenu().GetAwaiter().GetResult();
            }
            else
            {
                Console.WriteLine("Specify -Connectionstring and -TopicName to execute the example.");
                Console.ReadKey();
            }
        }

        public async Task PresentMenu()
        {
            Console.WriteLine("Choose an action:");
            Console.WriteLine("[1] Remove the default filters which accept all messages. DO THIS ALWAYS FIRST.");
            Console.WriteLine("[2] Create your own filters. DO THIS SECOND.");
            Console.WriteLine("[3] Remove your own filters. OPTIONAL");
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
                    Console.WriteLine("Unknown command, press enter to exit");
                    Console.ReadLine();
                    break;
            }
        }

        private async Task SendMessages()
        {
            SendReceive sr = new SendReceive
            {
                ServiceBusConnectionString = ServiceBusConnectionString,
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
                ServiceBusConnectionString = ServiceBusConnectionString,
                TopicName = TopicName,
                Subscriptions = Subscriptions,
                Store = Store,
                NrOfMessagesPerStore = NrOfMessagesPerStore
            };
           
            Console.WriteLine("\nReceiveing messages. Press any key to exit once all messages have been received. Alternatively press \"M\" to get to the menu\n");

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

            try
            {
                foreach (var subscription in Subscriptions)
                {
                    SubscriptionClient s = new SubscriptionClient(ServiceBusConnectionString, TopicName, subscription);
                    await s.RemoveRuleAsync(RuleDescription.DefaultRuleName);
                    Console.WriteLine($"Default filter for {subscription} has been removed.");
                    await s.CloseAsync();
                }

                Console.WriteLine("All default Rules have been removed.\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            await PresentMenu();
        }

        private async Task CreateCustomFilters()
        {
            try
            {
                for (int i = 0; i < Subscriptions.Length; i++)
                {
                    SubscriptionClient s = new SubscriptionClient(ServiceBusConnectionString, TopicName, Subscriptions[i]);
                    string[] filters = SubscriptionFilters[Subscriptions[i]];
                    if (filters[0] != "")
                    {
                        int count = 0;
                        foreach (var myFilter in filters)
                        {
                            count++;

                            string action = SubscriptionAction[Subscriptions[i]];
                            if (action != "")
                            {
                                await s.AddRuleAsync(new RuleDescription
                                {
                                    Filter = new SqlFilter(myFilter),
                                    Action = new SqlRuleAction(action),
                                    Name = $"MyRule{count}"
                                });
                            }
                            else
                            {
                                await s.AddRuleAsync(new RuleDescription
                                {
                                    Filter = new SqlFilter(myFilter),
                                    Name = $"MyRule{count}"
                                });
                            }
                        }
                    }

                    Console.WriteLine($"Filters and actions for {Subscriptions[i]} have been created.");
                }

                Console.WriteLine("All filters and actions have been created.\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            await PresentMenu();
        }

        private async Task CleanUpCustomFilters()
        {
            foreach (var subscription in Subscriptions)
            {
                try
                {
                    SubscriptionClient s = new SubscriptionClient(ServiceBusConnectionString, TopicName, subscription);
                    IEnumerable<RuleDescription> rules = await s.GetRulesAsync();
                    foreach (RuleDescription r in rules)
                    {
                        await s.RemoveRuleAsync(r.Name);
                        Console.WriteLine($"Rule {r.Name} has been removed.");
                    }
                    await s.CloseAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
            Console.WriteLine("All default filters have been removed.\n");

            await PresentMenu();
        }
    }
}
