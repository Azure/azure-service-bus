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
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;
    using Newtonsoft.Json;

    public class Program : ISessionQueueSendReceiveSample
    {
        public async Task Run(string namespaceAddress, string queueName, string sendToken, string receiveToken)
        {
            Console.WriteLine("Press any key to exit the scenario");

            CancellationTokenSource cts = new CancellationTokenSource();

            await Task.WhenAll(
                this.SendMessagesAsync(Guid.NewGuid().ToString(), namespaceAddress, queueName, sendToken),
                this.SendMessagesAsync(Guid.NewGuid().ToString(), namespaceAddress, queueName, sendToken),
                this.SendMessagesAsync(Guid.NewGuid().ToString(), namespaceAddress, queueName, sendToken),
                this.SendMessagesAsync(Guid.NewGuid().ToString(), namespaceAddress, queueName, sendToken));

            this.InitializeReceiver(namespaceAddress, queueName, receiveToken, cts.Token);
            Console.ReadKey();
            cts.Cancel();
        }

        async Task SendMessagesAsync(string sessionId, string namespaceAddress, string queueName, string sendToken)
        {
            var senderFactory = MessagingFactory.Create(
                namespaceAddress,
                new MessagingFactorySettings
                {
                    TransportType = TransportType.Amqp,
                    TokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(sendToken)
                });

            var sender = await senderFactory.CreateMessageSenderAsync(queueName);

            dynamic data = new[]
            {
                new {step = 1, title = "Shop"},
                new {step = 2, title = "Unpack"},
                new {step = 3, title = "Prepare"},
                new {step = 4, title = "Cook"},
                new {step = 5, title = "Eat"},
            };

            for (int i = 0; i < data.Length; i++)
            {
                var message = new BrokeredMessage(new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data[i]))))
                {
                    SessionId = sessionId,
                    ContentType = "application/json",
                    Label = "RecipeStep",
                    MessageId = i.ToString(),
                    TimeToLive = TimeSpan.FromMinutes(2)
                };
                await sender.SendAsync(message);
                lock (Console.Out)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Message sent: Session {0}, MessageId = {1}", message.SessionId, message.MessageId);
                    Console.ResetColor();
                }
            }
        }

        void InitializeReceiver(string namespaceAddress, string queueName, string receiveToken, CancellationToken ct)
        {
            var receiverFactory = MessagingFactory.Create(
                namespaceAddress,
                new MessagingFactorySettings
                {
                    TransportType = TransportType.NetMessaging, // deferral not yet supported on AMQP 
                    TokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(receiveToken)
                });

            ct.Register(() => receiverFactory.Close());

            var client = receiverFactory.CreateQueueClient(queueName, ReceiveMode.PeekLock);
            client.RegisterSessionHandler(
                typeof(SessionHandler),
                new SessionHandlerOptions
                {
                    MessageWaitTimeout = TimeSpan.FromSeconds(5),
                    MaxConcurrentSessions = 1,
                    AutoComplete = false
                });
        }

        class SessionHandler : IMessageSessionAsyncHandler
        {
            public async Task OnMessageAsync(MessageSession session, BrokeredMessage message)
            {
                if (message.Label != null &&
                  message.ContentType != null &&
                  message.Label.Equals("RecipeStep", StringComparison.InvariantCultureIgnoreCase) &&
                  message.ContentType.Equals("application/json", StringComparison.InvariantCultureIgnoreCase))
                {
                    var body = message.GetBody<Stream>();

                    dynamic recipeStep = JsonConvert.DeserializeObject(new StreamReader(body, true).ReadToEnd());
                    lock (Console.Out)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine(
                            "\t\t\t\tMessage received:  \n\t\t\t\t\t\tSessionId = {0}, \n\t\t\t\t\t\tMessageId = {1}, \n\t\t\t\t\t\tSequenceNumber = {2}," +
                            "\n\t\t\t\t\t\tContent: [ step = {3}, title = {4} ]",
                            message.SessionId,
                            message.MessageId,
                            message.SequenceNumber,
                            recipeStep.step,
                            recipeStep.title);
                        Console.ResetColor();
                    }
                    await message.CompleteAsync();

                    if (recipeStep.step == 5)
                    {
                        // end of the session!
                        await session.CloseAsync();
                    }
                }
                else
                {
                    await message.DeadLetterAsync("BadMessage", "Unexpected message");
                }
            }

            public async Task OnCloseSessionAsync(MessageSession session)
            {
                // nothing to do
            }

            public async Task OnSessionLostAsync(Exception exception)
            {
                // nothing to do
            }
        }        
    }
}