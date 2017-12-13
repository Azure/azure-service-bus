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

namespace GeoSenderPassiveReplication
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;

    public class Program : MessagingSamples.Sample
    {
        readonly object swapMutex = new object();
        QueueClient activeQueueClient;
        QueueClient backupQueueClient;

        public async Task Run(string connectionString)
        {
            var primaryFactory = MessagingFactory.CreateFromConnectionString(connectionString);
            var secondaryFactory = MessagingFactory.CreateFromConnectionString(connectionString);

            this.activeQueueClient = primaryFactory.CreateQueueClient(BasicQueueName);
            this.backupQueueClient = secondaryFactory.CreateQueueClient(BasicQueue2Name);

            try
            {
                // Create a primary and secondary queue client.

                // forcing an error after 5 seconds by taking down the primary factory
                // usually, errors will be more transient
#pragma warning disable 4014
                Task.Delay(5000).ContinueWith(
                    t => primaryFactory.Abort()
                    );
#pragma warning restore 4014


                Console.WriteLine("\nSending messages to primary or secondary queue...\n");

                for (var i = 1; i <= 500; i++)
                {
                    // Create brokered message.
                    var message = new BrokeredMessage("Message" + i)
                    {
                        MessageId = i.ToString(),
                        TimeToLive = TimeSpan.FromMinutes(2.0)
                    };
                    var m1 = message;

                    try
                    {
                        await this.SendMessage(m1);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Unable to send to primary or secondary queue: Exception {0}", e);
                    }
                }
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

        // Send message to active queue. If this fails, send message to backup queue.
        // If the send operation to the backup queue succeeds, make the backup queue the new active queue.
        async Task SendMessage(BrokeredMessage m1, int maxSendRetries = 10)
        {
            do
            {
                var m2 = m1.Clone();
                try
                {
                    await this.activeQueueClient.SendAsync(m1);
                    return;
                }
                catch
                {
                    if (--maxSendRetries <= 0)
                    {
                        throw;
                    }

                    lock (this.swapMutex)
                    {
                        var c = this.activeQueueClient;
                        this.activeQueueClient = this.backupQueueClient;
                        this.backupQueueClient = c;
                    }
                    m1 = m2.Clone();
                }
            }
            while (true);
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