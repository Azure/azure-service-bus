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
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Core;
    using Newtonsoft.Json;

    public class Program : IConnectionStringSample
    {
        public async Task Run(string connectionString)
        {
            Console.WriteLine("Press any key to exit the scenario");

            var cts = new CancellationTokenSource();

            var sender = new MessageSender(connectionString, Sample.BasicQueueName);

            // MaxDeliveryCount scenario
            await this.SendMessagesAsync(sender, 1);
            await this.ExceedMaxDelivery(connectionString, Sample.BasicQueueName);

            // Fixup scenario
            var sendTask = this.SendMessagesAsync(sender, int.MaxValue);
            var receiveTask = this.ReceiveMessagesAsync(connectionString, Sample.BasicQueueName, cts.Token);
            var fixupTask = this.PickUpAndFixDeadletters(connectionString, Sample.BasicQueueName, sender, cts.Token);

            Console.ReadKey();
            cts.Cancel();

            await Task.WhenAll(sendTask, receiveTask);
        }

        async Task SendMessagesAsync(MessageSender sender, int maxMessages)
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


            for (int i = 0; i < Math.Min(data.Length, maxMessages); i++)
            {
                var message = new Message(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data[i])))
                {
                    ContentType = "application/json",
                    Label = i % 2 == 0 ? "Scientist" : "Physicist",
                    MessageId = i.ToString(),
                    TimeToLive = TimeSpan.FromMinutes(2)
                };

                await sender.SendAsync(message);
                lock (Console.Out)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Message sent: Id = {0}", message.MessageId);
                    Console.ResetColor();
                }
            }
        }

        async Task ExceedMaxDelivery(string connectionString, string queueName)
        {
            var receiver = new MessageReceiver(connectionString, queueName, ReceiveMode.PeekLock);

            while (true)
            {
                var msg = await receiver.ReceiveAsync(TimeSpan.Zero);
                if (msg != null)
                {
                    Console.WriteLine("Picked up message; DeliveryCount {0}", msg.SystemProperties.DeliveryCount);
                    await receiver.AbandonAsync(msg.SystemProperties.LockToken);
                }
                else
                {
                    break;
                }
            }

            var deadletterReceiver = new MessageReceiver(connectionString, EntityNameHelper.FormatDeadLetterPath(queueName), ReceiveMode.PeekLock);
            while (true)
            {
                var msg = await deadletterReceiver.ReceiveAsync(TimeSpan.Zero);
                if (msg != null)
                {
                    Console.WriteLine("Deadletter message:");
                    foreach (var prop in msg.UserProperties)
                    {
                        Console.WriteLine("{0}={1}", prop.Key, prop.Value);
                    }
                    await receiver.CompleteAsync(msg.SystemProperties.LockToken);
                }
                else
                {
                    break;
                }
            }
        }

        async Task ReceiveMessagesAsync(string connectionString, string queueName, CancellationToken cancellationToken)
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
                        await receiver.DeadLetterAsync(message.SystemProperties.LockToken);//, "ProcessingError", "Don't know what to do with this message");
                    }
                },
                new MessageHandlerOptions((e) => LogMessageHandlerException(e)) { AutoComplete = false, MaxConcurrentCalls = 1 });

            await doneReceiving.Task;
        }

        async Task PickUpAndFixDeadletters(string connectionString, string queueName, MessageSender resubmitSender, CancellationToken cancellationToken)
        {
            var doneReceiving = new TaskCompletionSource<bool>();

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
                    var resubmitMessage = message.Clone();
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
                        resubmitMessage.Label = "Scientist";
                        await resubmitSender.SendAsync(resubmitMessage);
                    }
                    await dlqReceiver.CompleteAsync(message.SystemProperties.LockToken);
                },
                new MessageHandlerOptions((e) => LogMessageHandlerException(e)) { AutoComplete = true, MaxConcurrentCalls = 1 });

            await doneReceiving.Task;
        }

        private Task LogMessageHandlerException(ExceptionReceivedEventArgs e)
        {
            Console.WriteLine("Exception: \"{0}\" {0}", e.Exception.Message, e.ExceptionReceivedContext.EntityPath);
            return Task.CompletedTask;
        }
    }
}