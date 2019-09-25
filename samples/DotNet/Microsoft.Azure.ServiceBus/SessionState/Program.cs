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

namespace SessionState
{
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Core;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;

    public class Program : MessagingSamples.Sample
    {
        public async Task Run(string connectionString)
        {
            Console.WriteLine("Press any key to exit the scenario");

            var sendTask = this.SendMessagesAsync(Guid.NewGuid().ToString(), connectionString, SessionQueueName);
            var receiveTask = this.ReceiveMessagesAsync(connectionString, SessionQueueName);

            await Task.WhenAll(sendTask, receiveTask);
        }

        async Task SendMessagesAsync(string session, string connectionString, string queueName)
        {
            var sender = new MessageSender(connectionString, queueName);


            Console.WriteLine("Sending messages to Queue...");

            ProcessingState[] data = new[]
            {
                new ProcessingState {Step = 1, Title = "Buy"},
                new ProcessingState {Step = 2, Title = "Unpack"},
                new ProcessingState {Step = 3, Title = "Prepare"},
                new ProcessingState {Step = 4, Title = "Cook"},
                new ProcessingState {Step = 5, Title = "Eat"},
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
            var client = new SessionClient(connectionString, queueName, ReceiveMode.PeekLock);

            while (true)
            {
                var session = await client.AcceptMessageSessionAsync();
                await Task.Run(
                    async () =>
                    {
                        ProcessingState processingState;

                        var stateData = await session.GetStateAsync();
                        if (stateData != null)
                        {
                            processingState = JsonConvert.DeserializeObject<ProcessingState>(Encoding.UTF8.GetString(stateData));
                        }
                        else
                        {
                            processingState = new ProcessingState
                            {
                                LastProcessedRecipeStep = 0,
                                DeferredSteps = new Dictionary<int, long>()
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

                                        ProcessingState recipeStep = JsonConvert.DeserializeObject<ProcessingState>(Encoding.UTF8.GetString(body));
                                        if (recipeStep.Step == processingState.LastProcessedRecipeStep + 1)
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
                                                    recipeStep.Step,
                                                    recipeStep.Title);
                                                Console.ResetColor();
                                            }
                                            await session.CompleteAsync(message.SystemProperties.LockToken);
                                            processingState.LastProcessedRecipeStep = recipeStep.Step;
                                            await
                                                session.SetStateAsync(
                                                    Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(processingState)));
                                        }
                                        else
                                        {
                                            processingState.DeferredSteps.Add((int)recipeStep.Step, (long)message.SystemProperties.SequenceNumber);
                                            await session.DeferAsync(message.SystemProperties.LockToken);
                                            await session.SetStateAsync(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(processingState)));
                                        }
                                    }
                                    else
                                    {
                                        await session.DeadLetterAsync(message.SystemProperties.LockToken);//, "ProcessingError", "Don't know what to do with this message");
                                    }
                                }
                                else
                                {
                                    while (processingState.DeferredSteps.Count > 0)
                                    {
                                        long step;

                                        if (processingState.DeferredSteps.TryGetValue(processingState.LastProcessedRecipeStep + 1, out step))
                                        {
                                            var deferredMessage = await session.ReceiveDeferredMessageAsync(step);
                                            var body = deferredMessage.Body;
                                            ProcessingState recipeStep = JsonConvert.DeserializeObject<ProcessingState>(Encoding.UTF8.GetString(body));
                                            lock (Console.Out)
                                            {
                                                Console.ForegroundColor = ConsoleColor.Cyan;
                                                Console.WriteLine(
                                                    "\t\t\t\tdeferredMessage received: \n\t\t\t\t\t\tMessageId = {0}, \n\t\t\t\t\t\tSequenceNumber = {1}, \n\t\t\t\t\t\tEnqueuedTimeUtc = {2}," +
                                                    "\n\t\t\t\t\t\tExpiresAtUtc = {5}, \n\t\t\t\t\t\tContentType = \"{3}\", \n\t\t\t\t\t\tSize = {4},  \n\t\t\t\t\t\tContent: [ step = {6}, title = {7} ]",
                                                    deferredMessage.MessageId,
                                                    deferredMessage.SystemProperties.SequenceNumber,
                                                    deferredMessage.SystemProperties.EnqueuedTimeUtc,
                                                    deferredMessage.ContentType,
                                                    deferredMessage.Size,
                                                    deferredMessage.ExpiresAtUtc,
                                                    recipeStep.Step,
                                                    recipeStep.Title);
                                                Console.ResetColor();
                                            }
                                            await session.CompleteAsync(deferredMessage.SystemProperties.LockToken);
                                            processingState.LastProcessedRecipeStep = processingState.LastProcessedRecipeStep + 1;
                                            processingState.DeferredSteps.Remove(processingState.LastProcessedRecipeStep);
                                            await session.SetStateAsync(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(processingState)));
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

        class ProcessingState
        {
            [JsonProperty]
            public int LastProcessedRecipeStep { get; set; }
            [JsonProperty]
            public Dictionary<int, long> DeferredSteps { get; set; }
            [JsonProperty]
            public int Step { get; internal set; }
            [JsonProperty]
            public string Title { get; internal set; }
        }
    }
}