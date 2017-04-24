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
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;

    class Program : IBasicQueueReceiveSample
    {
        public async Task Run(string namespaceAddress, string queueName, string receiveToken)
        {
            var receiverFactory = MessagingFactory.Create(
                namespaceAddress,
                new MessagingFactorySettings
                {
                    TransportType = TransportType.Amqp,
                    TokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(receiveToken)
                });
            try
            {
                var receiver = receiverFactory.CreateMessageReceiver(queueName, ReceiveMode.PeekLock);
                try
                {
                    var responder = new RequestReplyResponder(
                        new Uri(namespaceAddress),
                        receiver,
                        async m =>
                        {
                            Console.WriteLine("Got {0}", m.Label);
                            switch (m.Label)
                            {
                                case "requestA":
                                    return new BrokeredMessage
                                    {
                                        Label = "responseA"
                                    };
                                case "requestB":
                                    return new BrokeredMessage
                                    {
                                        Label = "responseB"
                                    };
                                default:
                                    await m.DeadLetterAsync("Unknown", "Unknown Message");
                                    return null;
                            }
                        });
                    var cts = new CancellationTokenSource();

                    var runTask = responder.Run(cts.Token);

                    Console.WriteLine("Press ENTER to stop processing requests.");
                    Console.ReadLine();
                    cts.Cancel();

                    await runTask;
                }
                finally
                {
                    receiver.Close();
                }
            }
            finally
            {
                receiverFactory.Close();
            }
        }
    }
}