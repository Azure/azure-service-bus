﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace SendReceiveUsingMessageSenderReceiver
{
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Core;
    using System;
    using System.Text;
    using System.Threading.Tasks;

    class Program
    {
        const string ServiceBusConnectionString = "{Service Bus connection string}";
        const string QueueName = "{Queue Name}";
        static MessageSender messageSender;
        static MessageReceiver messageReceiver;

        static void Main(string[] args)
        {
            MainAsync().GetAwaiter().GetResult();
        }

        static async Task MainAsync()
        {
            const int numberOfMessages = 10;
            messageSender = new MessageSender(ServiceBusConnectionString, QueueName);
            messageReceiver = new MessageReceiver(ServiceBusConnectionString, QueueName, ReceiveMode.PeekLock);

            Console.WriteLine("======================================================");
            Console.WriteLine("Press any key to exit after receiving all the messages.");
            Console.WriteLine("======================================================");      
            
            // Send Messages
            await SendMessagesAsync(numberOfMessages);

            // Receive Messages
            await ReceiveMessagesAsync(numberOfMessages);

            Console.ReadKey();

            await messageSender.CloseAsync();
            await messageReceiver.CloseAsync();
        }

        static async Task ReceiveMessagesAsync(int numberOfMessagesToReceive)
        {
            while(numberOfMessagesToReceive-- > 0)
            {
                // Receive the message
                Message message = await messageReceiver.ReceiveAsync();

                // Process the message
                Console.WriteLine($"Received message: SequenceNumber:{message.SystemProperties.SequenceNumber} Body:{Encoding.UTF8.GetString(message.Body)}");

                // Complete the message so that it is not received again.
                // This can be done only if the MessageReceiver is created in ReceiveMode.PeekLock mode.
                await messageReceiver.CompleteAsync(message.SystemProperties.LockToken);
            }
        }

        static async Task SendMessagesAsync(int numberOfMessagesToSend)
        {
            try
            {
                for (var i = 0; i < numberOfMessagesToSend; i++)
                {
                    // Create a new message to send to the queue
                    var message = new Message(Encoding.UTF8.GetBytes($"Message {i}"));

                    // Write the body of the message to the console
                    Console.WriteLine($"Sending message: {Encoding.UTF8.GetString(message.Body)}");

                    // Send the message to the queue
                    await messageSender.SendAsync(message);
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine($"{DateTime.Now} :: Exception: {exception.Message}");
            }
        }
    }
}