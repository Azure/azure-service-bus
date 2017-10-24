//   
//   Copyright (c) Microsoft Corporation, All Rights Reserved
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
    using System.Threading.Tasks;
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.Management.ResourceManager.Fluent;
    using Microsoft.Azure.Management.ServiceBus;
    using Microsoft.Azure.Management.ServiceBus.Models;
    
    public class Program : IConnectionStringSample
    {
        const string TopicName = "PrioritySubscriptionsTopic";

        readonly ConsoleColor[] colors =
        {
            ConsoleColor.Red,
            ConsoleColor.Green,
            ConsoleColor.Yellow,
            ConsoleColor.Cyan,
            ConsoleColor.Magenta,
            ConsoleColor.Blue,
            ConsoleColor.White
        };

        public async Task Run(string connectionString)
        {
            var managementCredentials = SdkContext.AzureCredentialsFactory.FromFile(Environment.GetEnvironmentVariable("AZURE_AUTH_LOCATION"));
            var managementClient = new ServiceBusManagementClient(managementCredentials);
            var namespaceName = "";
            var resourceGroupName = "";
            await managementClient.Topics.CreateOrUpdateAsync(resourceGroupName, namespaceName, TopicName,
                new SBTopic { /* defaults are fine */ });

            await Task.WhenAll(
                // this sub receives messages for Priority = 1
                managementClient.Subscriptions.CreateOrUpdateAsync(
                    resourceGroupName, namespaceName, TopicName, "Priority1Subscription", 
                    new SBSubscription { /* defaults are fine */ }).
                    ContinueWith(t =>
                       managementClient.Rules.CreateOrUpdateAsync(resourceGroupName, namespaceName, TopicName, t.Result.Name, "$default",
                           new Rule { SqlFilter = new Microsoft.Azure.Management.ServiceBus.Models.SqlFilter { SqlExpression = "Priority = 1" } })),
                // this sub receives messages for Priority = 2
                managementClient.Subscriptions.CreateOrUpdateAsync(
                    resourceGroupName, namespaceName, TopicName, "Priority2Subscription", 
                    new SBSubscription { /* defaults are fine */ }).
                    ContinueWith(t =>
                       managementClient.Rules.CreateOrUpdateAsync(resourceGroupName, namespaceName, TopicName, t.Result.Name, "$default",
                           new Rule { SqlFilter = new Microsoft.Azure.Management.ServiceBus.Models.SqlFilter { SqlExpression = "Priority = 2" } })),
                // this sub receives messages for Priority Less than 2
                managementClient.Subscriptions.CreateOrUpdateAsync(
                    resourceGroupName, namespaceName, TopicName, "PriorityGreaterThan2Subscription", 
                    new SBSubscription { /* defaults are fine */ }).
                    ContinueWith(t =>
                       managementClient.Rules.CreateOrUpdateAsync(resourceGroupName, namespaceName, TopicName, t.Result.Name, "$default",
                           new Rule { SqlFilter = new Microsoft.Azure.Management.ServiceBus.Models.SqlFilter { SqlExpression = "Priority > 2" } }))
                );


            // Start senders and receivers:
            Console.WriteLine("\nLaunching senders and receivers...");

    
            var topicClient = new TopicClient(connectionString, TopicName);

            Console.WriteLine("Preparing to send messages to {0}...", topicClient.Path);

            // Send messages to queue:
            Console.WriteLine("Sending messages to topic {0}", topicClient.Path);

            var rand = new Random();
            for (var i = 0; i < 100; ++i)
            {
                var msg = new Message()
                {
                    TimeToLive = TimeSpan.FromMinutes(2),
                    UserProperties =
                    {
                        { "Priority", rand.Next(1, 4) }
                    }
                };

                await topicClient.SendAsync(msg);

                this.OutputMessageInfo("Sent: ", msg);
            }

            Console.WriteLine();


            // All messages sent
            Console.WriteLine("\nSender complete. Press ENTER");
            Console.ReadLine();

            // start receive
            Console.WriteLine("Receiving messages by priority ...");
            var subClient1 = new Microsoft.Azure.ServiceBus.SubscriptionClient( connectionString,
                TopicName, "Priority1Subscription", ReceiveMode.PeekLock);
            var subClient2 = new Microsoft.Azure.ServiceBus.SubscriptionClient(connectionString,
                TopicName, "Priority2Subscription", ReceiveMode.PeekLock);
            var subClient3 = new Microsoft.Azure.ServiceBus.SubscriptionClient(connectionString,
                TopicName, "PriorityGreaterThan2Subscription", ReceiveMode.PeekLock);

#if null            
            Func<Microsoft.Azure.ServiceBus.SubscriptionClient, Message,CancellationToken,Task> callback = async (c, m, ct) =>
             {
                 try
                 {
                     if (message != null)
                     {
                         this.OutputMessageInfo("Received: ", message);
                     }
                     else
                     {
                         break;
                     }
                 }
                 catch (MessageNotFoundException)
                 {
                     Console.WriteLine("Got MessageNotFoundException, waiting for messages to be available");
                 }
                 catch (ServiceBusException e)
                 {
                     Console.WriteLine("Error: " + e.Message);
                 }
             };

#endif

            Console.WriteLine("\nReceiver complete. press ENTER");
            Console.ReadLine();

            // Cleanup:
            await managementClient.Topics.DeleteAsync(resourceGroupName, namespaceName, TopicName);
        }

        public void OutputMessageInfo(string action, Message message, string additionalText = "")
        {
            var prop = message?.UserProperties["Priority"];
            if (prop != null)
            {
                Console.ForegroundColor = this.colors[int.Parse(prop.ToString()) % this.colors.Length];
                Console.WriteLine("{0}{1} - Priority {2}. {3}", action, message.MessageId, message.UserProperties["Priority"], additionalText);
                Console.ResetColor();
            }
        }
    }
}