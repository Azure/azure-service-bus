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

namespace DeadletterQueue
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Core;
    using Newtonsoft.Json;

    // This sample shows how to move messages to the Dead-letter queue, how to retrieve
    // messages from it, and resubmit corrected message back into the main queue. 
    public class Program : MessagingSamples.Sample
    {

        async Task Run(string connectionString)
        {
            var cts = new CancellationTokenSource();

            var sender = new MessageSender(connectionString, BasicQueueName);

            // For the delivery count scenario, we first send a single message, 
            // and then pick it up and abandon it until is "disappears" from the queue.
            // Then we fetch the message from the dead-letter queue (DLQ) and inspect it. 
            await this.SendMessagesAsync(sender, 1);
            await this.ExceedMaxDelivery(connectionString, BasicQueueName);

            // For the fix-up scenario, we send a series of messages to a queue, and
            // run a receive loop that explicitly pushes messages into the DLQ when 
            // they don't satisfy a processing condition. The fix-up receive loop inspects 
            // the DLQ, fixes the "faulty" messages, and resubmits them into processing. 
            var sendTask = this.SendMessagesAsync(sender, int.MaxValue);
            var receiveTask = this.ReceiveMessagesAsync(connectionString, BasicQueueName, cts.Token);
            var fixupTask = this.PickUpAndFixDeadletters(connectionString, BasicQueueName, sender, cts.Token);

            // wait for a key press or 10 seconds
            await Task.WhenAny(
                    Task.Run(() => Console.ReadKey()),
                    Task.Delay(TimeSpan.FromSeconds(10))
                );

            // end the processing 
            cts.Cancel();
            // await shutdown and exit
            await Task.WhenAll( sendTask, receiveTask, fixupTask);
        }

        Task SendMessagesAsync(MessageSender sender, int maxMessages)
        {
            dynamic data = new[]
            {
                new {name = "Einstein", firstName = "Albert"},
                new {name = "Heisenberg", firstName = "Werner"},
                new {name = "Curie", firstName = "Marie"},
                new {name = "Hawking", firstName = "Steven"},
                new {name = "Newton", firstName = "Isaac"},
                new {name = "Bohr", firstName = "Niels"},
                new {name = "Faraday", firstName = "Michael"},
                new {name = "Galilei", firstName = "Galileo"},
                new {name = "Kepler", firstName = "Johannes"},
                new {name = "Kopernikus", firstName = "Nikolaus"}
            };

            // send a message for each data entry, but at most maxMessages
            // we're sending in a loop, but don't block on each send, but 
            // rather collect all sends in a list and then wait for all of
            // them to complete asynchronously, which is much faster  
            var tasks = new List<Task>();
            for (int i = 0; i < Math.Min(data.Length, maxMessages); i++)
            {
                // each message has a JSON body with one of the data rows
                var message = new Message(
                    Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data[i])))
                {
                    ContentType = "application/json",  // JSON data
                    Label = i % 2 == 0 ? "Scientist" : "Physicist", // random picked header
                    MessageId = i.ToString(), // message-id 
                    TimeToLive = TimeSpan.FromMinutes(2) // message expires in 2 minutes 
                };

                // start sending this message
                lock (Console.Out)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("Message sent: Id = {0}", message.MessageId);
                    Console.ResetColor();
                };
                tasks.Add(sender.SendAsync(message).ContinueWith((t)=>{
                lock (Console.Out)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\tMessage acknowledged: Id = {0}", message.MessageId);
                    Console.ResetColor();
                }}));
            }
            return Task.WhenAll(tasks);
        }


        async Task ExceedMaxDelivery(string connectionString, string queueName)
        {
            var receiver = new MessageReceiver(connectionString, queueName, ReceiveMode.PeekLock);

            while (true)
            {
                // Ask the broker to return any message readily available or return with no 
                // result after 2 seconds (allowing for clients with great network latency)
                var msg = await receiver.ReceiveAsync(TimeSpan.FromSeconds(2));
                if (msg != null)
                {
                    // Now we immediately abandon the message, which increments the DeliveryCount
                    Console.WriteLine("Picked up message; DeliveryCount {0}", msg.SystemProperties.DeliveryCount);
                    await receiver.AbandonAsync(msg.SystemProperties.LockToken);
                }
                else
                {
                    // Once the system moves the message to the DLQ, the main queue is empty 
                    // and the loop exits as ReceiveAsync returns null.
                    break;
                }
            }

            // For picking up the message from a DLQ, we make a receiver just like for a 
            // regular queue. We could also use QueueClient and a registered handler here. 
            // The required path is constructed with the EntityNameHelper.FormatDeadLetterPath() 
            // helper method, and always follows the pattern "{entity}/$DeadLetterQueue", 
            // meaning that for a queue "Q1", the path is "Q1/$DeadLetterQueue" and for a 
            // topic "T1" and subscription "S1", the path is "T1/Subscriptions/S1/$DeadLetterQueue" 
            var deadletterReceiver = new MessageReceiver(connectionString, EntityNameHelper.FormatDeadLetterPath(queueName), ReceiveMode.PeekLock);
            while (true)
            {
                // receive a message
                var msg = await deadletterReceiver.ReceiveAsync(TimeSpan.FromSeconds(10));
                if (msg != null)
                {
                    // write out the message and its user properties
                    Console.WriteLine("Deadletter message:");
                    foreach (var prop in msg.UserProperties)
                    {
                        Console.WriteLine("{0}={1}", prop.Key, prop.Value);
                    }
                    // complete and therefore remove the message from the DLQ
                    await deadletterReceiver.CompleteAsync(msg.SystemProperties.LockToken);
                }
                else
                {
                    // DLQ was empty on last receive attempt
                    break;
                }
            }
        }

        Task ReceiveMessagesAsync(string connectionString, string queueName, CancellationToken cancellationToken)
        {
            var doneReceiving = new TaskCompletionSource<bool>();
            var receiver = new MessageReceiver(connectionString, queueName, ReceiveMode.PeekLock);

            // close the receiver and factory when the CancellationToken fires 
            cancellationToken.Register(
                async () =>
                {
                    await receiver.CloseAsync();
                    doneReceiving.SetResult(true);
                });

            // register the RegisterMessageHandler callback
            receiver.RegisterMessageHandler(
                async (message, cancellationToken1) =>
                {
                    // If the message holds JSON data and the label is set to "Scientist", 
                    // we accept the message and print it.
                    if (message.Label != null &&
                        message.ContentType != null &&
                        message.Label.Equals("Scientist", StringComparison.InvariantCultureIgnoreCase) &&
                        message.ContentType.Equals("application/json", StringComparison.InvariantCultureIgnoreCase))
                    {
                        var body = message.Body;

                        dynamic scientist = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(body));

                        lock (Console.Out)
                        {
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine(
                                "\t\t\t\tMessage received: \n\t\t\t\t\t\tMessageId = {0}, \n\t\t\t\t\t\tSequenceNumber = {1}, \n\t\t\t\t\t\tEnqueuedTimeUtc = {2}," +
                                "\n\t\t\t\t\t\tExpiresAtUtc = {5}, \n\t\t\t\t\t\tContentType = \"{3}\", \n\t\t\t\t\t\tSize = {4},  \n\t\t\t\t\t\tContent: [ firstName = {6}, name = {7} ]",
                                message.MessageId,
                                message.SystemProperties.SequenceNumber,
                                message.SystemProperties.EnqueuedTimeUtc,
                                message.ContentType,
                                message.Size,
                                message.ExpiresAtUtc,
                                scientist.firstName,
                                scientist.name);
                            Console.ResetColor();
                        }
                        await receiver.CompleteAsync(message.SystemProperties.LockToken);
                    }
                    else
                    {
                        // if the messages doesn't fit the criteria above, we deadletter it
                        await receiver.DeadLetterAsync(message.SystemProperties.LockToken);//"ProcessingError", "Don't know what to do with this message");
                    }
                },
                new MessageHandlerOptions((e) => LogMessageHandlerException(e)) { AutoComplete = false, MaxConcurrentCalls = 1 });

            return doneReceiving.Task;
        }

        Task PickUpAndFixDeadletters(string connectionString, string queueName, MessageSender resubmitSender, CancellationToken cancellationToken)
        {
            var doneReceiving = new TaskCompletionSource<bool>();

            // here, we create a receiver on the Deadletter queue
            var dlqReceiver = new MessageReceiver(connectionString, EntityNameHelper.FormatDeadLetterPath(queueName), ReceiveMode.PeekLock);

            // close the receiver and factory when the CancellationToken fires 
            cancellationToken.Register(
                async () =>
                {
                    await dlqReceiver.CloseAsync();
                    doneReceiving.SetResult(true);
                });

            // register the RegisterMessageHandler callback
            dlqReceiver.RegisterMessageHandler(
                async (message, cancellationToken1) =>
                {
                    // first, we create a clone of the picked up message
                    // that we can resubmit. 
                    var resubmitMessage = message.Clone();
                    // if the cloned message has an "error" we know the main loop
                    // can't handle, let's fix the message
                    if (resubmitMessage.Label != null && resubmitMessage.Label.Equals("Physicist"))
                    {
                        lock (Console.Out)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine(
                                "\t\tFixing: \n\t\t\tMessageId = {0}, \n\t\t\tSequenceNumber = {1}, \n\t\t\tLabel = {2}",
                                message.MessageId,
                                message.SystemProperties.SequenceNumber,
                                message.Label);
                            Console.ResetColor();
                        }
                        // set the label to "Scientist"
                        resubmitMessage.Label = "Scientist";
                        // and re-enqueue the cloned message
                        await resubmitSender.SendAsync(resubmitMessage);
                    }
                    // finally complete the original message and remove it from the DLQ
                    await dlqReceiver.CompleteAsync(message.SystemProperties.LockToken);
                },
                new MessageHandlerOptions((e) => LogMessageHandlerException(e)) { AutoComplete = false, MaxConcurrentCalls = 1 });

            return doneReceiving.Task;
        }

        Task LogMessageHandlerException(ExceptionReceivedEventArgs e)
        {
            Console.WriteLine("Exception: \"{0}\" {0}", e.Exception.Message, e.ExceptionReceivedContext.EntityPath);
            return Task.CompletedTask;
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