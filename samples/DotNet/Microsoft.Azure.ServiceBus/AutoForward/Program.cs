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

namespace MessagingSamples
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Core;
    using System.Text;

    class Program : IConnectionStringSample
    {
        string sharedAccessRuleKey;

        public async Task Run(string connectionString)
        {
           
            Console.WriteLine("\nSending messages\n");

            var topicSender = new MessageSender(connectionString, "SourceTopic");
            await topicSender.SendAsync(CreateMessage("M1"));

            var queueSender = new MessageSender(connectionString, "TargetQueue");
            await queueSender.SendAsync(CreateMessage("M1"));


            var targetQueueReceiver = new MessageReceiver(connectionString, "TargetQueue");
            while (true)
            {
                var message = await targetQueueReceiver.ReceiveAsync(TimeSpan.FromSeconds(10));
                if (message != null)
                {
                    await this.PrintReceivedMessage(message);
                    await targetQueueReceiver.CompleteAsync(message.SystemProperties.LockToken);
                }
                else
                {
                    break;
                }
            }
            await targetQueueReceiver.CloseAsync();

            Console.WriteLine("\nPress ENTER to delete topics and exit\n");
            Console.ReadLine();
        }

        async Task PrintReceivedMessage(Message receivedMessage)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            await Console.Out.WriteLineAsync(string.Format("Received message:\n" + "\tLabel:\t{0}\n" + "\tBody:\t{1}\n", receivedMessage.Label, receivedMessage.GetBody<string>()));
            foreach (var p in receivedMessage.UserProperties)
            {
                await Console.Out.WriteLineAsync(string.Format("\tProperty:\t{0} = {1}", p.Key, p.Value));
            }
            Console.ResetColor();
        }
        
        // Create a new Service Bus message.
        public static Message CreateMessage(string label)
        {
            // Creat1e a Service Bus message.
            var msg = new Message(Encoding.UTF8.GetBytes("This is the body of message \"" + label + "\"."));
            msg.UserProperties.Add("Priority", 1);
            msg.UserProperties.Add("Importance", "High");
            msg.Label = label;
            msg.TimeToLive = TimeSpan.FromSeconds(90);
            return msg;
        }
        
    }
}

