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
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;

    public class RequestReplyResponder : IDisposable
    {
        readonly Uri namespaceUri;
        readonly MessageReceiver receiver;
        readonly Dictionary<Uri, ReplyDestination> responderFactories = new Dictionary<Uri, ReplyDestination>();
        readonly object replyDestinationsMutex = new object();
        readonly Func<BrokeredMessage, Task<BrokeredMessage>> responseFunction;

        public RequestReplyResponder(Uri namespaceUri, MessageReceiver receiver, Func<BrokeredMessage, Task<BrokeredMessage>> responseFunction)
        {
            this.namespaceUri = namespaceUri;
            this.receiver = receiver;
            this.responseFunction = responseFunction;
        }

        public void Dispose()
        {
            foreach (var rpf in this.responderFactories.Values)
            {
                rpf.Factory.Abort();
            }
            this.responderFactories.Clear();
        }

        public async Task Run(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<bool>();
            token.Register(() => { this.receiver.Close(); tcs.SetResult(true); });
            this.receiver.OnMessageAsync(rq => this.Respond(rq, this.responseFunction), new OnMessageOptions { AutoComplete = false });
            await tcs.Task;
        }

        async Task Respond(BrokeredMessage request, Func<BrokeredMessage, Task<BrokeredMessage>> handleRequest)
        {
            // evaluate ReplyTo
            if (!string.IsNullOrEmpty(request.ReplyTo))
            {
                Uri targetUri;

                if (Uri.TryCreate(request.ReplyTo, UriKind.RelativeOrAbsolute, out targetUri))
                {
                    // make the URI absolute to this namespace 
                    if (!targetUri.IsAbsoluteUri)
                    {
                        targetUri = new Uri(this.namespaceUri, targetUri);
                    }
                    var replyToken = GetReplyToken(targetUri);
                    if (replyToken == null)
                    {
                        await request.DeadLetterAsync("NoReplyToToken", "No 'tk' query parameter in ReplyTo field URI found");
                        return;
                    }
                    // truncate the query portion of the URI
                    targetUri = new Uri(targetUri.GetLeftPart(UriPartial.Path));
                    
                    // now we're reasonably confident that the input message can be
                    // replied to, so let's execute the message processing
                    try
                    {
                        // call the callback
                        var reply = await handleRequest(request);
                        // set the correlation-id on the reply 
                        reply.CorrelationId = request.MessageId;

                        var replyDestination = this.GetOrCreateReplyDestination(replyToken, targetUri);
                        var sender = await replyDestination.Factory.CreateMessageSenderAsync(targetUri.AbsolutePath.Substring(1));
                        await sender.SendAsync(reply);
                        await request.CompleteAsync();
                    }
                    catch (Exception e)
                    {
                        await request.DeadLetterAsync("ErrorHandlingMessage", e.Message);
                    }
                }
                else
                {
                    await request.DeadLetterAsync("NoReplyTo", "No ReplyTo field found");
                }
            }
        }

        ReplyDestination GetOrCreateReplyDestination(string replyToken, Uri targetUri)
        {
            ReplyDestination replyDestination;
            lock (this.replyDestinationsMutex)
            {
                var signatureTokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(replyToken);
                if (this.responderFactories.TryGetValue(targetUri, out replyDestination))
                {
                    replyDestination.TokenProvider.TokenProvider = signatureTokenProvider;
                }
                else
                {
                    var tokenProvider = new DelegatingTokenProvider(signatureTokenProvider);
                    var receiverFactory = MessagingFactory.Create(targetUri.GetLeftPart(UriPartial.Authority),
                                            new MessagingFactorySettings { TransportType = TransportType.Amqp, TokenProvider = tokenProvider });
                    replyDestination = new ReplyDestination { Factory = receiverFactory, TokenProvider = tokenProvider };
                    this.responderFactories.Add(targetUri, replyDestination);
                }
            }
            return replyDestination;
        }

        static string GetReplyToken(Uri targetUri)
        {
            string replyToken = null;
            var queryPortion = targetUri.Query;
            if (!string.IsNullOrEmpty(queryPortion) && queryPortion.Length > 1)
            {
                var nvm = HttpUtility.ParseQueryString(queryPortion.Substring(1));
                var tokenString = nvm["tk"];
                if (tokenString != null)
                {
                    replyToken = tokenString;
                }
            }
            return replyToken;
        }

        class ReplyDestination
        {
            public MessagingFactory Factory { get; set; }
            public DelegatingTokenProvider TokenProvider { get; set; }
        }
    }
}