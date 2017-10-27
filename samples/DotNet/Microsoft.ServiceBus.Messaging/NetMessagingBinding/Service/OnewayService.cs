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

namespace NetMessagingBindingService
{
    using System;
    using System.ServiceModel;
    using System.ServiceModel.Channels;

    public class OnewayService : IOnewayServiceContract
    {
        public void Process(string data)
        {
            // Print message
            Console.WriteLine("Receive: Message [{0}].", data);
        }

        public void ProcessExplicit(string data)
        {
            // Print message
            Console.WriteLine("Receive: Message [{0}].", data);

            //Complete the Message
            ReceiveContext receiveContext;
            if (ReceiveContext.TryGet(OperationContext.Current.IncomingMessageProperties, out receiveContext))
            {
                receiveContext.Complete(TimeSpan.FromSeconds(10.0d));
            }
            else
            {
                throw new InvalidOperationException("Receiver is in peek lock mode but receive context is not available!");
            }
        }
    }
}