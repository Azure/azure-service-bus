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
    using System.Messaging;
    using Microsoft.ServiceBus.Messaging;

    public class MsmqHelper
    {
        // Create the specified transactional MSMQ queue if it doesn't exist.
        // If it exists, open existing queue. Return the queue handle.
        public static MessageQueue GetMsmqQueue(string queueName)
        {
            var msmqQueue = new MessageQueue(queueName, true);
            if (!MessageQueue.Exists(queueName))
            {
                MessageQueue.Create(queueName, true);
            }
            else
            {
                msmqQueue.Refresh();
            }
            msmqQueue.MessageReadPropertyFilter.SetAll();
            msmqQueue.Formatter = new XmlMessageFormatter(new[] {typeof (BrokeredMessage)});
            return msmqQueue;
        }

        // Create an MSMQ queue.
        public static string CreateMsmqQueueName(string hostName, string serviceBusQueueName, string suffix)
        {
            return (".\\private$\\sb_" + hostName.Replace(".", "_") + serviceBusQueueName.Replace("/", "_") + "_" + suffix);
        }

        // Pack a single brokered message into an MSMQ message.
        public static Message PackServiceBusMessageIntoMsmqMessage(BrokeredMessage serviceBusMessage)
        {
            var msmqMessage = new Message(serviceBusMessage)
            {
                Label = serviceBusMessage.Label
            };
            return msmqMessage;
        }

        // Extract a single brokered message from an MSMQ message.
        public static BrokeredMessage UnpackServiceBusMessageFromMsmqMessage(Message msmqMessage)
        {
            var brokeredMessage = (BrokeredMessage) msmqMessage.Body;
            return brokeredMessage;
        }
    }
}