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
    class SendReceive
    {
        public string ServiceBusConnectionString;
        public string TopicName;
        public string[] Subscriptions;
        public string[] Store;
        public int NrOfMessagesPerStore;

        public async Task SendMessages()
        {
            try
            {
                TopicClient tc = new TopicClient(ServiceBusConnectionString, TopicName);

                var taskList = new List<Task>();
                for (int i = 0; i < Store.Length; i++)
                {
                    taskList.Add(SendItems(tc, Store[i]));
                }

                Task.WaitAll(taskList.ToArray());
                await tc.CloseAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            Console.WriteLine("\nAll messages sent.\n");            
        }

        private async Task SendItems(TopicClient tc, string store)
        {
            for (int i = 0; i < NrOfMessagesPerStore; i++)
            {
                Random r = new Random();
                Item item = new Item(r.Next(5), r.Next(5), r.Next(5));
                Message message = new Message(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(item)))
                {
                    To = store
                };
                message.UserProperties.Add("StoreId", store);
                message.UserProperties.Add("Price", item.getPrice().ToString());
                message.UserProperties.Add("Color", item.getColor());
                message.UserProperties.Add("Category", item.getItemCategory());

                await tc.SendAsync(message);
                Console.WriteLine("Sent item to Store {0}. Price={1}, Color={2}, Category={3}", store, item.getPrice().ToString(), item.getColor(), item.getItemCategory()); ;
            }
        }

        public async Task Receive()
        {
            var taskList = new List<Task>();
            for (int i = 0; i < Subscriptions.Length; i++)
            {
                taskList.Add(this.ReceiveMessages(Subscriptions[i]));
            }

            Task.WaitAll(taskList.ToArray());            
        }

        private async Task ReceiveMessages(string subscription)
        {
            var receiver = new MessageReceiver(ServiceBusConnectionString, TopicName + "/Subscriptions/" + subscription, ReceiveMode.PeekLock, RetryPolicy.Default, 100);
            
                while (true)
                {
                    try
                    {
                        Message message = await receiver.ReceiveAsync(TimeSpan.FromSeconds(2));
                        if (message != null)
                        {
                            lock (Console.Out)
                            {
                                Item item = JsonConvert.DeserializeObject<Item>(Encoding.UTF8.GetString(message.Body));
                                IDictionary<String, Object> myUserProperties = message.UserProperties;
                                Console.WriteLine("StoreId={0}", myUserProperties["StoreId"].ToString());

                                if (message.Label != null)
                                    Console.WriteLine("Label={0}", message.Label);

                                Console.WriteLine("Item data: Price={0}, Color={1}, Category={2}", item.getPrice(), item.getColor(), item.getItemCategory());
                            }
                            await receiver.CompleteAsync(message.SystemProperties.LockToken);
                        }
                        else
                        {
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }
                }            

            await receiver.CloseAsync();
        }
    }
}

