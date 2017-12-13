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

namespace Deferral
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

    // This sample illustrates and explains the use of the 
    // Deferral feature in Service Bus
    public class Program : MessagingSamples.Sample
    {
        public async Task Run(string connectionString)
        {
            // set up a task that sends messages
            var sendTask = this.SendMessagesAsync(connectionString, BasicQueueName);
            // set up a task that receives those messages
            var receiveTask = this.ReceiveMessagesAsync(connectionString, BasicQueueName);

            // wait until both tasks are done
            await Task.WhenAll(sendTask, receiveTask);
        }

        async Task SendMessagesAsync(string connectionString, string queueName)
        {
            // First, we send a set of messages into the queue.
            // The messages represent an ordered set of workflow steps,
            // but we simulate that those messages are enqueued out
            // of the expected handling order for some reason
            var sender = new MessageSender(connectionString, queueName);

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
                    ContentType = "application/json",
                    Label = "RecipeStep",
                    MessageId = i.ToString(),
                    TimeToLive = TimeSpan.FromMinutes(2)
                };

                // the way we shuffle the message order is to introduce
                // a tiny random delay before each of the messages is sent
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
            // report completion when all the send tasks are complete 
            await Task.WhenAll(tasks);
        }

        async Task ReceiveMessagesAsync(string connectionString, string queueName)
        {
            // create a receiver to pick up the messages from the queue
            var receiver = new MessageReceiver(connectionString, queueName, ReceiveMode.PeekLock);

            Console.WriteLine("Receiving message from Queue...");

            int lastProcessedRecipeStep = 0;
            var deferredSteps = new Dictionary<int, long>();

            while (true)
            {
                try
                {
                    //receive a message
                    var message = await receiver.ReceiveAsync(TimeSpan.FromSeconds(5));
                    if (message != null)
                    {
                        if (message.Label != null &&
                            message.ContentType != null &&
                            message.Label.Equals("RecipeStep", StringComparison.InvariantCultureIgnoreCase) &&
                            message.ContentType.Equals("application/json", StringComparison.InvariantCultureIgnoreCase))
                        {
                            var body = message.Body;

                            dynamic recipeStep = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(body));
                            // now let's check whether the step we received is the 
                            // step we expect at this stage of the workflow
                            if (recipeStep.step == lastProcessedRecipeStep + 1)
                            {
                                // if so, print it
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
                                await receiver.CompleteAsync(message.SystemProperties.LockToken);
                                lastProcessedRecipeStep = recipeStep.step;
                            }
                            else
                            {
                                // if this is not the step we expected, we defer the message, 
                                // meaning that we leave it in the queue but take it out of 
                                // the delivery order. We put it aside. To retrieve it later,
                                // we remeber its sequence number
                                deferredSteps.Add((int)recipeStep.step, (long)message.SystemProperties.SequenceNumber);
                                await receiver.DeferAsync(message.SystemProperties.LockToken);
                            }
                        }
                        else
                        {
                            // we dead-letter the message if we don't know what to do with it.
                            await receiver.DeadLetterAsync(message.SystemProperties.LockToken); //, "ProcessingError", "Don't know what to do with this message");
                        }
                    }
                    else
                    {
                        //no more messages in the queue
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

            // Now that the queue is drained for the time being, we take a look at the 
            // deferred steps. The implementation here is rather naive for better legibility. 
            // You might prefer a strategy where the deferredSteps array is (repeatedly) 
            // consulted right after a step has been completed and before a new message is retrieved.
            while (deferredSteps.Count > 0)
            {
                long step;
                
                // if we have a step put away that follows the currently 
                // processed one, fetch it and process it.
                if (deferredSteps.TryGetValue(lastProcessedRecipeStep + 1, out step))
                {
                    var message = await receiver.ReceiveDeferredMessageAsync(step);
                    var body = message.Body;
                    dynamic recipeStep = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(body));
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
                    await receiver.CompleteAsync(message.SystemProperties.LockToken);
                    lastProcessedRecipeStep = lastProcessedRecipeStep + 1;
                    deferredSteps.Remove(lastProcessedRecipeStep);
                }
            }

            await receiver.CloseAsync();
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