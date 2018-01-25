// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace TopicSubscriptionWithRuleOperationsSample
{
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Core;
    using System;
    using System.Linq;
    using System.Threading.Tasks;

    class Program
    {
        // Connection String for the namespace can be obtained from the Azure portal under the 
        // 'Shared Access policies' section.
        const string ServiceBusConnectionString = "{ServiceBus connection string}";
        const string TopicName = "{Topic Name}";

        // Simply create 4 default subscriptions (no rules specified explicitly) and provide subscription names. 
        // The Rule addition will be done as part of the sample depending on the subscription behavior expected.
        const string allMessagesSubscriptionName = "{Subscription 1 Name}";
        const string sqlFilterOnlySubscriptionName = "{Subscription 2 Name}";
        const string sqlFilterWithActionSubscriptionName = "{Subscription 3 Name}";
        const string correlationFilterSubscriptionName = "{Subscription 4 Name}";

        static ITopicClient topicClient;
        static ISubscriptionClient allMessagessubscriptionClient, sqlFilterOnlySubscriptionClient, sqlFilterWithActionSubscriptionClient, correlationFilterSubscriptionClient;

        static void Main(string[] args)
        {
            MainAsync().GetAwaiter().GetResult();
        }

        static async Task MainAsync()
        {
            topicClient = new TopicClient(ServiceBusConnectionString, TopicName);
            allMessagessubscriptionClient = new SubscriptionClient(ServiceBusConnectionString, TopicName, allMessagesSubscriptionName);
            sqlFilterOnlySubscriptionClient = new SubscriptionClient(ServiceBusConnectionString, TopicName, sqlFilterOnlySubscriptionName);
            sqlFilterWithActionSubscriptionClient = new SubscriptionClient(ServiceBusConnectionString, TopicName, sqlFilterWithActionSubscriptionName);
            correlationFilterSubscriptionClient = new SubscriptionClient(ServiceBusConnectionString, TopicName, correlationFilterSubscriptionName);

            // First Subscription is already created with default rule. Leave as is.
            Console.WriteLine($"SubscriptionName: {allMessagesSubscriptionName}, Removing and re-adding Default Rule");
            await allMessagessubscriptionClient.RemoveRuleAsync(RuleDescription.DefaultRuleName);
            await allMessagessubscriptionClient.AddRuleAsync(new RuleDescription(RuleDescription.DefaultRuleName, new TrueFilter()));

            // 2nd Subscription: Add SqlFilter on Subscription 2
            // Delete Default Rule.
            // Add the required SqlFilter Rule
            // Note: Does not apply to this sample but if there are multiple rules configured for a 
            // single subscription, then one message is delivered to the subscription when any of the 
            // rule matches. If more than one rules match and if there is no `SqlRuleAction` set for the
            // rule, then only one message will be delivered to the subscription. If more than one rules
            // match and there is a `SqlRuleAction` specified for the rule, then one message per `SqlRuleAction`
            // is delivered to the subscription.
            Console.WriteLine($"SubscriptionName: {sqlFilterOnlySubscriptionName}, Removing Default Rule and Adding SqlFilter");
            await sqlFilterOnlySubscriptionClient.RemoveRuleAsync(RuleDescription.DefaultRuleName);
            await sqlFilterOnlySubscriptionClient.AddRuleAsync(new RuleDescription
            {
                Filter = new SqlFilter("Color = 'Red'"),
                Name = "RedSqlRule"
            });

            // 3rd Subscription: Add SqlFilter and SqlRuleAction on Subscription 3
            // Delete Default Rule
            // Add the required SqlFilter Rule and Action
            Console.WriteLine($"SubscriptionName: {sqlFilterWithActionSubscriptionName}, Removing Default Rule and Adding SqlFilter and SqlRuleAction");
            await sqlFilterWithActionSubscriptionClient.RemoveRuleAsync(RuleDescription.DefaultRuleName);
            await sqlFilterWithActionSubscriptionClient.AddRuleAsync(new RuleDescription
            {
                Filter = new SqlFilter("Color = 'Blue'"),
                Action = new SqlRuleAction("SET Color = 'BlueProcessed'"),
                Name = "BlueSqlRule"
            });

            // 4th Subscription: Add Correlation Filter on Subscription 4
            Console.WriteLine($"SubscriptionName: {sqlFilterWithActionSubscriptionName}, Removing Default Rule and Adding CorrelationFilter");
            await correlationFilterSubscriptionClient.RemoveRuleAsync(RuleDescription.DefaultRuleName);
            await correlationFilterSubscriptionClient.AddRuleAsync(new RuleDescription
            {
                Filter = new CorrelationFilter() { Label = "Red", CorrelationId = "important" },
                Name = "ImportantCorrelationRule"
            });

            // Get Rules on Subscription, called here only for one subscription as example
            var rules = (await correlationFilterSubscriptionClient.GetRulesAsync()).ToList();
            Console.WriteLine($"GetRules:: SubscriptionName: {correlationFilterSubscriptionName}, CorrelationFilter Name: {rules[0].Name}, Rule: {rules[0].Filter}");

            // Send messages to Topic
            await SendMessagesAsync();

            // Receive messages from 'allMessagesSubscriptionName'. Should receive all 9 messages 
            await ReceiveMessagesAsync(allMessagesSubscriptionName);

            // Receive messages from 'sqlFilterOnlySubscriptionName'. Should receive all messages with Color = 'Red' i.e 3 messages
            await ReceiveMessagesAsync(sqlFilterOnlySubscriptionName);

            // Receive messages from 'sqlFilterWithActionSubscriptionClient'. Should receive all messages with Color = 'Blue'
            // i.e 3 messages AND all messages should have color set to 'BlueProcessed'
            await ReceiveMessagesAsync(sqlFilterWithActionSubscriptionName);

            // Receive messages from 'correlationFilterSubscriptionName'. Should receive all messages  with Color = 'Red' and CorrelationId = "important"
            // i.e 1 message
            await ReceiveMessagesAsync(correlationFilterSubscriptionName);

            Console.WriteLine("=========================================================");
            Console.WriteLine("Completed Receiving all messages... Press any key to exit");
            Console.WriteLine("=========================================================");

            Console.ReadKey();

            await allMessagessubscriptionClient.CloseAsync();
            await sqlFilterOnlySubscriptionClient.CloseAsync();
            await sqlFilterWithActionSubscriptionClient.CloseAsync();
            await correlationFilterSubscriptionClient.CloseAsync();
            await topicClient.CloseAsync();
        }

        static async Task SendMessagesAsync()
        {
            Console.WriteLine($"==========================================================================");
            Console.WriteLine("Sending Messages to Topic");
            try
            {
                await Task.WhenAll(
                    SendMessageAsync(label: "Red"),
                    SendMessageAsync(label: "Blue"),
                    SendMessageAsync(label: "Red", correlationId: "important"),
                    SendMessageAsync(label: "Blue", correlationId: "important"),
                    SendMessageAsync(label: "Red", correlationId: "notimportant"),
                    SendMessageAsync(label: "Blue", correlationId: "notimportant"),
                    SendMessageAsync(label: "Green"),
                    SendMessageAsync(label: "Green", correlationId: "important"),
                    SendMessageAsync(label: "Green", correlationId: "notimportant")
                );
            }
            catch (Exception exception)
            {
                Console.WriteLine($"{DateTime.Now} :: Exception: {exception.Message}");
            }
        }

        static async Task SendMessageAsync(string label, string correlationId = null)
        {
            Message message = new Message { Label = label };
            message.UserProperties.Add("Color", label);

            if (correlationId != null)
            {
                message.CorrelationId = correlationId;
            }

            await topicClient.SendAsync(message);
            Console.WriteLine($"Sent Message:: Label: {message.Label}, CorrelationId: {message.CorrelationId ?? message.CorrelationId}");
        }

        static async Task ReceiveMessagesAsync(string subscriptionName)
        {
            string subscriptionPath = EntityNameHelper.FormatSubscriptionPath(TopicName, subscriptionName);
            IMessageReceiver subscriptionReceiver = new MessageReceiver(ServiceBusConnectionString, subscriptionPath, ReceiveMode.ReceiveAndDelete);

            Console.WriteLine($"==========================================================================");
            Console.WriteLine($"{DateTime.Now} :: Receiving Messages From Subscription: {subscriptionName}");
            int receivedMessageCount = 0;
            while (true)
            {
                var receivedMessage = await subscriptionReceiver.ReceiveAsync(TimeSpan.Zero);
                if (receivedMessage != null)
                {
                    object colorProperty;
                    receivedMessage.UserProperties.TryGetValue("Color", out colorProperty);
                    Console.WriteLine($"Color Property = {colorProperty}, CorrelationId = {receivedMessage.CorrelationId ?? receivedMessage.CorrelationId}");
                    receivedMessageCount++;
                }
                else
                {
                    break;
                }
            }

            Console.WriteLine($"{DateTime.Now} :: Received '{receivedMessageCount}' Messages From Subscription: {subscriptionName}");
            Console.WriteLine($"==========================================================================");
            await subscriptionReceiver.CloseAsync();
        }
    }
}