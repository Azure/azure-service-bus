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
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Core;
    using Newtonsoft.Json;

    public class Program : IBasicQueueSendReceiveSample
    {
        public async Task Run(string connectionString, string queueName, string sendToken)
        {
            Console.WriteLine("Press any key to exit the scenario");

            var sendTask = this.SendMessagesAsync(Guid.NewGuid().ToString(), connectionString, queueName, sendToken);
            var receiveTask = this.ReceiveMessagesAsync(connectionString, queueName, receiveToken);

            await Task.WhenAll(sendTask, receiveTask);

            Console.ReadKey();
        }

        async Task SendMessagesAsync(string session, string connectionString, string queueName, string sendToken)
        {
            var senderFactory = MessagingFactory.Create(
                connectionString,
                new MessagingFactorySettings
                {
                    TransportType = TransportType.Amqp,
                    TokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(sendToken)
                });
            senderFactory.RetryPolicy = new RetryExponential(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), 10);

            var sender = new MessageSender(connectionString,queueName);


            Console.WriteLine("Sending messages to Queue...");

            dynamic data = new[]
            {
                new {step = 1, title = "Shop"},
                new {step = 2, title = "Unpack"},
                new {step = 3, title = "Prepare"},
                new {step = 4, title = "Cook"},
                new {step = 5, title = "Eat"},
            };

            var rnd = new Random();
            var tasks = new List<Task>();
            for (int i = 0; i < data.Length; i++)
            {
                var message = new Message(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data[i])))
                {
                    SessionId = session,
                    ContentType = "application/json",
                    Label = "RecipeStep",
                    MessageId = i.ToString(),
                    TimeToLive = TimeSpan.FromMinutes(2)
                };

                tasks.Add(Task.Delay(rnd.Next(30)).ContinueWith(
                      async (t) =>
                      {
                          await sender.SendAsync(message);
                          lock (Console.Out)
                          {
                              Console.ForegroundColor = ConsoleColor.Yellow;
                              Console.WriteLine("Message sent: Id = {0}", message.MessageId);
                              Console.ResetColor();
                          }
                      }));
            }
            await Task.WhenAll(tasks);
        }

        async Task ReceiveMessagesAsync(string connectionString, string queueName)
        {
            var receiverFactory = MessagingFactory.Create(
                connectionString,
                new MessagingFactorySettings
                {
                    TransportType = TransportType.NetMessaging, // deferral not yet supported on AMQP 
                    TokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(receiveToken)
                });
            receiverFactory.RetryPolicy = new RetryExponential(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), 10);

            var client = receiverFactory.CreateQueueClient(queueName, ReceiveMode.PeekLock);
            while (true)
            {
                var session = await client.AcceptMessageSessionAsync();
                await Task.Run(
                    async () =>
                    {
                        dynamic processingState;

                        var stateStream = await session.GetStateAsync();
                        if (stateStream != null)
                        {
                            processingState = JsonConvert.DeserializeObject(new StreamReader(stateStream, true).ReadToEnd());
                        }
                        else
                        {
                            processingState = new
                            {
                                lastProcessedRecipeStep = 0,
                                deferredSteps = new Dictionary<int, long>()
                            };
                        }

                        while (true)
                        {
                            try
                            {
                                //receive messages from Queue
                                var message = await session.ReceiveAsync(TimeSpan.FromSeconds(5));
                                if (message != null)
                                {
                                    if (message.Label != null &&
                                        message.ContentType != null &&
                                        message.Label.Equals("RecipeStep", StringComparison.InvariantCultureIgnoreCase) &&
                                        message.ContentType.Equals("application/json", StringComparison.InvariantCultureIgnoreCase))
                                    {
                                        var body = message.Body;

                                        dynamic recipeStep = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(body));
                                        if (recipeStep.step == processingState.lastProcessedRecipeStep + 1)
                                        {
                                            lock (Console.Out)
                                            {
                                                Console.ForegroundColor = ConsoleColor.Cyan;
                                                Console.WriteLine(
                                                    "\t\t\t\tMessage received: \n\t\t\t\t\t\tMessageId = {0}, \n\t\t\t\t\t\tSequenceNumber = {1}, \n\t\t\t\t\t\tEnqueuedTimeUtc = {2}," +
                                                    "\n\t\t\t\t\t\tExpiresAtUtc = {5}, \n\t\t\t\t\t\tContentType = \"{3}\", \n\t\t\t\t\t\tSize = {4},  \n\t\t\t\t\t\tContent: [ step = {6}, title = {7} ]",
                                                    message.MessageId,
                                                    message.SystemProperties.SequenceNumber,
                                                    message.SystemProperties.EnqueuedTimeUtc,
                                                    message.ContentType,
                                                    message.Size,
                                                    message.ExpiresAtUtc,
                                                    recipeStep.step,
                                                    recipeStep.title);
                                                Console.ResetColor();
                                            }
                                            await receiveClient.CompleteAsync(message.SystemProperties.LockToken);
                                            processingState.lastProcessedRecipeStep = recipeStep.step;
                                            await
                                                session.SetStateAsync(
                                                    Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(processingState)));
                                        }
                                        else
                                        {
                                            processingState.deferredSteps.Add((int) recipeStep.step, (long) message.SystemProperties.SequenceNumber);
                                            await message.DeferAsync();
                                            await
                                                session.SetStateAsync(
                                                    Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(processingState)));
                                        }
                                    }
                                    else
                                    {
                                        await receiver.DeadLetterAsync(message.SystemProperties.LockToken, "ProcessingError", "Don't know what to do with this message");
                                    }
                                }
                                else
                                {
                                    while (processingState.deferredSteps.Count > 0)
                                    {
                                        long step;

                                        if (processingState.deferredSteps.TryGetValue(processingState.lastProcessedRecipeStep + 1, out step))
                                        {
                                            var deferredMessage = await session.ReceiveAsync(step);
                                            var body = deferredMessage.Body;
                                            dynamic recipeStep = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(body));
                                            lock (Console.Out)
                                            {
                                                Console.ForegroundColor = ConsoleColor.Cyan;
                                                Console.WriteLine(
                                                    "\t\t\t\tdeferredMessage received: \n\t\t\t\t\t\tMessageId = {0}, \n\t\t\t\t\t\tSequenceNumber = {1}, \n\t\t\t\t\t\tEnqueuedTimeUtc = {2}," +
                                                    "\n\t\t\t\t\t\tExpiresAtUtc = {5}, \n\t\t\t\t\t\tContentType = \"{3}\", \n\t\t\t\t\t\tSize = {4},  \n\t\t\t\t\t\tContent: [ step = {6}, title = {7} ]",
                                                    deferredMessage.MessageId,
                                                    deferredmessage.SystemProperties.SequenceNumber,
                                                    deferredmessage.SystemProperties.EnqueuedTimeUtc,
                                                    deferredMessage.ContentType,
                                                    deferredMessage.Size,
                                                    deferredMessage.ExpiresAtUtc,
                                                    recipeStep.step,
                                                    recipeStep.title);
                                                Console.ResetColor();
                                            }
                                            await deferredreceiveClient.CompleteAsync(message.SystemProperties.LockToken);
                                            processingState.lastProcessedRecipeStep = processingState.lastProcessedRecipeStep + 1;
                                            processingState.deferredSteps.Remove(processingState.lastProcessedRecipeStep);
                                            await
                                                session.SetStateAsync(
                                                    Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(processingState)));
                                        }
                                    }
                                    break;
                                }
                            }
                            catch (ServiceBusException e)
                            {
                                if (!e.IsTransient)
                                {
                                    Console.WriteLine(e.Message);
                                    throw;
                                }
                            }
                        }
                        await session.CloseAsync();
                    });
            }
            await receiverFactory.CloseAsync();
        }
    }
}