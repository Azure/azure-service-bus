// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BasicSessionSendReceiveUsingQueueClient
{
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Primitives;
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    class Program
    {
        const string ServiceBusConnectionString = "{Service Bus connection string}";
        const string QueueName = "{Queue Name of a Queue that supports sessions}";
        static IQueueClient queueClient;

        static void Main(string[] args)
        {
            MainAsync().GetAwaiter().GetResult();
        }

        static async Task MainAsync()
        {
            const int numberOfSessions = 5;
            const int numberOfMessagesPerSession = 3;

            queueClient = new QueueClient(ServiceBusConnectionString, QueueName);

            Console.WriteLine("======================================================");
            Console.WriteLine("Press any key to exit after receiving all the messages.");
            Console.WriteLine("======================================================");

            // Register Session Handler and Receive Session Messages
            RegisterOnSessionHandlerAndReceiveSessionMessages();

            // Send messages with sessionId set
            await SendSessionMessagesAsync(numberOfSessions, numberOfMessagesPerSession);

            Console.ReadKey();

            await queueClient.CloseAsync();
        }

        static void RegisterOnSessionHandlerAndReceiveSessionMessages()
        {
            // Configure the SessionHandler Options in terms of exception handling, number of concurrent sessions to deliver etc.
            SessionHandlerOptions sessionHandlerOptions =
                    new SessionHandlerOptions(ExceptionReceivedHandler)
                    {
                        MaxConcurrentSessions = 2,
                        MessageWaitTimeout = TimeSpan.FromSeconds(1),
                        AutoComplete = false
                    };

            // Register the function that will process session messages
            queueClient.RegisterSessionHandler(ProcessSessionMessagesAsync, sessionHandlerOptions);
        }

        static async Task ProcessSessionMessagesAsync(IMessageSession session, Message message, CancellationToken token)
        {
            Console.WriteLine($"Received Session: {session.SessionId} message: SequenceNumber: {message.SystemProperties.SequenceNumber}");

            // Complete the message so that it is not received again.
            // This can be done only if the queueClient is created in ReceiveMode.PeekLock mode (which is default).
            await session.CompleteAsync(message.SystemProperties.LockToken);

            // Note: Use the cancellationToken passed as necessary to determine if the queueClient has already been closed.
            // If queueClient has already been Closed, you may chose to not call CompleteAsync() or AbandonAsync() etc. calls 
            // to avoid unnecessary exceptions.
        }

        // Use this Handler to look at the exceptions received on the MessagePump
        static Task ExceptionReceivedHandler(ExceptionReceivedEventArgs exceptionReceivedEventArgs)
        {
            Console.WriteLine($"Message handler encountered an exception {exceptionReceivedEventArgs.Exception}.");
            var context = exceptionReceivedEventArgs.ExceptionReceivedContext;
            Console.WriteLine("Exception context for troubleshooting:");
            Console.WriteLine($"- Endpoint: {context.Endpoint}");
            Console.WriteLine($"- Entity Path: {context.EntityPath}");
            Console.WriteLine($"- Executing Action: {context.Action}");
            return Task.CompletedTask;
        }

        static async Task SendSessionMessagesAsync(int numberOfSessions, int messagesPerSession)
        {
            const string SessionPrefix = "session";

            if (numberOfSessions == 0 || messagesPerSession == 0)
            {
                await Task.FromResult(false);
            }

            for (int i = 0; i < numberOfSessions; i++)
            {
                var messagesToSend = new List<Message>();
                string sessionId = SessionPrefix + i;
                for (int j = 0; j < messagesPerSession; j++)
                {
                    // Create a new message to send to the queue
                    var message = new Message(Encoding.UTF8.GetBytes("test" + j));
                    message.Label = "test" + j;
                    // Assign a SessionId for the message
                    message.SessionId = sessionId;
                    messagesToSend.Add(message);

                    // Write the sessionId, body of the message to the console
                    Console.WriteLine($"Sending SessionId: {message.SessionId}, message: {Encoding.UTF8.GetString(message.Body)}");
                }

                // Send a batch of messages corresponding to this sessionId to the queue
                await queueClient.SendAsync(messagesToSend);
            }

            Console.WriteLine($"Sent {messagesPerSession} messages each for {numberOfSessions} sessions.");
        }
    }
}