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
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;
    using DurableSenderLibrary;

    public class Program : MessagingSamples.Sample
    {
        public async Task Run(string connectionString)
        {
            Trace.Listeners.Add(new ConsoleTraceListener());

            var sendFactory = MessagingFactory.CreateFromConnectionString(connectionString);

            // Create a durable sender.
            var durableSender = new DurableSender(sendFactory, DupdetectQueueName);

            /*
            ** Send messages.
            */

            // Example 1:
            // Send a message outside a transaction scope. If a transactional MSMQ send queue
            // is used, (Transactional = true) an internal MSMQ transaction is created.
            var nonTxMsg = CreateBrokeredMessage(1);
            Console.WriteLine("Sending message {0} outside of a transaction.", nonTxMsg.Label);
            durableSender.Send(nonTxMsg);

            // Example 2:
            // Send a message inside a transaction scope.
            var txMsg = CreateBrokeredMessage(2);
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
                var dtcMsg = CreateBrokeredMessage(i);
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

            var receiveFactory = MessagingFactory.CreateFromConnectionString(connectionString);
            var receiver = receiveFactory.CreateQueueClient(DupdetectQueueName, ReceiveMode.ReceiveAndDelete);
            for (var i = 1; i <= 4; i++)
            {
                try
                {
                    var msg = receiver.Receive();
                    if (msg != null)
                    {
                        PrintBrokeredMessage(msg);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Receiver: " + ex.Message);
                }
            }

            durableSender.Dispose();
            receiver.Close();
            sendFactory.Close();
        }

        // Create a new Service Bus message.
        public static BrokeredMessage CreateBrokeredMessage(int i)
        {
            // Create a Service Bus message.
            var msg = new BrokeredMessage("This is the body of message " + i);
            msg.Properties.Add("Priority", 1);
            msg.Properties.Add("Importance", "High");
            msg.Label = "M" + i;
            msg.TimeToLive = TimeSpan.FromSeconds(90);
            return msg;
        }

        // Print the Service Bus message.
        public static void PrintBrokeredMessage(BrokeredMessage msg)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Received message:");
            Console.WriteLine("   Label:    " + msg.Label);
            Console.WriteLine("   Body:     " + msg.GetBody<string>());
            Console.WriteLine("   Sent at:  " + msg.EnqueuedTimeUtc);
            Console.WriteLine("   ID:       " + msg.MessageId);
            Console.WriteLine("   SeqNum:   " + msg.SequenceNumber);
            foreach (var p in msg.Properties)
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