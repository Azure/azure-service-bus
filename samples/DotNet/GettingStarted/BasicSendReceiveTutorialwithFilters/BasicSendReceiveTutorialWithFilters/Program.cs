using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BasicSendReceiveTutorialWithFilters
{
    class Program
    {

        string ServiceBusConnectionString = "";
        string TopicName = "";

        static string[] Subscriptions = {"S1","S2","S3"};
        static IDictionary<string, string[]> SubscriptionFilters = new Dictionary<string, string[]> {
            { "S1", new string[] { "StoreId IN('Store1', 'Store2', 'Store3')", "StoreId = 'Store4'"} },
            { "S2", new string[] { "sys.To IN ('Store5','Store6','Store7') OR StoreId = 'Store8'" } },
            { "S3", new string[] { "sys.To NOT IN ('Store1','Store2','Store3','Store4','Store5','Store6','Store7','Store8') OR StoreId NOT IN ('Store1','Store2','Store3','Store4','Store5','Store6','Store7','Store8')" } }
        };
        static IDictionary<string, string[]> SubscriptionActions = new Dictionary<string, string[]> {            
            { "S3", new string[] { "SET sys.Label = 'SalesEvent'" } }
        };
        static string[] Store = {"Store1","Store2","Store3","Store4","Store5","Store6","Store7","Store8","Store9","Store10"};
        static string SysField = "sys.To";
        static string CustomField = "StoreId";    
        static int NrOfMessagesPerStore = 1; // Send at least 1.

        public static Program StartProgram(string ServiceBusConnectionString, string TopicName)
        {
            Program P = new Program
            {
                ServiceBusConnectionString = ServiceBusConnectionString,
                TopicName = TopicName
            };

            return P;
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
            Console.WriteLine("[5] Receive messages.");

            Char key = Console.ReadKey(true).KeyChar;
            String keyPressed = key.ToString().ToUpper();

            switch (keyPressed)
            {
                case "1":
                    await RemoveDefaultFilters(); 
                    break;
                case "2":
                    await CreateCustomFilters();
                    break;
                case "3":
                    await CleanUpCustomFilters();
                    break;
                case "4":
                    await SendMessages();
                    break;
                case "5":
                    await ReceiveMessages();
                    break;
                default:
                    Console.WriteLine("Unknown command, press enter to exit");
                    Console.ReadLine();
                    break;
            }
        }

        private async Task RemoveDefaultFilters()
        {
            foreach(var subscription in Subscriptions)
            {
                try
                {
                    SubscriptionClient s = new SubscriptionClient(ServiceBusConnectionString, TopicName, subscription);
                    await s.RemoveRuleAsync(RuleDescription.DefaultRuleName);
                    Console.WriteLine($"Default filter for {subscription} has been removed.");
                    await s.CloseAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }                
            }
            Console.WriteLine("All default Rules have been removed.");

            await PresentMenu();
        }

        private async Task CreateCustomFilters()
        {
            for (int i = 1; i <= Subscriptions.Length; i++)
            {
                try
                {
                    SubscriptionClient s = new SubscriptionClient(ServiceBusConnectionString, TopicName, Subscriptions[i]);
                    string[] filters = SubscriptionFilters[Subscriptions[i]];
                    int count = 0;
                    foreach (var myFilter in filters)
                    {
                        count++;
                        await s.AddRuleAsync(new RuleDescription
                        {
                            Filter = new SqlFilter(myFilter),                            
                            Name = "MyRule" + count.ToString()
                        });
                    }

                    string[] actions = SubscriptionActions[Subscriptions[i]];
                    count = 0;
                    foreach (var myFilter in filters)
                    {
                        await s.AddRuleAsync(new RuleDescription
                        {
                            Action = new SqlRuleAction(myFilter),
                            Name = "MyAction" + count.ToString()
                        });
                    }

                    Console.WriteLine($"Filter for {Subscriptions[i]} has been created.");
                }                
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }                
            }

            Console.WriteLine("All filters have been created.");

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
            Console.WriteLine("All default filters have been removed.");

            await PresentMenu();
        }

        private async Task SendMessages()
        {
            try
            {
                TopicClient tc = new TopicClient(ServiceBusConnectionString, TopicName);

                var taskList = new List<Task>();
                for (int i = 0; i <= Store.Length; i++)
                {
                    taskList.Add(SendItems(tc,Store[i]));
                }

                Task.WaitAll(taskList.ToArray());
                await tc.CloseAsync();
            } 
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            Console.WriteLine("\nAll messages sent.\n");

            await PresentMenu();
        }   
        
        private async Task SendItems(TopicClient tc, string store)
        {
            for (int i = 0; i < NrOfMessagesPerStore; i++)
            {
                Random r = new Random();
                Item item = new Item(r.Next(5), r.Next(5), r.Next(5));
                Message message = new Message(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(item))) {
                    To = store                     
                };
                message.UserProperties.Add("StoreId",store);
                message.UserProperties.Add("Price", item.getPrice().ToString());
                message.UserProperties.Add("Color", item.getColor());
                message.UserProperties.Add("Category", item.getItemCategory());

                await tc.SendAsync(message);
                Console.WriteLine("Sent item to Store {0}. Price={1}, Color={2}, Category={3}\n", store, item.getPrice().ToString(), item.getColor(), item.getItemCategory()); ;
            }
        }

        private async Task ReceiveMessages()
        {
            foreach(var subs in Subscriptions)
            {
                SubscriptionClient sc = new SubscriptionClient(ServiceBusConnectionString, TopicName, subs);

                var messageHandlerOptions = new MessageHandlerOptions(ExceptionReceivedHandler)
                {
                    MaxConcurrentCalls = 10,
                    AutoComplete = true
                };

                sc.RegisterMessageHandler(ProcessMessagesAsync,messageHandlerOptions);                
            }           
        }

        private Task ExceptionReceivedHandler(ExceptionReceivedEventArgs exceptionReceivedEventArgs)
        {
            Console.WriteLine($"Message handler encountered an exception {exceptionReceivedEventArgs.Exception}.");
            var context = exceptionReceivedEventArgs.ExceptionReceivedContext;
            Console.WriteLine("Exception context for troubleshooting:");
            Console.WriteLine($"- Endpoint: {context.Endpoint}");
            Console.WriteLine($"- Entity Path: {context.EntityPath}");
            Console.WriteLine($"- Executing Action: {context.Action}");
            return Task.CompletedTask;
        }

        private async Task ProcessMessagesAsync(Message message, CancellationToken token)
        {
            try
            {                
                Item item = JsonConvert.DeserializeObject<Item>(Encoding.UTF8.GetString(message.Body));
                IDictionary<String, Object> myUserProperties = message.UserProperties;
                Console.WriteLine("StoreId={0}\n", myUserProperties["StoreId"].ToString());

                if (message.Label != null)
                    Console.WriteLine("Label={0}\n", message.Label);

                Console.WriteLine("Item data. Price={0}, Color={0}, Category={0}\n", item.getPrice(), item.getColor(), item.getItemCategory());
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
