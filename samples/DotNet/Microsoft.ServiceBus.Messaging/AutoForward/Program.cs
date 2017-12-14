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

namespace AutoForward
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;

    public class Program : MessagingSamples.Sample
    {
        public async Task Run(string connectionString)
        {
            Console.WriteLine("\nSending messages\n");

            var messagingFactory = MessagingFactory.CreateFromConnectionString(connectionString);
            var topicSender = await messagingFactory.CreateMessageSenderAsync("AutoForwardSourceTopic");
            await topicSender.SendAsync(CreateMessage("M1"));

            var queueSender = await messagingFactory.CreateMessageSenderAsync("AutoForwardTargetQueue");
            await queueSender.SendAsync(CreateMessage("M1"));

            var targetQueueReceiver = messagingFactory.CreateQueueClient("AutoForwardTargetQueue");
            for (int i = 0; i < 2; i++)
            {
                var message = await targetQueueReceiver.ReceiveAsync(TimeSpan.FromSeconds(10));
                if (message != null)
                {
                    await this.PrintReceivedMessage(message);
                    await message.CompleteAsync();
                }
                else
                {
                    throw new Exception("Expected message not received.");
                }
            }
            await targetQueueReceiver.CloseAsync();
            messagingFactory.Close();
        }

        async Task PrintReceivedMessage(BrokeredMessage receivedMessage)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            await Console.Out.WriteLineAsync(string.Format("Received message:\n" + "\tLabel:\t{0}\n" + "\tBody:\t{1}\n", receivedMessage.Label, receivedMessage.GetBody<string>()));
            foreach (var p in receivedMessage.Properties)
            {
                await Console.Out.WriteLineAsync(string.Format("\tProperty:\t{0} = {1}", p.Key, p.Value));
            }
            Console.ResetColor();
        }
        
        // Create a new Service Bus message.
        public static BrokeredMessage CreateMessage(string label)
        {
            // Create a Service Bus message.
            var msg = new BrokeredMessage("This is the body of message \"" + label + "\".");
            msg.Properties.Add("Priority", 1);
            msg.Properties.Add("Importance", "High");
            msg.Label = label;
            msg.TimeToLive = TimeSpan.FromSeconds(90);
            return msg;
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