#if null

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
    using System.Threading.Tasks;
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Core;

    class Program : IDynamicSample
    {
        string sharedAccessRuleKey;

        public async Task Run(string connectionString, string manageToken)
        {
            Console.WriteLine("\nCreating topology\n");
            this.sharedAccessRuleKey = SharedAccessAuthorizationRule.GenerateRandomKey();
            var namespaceManageTokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(manageToken);

            // Create namespace manager and create destination queue with a SAS rule that allows sending to that queue.
            var namespaceManager = new NamespaceManager(connectionString, namespaceManageTokenProvider);

            var targetQueue = new QueueDescription("TargetQueue")
            {
                Authorization = { new SharedAccessAuthorizationRule("SendKey", this.sharedAccessRuleKey, new[] { AccessRights.Send }) },
            };

            targetQueue = (await namespaceManager.QueueExistsAsync(targetQueue.Path))
                ? await namespaceManager.UpdateQueueAsync(targetQueue)
                : await namespaceManager.CreateQueueAsync(targetQueue);

            var topic = new TopicDescription("SourceTopic")
            {
                Authorization = { new SharedAccessAuthorizationRule("SendKey", this.sharedAccessRuleKey, new[] { AccessRights.Send }) }
            };
            topic = (await namespaceManager.TopicExistsAsync(topic.Path))
                ? await namespaceManager.UpdateTopicAsync(topic)
                : await namespaceManager.CreateTopicAsync(topic);
            var forwardingSubscription = namespaceManager.CreateSubscription(
                new SubscriptionDescription(topic.Path, "Sub1")
                {
                    ForwardTo = targetQueue.Path
                });

            var forwardingQueue = new QueueDescription("SourceQueue")
            {
                ForwardTo = targetQueue.Path,
                Authorization =
                {
                    new SharedAccessAuthorizationRule(
                        "SendKey",
                        this.sharedAccessRuleKey,
                        new[] {AccessRights.Send})
                }
            };
            forwardingQueue = (await namespaceManager.QueueExistsAsync(forwardingQueue.Path))
                ? await namespaceManager.UpdateQueueAsync(forwardingQueue)
                : await namespaceManager.CreateQueueAsync(forwardingQueue);


            Console.WriteLine("\nSending messages\n");

            var topicFactory = MessagingFactory.Create(connectionString, TokenProvider.CreateSharedAccessSignatureTokenProvider("SendKey", this.sharedAccessRuleKey));
            var topicSender = new MessageSender(connectionString,topic.Path);
            await topicSender.SendAsync(CreateMessage("M1"));

            var queueFactory = MessagingFactory.Create(connectionString, TokenProvider.CreateSharedAccessSignatureTokenProvider("SendKey", this.sharedAccessRuleKey));
            var queueSender = new MessageSender(connectionString,forwardingQueue.Path);
            await queueSender.SendAsync(CreateMessage("M1"));


            var messagingFactory = MessagingFactory.Create(connectionString, namespaceManageTokenProvider);
            var targetQueueReceiver = messagingFactory.CreateQueueClient(targetQueue.Path);
            while (true)
            {
                var message = await targetQueueReceiver.ReceiveAsync(TimeSpan.FromSeconds(10));
                if (message != null)
                {
                    await this.PrintReceivedMessage(message);
                    await receiveClient.CompleteAsync(message.SystemProperties.LockToken);
                }
                else
                {
                    break;
                }
            }
            await targetQueueReceiver.CloseAsync();

            Console.WriteLine("\nPress ENTER to delete topics and exit\n");
            Console.ReadLine();
            messagingFactory.Close();
            Task.WaitAll(
                namespaceManager.DeleteQueueAsync(targetQueue.Path),
                namespaceManager.DeleteQueueAsync(forwardingQueue.Path),
                namespaceManager.DeleteTopicAsync(topic.Path));
        }

        async Task PrintReceivedMessage(Message receivedMessage)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            await Console.Out.WriteLineAsync(string.Format("Received message:\n" + "\tLabel:\t{0}\n" + "\tBody:\t{1}\n", receivedMessage.Label, receivedMessage.GetBody<string>()));
            foreach (var p in receivedMessage.Properties)
            {
                await Console.Out.WriteLineAsync(string.Format("\tProperty:\t{0} = {1}", p.Key, p.Value));
            }
            Console.ResetColor();
        }
        
        // Create a new Service Bus message.
        public static Message CreateMessage(string label)
        {
            // Create a Service Bus message.
            var msg = new Message("This is the body of message \"" + label + "\".");
            msg.Properties.Add("Priority", 1);
            msg.Properties.Add("Importance", "High");
            msg.Label = label;
            msg.TimeToLive = TimeSpan.FromSeconds(90);
            return msg;
        }
        
    }
}

#endif