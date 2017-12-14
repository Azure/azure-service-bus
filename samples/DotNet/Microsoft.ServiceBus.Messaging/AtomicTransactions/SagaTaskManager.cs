//   
//   Copyright © Microsoft Corporation, All Rights Reserved
// 
//   Licensed under the Apache License, Version 2.0 (the "License"); 
//   you may not use this file except in compliance with the License. 
//   You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0 
// 
//   THIS CODE IS PROVIDED ON AN *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
//   OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION
//   ANY IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A
//   PARTICULAR PURPOSE, MERCHANTABILITY OR NON-INFRINGEMENT.
// 
//   See the Apache License, Version 2.0 for the specific language
//   governing permissions and limitations under the License. 

namespace AtomicTransactions
{
    using System;
    using System.Collections;
    using System.Collections.ObjectModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus.Messaging;

    class SagaTaskManager : IEnumerable
    {
        readonly MessagingFactory messagingFactory;
        readonly Collection<Task> tasks = new Collection<Task>();
        CancellationToken cancellationToken;

        public SagaTaskManager(MessagingFactory messagingFactory, CancellationToken cancellationToken)
        {
            this.messagingFactory = messagingFactory;
            this.cancellationToken = cancellationToken;
        }

        public Task Task => Task.WhenAll(this.tasks);

        public IEnumerator GetEnumerator()
        {
            return this.tasks.GetEnumerator();
        }

        public void Add(
            string taskQueueName,
            Func<BrokeredMessage, MessageSender, MessageSender, Task> doWork,
            string nextStepQueue,
            string compensatorQueue)
        {
            var tcs = new TaskCompletionSource<bool>();
            var rcv = this.messagingFactory.CreateMessageReceiver(taskQueueName);
            var nextStepSender = this.messagingFactory.CreateMessageSender(nextStepQueue, taskQueueName);
            var compensatorSender = this.messagingFactory.CreateMessageSender(compensatorQueue, taskQueueName);

            this.cancellationToken.Register(
                () =>
                {
                    rcv.Close();
                    tcs.SetResult(true);
                });
            rcv.OnMessageAsync(m => doWork(m, nextStepSender, compensatorSender), new OnMessageOptions {AutoComplete = false});
            this.tasks.Add(tcs.Task);
        }
    }
}