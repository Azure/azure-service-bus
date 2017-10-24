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

namespace WorkerRole1
{
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure;
    using Microsoft.ServiceBus.Messaging;
    using Microsoft.WindowsAzure.ServiceRuntime;

    public class WorkerRole : RoleEntryPoint
    {
        // The name of your queue
        const string QueueName = "BasicQueue";
        readonly ManualResetEvent completedEvent = new ManualResetEvent(false);
        // QueueClient is thread-safe. Recommended that you cache 
        // rather than recreating it on every request
        QueueClient client;

        public override void Run()
        {
            Trace.WriteLine("Starting processing of messages");

            // Initiates the message pump and callback is invoked for each message that is received, calling close on the client will stop the pump.
            this.client.OnMessageAsync(
                async receivedMessage =>
                {
                    // do work
                    await Task.Delay(1);

                    Trace.WriteLine(
                        string.Format(
                            "Got Message Id:{0}, Sequence:{1}, Token:{2}, Label:{3}",
                            string.IsNullOrWhiteSpace(receivedMessage.MessageId) ? "null" : receivedMessage.MessageId,
                            receivedMessage.SequenceNumber,
                            receivedMessage.LockToken,
                            receivedMessage.Label));
                });

            this.completedEvent.WaitOne();
        }

        public override bool OnStart()
        {
            // Create the queue if it does not exist already
            string connectionString = CloudConfigurationManager.GetSetting("Microsoft.ServiceBus.ConnectionString");

            // Initialize the connection to Service Bus Queue
            this.client = QueueClient.CreateFromConnectionString(connectionString, QueueName);
            return base.OnStart();
        }

        public override void OnStop()
        {
            // Close the connection to Service Bus Queue
            this.client.Close();
            this.completedEvent.Set();
            base.OnStop();
        }
    }
}