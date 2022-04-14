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

namespace CreateTopicsAndSubscriptionsWithFilters
{
    using Azure.Messaging.ServiceBus.Administration;
    using System;
    using System.Threading.Tasks;

    public class Program
    {
        // Service Bus Administration Client object to create topics and subscriptions
        static ServiceBusAdministrationClient adminClient;

        // connection string to the Service Bus namespace
        static readonly string connectionString = "<SERVICE BUS NAMESPACE - CONNECTION STRING>";

        // name of the Service Bus topic
        static readonly string topicName = "topicfiltersampletopic";

        // names of subscriptions to the topic
        static readonly string subscriptionAllOrders = "AllOrders";
        static readonly string subscriptionColorRed = "ColorRed";
        static readonly string subscriptionColorBlueSize10Orders = "ColorBlueSize10Orders";
        static readonly string subscriptionHighPriorityRedOrders = "HighPriorityRedOrders";

        public static async Task Main()
        {
            try
            {

                Console.WriteLine("Creating the Service Bus Administration Client object");
                adminClient = new ServiceBusAdministrationClient(connectionString);
                
                Console.WriteLine($"Creating the topic {topicName}");
                await adminClient.CreateTopicAsync(topicName);

                Console.WriteLine($"Creating the subscription {subscriptionAllOrders} for the topic with a SQL filter ");
                
                // Create a True Rule filter with an expression that always evaluates to true
                // It's equivalent to using SQL rule filter with 1=1 as the expression

                await adminClient.CreateSubscriptionAsync(
                        new CreateSubscriptionOptions(topicName, subscriptionAllOrders), 
                        new CreateRuleOptions("AllOrders", new TrueRuleFilter()));

                Console.WriteLine($"Creating the subscription {subscriptionColorBlueSize10Orders} with a SQL filter");
                // Create a SQL filter with color set to blue and quantity to 10
                await adminClient.CreateSubscriptionAsync(
                        new CreateSubscriptionOptions(topicName, subscriptionColorBlueSize10Orders), 
                        new CreateRuleOptions("BlueSize10Orders", new SqlRuleFilter("color='blue' AND quantity=10")));

                Console.WriteLine($"Creating the subscription {subscriptionColorRed} with a SQL filter");
                // Create a SQL filter with color equals to red and a SQL action with a set of statements
                await adminClient.CreateSubscriptionAsync(topicName, subscriptionColorRed);
                // remove the $Default rule
                await adminClient.DeleteRuleAsync(topicName, subscriptionColorRed, "$Default");
                // now create the new rule. notice that user. prefix is used for the user/application property
                await adminClient.CreateRuleAsync(topicName, subscriptionColorRed, new CreateRuleOptions 
                                { 
                                    Name = "RedOrdersWithAction",
                                    Filter = new SqlRuleFilter("user.color='red'"),
                                    Action = new SqlRuleAction("SET quantity = quantity / 2; REMOVE priority;SET sys.CorrelationId = 'low';")

                                }
                );

                Console.WriteLine($"Creating the subscription {subscriptionHighPriorityRedOrders} with a correlation filter");
                // Create a correlation filter with color set to Red and priority set to High
                await adminClient.CreateSubscriptionAsync(
                        new CreateSubscriptionOptions(topicName, subscriptionHighPriorityRedOrders), 
                        new CreateRuleOptions("HighPriorityRedOrders", new CorrelationRuleFilter() {Subject = "red", CorrelationId = "high"} ));

                // delete resources
                //await adminClient.DeleteTopicAsync(topicName);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }
}
