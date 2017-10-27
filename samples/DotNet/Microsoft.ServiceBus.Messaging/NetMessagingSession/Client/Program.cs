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

namespace NetMessagingSessionClient
{
    using System;
    using System.ServiceModel;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;
    using NetMessagingSessionService;

    public class Program : MessagingSamples.Sample
    {
        public async Task Run(string connectionString)
        {
            var sbb = new ServiceBusConnectionStringBuilder(connectionString);

            try
            {
                // Create sender to Sequence Service
                using (var sendChannelFactory = new ChannelFactory<ISequenceServiceChannel>("sequenceSendClient"))
                {
                    sendChannelFactory.Endpoint.Address = new EndpointAddress(
                        new Uri(sbb.GetAbsoluteRuntimeEndpoints()[0], SessionQueueName));
                    sendChannelFactory.Endpoint.EndpointBehaviors.Add(
                       new TransportClientEndpointBehavior(TokenProvider.CreateSharedAccessSignatureTokenProvider(sbb.SharedAccessKeyName, sbb.SharedAccessKey)));

                    using (var clientChannel = sendChannelFactory.CreateChannel())
                    {

                        for (int j = 0; j < 3; j++)
                        {
                            var contextId = Guid.NewGuid().ToString();


                            // Send messages
                            var sequenceLength = new Random().Next(5, 10);
                            for (var i = 0; i < sequenceLength; i++)
                            {

                                // Generating a random sequence item
                                var sequenceItem = new SequenceItem(
                                    string.Format("{0:00000}", new Random().Next(0, 10000)),
                                    new Random().Next(1, 100));

                                // set the operation context for the subsequent call, this MUST be a new context
                                OperationContext.Current = new OperationContext(clientChannel)
                                {
                                    OutgoingMessageProperties = { { BrokeredMessageProperty.Name, new BrokeredMessageProperty { SessionId = contextId, TimeToLive = TimeSpan.FromMinutes(5) } } }
                                };
                                // Correlating ServiceBus SessionId to ContextId 
                                await clientChannel.SubmitSequenceItemAsync(sequenceItem);

                                Console.WriteLine("Sequence: {0} [{1}] - ContextId {2}.", sequenceItem.ItemId, sequenceItem.Quantity, contextId);
                            }

                            // set the operation context for the subsequent call, this MUST be a new context
                            OperationContext.Current = new OperationContext(clientChannel)
                            {
                                OutgoingMessageProperties = { { BrokeredMessageProperty.Name, new BrokeredMessageProperty { SessionId = contextId, TimeToLive = TimeSpan.FromMinutes(5) } } }
                            };
                            await clientChannel.TerminateSequenceAsync();
                        }
                        clientChannel.Close();
                    }
                    // Close sender
                    sendChannelFactory.Close();
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine("Exception occurred: {0}", exception);
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
    }
}