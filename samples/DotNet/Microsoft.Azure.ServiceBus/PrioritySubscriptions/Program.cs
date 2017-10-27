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

namespace PrioritySubscriptions
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.Azure.ServiceBus;
    using System.Threading;

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
            Console.WriteLine("\nSender complete.");

            // start receive
            Console.WriteLine("Receiving messages by priority ...");
            var subClient1 = new Microsoft.Azure.ServiceBus.SubscriptionClient(connectionString,
                TopicName, "Priority1Subscription", ReceiveMode.PeekLock);
            var subClient2 = new Microsoft.Azure.ServiceBus.SubscriptionClient(connectionString,
                TopicName, "Priority2Subscription", ReceiveMode.PeekLock);
            var subClient3 = new Microsoft.Azure.ServiceBus.SubscriptionClient(connectionString,
                TopicName, "PriorityGreaterThan2Subscription", ReceiveMode.PeekLock);


            Func<SubscriptionClient, Message, CancellationToken, Task> callback = (c, message, ct) =>
               {
                   this.OutputMessageInfo("Received: ", message);
                   return Task.CompletedTask;
               };

            subClient1.RegisterMessageHandler((m, c) => callback(subClient1, m, c),
                new MessageHandlerOptions(LogMessageHandlerException) { MaxConcurrentCalls = 10, AutoComplete = true });
            subClient2.RegisterMessageHandler((m, c) => callback(subClient1, m, c),
                new MessageHandlerOptions(LogMessageHandlerException) { MaxConcurrentCalls = 5, AutoComplete = true });
            subClient3.RegisterMessageHandler((m, c) => callback(subClient1, m, c),
                new MessageHandlerOptions(LogMessageHandlerException) { MaxConcurrentCalls = 1, AutoComplete = true });
            
            Task.WaitAny(
              Task.Run(() => Console.ReadKey()),
              Task.Delay(TimeSpan.FromSeconds(10)));

            await Task.WhenAll(subClient1.CloseAsync(), subClient2.CloseAsync(), subClient3.CloseAsync());
        }

        Task LogMessageHandlerException(ExceptionReceivedEventArgs e)
        {
            Console.WriteLine("Exception: \"{0}\" {0}", e.Exception.Message, e.ExceptionReceivedContext.EntityPath);
            return Task.CompletedTask;
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