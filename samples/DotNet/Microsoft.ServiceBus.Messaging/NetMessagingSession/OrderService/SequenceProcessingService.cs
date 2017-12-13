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

namespace NetMessagingSessionService
{
    using System;
    using System.Collections.Generic;
    using System.ServiceModel;
    using System.ServiceModel.Channels;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus.Messaging;

    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Single)]
    public class SequenceProcessingService : ISequenceProcessingSessionContract, IDisposable
    {
        readonly List<SequenceItem> sequenceItems;
        int messageCounter;
        string sessionId;

        public SequenceProcessingService()
        {
            this.sequenceItems = new List<SequenceItem>();
            this.sessionId = string.Empty;
        }


        public void Dispose()
        {
            Console.WriteLine("{0}: {1} - ContextId {2}.", "Process Sequence", string.Format("Finished processing sequence. Total {0} items", this.sequenceItems.Count), this.sessionId);
        }

        [OperationBehavior]
        public async Task SubmitSequenceItemAsync(SequenceItem sequenceItem)
        {
            // Get the BrokeredMessageProperty from OperationContext
            var incomingProperties = OperationContext.Current.IncomingMessageProperties;
            var property = (BrokeredMessageProperty)incomingProperties[BrokeredMessageProperty.Name];

            // Get the current ServiceBus SessionId
            if (this.sessionId == string.Empty)
            {
                this.sessionId = property.SessionId;
            }

            // Print message
            if (this.messageCounter == 0)
            {
                Console.WriteLine("{0}: {1} - ContextId {2}.", "Process Sequence", "Started processing sequence.", this.sessionId);
            }

            //Complete the Message
            ReceiveContext receiveContext;
            if (ReceiveContext.TryGet(incomingProperties, out receiveContext))
            {
                receiveContext.Complete(TimeSpan.FromSeconds(10.0d));
                this.sequenceItems.Add(sequenceItem);
                this.messageCounter++;
            }
            else
            {
                throw new InvalidOperationException("Receiver is in peek lock mode but receive context is not available!");
            }
        }

        public async Task TerminateSequenceAsync()
        {
            // do nothing. this shuts down the session and you'll see Dispose getting called
        }
    }
}