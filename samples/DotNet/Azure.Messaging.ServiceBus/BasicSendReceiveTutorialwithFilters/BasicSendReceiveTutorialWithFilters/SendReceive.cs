using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Azure.Messaging.ServiceBus;
using System.Linq;
using Azure.Identity;

namespace BasicSendReceiveTutorialWithFilters
{
    class SendReceive
    {
        public string FullyQualifiedNamespace;
        public string TopicName;
        public string[] Subscriptions;
        public string[] Store;
        public int NrOfMessagesPerStore;

        public async Task SendMessages()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await using var client = new ServiceBusClient(FullyQualifiedNamespace, new DefaultAzureCredential());
            await SendMessages(client, timeout.Token);
        }

        internal async Task SendMessages(ServiceBusClient client, CancellationToken token)
        {
            var taskList = new List<Task>();
            for (int i = 0; i < Store.Length; i++)
            {
                taskList.Add(SendItems(client, Store[i], token));
            }
            await Task.WhenAll(taskList);
            Console.WriteLine("\nAll messages sent.\n");
        }

        private async Task SendItems(ServiceBusClient client, string store, CancellationToken token)
        {
            await using ServiceBusSender tc = client.CreateSender(TopicName);

            for (int i = 0; i < NrOfMessagesPerStore; i++)
            {
                Random r = new Random();
                Item item = new Item(r.Next(5), r.Next(5), r.Next(5));

                // Note the extension class which is serializing an deserializing messages
                ServiceBusMessage message = item.AsMessage();
                message.To = store;
                message.ApplicationProperties.Add("StoreId", store);
                message.ApplicationProperties.Add("Price", item.GetPrice().ToString());
                message.ApplicationProperties.Add("Color", item.GetColor());
                message.ApplicationProperties.Add("Category", item.GetItemCategory());

                await tc.SendMessageAsync(message, token);
                Console.WriteLine($"Sent item to Store {store}. Price={item.GetPrice()}, Color={item.GetColor()}, Category={item.GetItemCategory()}"); ;
            }
        }

        public async Task Receive()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await using var client = new ServiceBusClient(FullyQualifiedNamespace, new DefaultAzureCredential());
            await Receive(client, timeout.Token);
        }

        internal async Task Receive(ServiceBusClient client, CancellationToken token)
        {
            var taskList = new List<Task>();
            for (var i = 0; i < Subscriptions.Length; i++)
            {
                taskList.Add(this.ReceiveMessages(client, Subscriptions[i], token));
            }

            await Task.WhenAll(taskList);
        }

        private async Task ReceiveMessages(ServiceBusClient client, string subscription, CancellationToken token)
        {
            await using ServiceBusReceiver receiver = client.CreateReceiver(TopicName, subscription);

            while (true)
            {
                var messages = await receiver.ReceiveMessagesAsync(100, TimeSpan.FromSeconds(5), token);
                if (messages.Count == 0)
                    break;
                foreach (var message in messages)
                {
                    token.ThrowIfCancellationRequested();
                    lock (Console.Out)
                    {
                        Item item = message.As<Item>();
                        Console.WriteLine($"StoreId={message.ApplicationProperties["StoreId"]}");
                        if (message.Subject != null)
                            Console.WriteLine($"Subject={message.Subject}");
                        Console.WriteLine($"Item data: Price={item.GetPrice()}, Color={item.GetColor()}, Category={item.GetItemCategory()}");
                    }
                    await receiver.CompleteMessageAsync(message, token);
                }
            }
        }
    }
}
