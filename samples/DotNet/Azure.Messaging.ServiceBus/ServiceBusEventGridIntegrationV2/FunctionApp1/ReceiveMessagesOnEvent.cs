using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
//using Azure.Messaging.EventGrid;
//using Azure.Messaging.EventGrid.SystemEvents;
using Microsoft.Azure.EventGrid.Models;
using Newtonsoft.Json.Linq;

namespace SBEventGridIntegrationV2
{
    public static class ReceiveMessagesOnEvent
    {
        const string ServiceBusConnectionString = "<SERCICE BUS NAMESPACE - CONNECTION STRING>";

        [FunctionName("EventGridTriggerFunction")]
        public static void EventGridTriggerFunction([EventGridTrigger] EventGridEvent eventGridEvent, ILogger log)
        {
            log.LogInformation("C# Event Grid trigger function processed a request.");
            log.LogInformation(eventGridEvent.Data.ToString());

            var data = eventGridEvent.Data as JObject;
            ServiceBusActiveMessagesAvailableWithNoListenersEventData eventData = data.ToObject<ServiceBusActiveMessagesAvailableWithNoListenersEventData>();            
            
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

    public class GridEvent
    {
        public string Id { get; set; }
        public string EventType { get; set; }
        public string Subject { get; set; }
        public System.DateTime EventTime { get; set; }
        public Dictionary<string, string> Data { get; set; }
        public string Topic { get; set; }
    }
}
