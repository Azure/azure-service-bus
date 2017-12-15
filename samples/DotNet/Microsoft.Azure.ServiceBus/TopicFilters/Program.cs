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

namespace TopicFilters
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Core;
    using Newtonsoft.Json;
    
    public class Program : MessagingSamples.Sample
    {
        const string TopicName = "TopicFilterSampleTopic";
        const string SubscriptionAllMessages = "AllOrders";
        const string SubscriptionColorBlueSize10Orders = "ColorBlueSize10Orders";
        const string SubscriptionColorRed = "ColorRed";
        const string SubscriptionHighPriorityOrders = "HighPriorityOrders";

        public async Task Run(string connectionString)
        {
            // This sample demonstrates how to use advanced filters with ServiceBus topics and subscriptions.
            // The sample creates a topic and 3 subscriptions with different filter definitions.
            // Each receiver will receive matching messages depending on the filter associated with a subscription.

            await this.SendAndReceiveTestsAsync(connectionString);
        }

        async Task SendAndReceiveTestsAsync(string connectionString)
        {
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
            // Create client for the topic.
            var topicClient = new TopicClient(connectionString, TopicName);

            // Create a message sender from the topic client.

            Console.WriteLine("\nSending orders to topic.");

            // Now we can start sending orders.
            await Task.WhenAll(
                SendOrder(topicClient, new Order()),
                SendOrder(topicClient, new Order { Color = "blue", Quantity = 5, Priority = "low" }),
                SendOrder(topicClient, new Order { Color = "red", Quantity = 10, Priority = "high" }),
                SendOrder(topicClient, new Order { Color = "yellow", Quantity = 5, Priority = "low" }),
                SendOrder(topicClient, new Order { Color = "blue", Quantity = 10, Priority = "low" }),
                SendOrder(topicClient, new Order { Color = "blue", Quantity = 5, Priority = "high" }),
                SendOrder(topicClient, new Order { Color = "blue", Quantity = 10, Priority = "low" }),
                SendOrder(topicClient, new Order { Color = "red", Quantity = 5, Priority = "low" }),
                SendOrder(topicClient, new Order { Color = "red", Quantity = 10, Priority = "low" }),
                SendOrder(topicClient, new Order { Color = "red", Quantity = 5, Priority = "low" }),
                SendOrder(topicClient, new Order { Color = "yellow", Quantity = 10, Priority = "high" }),
                SendOrder(topicClient, new Order { Color = "yellow", Quantity = 5, Priority = "low" }),
                SendOrder(topicClient, new Order { Color = "yellow", Quantity = 10, Priority = "low" })
                );

            Console.WriteLine("All messages sent.");
        }

        async Task SendOrder(TopicClient topicClient, Order order)
        {
            var message = new Message(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(order)))
            {
                CorrelationId = order.Priority,
                Label = order.Color,
                UserProperties =
                {
                    { "color", order.Color },
                    { "quantity", order.Quantity },
                    { "priority", order.Priority }
                }
            };
            await topicClient.SendAsync(message);

            Console.WriteLine("Sent order with Color={0}, Quantity={1}, Priority={2}", order.Color, order.Quantity, order.Priority);
        }

        async Task ReceiveAllMessageFromSubscription(string connectionString, string subsName)
        {
            var receivedMessages = 0;

            // Create subscription client.
            var subscriptionClient = new Microsoft.Azure.ServiceBus.Core.MessageReceiver(connectionString, EntityNameHelper.FormatSubscriptionPath(TopicName, subsName), ReceiveMode.ReceiveAndDelete);

            // Create a receiver from the subscription client and receive all messages.
            Console.WriteLine("\nReceiving messages from subscription {0}.", subsName);

            while (true)
            {
                var receivedMessage = await subscriptionClient.ReceiveAsync(TimeSpan.FromSeconds(10));
                if (receivedMessage != null)
                {
                    foreach (var prop in receivedMessage.UserProperties)
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

       public static int Main(string[] args)
        {
            try
            {
                var app = new Program();
                app.RunSample(args, app.Run);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return 1;
            }
            return 0;
        }
    }
}