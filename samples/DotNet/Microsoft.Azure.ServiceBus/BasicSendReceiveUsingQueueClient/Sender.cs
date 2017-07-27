// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BasicSendReceiveUsingQueueClient
{
    using Microsoft.Azure.ServiceBus;
    using System;
    using System.Text;
    using System.Threading.Tasks;

    class Sender
    {
        readonly QueueClient queueClient;

        public Sender(string serviceBusConnectionString, string queueName)
        {
            queueClient = new QueueClient(serviceBusConnectionString, queueName);
        }

        public async Task SendMessagesAsync(int numberOfMessagesToSend)
        {
            for (var i = 0; i < numberOfMessagesToSend; i++)
            {
                try
                {
                    // Create a new message to send to the queue
                    var message = new Message(Encoding.UTF8.GetBytes($"Message {i}"));

                    // Write the body of the message to the console
                    Console.WriteLine($"Sending message: {Encoding.UTF8.GetString(message.Body)}");

                    // Send the message to the queue
                    await queueClient.SendAsync(message);
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"{DateTime.Now} > Exception: {exception.Message}");
                }

                // Delay by 10 milliseconds so that the console can keep up
                await Task.Delay(10);
            }
        }

        public async Task CloseAsync()
        {
            await this.queueClient.CloseAsync();
        }
    }
}
