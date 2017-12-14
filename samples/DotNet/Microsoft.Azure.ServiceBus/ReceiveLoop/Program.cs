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

namespace ReceiveLoop
{
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Core;
    using Newtonsoft.Json;
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public class Program : MessagingSamples.Sample
    {
        public async Task Run(string connectionString)
        {
            // send a set of messages
            var sendTask = this.SendMessagesAsync(connectionString, BasicQueueName);
            // the receive those messages
            var cts = new CancellationTokenSource();
            var receiveTask = this.ReceiveMessagesAsync(connectionString, BasicQueueName, cts.Token);

            // wait until both tasks are complete
            await Task.WhenAll(
                // run 20 seconds, then cancel the receive loop
                Task.Delay(TimeSpan.FromSeconds(20)).ContinueWith((t) => cts.Cancel()),
                // wait for the send task
                sendTask,
                // wait for the receive task
                receiveTask);
        }

        async Task SendMessagesAsync(string connectionString, string queueName)
        {
            var sender = new MessageSender(connectionString, queueName);

            Console.WriteLine("Sending messages to Queue...");

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

            // send a message for each entry in the above array
            for (int i = 0; i < data.Length; i++)
            {
                var message = new Message(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data[i])))
                {
                    ContentType = "application/json",
                    Label = "Scientist",
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

        async Task ReceiveMessagesAsync(string connectionString, string queueName, CancellationToken cancellationToken)
        {

            // First, we create a generic MessageReceiver for the queue. This class can 
            // also receive from a topic subscription when given the correct path. 
            // 
            // Note that the receiver is created the with the ReceiveMode.PeekLock receive mode. 
            // This mode will pass the message to the receiver while the broker maintains 
            // a lock on message and hold on to the message. If the message has not been 
            // completed, deferred, deadlettered, or abandoned during the lock timeout period, 
            // the message will again appear in the queue (or in the topic subscription) 
            // for retrieval.
            //
            // This is different from the ReceiveMode.ReceiveAndDelete alternative where the 
            // message has been deleted as it arrives at the receiver. Here, the message
            // is either completed or deadlettered as you will see further below.

            var receiver = new MessageReceiver(connectionString, queueName, ReceiveMode.PeekLock);

            // If the cancellation token is triggered, we close the receiver, which will trigger 
            // the receive operation below to return null as the receiver closes.
            cancellationToken.Register(() => receiver.CloseAsync());

            Console.WriteLine("Receiving message from Queue...");

            // With the receiver set up, we then enter into a simple receive loop that terminates 
            // when the cancellation token if triggered.
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // ask for the next message "forever" or until the cancellation token is triggered
                    var message = await receiver.ReceiveAsync();
                    if (message != null)
                    {
                        // If we have obtained a valid message, we'll first check whether it 
                        // is a message that we can handle. For this example, we check the Label 
                        // and ContentType properties for whether they contain the expected values,
                        // indicating that we can successfully decode and process the message body.
                        if (message.Label != null &&
                            message.ContentType != null &&
                            message.Label.Equals("Scientist", StringComparison.InvariantCultureIgnoreCase) &&
                            message.ContentType.Equals("application/json", StringComparison.InvariantCultureIgnoreCase))
                        {
                            /// If they do, we acquire the body content and deserialize it: 
                            dynamic scientist = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(message.Body));
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

                            // Now that we're done with "processing" the message, we tell the broker about that being the
                            // case. The MessageReceiver.CompleteAsync operation will settle the message transfer with 
                            // the broker and remove it from the broker. 
                            await receiver.CompleteAsync(message.SystemProperties.LockToken);
                        }
                        else
                        {
                            // If the message does not meet our processing criteria, we will deadletter it, meaning
                            // it is put into a special queue for handling defective messages. The broker will automatically
                            // deadletter the message, if delivery has been attempted too many times. 
                            await receiver.DeadLetterAsync(message.SystemProperties.LockToken);//, "ProcessingError", "Don't know what to do with this message");
                        }
                    }
                }
                catch (ServiceBusException e)
                {
                    if (!e.IsTransient)
                    {
                        // When any kind of messaging exception occurs and that exception is 
                        // not transient, meaning that things will not get better if we retry 
                        // the operation, then we "log" and rethrow for external handling. 
                        // Otherwise we'll absorb the exception (you might want to log it for 
                        // monitoring purposes) and keep going.
                        Console.WriteLine(e.Message);
                        throw;
                    }
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