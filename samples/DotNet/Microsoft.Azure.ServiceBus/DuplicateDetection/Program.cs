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

namespace DuplicateDetection
{
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Core;
    using System;
    using System.Threading.Tasks;

    // This sample illustrates the "duplicate detection" feature 
    // of Azure Service Bus.
    public class Program : MessagingSamples.Sample
    {
        public async Task Run(string connectionString)
        {
            // first send the two messages
            await Send(connectionString);
            // then retrieve the messages
            await Receive(connectionString);
        }

        static async Task Send(string connectionString)
        {
            // Create a sender over the previously configured duplicate-detection
            // enabled queue.
            var sender = new MessageSender(connectionString, DupdetectQueueName);

            // Create the message-id 
            string messageId = Guid.NewGuid().ToString();
            
            Console.WriteLine("\tSending messages to {0} ...", sender.Path);
            // send the first message using the message-id
            var message = new Message
            {
                MessageId = messageId,
                TimeToLive = TimeSpan.FromMinutes(1)
            };
            await sender.SendAsync(message);
            Console.WriteLine("\t=> Sent a message with messageId {0}", message.MessageId);

            // send the second message using the message-id
            var message2 = new Message
            {
                MessageId = messageId,
                TimeToLive = TimeSpan.FromMinutes(1)
            };
            await sender.SendAsync(message2);
            Console.WriteLine("\t=> Sent a duplicate message with messageId {0}", message.MessageId);
            await sender.CloseAsync();
        }
        static async Task Receive(string connectionString)
        {
            // create receiver over the duplicate-detection enabled queue
            var receiver = new MessageReceiver(connectionString, DupdetectQueueName, ReceiveMode.PeekLock);

            
            var receivedMessageId = "";

            Console.WriteLine("\n\tWaiting up to 5 seconds for messages from {0} ...", receiver.Path);
            while (true)
            {
                // receive a message
                var receivedMessage = await receiver.ReceiveAsync(TimeSpan.FromSeconds(5));
                if (receivedMessage == null)
                {
                    // if the message is null, the queue was empty
                    break;
                }
                // complete the received message
                Console.WriteLine("\t<= Received a message with messageId {0}", receivedMessage.MessageId);
                await receiver.CompleteAsync(receivedMessage.SystemProperties.LockToken);

                if (receivedMessageId.Equals(receivedMessage.MessageId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception("Received a duplicate messsage");
                }
                receivedMessageId = receivedMessage.MessageId;
            }
            Console.WriteLine("\tDone receiving messages from {0}", receiver.Path);

            await receiver.CloseAsync();
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