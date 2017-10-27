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

namespace DurableSenderClient
{
    using System;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using System.Transactions;
    using Microsoft.Azure.ServiceBus;
    using System.Text;
    using Microsoft.Azure.ServiceBus.Core;
    using DurableSenderLibrary;

    public class Program : MessagingSamples.Sample
    {
        public async Task Run(string connectionString)
        {
            Trace.Listeners.Add(new ConsoleTraceListener());

            // Create a durable sender.
            var durableSender = new DurableSender(connectionString, DupdetectQueueName);

            /*
            ** Send messages.
            */

            // Example 1:
            // Send a message outside a transaction scope. If a transactional MSMQ send queue
            // is used, (Transactional = true) an internal MSMQ transaction is created.
            var nonTxMsg = CreateMessage(1);
            Console.WriteLine("Sending message {0} outside of a transaction.", nonTxMsg.Label);
            durableSender.Send(nonTxMsg);

            // Example 2:
            // Send a message inside a transaction scope.
            var txMsg = CreateMessage(2);
            Console.WriteLine("Sending message {0} within a transaction.", txMsg.Label);
            using (var scope = new TransactionScope())
            {
                durableSender.Send(txMsg);
                scope.Complete();
            }

            // Example 3:
            // Send two messages inside a transaction scope. If another resource manager is used
            // (e.g., SQL server), the transaction is automatically promoted to a distributed
            // transaction. If a non-transactional MSMQ send queue is used, (TransactionalSend = false),
            // sending the message is not part of the transaction.
            for (var i = 3; i <= 4; i++)
            {
                var dtcMsg = CreateMessage(i);
                Console.WriteLine("Sending message {0} within a distributed transaction.", dtcMsg.Label);
                try
                {
                    using (var scope = new TransactionScope())
                    {
                        // SQL Server would be used here, for instance

                        // Send message.
                        durableSender.Send(dtcMsg);
                        scope.Complete();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Sender: " + ex.Message);
                }
            }

            /*
            ** Receive messages.
            */

            var receiver = new MessageReceiver(connectionString, DupdetectQueueName, ReceiveMode.ReceiveAndDelete);
            for (var i = 1; i <= 4; i++)
            {
                try
                {
                    var msg = await receiver.ReceiveAsync();
                    if (msg != null)
                    {
                        PrintMessage(msg);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Receiver: " + ex.Message);
                }
            }

            /*
            ** Cleanup
            */
            
            durableSender.Dispose();
            await receiver.CloseAsync();
        }

        // Create a new Service Bus message.
        public static Microsoft.Azure.ServiceBus.Message CreateMessage(int i)
        {
            // Create a Service Bus message.
            var msg = new Message(Encoding.UTF8.GetBytes("This is the body of message " + i));
            msg.UserProperties.Add("Priority", 1);
            msg.UserProperties.Add("Importance", "High");
            msg.Label = "M" + i;
            msg.TimeToLive = TimeSpan.FromSeconds(90);
            return msg;
        }

        // Print the Service Bus message.
        public static void PrintMessage(Message msg)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Received message:");
            Console.WriteLine("   Label:    " + msg.Label);
            Console.WriteLine("   Body:     " + Encoding.UTF8.GetString(msg.Body));
            Console.WriteLine("   Sent at:  " + msg.SystemProperties.EnqueuedTimeUtc);
            Console.WriteLine("   ID:       " + msg.MessageId);
            Console.WriteLine("   SeqNum:   " + msg.SystemProperties.SequenceNumber);
            foreach (var p in msg.UserProperties)
            {
                Console.WriteLine("   Property: " + p.Key + " = " + p.Value);
            }
            Console.ForegroundColor = ConsoleColor.Gray;
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