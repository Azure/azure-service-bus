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

namespace Prefetch
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;
    using Microsoft.ServiceBus.Messaging.Amqp;

    /// <summary>
    ///     This sample demonstrates how to use the messages prefetch feature upon receive
    ///     The sample creates a Queue, sends messages to it and receives all messages
    ///     using 2 receivers one with prefetchCount = 0 (disabled) and the other with
    ///     prefecthCount = 100. For each case, it calculates the time taken to receive and complete
    ///     all messages and at the end, it prints the difference between both times.
    /// </summary>
    public class Program : MessagingSamples.Sample
    {
        public async Task Run(string connectionString)
        {
            // Create communication objects to send and receive on the queue
            var senderMessagingFactory =
                MessagingFactory.CreateFromConnectionString(connectionString);
            var sender = await senderMessagingFactory.CreateMessageSenderAsync(BasicQueueName);

            var receiverMessagingFactory =
                MessagingFactory.CreateFromConnectionString(connectionString);


            // Run 1
            var receiver = await receiverMessagingFactory.CreateMessageReceiverAsync(BasicQueueName, ReceiveMode.PeekLock);
            receiver.PrefetchCount = 0;
            // Send and Receive messages with prefetch OFF
            var timeTaken1 = await this.SendAndReceiveMessages(sender, receiver, 100);
            
            receiver.Close();
            
            // Run 2
            receiver = await receiverMessagingFactory.CreateMessageReceiverAsync(BasicQueueName, ReceiveMode.PeekLock);
            receiver.PrefetchCount = 10;
            // Send and Receive messages with prefetch ON
            var timeTaken2 = await this.SendAndReceiveMessages(sender, receiver, 100);
            
            receiver.Close();

            // Calculate the time difference
            var timeDifference = timeTaken1 - timeTaken2;

            Console.WriteLine("\nTime difference = {0} milliseconds", timeDifference);
            
            // Cleanup:
            senderMessagingFactory.Close();
            receiverMessagingFactory.Close();
        }

        async Task<long> SendAndReceiveMessages(MessageSender sender, MessageReceiver receiver, int messageCount)
        {
            // Now we can start sending messages.
            var rnd = new Random();
            var mockPayload = new byte[100]; // 100 random-byte payload 

            rnd.NextBytes(mockPayload);

            Console.WriteLine("\nSending {0} messages to the queue", messageCount);
            var sendOps = new List<Task>();
            for (var i = 0; i < messageCount; i++)
            {
                sendOps.Add(
                    sender.SendAsync(
                        new BrokeredMessage(new MemoryStream(mockPayload))
                        {
                            TimeToLive = TimeSpan.FromMinutes(5)
                        }));

            }
            Task.WaitAll(sendOps.ToArray());

            Console.WriteLine("Send completed");

            // Receive the messages
            Console.WriteLine("Receiving messages...");

            // Start stopwatch
            var stopWatch = new Stopwatch();
            stopWatch.Start();

            var receivedMessage = await receiver.ReceiveAsync(TimeSpan.FromSeconds(5));
            while (receivedMessage != null )
            {
                // here's where you'd do any work

                // complete (roundtrips)
                await receivedMessage.CompleteAsync();

                if (--messageCount <= 0)
                    break;

                // now get the next message
                receivedMessage = await receiver.ReceiveAsync(TimeSpan.FromSeconds(5));
            }
            // Stop the stopwatch
            stopWatch.Stop();

            Console.WriteLine("Receive completed");

            var timeTaken = stopWatch.ElapsedMilliseconds; 
            Console.WriteLine("Time to receive and complete all messages = {0} milliseconds", timeTaken);

            return timeTaken;
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