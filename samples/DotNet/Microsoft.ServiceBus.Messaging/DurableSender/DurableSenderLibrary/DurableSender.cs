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

namespace DurableSenderLibrary
{
    using System;
    using System.Diagnostics;
    using System.Messaging;
    using System.Threading;
    using System.Transactions;
    using Microsoft.ServiceBus.Messaging;

    public class DurableSender : IDisposable
    {
        const long WaitTimeAfterServiceBusReturnsAnIntermittentErrorInSeconds = 5;
        readonly MessageQueue msmqDeadletterQueue;
       readonly MessageQueue msmqQueue;
        readonly QueueClient queueClient;
        Timer waitAfterErrorTimer;

        public DurableSender(MessagingFactory messagingFactory, string serviceBusQueueName)
        {
            // Create a Service Bus queue client to send messages to the Service Bus queue.
            this.queueClient = messagingFactory.CreateQueueClient(serviceBusQueueName);

            // Create MSMQ queue if it doesn't exit. If it does, open the existing MSMQ queue.
            this.msmqQueue = MsmqHelper.GetMsmqQueue(MsmqHelper.CreateMsmqQueueName(messagingFactory.Address.DnsSafeHost, serviceBusQueueName, "SEND"));

            // Create MSMQ deadletter queue if it doesn't exit. If it does, open the existing MSMQ deadletter queue.
            this.msmqDeadletterQueue = MsmqHelper.GetMsmqQueue(MsmqHelper.CreateMsmqQueueName(messagingFactory.Address.DnsSafeHost, serviceBusQueueName, "SEND_DEADLETTER"));

            // Start receiving messages from the MSMQ queue.
            this.MsmqPeekBegin();
        }

        public void Dispose()
        {
            this.queueClient.Close();
            GC.SuppressFinalize(this);
        }

        public void Send(BrokeredMessage brokeredMessage)
        {
            var msmqMessage = MsmqHelper.PackServiceBusMessageIntoMsmqMessage(brokeredMessage);
            this.SendtoMsmq(this.msmqQueue, msmqMessage);
        }

        void SendtoMsmq(MessageQueue msmqQueue, Message msmqMessage)
        {
            if (Transaction.Current == null)
            {
                msmqQueue.Send(msmqMessage, MessageQueueTransactionType.Single);
            }
            else
            {
                msmqQueue.Send(msmqMessage, MessageQueueTransactionType.Automatic);
            }
        }

        void MsmqPeekBegin()
        {
            this.msmqQueue.BeginPeek(TimeSpan.FromSeconds(60), null, this.MsmqOnPeekComplete);
        }

        void MsmqOnPeekComplete(IAsyncResult result)
        {
            // Complete the MSMQ peek operation. If a timeout occured, peek again.
            Message msmqMessage = null;
            try
            {
                msmqMessage = this.msmqQueue.EndPeek(result);
            }
            catch (MessageQueueException ex)
            {
                if (ex.MessageQueueErrorCode == MessageQueueErrorCode.IOTimeout)
                {
                    this.MsmqPeekBegin();
                    return;
                }
            }

            if (msmqMessage != null)
            {
                var brokeredMessage = MsmqHelper.UnpackServiceBusMessageFromMsmqMessage(msmqMessage);
                // Clone Service Bus message in case we need to deadletter it.
                var serviceBusDeadletterMessage = brokeredMessage.Clone();

                Trace.TraceInformation("DurableSender: Enqueue message {0} into Service Bus.", msmqMessage.Label);
                switch (this.SendMessageToServiceBus(brokeredMessage))
                {
                    case SendResult.Success: // Message was successfully sent to Service Bus. Remove MSMQ message from MSMQ queue.
                        Trace.TraceInformation("DurableSender: Service Bus send operation completed.");
                        this.msmqQueue.BeginReceive(TimeSpan.FromSeconds(60), null, this.MsmqOnReceiveComplete);
                        break;
                    case SendResult.WaitAndRetry: // Service Bus is temporarily unavailable. Wait.
                        Trace.TraceWarning("DurableSender: Service Bus is temporarily unavailable.");
                        this.waitAfterErrorTimer = new Timer(
                            this.ResumeSendingMessagesToServiceBus,
                            null,
                            WaitTimeAfterServiceBusReturnsAnIntermittentErrorInSeconds*1000,
                            Timeout.Infinite);
                        break;
                    case SendResult.PermanentFailure: // Permanent error. Deadletter MSMQ message.
                        Trace.TraceError("DurableSender: Permanent error when sending message to Service Bus. Deadletter message.");
                        var msmqDeadletterMessage = MsmqHelper.PackServiceBusMessageIntoMsmqMessage(serviceBusDeadletterMessage);
                        try
                        {
                            this.SendtoMsmq(this.msmqDeadletterQueue, msmqDeadletterMessage);
                        }
                        catch (Exception ex)
                        {
                            Trace.TraceError(
                                "DurableSender: Failure when sending message {0} to deadletter queue {1}: {2} {3}",
                                msmqDeadletterMessage.Label, this.msmqDeadletterQueue.FormatName,
                                ex.GetType(),
                                ex.Message);
                        }
                        this.msmqQueue.BeginReceive(TimeSpan.FromSeconds(60), null, this.MsmqOnReceiveComplete);
                        break;
                }
            }
        }

        void MsmqOnReceiveComplete(IAsyncResult result)
        {
            this.msmqQueue.EndReceive(result);
            Trace.TraceInformation("DurableSender: MSMQ receive operation completed.");
            this.MsmqPeekBegin();
        }

        // Send message to Service Bus.
        SendResult SendMessageToServiceBus(BrokeredMessage brokeredMessage)
        {
            try
            {
                this.queueClient.Send(brokeredMessage); // Use synchonous send to preserve message ordering.

                return SendResult.Success;
            }
            catch (MessagingException ex)
            {
                if (ex.IsTransient)
                {
                    Trace.TraceWarning(
                        "DurableSender: Transient exception when sending message {0}: {1} {2}",
                        brokeredMessage.Label,
                        ex.GetType(),
                        ex.Message);
                    return SendResult.WaitAndRetry;
                }
                Trace.TraceError(
                    "DurableSender: Permanent exception when sending message {0}: {1} {2}",
                    brokeredMessage.Label,
                    ex.GetType(),
                    ex.Message);
                return SendResult.PermanentFailure;
            }

            catch (Exception ex)
            {
                var exceptionType = ex.GetType();
                if (exceptionType == typeof (TimeoutException))
                {
                    Trace.TraceWarning("DurableSender: Exception: {0}", exceptionType);
                    return SendResult.WaitAndRetry;
                }
                // Indicate a permanent failure in case of:
                //  - ArgumentException
                //  - ArgumentNullException
                //  - ArgumentOutOfRangeException
                //  - InvalidOperationException
                //  - OperationCanceledException
                //  - TransactionException
                //  - TransactionInDoubtException
                //  - TransactionSizeExceededException
                //  - UnauthorizedAccessException
                Trace.TraceError("DurableSender: Exception: {0}", exceptionType);
                return SendResult.PermanentFailure;
            }
        }

        // This method is called when timer expires.
        void ResumeSendingMessagesToServiceBus(Object stateInfo)
        {
            Trace.TraceInformation("DurableSender: Resume peeking MSMQ messages.");
            this.MsmqPeekBegin();
        }

        enum SendResult
        {
            Success,
            WaitAndRetry,
            PermanentFailure
        };
    }
}