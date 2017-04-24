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
    using System.Threading.Tasks;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;

    public class Program : IDualBasicQueueSampleWithKeys
    {
        public async Task Run(
            string namespaceAddress,
            string basicQueueName,
            string basicQueue2Name,
            string sendKeyName,
            string sendKey,
            string receiveKeyName,
            string receiveKey)
        {
            var tokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(receiveKeyName, receiveKey);

            var primaryFactory = MessagingFactory.Create(
                namespaceAddress,
                new MessagingFactorySettings {TokenProvider = tokenProvider, TransportType = TransportType.Amqp});
            var secondaryFactory = MessagingFactory.Create(
                namespaceAddress,
                new MessagingFactorySettings {TokenProvider = tokenProvider, TransportType = TransportType.Amqp});

            try
            {
                // Create a primary and secondary queue client.
                var primaryQueueClient = primaryFactory.CreateQueueClient(basicQueueName);
                var secondaryQueueClient = secondaryFactory.CreateQueueClient(basicQueue2Name);


                this.OnMessageAsync(
                    primaryQueueClient,
                    secondaryQueueClient,
                    async m => { await Console.Out.WriteLineAsync(m.MessageId); });


                Console.WriteLine("Waiting for messages, press ENTER to exit.\n");
                Console.ReadLine();
            }
            catch (Exception e)
            {
                Console.WriteLine("Unexpected exception {0}", e);
                throw;
            }
            finally
            {
                // Closing factories closes all entities created from these factories.
                primaryFactory?.Close();
                secondaryFactory?.Close();
            }
        }

        void OnMessageAsync(
            QueueClient primaryQueueClient,
            QueueClient secondaryQueueClient,
            Func<BrokeredMessage, Task> handlerCallback,
            int maxDeduplicationListLength = 256)
        {
            var receivedMessageList = new List<string>();
            var receivedMessageListLock = new object();

            Func<int, Func<BrokeredMessage, Task>, BrokeredMessage, Task> callback = async (maxCount, fwd, message) =>
            {
                // Detect if a message with an identical ID has been received through the other queue.
                bool duplicate;
                lock (receivedMessageListLock)
                {
                    duplicate = receivedMessageList.Remove(message.MessageId);
                    if (!duplicate)
                    {
                        receivedMessageList.Add(message.MessageId);
                        if (receivedMessageList.Count > maxCount)
                        {
                            receivedMessageList.RemoveAt(0);
                        }
                    }
                }
                if (!duplicate)
                {
                    await fwd(message);
                }
                else
                {
                    await message.CompleteAsync();
                }
            };

            primaryQueueClient.OnMessageAsync(msg => callback(maxDeduplicationListLength, handlerCallback, msg));
            secondaryQueueClient.OnMessageAsync(msg => callback(maxDeduplicationListLength, handlerCallback, msg));
        }
    }
}