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
    using System.ServiceModel;
    using System.Threading.Tasks;

    // ServiceBus does not support IOutputSessionChannel.
    // All senders sending messages to sessionful queue must use a contract which does not enforce SessionMode.Required.
    // Sessionful messages are sent by setting the SessionId property of the BrokeredMessageProperty object.
    [ServiceContract(Name= "SequenceProcessing", Namespace = "")]
    public interface ISequenceServiceContract
    {
        [ReceiveContextEnabled(ManualControl = true)]
        [OperationContract(IsOneWay = true, Name = "SubmitSequenceItem")]
        Task SubmitSequenceItemAsync(SequenceItem sequenceItem);

        [ReceiveContextEnabled(ManualControl = false)]
        [OperationContract(IsOneWay = true, Name = "TerminateSequence")]
        Task TerminateSequenceAsync();

    }

    public interface ISequenceServiceChannel : ISequenceServiceContract, IClientChannel
    {
    }

    // ServiceBus supports both IInputChannel and IInputSessionChannel. 
    // A sessionful service listening to a sessionful queue must have SessionMode.Required in its contract.
    [ServiceContract(Name = "SequenceProcessing", SessionMode = SessionMode.Required, Namespace = "")]
    public interface ISequenceProcessingSessionContract 
    {
        [ReceiveContextEnabled(ManualControl = true)]
        [OperationContract(IsOneWay = true, Name = "SubmitSequenceItem", IsInitiating = true)]
        Task SubmitSequenceItemAsync(SequenceItem sequenceItem);

        [ReceiveContextEnabled(ManualControl = false)]
        [OperationContract(IsOneWay = true, Name = "TerminateSequence", IsInitiating = false, IsTerminating = true)]
        Task TerminateSequenceAsync();
    }
    
}