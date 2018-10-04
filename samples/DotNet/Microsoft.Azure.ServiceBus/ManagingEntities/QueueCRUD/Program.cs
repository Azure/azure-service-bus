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

namespace QueueCRUD
{
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Management;
    using System;
    using System.Threading.Tasks;

    public class Program : MessagingSamples.Sample
    {
        ManagementClient managementClient;

        public async Task RunAsync(string connectionString)
        {
            this.managementClient = new ManagementClient(connectionString);

            var queueName = Guid.NewGuid().ToString("D").Substring(0, 8);

            Console.WriteLine($"Creating a new Queue with name - {queueName}");
            await CreateQueueAsync(queueName).ConfigureAwait(false);

            Console.WriteLine("Retrieving the created queue");
            var getQueue = await GetQueueAsync(queueName).ConfigureAwait(false);

            Console.WriteLine($"Updating few properties of the queue");
            await UpdateQueueAsync(getQueue).ConfigureAwait(false);

            Console.WriteLine("Retrieving runtime information of the queue");
            await GetQueueRuntimeInfoAsync(queueName).ConfigureAwait(false);

            Console.WriteLine("Deleting the queue");
            await DeleteQueueAsync(queueName);

            await this.managementClient.CloseAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a new Queue using the managementClient with the name provided.
        /// </summary>
        private async Task CreateQueueAsync(string queueName)
        {
            // All the values have defaults and hence optional. 
            // The only required parameter is the path of the queue (in this case, queueName)
            var queueDescription = new QueueDescription(queueName)
            {
                // The duration of a peek lock; that is, the amount of time that a message is locked from other receivers.
                LockDuration = TimeSpan.FromSeconds(45),

                // Size of the Queue. For non-partitioned entity, this would be the size of the queue. 
                // For partitioned entity, this would be the size of each partition.
                MaxSizeInMB = 2048,

                // This value indicates if the queue requires guard against duplicate messages. 
                // Find out more in DuplicateDetection sample
                RequiresDuplicateDetection = false,

                //Since RequiresDuplicateDetection is false, the following need not be specified and will be ignored.
                //DuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(2),

                // This indicates whether the queue supports the concept of session.
                // Find out more in "Session and Workflow Management Features" sample
                RequiresSession = false,

                // The default time to live value for the messages
                // Find out more in "TimeToLive" sample.
                DefaultMessageTimeToLive = TimeSpan.FromDays(7),

                // Duration of idle interval after which the queue is automatically deleted. 
                AutoDeleteOnIdle = TimeSpan.MaxValue,

                // Decides whether an expired message due to TTL should be dead-letterd
                // Find out more in "TimeToLive" sample.
                EnableDeadLetteringOnMessageExpiration = false,

                // The maximum delivery count of a message before it is dead-lettered
                // Find out more in "DeadletterQueue" sample
                MaxDeliveryCount = 8,

                // Creating only one partition. 
                // Find out more in PartitionedQueues sample.
                EnablePartitioning = false
            };

            try
            {
                QueueDescription createdQueue = await managementClient.CreateQueueAsync(queueName).ConfigureAwait(false);
            }
            catch (ServiceBusException ex)
            {
                Console.WriteLine($"Encountered exception while creating Queue -\n{ex}");
                throw;
            }
        }

        /// <summary>
        /// Retrieve a queue
        /// </summary>
        private async Task<QueueDescription> GetQueueAsync(string queueName)
        {
            try
            {
                QueueDescription getQueue = await managementClient.GetQueueAsync(queueName).ConfigureAwait(false);
                return getQueue;
            }
            catch (ServiceBusException ex)
            {
                Console.WriteLine($"Encountered exception while retrieving Queue -\n{ex}");
                throw;
            }
        }

        // Note - the following properties cannot be updated once created -
        // - Path
        // - RequiresSession
        // - EnablePartitioning
        private async Task UpdateQueueAsync(QueueDescription queueDescription)
        {
            try
            {
                Console.WriteLine($"Before update - MaxDeliveryCount:{queueDescription.MaxDeliveryCount}; " +
                    $"LockDuration:{queueDescription.LockDuration}");

                // Updating the properties of the queue.
                queueDescription.MaxDeliveryCount = 15;
                queueDescription.LockDuration = TimeSpan.FromMinutes(5);

                QueueDescription updatedQueue = await managementClient.UpdateQueueAsync(queueDescription).ConfigureAwait(false);

                Console.WriteLine($"After update - MaxDeliveryCount:{updatedQueue.MaxDeliveryCount}; " +
                    $"LockDuration:{updatedQueue.LockDuration}");
            }
            catch (ServiceBusException ex)
            {
                Console.WriteLine($"Encountered exception while updating Queue -\n{ex}");
                throw;
            }
        }

        private async Task GetQueueRuntimeInfoAsync(string queueName)
        {
            try
            {
                var queueRuntimeInfo = await managementClient.GetQueueRuntimeInfoAsync(queueName).ConfigureAwait(false);
                Console.WriteLine($"Retrieved runtime information of queue\n " +
                    $"Active messages:{queueRuntimeInfo.MessageCountDetails.ActiveMessageCount}\n " +
                    $"Size of queue:{queueRuntimeInfo.SizeInBytes}\n" +
                    $"Queue Creation time: {queueRuntimeInfo.CreatedAt}\n" +
                    $"Queue last updation time: {queueRuntimeInfo.UpdatedAt}\n");
            }
            catch (ServiceBusException ex)
            {
                Console.WriteLine($"Encountered exception while retrieving runtime information for Queue -\n{ex}");
                throw;
            }
        }

        private async Task DeleteQueueAsync(string queueName)
        {
            try
            {
                await this.managementClient.DeleteQueueAsync(queueName).ConfigureAwait(false);
            }
            catch (ServiceBusException ex)
            {
                Console.WriteLine($"Encountered exception while deleting Queue -\n{ex}");
                throw;
            }
        }

        public static int Main(string[] args)
        {
            try
            {
                var app = new Program();
                app.RunSample(args, app.RunAsync);
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
