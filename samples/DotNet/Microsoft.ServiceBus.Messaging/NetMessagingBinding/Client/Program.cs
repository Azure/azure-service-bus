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
    using System.ServiceModel;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus;

    public class Program : IBasicQueueSendSample
    {
        static readonly Random random = new Random();

        public async Task Run(string namespaceAddress, string queueName, string sendToken)
        {
            try
            {
                // Send messages to queue which does not require session
                Console.Title = "Client";

                // Create sender to Order Service
                using (var factory = new ChannelFactory<IOnewayServiceChannel>("client"))
                {
                    factory.Endpoint.Address = new EndpointAddress(
                        new Uri(new Uri(namespaceAddress), queueName));
                    factory.Endpoint.EndpointBehaviors.Add(
                        new TransportClientEndpointBehavior(TokenProvider.CreateSharedAccessSignatureTokenProvider(sendToken)));

                    using (var clientChannel = factory.CreateChannel())
                    {
                        int numberOfMessages = random.Next(10, 30);
                        Console.WriteLine("Sending {0} messages to {1}...", numberOfMessages, queueName);
                        
                        // Send messages to queue
                        for (var i = 0; i < numberOfMessages; i++)
                        {
                            var data = Guid.NewGuid().ToString();
                            clientChannel.Process(data);
                            Console.WriteLine("{0}: Message [{1}].", "Send", data);
                        }

                        // Close sender
                        clientChannel.Close();
                    }
                    factory.Close();
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine("Exception occurred: {0}", exception);
            }

            Console.WriteLine("\nSender complete.");
            Console.WriteLine("\nPress [Enter] to exit.");
            Console.ReadLine();
        }
    }
}