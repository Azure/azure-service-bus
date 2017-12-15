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

namespace MessagingSamples
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus.Messaging;

    public class RequestReplySender
    {
        readonly ConcurrentDictionary<string, TaskCompletionSource<BrokeredMessage>> pendingRequests =
            new ConcurrentDictionary<string, TaskCompletionSource<BrokeredMessage>>();

        readonly MessageReceiver receiver;
        readonly MessageSender sender;

        public RequestReplySender(MessageSender sender, MessageReceiver receiver)
        {
            this.sender = sender;
            this.receiver = receiver;

            receiver.OnMessageAsync(this.DispatchReply, new OnMessageOptions {AutoComplete = false});
        }

        async Task DispatchReply(BrokeredMessage m)
        {
            TaskCompletionSource<BrokeredMessage> tc;
            if (this.pendingRequests.TryGetValue(m.CorrelationId, out tc))
            {
                tc.SetResult(m);
            }
            else
            {
                // can't correlate, toss out
                await m.DeadLetterAsync();
            }
        }

        public async Task Request(BrokeredMessage m, TimeSpan timeout, Func<BrokeredMessage, Task<bool>> replyHandler)
        {
            if (string.IsNullOrWhiteSpace(m.MessageId))
            {
                throw new ArgumentException("Message must have a valid MessageId");
            }

            var tcs = new TaskCompletionSource<BrokeredMessage>();
            if (!this.pendingRequests.TryAdd(m.MessageId, tcs))
            {
                throw new InvalidOperationException("Request with this MessageId is already pending");
            }
            var cts = new CancellationTokenSource(timeout);
            cts.Token.Register(tcs.SetCanceled);

            await this.sender.SendAsync(m);

            do
            {
                var reply = await tcs.Task;
                try
                {
                    bool processed = await replyHandler(reply);
                    if (processed)
                    {
                        this.pendingRequests.TryRemove(reply.CorrelationId, out tcs);
                        return;
                    }
                }
                catch
                {
                    await reply.AbandonAsync();
                    this.pendingRequests.TryRemove(reply.CorrelationId, out tcs);
                    throw;
                }
            }
            while (!cts.IsCancellationRequested);
        }
    }
}