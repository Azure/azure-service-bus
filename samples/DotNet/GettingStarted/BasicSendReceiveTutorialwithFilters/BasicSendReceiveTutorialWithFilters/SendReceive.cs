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

                await Task.WhenAll(taskList);
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

                Message message = item.AsMessage();
                message.To = store;
                message.UserProperties.Add("StoreId", store);
                message.UserProperties.Add("Price", item.getPrice().ToString());
                message.UserProperties.Add("Color", item.getColor());
                message.UserProperties.Add("Category", item.getItemCategory());

                await tc.SendAsync(message);
                Console.WriteLine($"Sent item to Store {store}. Price={item.getPrice()}, Color={item.getColor()}, Category={item.getItemCategory()}"); ;
            }
        }

        public async Task Receive()
        {
            var taskList = new List<Task>();
            for (var i = 0; i < Subscriptions.Length; i++)
            {
                taskList.Add(this.ReceiveMessages(Subscriptions[i]));
            }

            await Task.WhenAll(taskList);
        }

        private async Task ReceiveMessages(string subscription)
        {
            var entityPath = EntityNameHelper.FormatSubscriptionPath(TopicName, subscription);
            var receiver = new MessageReceiver(ServiceBusConnectionString, entityPath, ReceiveMode.PeekLock, RetryPolicy.Default, 100);

            while (true)
            {
                try
                {
                    IList<Message> messages = await receiver.ReceiveAsync(10, TimeSpan.FromSeconds(2));
                    if (messages != null)
                    {
                        foreach (var message in messages)
                        {
                            lock (Console.Out)
                            {
                                Item item = message.As<Item>();
                                IDictionary<string, object> myUserProperties = message.UserProperties;
                                Console.WriteLine($"StoreId={myUserProperties["StoreId"]}");

                                if (message.Label != null)
                                {
                                    Console.WriteLine($"Label={message.Label}");
                                }

                                Console.WriteLine(
                                    $"Item data: Price={item.getPrice()}, Color={item.getColor()}, Category={item.getItemCategory()}");
                            }

                            await receiver.CompleteAsync(message.SystemProperties.LockToken);
                        }
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

