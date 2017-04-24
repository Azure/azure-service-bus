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

    class Program : IDualBasicQueueSampleWithKeys
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
            var senderFactory = MessagingFactory.Create(
                namespaceAddress,
                new MessagingFactorySettings
                {
                    TransportType = TransportType.Amqp,
                    TokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(sendKeyName, sendKey)
                });
            
            var receiverFactory = MessagingFactory.Create(
                namespaceAddress,
                new MessagingFactorySettings
                {
                    TransportType = TransportType.Amqp,
                    TokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(receiveKeyName, receiveKey)
                });
            
            var sender = senderFactory.CreateMessageSender(basicQueueName);
            var receiver = receiverFactory.CreateMessageReceiver(basicQueue2Name);
            var rr = new RequestReplySender(sender, receiver);

            var replyTokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(sendKeyName, sendKey);
            var replyTo = new Uri(new Uri(namespaceAddress), basicQueue2Name);

            for (int i = 0; i < 10; i++)
            {
                await rr.Request(
                    new BrokeredMessage
                    {
                        Label = "requestA",
                        TimeToLive = TimeSpan.FromMinutes(5),
                        MessageId = Guid.NewGuid().ToString(),
                        ReplyTo = await FormatReplyTo(replyTo, replyTokenProvider)
                    },
                    TimeSpan.FromMinutes(1),
                    async m =>
                    {
                        await Console.Out.WriteLineAsync(string.Format("{0}, {1}", m.CorrelationId, m.Label));
                        return true;
                    });
            }

            // All messages sent
            Console.WriteLine("\nClient complete.");
            Console.ReadLine();
        }

        static async Task<string> FormatReplyTo(Uri replyTo, TokenProvider replyTokenProvider)
        {
            return new UriBuilder(replyTo)
            {
                Query = string.Format(
                    "tk={0}",
                    Uri.EscapeDataString(await replyTokenProvider.GetWebTokenAsync(replyTo.AbsoluteUri, string.Empty, false, TimeSpan.FromMinutes(1))))
            }.ToString();
        }
    }
}