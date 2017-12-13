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

namespace TimeToLive
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;
    using Newtonsoft.Json;

    public class Program : MessagingSamples.Sample
    {
        public async Task Run(string connectionString)
        {
            Console.WriteLine("Press any key to exit the scenario");

            var cts = new CancellationTokenSource();

            var senderFactory = MessagingFactory.CreateFromConnectionString(connectionString);
            senderFactory.RetryPolicy = new RetryExponential(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), 10);

            var receiverFactory = MessagingFactory.CreateFromConnectionString(connectionString);
            receiverFactory.RetryPolicy = new RetryExponential(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), 10);

            var sender = await senderFactory.CreateMessageSenderAsync(BasicQueueName);


            var sendTask = this.SendMessagesAsync(sender);
            var receiveTask = this.ReceiveMessagesAsync(receiverFactory, BasicQueueName, cts.Token);
            var fixupTask = this.PickUpAndFixDeadletters(receiverFactory, BasicQueueName, sender, cts.Token);

            await Task.WhenAny(
                Task.Run(() => Console.ReadKey()),
                Task.Delay(TimeSpan.FromSeconds(10))
            );

            cts.Cancel();

            await Task.WhenAll(sendTask, receiveTask);
        }

        async Task SendMessagesAsync(MessageSender sender)
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


            for (int i = 0; i < data.Length; i++)
            {
                var message = new BrokeredMessage(new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data[i]))))
                {
                    ContentType = "application/json",
                    Label = i % 2 == 0 ? "Scientist" : "Physicist",
                    MessageId = i.ToString(),
                    TimeToLive = TimeSpan.FromSeconds(15)
                };

                await sender.SendAsync(message);
                lock (Console.Out)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Message sent: Id = {0}", message.MessageId);
                    Console.ResetColor();
                }
            }
            
            await Task.Delay(15); // let all messages expire
        }

        async Task ReceiveMessagesAsync(MessagingFactory receiverFactory, string queueName, CancellationToken cancellationToken)
        {
            var doneReceiving = new TaskCompletionSource<bool>();
            var receiver = await receiverFactory.CreateMessageReceiverAsync(queueName, ReceiveMode.PeekLock);

            // close the receiver and factory when the CancellationToken fires 
            cancellationToken.Register(
                async () =>
                {
                    await receiver.CloseAsync();
                   doneReceiving.SetResult(true);
                });

            // register the OnMessageAsync callback
            receiver.OnMessageAsync(
                async message =>
                {
                    if (message.Label != null &&
                        message.ContentType != null &&
                        message.Label.Equals("Scientist", StringComparison.InvariantCultureIgnoreCase) &&
                        message.ContentType.Equals("application/json", StringComparison.InvariantCultureIgnoreCase))
                    {
                        var body = message.GetBody<Stream>();

                        dynamic scientist = JsonConvert.DeserializeObject(new StreamReader(body, true).ReadToEnd());

                        lock (Console.Out)
                        {
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine(
                                "\t\t\t\tMessage received: \n\t\t\t\t\t\tMessageId = {0}, \n\t\t\t\t\t\tSequenceNumber = {1}, \n\t\t\t\t\t\tEnqueuedTimeUtc = {2}," +
                                "\n\t\t\t\t\t\tExpiresAtUtc = {5}, \n\t\t\t\t\t\tContentType = \"{3}\", \n\t\t\t\t\t\tSize = {4},  \n\t\t\t\t\t\tContent: [ firstName = {6}, name = {7} ]",
                                message.MessageId,
                                message.SequenceNumber,
                                message.EnqueuedTimeUtc,
                                message.ContentType,
                                message.Size,
                                message.ExpiresAtUtc,
                                scientist.firstName,
                                scientist.name);
                            Console.ResetColor();
                        }
                        await message.CompleteAsync();
                    }
                    else
                    {
                        await message.DeadLetterAsync("ProcessingError", "Don't know what to do with this message");
                    }
                },
                new OnMessageOptions { AutoComplete = false, MaxConcurrentCalls = 1 });

            await doneReceiving.Task;
        }

        async Task PickUpAndFixDeadletters(MessagingFactory receiverFactory, string queueName, MessageSender resubmitSender, CancellationToken cancellationToken)
        {
            var doneReceiving = new TaskCompletionSource<bool>();
           
            var dlqReceiver = await receiverFactory.CreateMessageReceiverAsync(QueueClient.FormatDeadLetterPath(queueName), ReceiveMode.PeekLock);

            // close the receiver and factory when the CancellationToken fires 
            cancellationToken.Register(
                async () =>
                {
                    await dlqReceiver.CloseAsync();
                     doneReceiving.SetResult(true);
                });

            // register the OnMessageAsync callback
            dlqReceiver.OnMessageAsync(
                async message =>
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
                                message.SequenceNumber,
                                message.Label);
                            Console.ResetColor();
                        }
                        resubmitMessage.Label = "Scientist";
                        await resubmitSender.SendAsync(resubmitMessage);
                    }
                    await message.CompleteAsync();
                },
                new OnMessageOptions { AutoComplete = true, MaxConcurrentCalls = 1 });

            await doneReceiving.Task;
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