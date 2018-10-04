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

namespace SASAuthorizationRule
{
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Management;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using System.Linq;
    using Microsoft.Azure.ServiceBus.Core;
    using Microsoft.Azure.ServiceBus.Primitives;

    public class Program : MessagingSamples.Sample
    {
        ManagementClient managementClient;
        string connectionString;

        public async Task RunAsync(string connectionString)
        {
            this.managementClient = new ManagementClient(connectionString);
            this.connectionString = connectionString;

            var topicName = Guid.NewGuid().ToString("D").Substring(0, 8);
            var subscriptionName1 = Guid.NewGuid().ToString("D").Substring(0, 8);
            var subscriptionName2 = Guid.NewGuid().ToString("D").Substring(0, 8);

            Console.WriteLine($"Creating a new Topic with name - {topicName} with 2 subscriptions - {subscriptionName1} and {subscriptionName2}");
            await this.managementClient.CreateTopicAsync(topicName);
            await this.managementClient.CreateSubscriptionAsync(topicName, subscriptionName1);
            await this.managementClient.CreateSubscriptionAsync(topicName, subscriptionName2);

            // We are trying to create an authorization rule for the topic.
            // We will be adding a new SAS policy which has Send-only claims
            // i.e., using that messages can only be sent to entity, but not received.
            // The access is defined using {SASKeyName, SASKey} combination.
            Console.WriteLine($"\nCreating a send-only rule for topic");
            var sendOnlyAuthRule = await CreateAuthRuleForTopicAsync(topicName, AccessRights.Send);

            // We will try to send and receive using a send-only SAS rule (using sasKeyName and sasKey).
            // Send should succeed below and receive should fail.
            Console.WriteLine($"Trying to send and receive using the send-only rule");
            var csBuilder = new ServiceBusConnectionStringBuilder(connectionString)
            {
                SasKeyName = sendOnlyAuthRule.KeyName,
                SasKey = sendOnlyAuthRule.PrimaryKey    // or secondary key
            };
            await PerformSendAndReceiveOperation(topicName, subscriptionName1, csBuilder);


            // Given a SAS policy, we can also create SAS Tokens which already have authentication information.
            // Lets try SASToken based authentication for subscription1.
            Console.WriteLine($"\nCreating a receive-only authentication token for {subscriptionName1}.");
            var receiveOnlyAuthRule = await CreateAuthRuleForTopicAsync(topicName, AccessRights.Listen);
            var SasTokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(receiveOnlyAuthRule.KeyName, receiveOnlyAuthRule.PrimaryKey);
            var tokenAudience = new Uri(new Uri(csBuilder.Endpoint), EntityNameHelper.FormatSubscriptionPath(topicName, subscriptionName1)).ToString();
            var sasToken = (await SasTokenProvider.GetTokenAsync(tokenAudience, TimeSpan.FromMinutes(60))).TokenValue;

            // We will try to send and receive using a receive-only SAS rule (using sasToken generated for subscriptionName1).
            // Receive should succeed below and send should fail.
            Console.WriteLine($"\nTrying to send and receive using the receive-only token for {subscriptionName1}");
            csBuilder = new ServiceBusConnectionStringBuilder(connectionString)
            {
                SasToken = sasToken
            };
            await PerformSendAndReceiveOperation(topicName, subscriptionName1, csBuilder);

            // We will try to send and receive using the same receive-only SAS rule created above.
            // Both operations should fail as the token was generated for subscriptionName1 and not subscriptionName2
            Console.WriteLine($"\nTrying to send and receive using the receive-only token for {subscriptionName2}");
            await PerformSendAndReceiveOperation(topicName, subscriptionName2, csBuilder);

            // delete resources
            await this.managementClient.DeleteTopicAsync(topicName);
            await this.managementClient.CloseAsync();
        }

        private async Task<SharedAccessAuthorizationRule> CreateAuthRuleForTopicAsync(string topicName, AccessRights accessRights)
        {
            var topicDescription = await this.managementClient.GetTopicAsync(topicName);
            topicDescription.AuthorizationRules.Clear();
            topicDescription.AuthorizationRules.Add(new SharedAccessAuthorizationRule("ruleWith" + accessRights.ToString(), new List<AccessRights> { accessRights }));
            var updatedTd = await this.managementClient.UpdateTopicAsync(topicDescription);
            return updatedTd.AuthorizationRules.FirstOrDefault() as SharedAccessAuthorizationRule;
        }

        private async Task PerformSendAndReceiveOperation(string topicName, string subscriptionName, ServiceBusConnectionStringBuilder csBuilder)
        {
            var connection = new ServiceBusConnection(csBuilder);
            var sender = new MessageSender(connection, topicName);
            var receiver = new MessageReceiver(connection, EntityNameHelper.FormatSubscriptionPath(topicName, subscriptionName));

            try
            {
                await sender.SendAsync(new Message() { MessageId = "1" });
                Console.WriteLine("Sent message successfully");
            }
            catch (UnauthorizedException)
            {
                Console.WriteLine($"Could not send message due to authorization failure");
            }

            try
            {
                var msg = await receiver.ReceiveAsync();
                Console.WriteLine("Received message successfully");
            }
            catch(UnauthorizedException)
            {
                Console.WriteLine($"Could not receive message due to authorization failure");
            }

            await sender.CloseAsync();
            await receiver.CloseAsync();
            await connection.CloseAsync();
        }

        public static int Main(string[] args)
        {
            try
            {
                var app = new Program();
                app.RunSample(args, app.RunAsync);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return 1;
            }
            return 0;
        }
    }
}
