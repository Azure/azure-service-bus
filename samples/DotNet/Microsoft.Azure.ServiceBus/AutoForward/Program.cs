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
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Core;
    using System;
    using System.Text;
    using System.Threading.Tasks;

    // This sample demonstrates how to automatically forward messages from a queue,
    // subscription, or deadletter queue into another queue or topic. 
    // The sample assumes prior setup of a topology of Service Bus entities 
    // as described in the accompanying README file.
    public class Program : MessagingSamples.Sample
    {
        public async Task Run(string connectionString)
        {
            Console.WriteLine("\nSending messages\n");

            // Create sender and send message M1 into the source topic
            var topicSender = new MessageSender(connectionString, "AutoForwardSourceTopic");
            await topicSender.SendAsync(CreateMessage("M1"));

            // Create sender and send message M2 directly into the target queue
            var queueSender = new MessageSender(connectionString, "AutoForwardTargetQueue");
            await queueSender.SendAsync(CreateMessage("M2"));

            // Create the receiver on the target queue
            Console.WriteLine("\nReceiving messages\n");
            var targetQueueReceiver = new MessageReceiver(connectionString, "AutoForwardTargetQueue");
            for (int i = 0; i < 2; i++)
            {
                // We are expecting twp messages to arrive into the target queue. 
                // 1) Message M2 has been sent directly
                // 2) Message M1 has been auto-forwarded from a subscription on the source topic
                var message = await targetQueueReceiver.ReceiveAsync(TimeSpan.FromSeconds(10));
                if (message != null)
                {
                    await this.PrintReceivedMessage(message);
                    await targetQueueReceiver.CompleteAsync(message.SystemProperties.LockToken);
                }
                else
                {
                    throw new Exception("Expected message not received.");
                }
            }
            await targetQueueReceiver.CloseAsync();
        }

        async Task PrintReceivedMessage(Message receivedMessage)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            await Console.Out.WriteLineAsync(string.Format("Received message:\n" + "\tLabel:\t{0}\n" + "\tBody:\t{1}\n", receivedMessage.Label, Encoding.UTF8.GetString(receivedMessage.Body)));
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

