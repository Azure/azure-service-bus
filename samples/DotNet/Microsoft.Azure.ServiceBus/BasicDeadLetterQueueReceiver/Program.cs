// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BasicDeadLetterQueueReceiver
{
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Core;
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    class Program
    {
        // Connection String for the namespace can be obtained from the Azure portal under the 
        // 'Shared Access policies' section.
        const string ServiceBusConnectionString = "{ServiceBus connection string}";
        const string EntityPath = "{Queue Name or Subscription Path}";
        static IMessageReceiver deadLetterReceiver;

        static void Main(string[] args)
        {
            MainAsync().GetAwaiter().GetResult();
        }

        static async Task MainAsync()
        {
            deadLetterReceiver = new MessageReceiver(ServiceBusConnectionString, EntityNameHelper.FormatDeadLetterPath(EntityPath), ReceiveMode.PeekLock);

            Console.WriteLine("==========================================================================");
            Console.WriteLine("Press any key to exit after processing all the dead letter queue messages.");
            Console.WriteLine("==========================================================================");
            
            // Register QueueClient's DLQ MessageHandler 
            RegisterDeadLetterQueueMessageHandler();
                        
            Console.ReadKey();
            
            await deadLetterReceiver.CloseAsync();
        }
        
        static void RegisterDeadLetterQueueMessageHandler()
        {
            // Configure the MessageHandler Options in terms of exception handling, number of concurrent messages to deliver etc.
            var messageHandlerOptions = new MessageHandlerOptions(ExceptionReceivedHandler)
            {
                // Maximum number of Concurrent calls to the callback `ProcessMessagesAsync`, set to 1 for simplicity.
                // Set it according to how many messages the application wants to process in parallel.
                MaxConcurrentCalls = 1,

                // Indicates whether MessagePump should automatically complete the messages after returning from User Callback.
                // False below indicates the Complete will be handled by the User Callback as in `ProcessMessagesAsync` below.
                AutoComplete = false
            };

            // Register the function that will process messages
            deadLetterReceiver.RegisterMessageHandler(ProcessDeadLetterQueueMessagesAsync, messageHandlerOptions);
        }        

        static async Task ProcessDeadLetterQueueMessagesAsync(Message message, CancellationToken token)
        {
            // Process the message
            // This could include retries or logging for further troubleshooting
            Console.WriteLine($"Received DLQ message: SequenceNumber:{message.SystemProperties} Body:{Encoding.UTF8.GetString(message.Body)}");

            // Complete the message so that it is not received again.
            // This can be done only if the deadLetterReceiver is created in ReceiveMode.PeekLock mode (which is default).
            await deadLetterReceiver.CompleteAsync(message.SystemProperties.LockToken);

            // Note: Use the cancellationToken passed as necessary to determine if the deadLetterReceiver has already been closed.
            // If deadLetterReceiver has already been Closed, you may chose to not call CompleteAsync() or AbandonAsync() etc. calls 
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
    }
}