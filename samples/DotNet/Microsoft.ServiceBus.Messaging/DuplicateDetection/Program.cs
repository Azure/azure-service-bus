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
    using System.Threading.Tasks;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;

    class Program : IDupdetectQueueSendReceiveSample
    {
        public async Task Run(string namespaceAddress, string queueName, string sendToken, string receiveToken)
        {
            // Create communication objects to send and receive on the queue
            var senderMessagingFactory =
                await MessagingFactory.CreateAsync(namespaceAddress, TokenProvider.CreateSharedAccessSignatureTokenProvider(sendToken));
            var sender = await senderMessagingFactory.CreateMessageSenderAsync(queueName);

            var receiverMessagingFactory =
                await MessagingFactory.CreateAsync(namespaceAddress, TokenProvider.CreateSharedAccessSignatureTokenProvider(receiveToken));
            var receiver = await receiverMessagingFactory.CreateMessageReceiverAsync(queueName, ReceiveMode.PeekLock);

            // Send messages to queue
            Console.WriteLine("\tSending messages to {0} ...", queueName);
            var message = new BrokeredMessage
            {
                MessageId = "ABC123",
                TimeToLive = TimeSpan.FromMinutes(1)
            };
            await sender.SendAsync(message);
            Console.WriteLine("\t=> Sent a message with messageId {0}", message.MessageId);

            var message2 = new BrokeredMessage
            {
                MessageId = "ABC123",
                TimeToLive = TimeSpan.FromMinutes(1)
            };
            await sender.SendAsync(message2);
            Console.WriteLine("\t=> Sent a duplicate message with messageId {0}", message.MessageId);

            // Receive messages from queue
            var receivedMessageId = "";

            Console.WriteLine("\n\tWaiting for messages from {0} ...", queueName);
            while (true)
            {
                var receivedMessage = await receiver.ReceiveAsync(TimeSpan.FromSeconds(10));

                if (receivedMessage == null)
                {
                    break;
                }
                Console.WriteLine("\t<= Received a message with messageId {0}", receivedMessage.MessageId);
                await receivedMessage.CompleteAsync();
                if (receivedMessageId.Equals(receivedMessage.MessageId, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("\t\tRECEIVED a DUPLICATE MESSAGE");
                }

                receivedMessageId = receivedMessage.MessageId;
            }

            Console.WriteLine("\tDone receiving messages from {0}", queueName);

            await receiver.CloseAsync();
            await sender.CloseAsync();
        }
    }
}