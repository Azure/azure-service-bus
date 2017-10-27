//   
//   Copyright � Microsoft Corporation, All Rights Reserved
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

namespace PrioritySubscriptions
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;

    public class Program : MessagingSamples.Sample
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
            // Create the Topic / Subscription entities 
            var topicDescription = new TopicDescription(TopicName);

            // Start senders and receivers:
            Console.WriteLine("\nLaunching senders and receivers...");

            //send messages to topic            
            var messagingFactory = MessagingFactory.CreateFromConnectionString(connectionString);

            var topicClient = messagingFactory.CreateTopicClient(TopicName);

            Console.WriteLine("Preparing to send messages to {0}...", topicClient.Path);

            // Send messages to queue:
            Console.WriteLine("Sending messages to topic {0}", topicClient.Path);

            var rand = new Random();
            for (var i = 0; i < 100; ++i)
            {
                var msg = new BrokeredMessage()
                {
                    TimeToLive = TimeSpan.FromMinutes(2),
                    Properties =
                    {
                        { "Priority", rand.Next(1, 4) }
                    }
                };

                await topicClient.SendAsync(msg);

                this.OutputMessageInfo("Sent: ", msg);
            }

            Console.WriteLine();


            // All messages sent
            Console.WriteLine("\nSender complete.");

            // start receive
            Console.WriteLine("Receiving messages by priority ...");
            var subClient1 = messagingFactory.CreateSubscriptionClient(
                TopicName,
                new SubscriptionDescription(TopicName, "Priority1Subscription").Name,
                ReceiveMode.ReceiveAndDelete);
            var subClient2 = messagingFactory.CreateSubscriptionClient(
                TopicName,
                new SubscriptionDescription(TopicName, "Priority2Subscription").Name,
                ReceiveMode.ReceiveAndDelete);
            var subClient3 = messagingFactory.CreateSubscriptionClient(
                TopicName,
                new SubscriptionDescription(TopicName, "PriorityLessThan2Subscription").Name,
                ReceiveMode.ReceiveAndDelete);

            while (true)
            {
                try
                {
                    // Please see the README.md file regarding this loop and 
                    // the handling strategy below. 
                    var message = await subClient1.ReceiveAsync(TimeSpan.Zero) ??
                                  (await subClient2.ReceiveAsync(TimeSpan.Zero) ??
                                   await subClient3.ReceiveAsync(TimeSpan.Zero));

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
                catch (MessagingException e)
                {
                    Console.WriteLine("Error: " + e.Message);
                }
            }

            Console.WriteLine("\nReceiver complete. ");
        }

        public void OutputMessageInfo(string action, BrokeredMessage message, string additionalText = "")
        {
            var prop = message?.Properties["Priority"];
            if (prop != null)
            {
                Console.ForegroundColor = this.colors[int.Parse(prop.ToString()) % this.colors.Length];
                Console.WriteLine("{0}{1} - Priority {2}. {3}", action, message.MessageId, message.Properties["Priority"], additionalText);
                Console.ResetColor();
            }
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