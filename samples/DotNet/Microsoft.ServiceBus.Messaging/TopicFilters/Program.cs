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

namespace MessagingSamples
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;
    using Newtonsoft.Json;

    class Program : IDynamicSample
    {
        const string TopicName = "TopicFilterSampleTopic";
        const string SubscriptionAllMessages = "AllOrders";
        const string SubscriptionColorBlueSize10Orders = "ColorBlueSize10Orders";
        const string SubscriptionColorRed = "ColorRed";
        const string SubscriptionHighPriorityOrders = "HighPriorityOrders";

        public async Task Run(string namespaceAddress, string manageToken)
        {
            // This sample demonstrates how to use advanced filters with ServiceBus topics and subscriptions.
            // The sample creates a topic and 3 subscriptions with different filter definitions.
            // Each receiver will receive matching messages depending on the filter associated with a subscription.

            // NOTE:
            // This is primarily an example illustrating the management features related to setting up 
            // Service Bus subscriptions. It is DISCOURAGED for applications to routinely set up and 
            // tear down topics and subscriptions as a part of regular message processing. Managing 
            // topics and subscriptions is a system configuration operation. 

            // Create messaging factory and ServiceBus namespace client.
            var sharedAccessSignatureTokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(manageToken);
            var namespaceManager = new NamespaceManager(namespaceAddress, sharedAccessSignatureTokenProvider);

            Console.WriteLine("\nCreating a topic and 3 subscriptions.");

            // Create a topic and several subscriptions; clean house ahead of time
            if (await namespaceManager.TopicExistsAsync(TopicName))
            {
                await namespaceManager.DeleteTopicAsync(TopicName);
            }

            var topicDescription = await namespaceManager.CreateTopicAsync(TopicName);
            Console.WriteLine("Topic created.");

            // Create a subscription for all messages sent to topic.
            await namespaceManager.CreateSubscriptionAsync(topicDescription.Path, SubscriptionAllMessages, new TrueFilter());
            Console.WriteLine("Subscription {0} added with filter definition set to TrueFilter.", SubscriptionAllMessages);
            

            // Create a subscription that'll receive all orders which have color "blue" and quantity 10.

            await namespaceManager.CreateSubscriptionAsync(
                topicDescription.Path,
                SubscriptionColorBlueSize10Orders,
                new SqlFilter("color = 'blue' AND quantity = 10"));
            Console.WriteLine(
                "Subscription {0} added with filter definition \"color = 'blue' AND quantity = 10\".",
                 SubscriptionColorBlueSize10Orders);

            // Create a subscription that'll receive all orders which have color "red"
            await namespaceManager.CreateSubscriptionAsync(
                topicDescription.Path,
                SubscriptionColorRed,
                new RuleDescription
                {
                    Name = "RedRule",
                    Filter = new SqlFilter("color = 'red'"),
                    Action = new SqlRuleAction(
                        "SET quantity = quantity / 2;" +
                        "REMOVE priority;" +
                        "SET sys.CorrelationId = 'low';")
                });
            Console.WriteLine("Subscription {0} added with filter definition \"color = 'red'\" and action definition.", SubscriptionColorRed);
     
            // Create a subscription that'll receive all high priority orders.
            namespaceManager.CreateSubscription(topicDescription.Path, SubscriptionHighPriorityOrders, 
                new CorrelationFilter { Label = "red", CorrelationId = "high"});
            Console.WriteLine("Subscription {0} added with correlation filter definition \"high\".", SubscriptionHighPriorityOrders);
     
            Console.WriteLine("Create completed.");


            await this.SendAndReceiveTestsAsync(namespaceAddress, sharedAccessSignatureTokenProvider);


            Console.WriteLine("Press [Enter] to quit...");
            Console.ReadLine();

            Console.WriteLine("\nDeleting topic and subscriptions from previous run if any.");

            try
            {
                namespaceManager.DeleteTopic(TopicName);
            }
            catch (MessagingEntityNotFoundException)
            {
                Console.WriteLine("No topic found to delete.");
            }

            Console.WriteLine("Delete completed.");
        }

        async Task SendAndReceiveTestsAsync(string namespaceAddress, TokenProvider sharedAccessSignatureTokenProvider)
        {
            var messagingFactory = MessagingFactory.Create(namespaceAddress, sharedAccessSignatureTokenProvider);

            // Send sample messages.
            await this.SendMessagesToTopicAsync(messagingFactory);

            // Receive messages from subscriptions.
            await this.ReceiveAllMessageFromSubscription(messagingFactory, SubscriptionAllMessages);
            await this.ReceiveAllMessageFromSubscription(messagingFactory, SubscriptionColorBlueSize10Orders);
            await this.ReceiveAllMessageFromSubscription(messagingFactory, SubscriptionColorRed);
            await this.ReceiveAllMessageFromSubscription(messagingFactory, SubscriptionHighPriorityOrders);

            messagingFactory.Close();
        }


        async Task SendMessagesToTopicAsync(MessagingFactory messagingFactory)
        {
            // Create client for the topic.
            var topicClient = messagingFactory.CreateTopicClient(TopicName);

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
            var message = new BrokeredMessage(new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(order))))
            {
                CorrelationId = order.Priority,
                Label = order.Color,
                Properties =
                {
                    { "color", order.Color },
                    { "quantity", order.Quantity },
                    { "priority", order.Priority }
                }
            };
            await topicClient.SendAsync(message);

            Console.WriteLine("Sent order with Color={0}, Quantity={1}, Priority={2}", order.Color, order.Quantity, order.Priority);
        }

        async Task ReceiveAllMessageFromSubscription(MessagingFactory messagingFactory, string subsName)
        {
            var receivedMessages = 0;

            // Create subscription client.
            var subscriptionClient =
                messagingFactory.CreateSubscriptionClient(TopicName, subsName, ReceiveMode.ReceiveAndDelete);

            // Create a receiver from the subscription client and receive all messages.
            Console.WriteLine("\nReceiving messages from subscription {0}.", subsName);

            while (true)
            {
                var receivedMessage = await subscriptionClient.ReceiveAsync(TimeSpan.Zero);
                if (receivedMessage != null)
                {
                    foreach (var prop in receivedMessage.Properties)
                    {
                        Console.Write("{0}={1},", prop.Key, prop.Value);
                    }
                    Console.WriteLine("CorrelationId={0}", receivedMessage.CorrelationId);

                    receivedMessage.Dispose();
                    receivedMessages++;
                }
                else
                {
                    // No more messages to receive.
                    break;
                }
            }
            Console.WriteLine("Received {0} messages from subscription {1}.", receivedMessages, subscriptionClient.Name);
        }
    }
}