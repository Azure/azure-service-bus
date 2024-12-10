// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}
using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.EventGrid;
using System.Collections.Generic;
using Azure.Messaging.EventGrid.SystemEvents;
using Newtonsoft.Json.Linq;
using Azure;

namespace ReceiveMessagesFunctionApp
{
    public static class ReceiveMessagesOnEvent
    {
        const string ServiceBusConnectionString = "<SERVICE BUS NAMESPACE - CONNECTION STRING>";

        [FunctionName("EventGridTriggerFunction")]
        public static void Run([EventGridTrigger]EventGridEvent eventGridEvent, ILogger log)
        {
            log.LogInformation("C# Event Grid trigger function processed a request.");
            log.LogInformation(eventGridEvent.Data.ToString());

            //var data = eventGridEvent.Data as JObject;
            //ServiceBusActiveMessagesAvailableWithNoListenersEventData eventData = data.ToObject<ServiceBusActiveMessagesAvailableWithNoListenersEventData>();
            ServiceBusActiveMessagesAvailableWithNoListenersEventData eventData = eventGridEvent.Data.ToObjectFromJson<ServiceBusActiveMessagesAvailableWithNoListenersEventData>();

            string topicName = eventData.TopicName;
            string subscriptionName = eventData.SubscriptionName;

            log.LogInformation($"Topic: {topicName} Subscription: {subscriptionName}");

            // crate a Service Bus Client using the connection string
            ServiceBusClient client = new ServiceBusClient(ServiceBusConnectionString);

            // create the receiver
            ServiceBusReceiver receiver = client.CreateReceiver(topicName, subscriptionName);

            // receive messages
            IReadOnlyList<ServiceBusReceivedMessage> receivedMessages = receiver.ReceiveMessagesAsync(maxMessages: 100).GetAwaiter().GetResult();

            foreach (ServiceBusReceivedMessage receivedMessage in receivedMessages)
            {
                // get the message body as a string
                string body = receivedMessage.Body.ToString();
                log.LogInformation($"Received: {body} from subscription: {subscriptionName}");
                // complete the message, thereby deleting it from the service
                receiver.CompleteMessageAsync(receivedMessage).GetAwaiter().GetResult();
            }
        }
    }
}
