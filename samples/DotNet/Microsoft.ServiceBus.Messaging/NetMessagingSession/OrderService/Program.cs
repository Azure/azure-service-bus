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

namespace NetMessagingSessionService
{
    using System;
    using System.ServiceModel;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus;
    using System.Linq;

    public class Program : MessagingSamples.Sample
    {
        public async Task Run(string connectionString)
        {
            var sbb = new ServiceBusConnectionStringBuilder(connectionString);
            
            // Create MessageReceiver for queue which requires session
            Console.WriteLine("Ready to receive messages from {0}...", SessionQueueName);

            // Creating the service host object as defined in config
            using (var serviceHost = new ServiceHost(typeof(SequenceProcessingService), new Uri(sbb.GetAbsoluteRuntimeEndpoints()[0], SessionQueueName)))
            {
                var authBehavior = new TransportClientEndpointBehavior(TokenProvider.CreateSharedAccessSignatureTokenProvider(sbb.SharedAccessKeyName, sbb.SharedAccessKey));
                serviceHost.Description.Behaviors.Add(new ErrorServiceBehavior());
                foreach (var ep in serviceHost.Description.Endpoints) { ep.EndpointBehaviors.Add(authBehavior); }

                // Subscribe to the faulted event.
                serviceHost.Faulted += serviceHost_Faulted;

                // Start service
                serviceHost.Open();

                Console.WriteLine("\nPress [Enter] to close ServiceHost.");
                await Task.WhenAny(
                    Task.Run(() => Console.ReadKey()),
                    Task.Delay(TimeSpan.FromSeconds(10))
                );

                // Close the service
                serviceHost.Close();
            }
        }

        static void serviceHost_Faulted(object sender, EventArgs e)
        {
            Console.WriteLine("Fault occurred. Aborting the service host object ...");
            ((ServiceHost)sender).Abort();
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