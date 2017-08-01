// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace SessionSendReceiveUsingSessionClient
{
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Core;
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;

    class Program
    {
        // Connection String for the namespace can be obtained from the Azure portal under the 
        // 'Shared Access policies' section.
        const string ServiceBusConnectionString = "{ServiceBus connection string}";
        const string QueueName = "{Queue Name of a Queue that supports sessions}";
        static IMessageSender messageSender;
        static ISessionClient sessionClient;
        const string SessionPrefix = "session-prefix";

        static void Main(string[] args)
        {
            MainAsync().GetAwaiter().GetResult();
        }

        static async Task MainAsync()
        {
            const int numberOfSessions = 5;
            const int numberOfMessagesPerSession = 3;

            messageSender = new MessageSender(ServiceBusConnectionString, QueueName);
            sessionClient = new SessionClient(ServiceBusConnectionString, QueueName);

            // Send messages with sessionId set
            await SendSessionMessagesAsync(numberOfSessions, numberOfMessagesPerSession);

            // Receive all Session based messages using SessionClient
            await ReceiveSessionMessagesAsync(numberOfSessions, numberOfMessagesPerSession);

            Console.WriteLine("=========================================================");
            Console.WriteLine("Completed Receiving all messages... Press any key to exit");
            Console.WriteLine("=========================================================");

            Console.ReadKey();

            await messageSender.CloseAsync();
            await sessionClient.CloseAsync();
        }

        static async Task ReceiveSessionMessagesAsync(int numberOfSessions, int messagesPerSession)
        {
            Console.WriteLine("===================================================================");
            Console.WriteLine("Accepting sessions in the reverse order of sends for demo purposes");
            Console.WriteLine("===================================================================");

            for (int i = 0; i < numberOfSessions; i++)
            {
                int messagesReceivedPerSession = 0;

                // AcceptMessageSessionAsync(i.ToString()) as below with session id as parameter will try to get a session with that sessionId.
                // AcceptMessageSessionAsync() without any messages will try to get any available session with messages associated with that session.
                IMessageSession session = await sessionClient.AcceptMessageSessionAsync(SessionPrefix + i.ToString());

                if(session != null)
                {
                    // Messages within a session will always arrive in order.
                    Console.WriteLine("=====================================");
                    Console.WriteLine($"Received Session: {session.SessionId}");                    

                    while (messagesReceivedPerSession++ < messagesPerSession)
                    {
                        Message message = await session.ReceiveAsync();

                        Console.WriteLine($"Received message: SequenceNumber:{message.SystemProperties.SequenceNumber} Body:{Encoding.UTF8.GetString(message.Body)}");

                        // Complete the message so that it is not received again.
                        // This can be done only if the queueClient is created in ReceiveMode.PeekLock mode (which is default).
                        await session.CompleteAsync(message.SystemProperties.LockToken);
                    }

                    Console.WriteLine($"Received all messages for Session: {session.SessionId}");
                    Console.WriteLine("=====================================");

                    // Close the Session after receiving all messages from the session
                    await session.CloseAsync();
                }
            }
        }

        static async Task SendSessionMessagesAsync(int numberOfSessions, int messagesPerSession)
        {      
            if (numberOfSessions == 0 || messagesPerSession == 0)
            {
                await Task.FromResult(false);
            }

            for (int i = numberOfSessions - 1; i >= 0; i--)
            {
                var messagesToSend = new List<Message>();
                string sessionId = SessionPrefix + i;
                for (int j = 0; j < messagesPerSession; j++)
                {
                    // Create a new message to send to the queue
                    string messageBody = "test" + j;
                    var message = new Message(Encoding.UTF8.GetBytes(messageBody));
                    // Assign a SessionId for the message
                    message.SessionId = sessionId;
                    messagesToSend.Add(message);

                    // Write the sessionId, body of the message to the console
                    Console.WriteLine($"Sending SessionId: {message.SessionId}, message: {messageBody}");
                }

                // Send a batch of messages corresponding to this sessionId to the queue
                await messageSender.SendAsync(messagesToSend);
            }

            Console.WriteLine("=====================================");
            Console.WriteLine($"Sent {messagesPerSession} messages each for {numberOfSessions} sessions.");
            Console.WriteLine("=====================================");
        }
    }
}