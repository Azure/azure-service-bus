//   
//   Copyright © Microsoft Corporation, All Rights Reserved
// 
//   Licensed under the Apache License, Version 2.0 (the "License"); 
//   you may not use this file except in compliance with the License. 
//   You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0 
// 
//   THIS CODE IS PROVIDED *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
//   OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION
//   ANY IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A
//   PARTICULAR PURPOSE, MERCHANTABILITY OR NON-INFRINGEMENT.
// 
//   See the Apache License, Version 2.0 for the specific language
//   governing permissions and limitations under the License. 

namespace SendAndReceiveMessages
{
    using System;
    using System.Text;
    using System.Threading.Tasks;
    using Azure.Messaging.ServiceBus;
    using Newtonsoft.Json;
    
    public class Program 
    {
        const string TopicName = "TopicFilterSampleTopic";
        const string SubscriptionAllMessages = "AllOrders";
        const string SubscriptionColorBlueSize10Orders = "ColorBlueSize10Orders";
        const string SubscriptionColorRed = "ColorRed";
        const string SubscriptionHighPriorityOrders = "HighPriorityRedOrders";

        // connection string to your Service Bus namespace
        static string connectionString = "<SERVICE BUS NAMESPACE - CONNECTION STRING>";

        // the client that owns the connection and can be used to create senders and receivers
        static ServiceBusClient client;

        // the sender used to publish messages to the topic
        static ServiceBusSender sender;

        // the receiver used to receive messages from the subscription 
        static ServiceBusReceiver receiver;

        public async Task SendAndReceiveTestsAsync(string connectionString)
        {
            // This sample demonstrates how to use advanced filters with ServiceBus topics and subscriptions.
            // The sample creates a topic and 3 subscriptions with different filter definitions.
            // Each receiver will receive matching messages depending on the filter associated with a subscription.

            // Send sample messages.
            await this.SendMessagesToTopicAsync(connectionString);

            // Receive messages from subscriptions.
            await this.ReceiveAllMessageFromSubscription(connectionString, SubscriptionAllMessages);
            await this.ReceiveAllMessageFromSubscription(connectionString, SubscriptionColorBlueSize10Orders);
            await this.ReceiveAllMessageFromSubscription(connectionString, SubscriptionColorRed);
            await this.ReceiveAllMessageFromSubscription(connectionString, SubscriptionHighPriorityOrders);
        }


        async Task SendMessagesToTopicAsync(string connectionString)
        {
            // Create the clients that we'll use for sending and processing messages.
            client = new ServiceBusClient(connectionString);
            sender = client.CreateSender(TopicName);

            Console.WriteLine("\nSending orders to topic.");

            // Now we can start sending orders.
            await Task.WhenAll(
                SendOrder(sender, new Order()),
                SendOrder(sender, new Order { Color = "blue", Quantity = 5, Priority = "low" }),
                SendOrder(sender, new Order { Color = "red", Quantity = 10, Priority = "high" }),
                SendOrder(sender, new Order { Color = "yellow", Quantity = 5, Priority = "low" }),
                SendOrder(sender, new Order { Color = "blue", Quantity = 10, Priority = "low" }),
                SendOrder(sender, new Order { Color = "blue", Quantity = 5, Priority = "high" }),
                SendOrder(sender, new Order { Color = "blue", Quantity = 10, Priority = "low" }),
                SendOrder(sender, new Order { Color = "red", Quantity = 5, Priority = "low" }),
                SendOrder(sender, new Order { Color = "red", Quantity = 10, Priority = "low" }),
                SendOrder(sender, new Order { Color = "red", Quantity = 5, Priority = "low" }),
                SendOrder(sender, new Order { Color = "yellow", Quantity = 10, Priority = "high" }),
                SendOrder(sender, new Order { Color = "yellow", Quantity = 5, Priority = "low" }),
                SendOrder(sender, new Order { Color = "yellow", Quantity = 10, Priority = "low" })
                );

            Console.WriteLine("All messages sent.");
        }

        async Task SendOrder(ServiceBusSender sender, Order order)
        {
            var message = new ServiceBusMessage(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(order)))
            {
                CorrelationId = order.Priority,
                Subject = order.Color,
                ApplicationProperties =
                {
                    { "color", order.Color },
                    { "quantity", order.Quantity },
                    { "priority", order.Priority }
                }
            };
            await sender.SendMessageAsync(message);

            Console.WriteLine("Sent order with Color={0}, Quantity={1}, Priority={2}", order.Color, order.Quantity, order.Priority);
        }

        async Task ReceiveAllMessageFromSubscription(string connectionString, string subsName)
        {
            var receivedMessages = 0;

            receiver = client.CreateReceiver(TopicName, subsName, new ServiceBusReceiverOptions() { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete } );
            
            // Create a receiver from the subscription client and receive all messages.
            Console.WriteLine("\nReceiving messages from subscription {0}.", subsName);

            while (true)
            {
                var receivedMessage = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10));
                if (receivedMessage != null)
                {
                    foreach (var prop in receivedMessage.ApplicationProperties)
                    {
                        Console.Write("{0}={1},", prop.Key, prop.Value);
                    }
                    Console.WriteLine("CorrelationId={0}", receivedMessage.CorrelationId);
                    receivedMessages++;
                }
                else
                {
                    // No more messages to receive.
                    break;
                }
            }
            Console.WriteLine("Received {0} messages from subscription {1}.", receivedMessages, subsName);
        }

       public static async Task Main()
        {
            try
            {
                Program app = new Program();
                await app.SendAndReceiveTestsAsync(connectionString);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }
}