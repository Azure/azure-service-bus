using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Azure.EventGrid.Models;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

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

            ReceiveAndProcess(topicName, subscriptionName, log).GetAwaiter().GetResult();
        }

        static async Task ReceiveAndProcess(string topicName, string subscriptionName, ILogger log)
        {

            log.LogInformation($"Topic: {topicName} Subscription: {subscriptionName}");

            // crate a Service Bus Client using the connection string
            ServiceBusClient client = new ServiceBusClient(ServiceBusConnectionString);

            // create the receiver
            ServiceBusReceiver receiver = client.CreateReceiver(topicName, subscriptionName);

            // receive messages
            IReadOnlyList<ServiceBusReceivedMessage> receivedMessages = await receiver.ReceiveMessagesAsync(maxMessages: 100);

            foreach (ServiceBusReceivedMessage receivedMessage in receivedMessages)
            {
                // get the message body as a string
                string body = receivedMessage.Body.ToString();
                log.LogInformation($"Received: {body} from subscription: {subscriptionName}");
                // complete the message, thereby deleting it from the service
                await receiver.CompleteMessageAsync(receivedMessage);
            }
        }

        [FunctionName("HTTPTriggerFunction")]
        public static async Task<IActionResult> HTTPTriggerFunction([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req, ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string jsonContent = await new StreamReader(req.Body).ReadToEndAsync();
            log.LogInformation($"Received Event with payload: {jsonContent}");

            IEnumerable<string> headerValues;
            headerValues = req.Headers.GetCommaSeparatedValues("Aeg-Event-Type");

            if (headerValues.Count<string>() != 0)
            {
                var validationHeaderValue = headerValues.FirstOrDefault<string>();
                if (validationHeaderValue == "SubscriptionValidation")
                {
                    log.LogInformation("Validating the subscription");
                    var events = JsonConvert.DeserializeObject<GridEvent[]>(jsonContent);
                    var code = events[0].Data["validationCode"];
                    log.LogInformation($"Validation code: {code}");
                    return (ActionResult)new OkObjectResult(new { validationResponse = code });
                }
                else
                {
                    GridEvent ge = JsonConvert.DeserializeObject<GridEvent[]>(jsonContent)[0];
                    ReceiveAndProcess(ge.Data["topicName"], ge.Data["subscriptionName"], log).GetAwaiter().GetResult();
                }
            }

            return jsonContent == null
                ? new BadRequestObjectResult("Please pass a name on the query string or in the request body")
                : (ActionResult)new OkObjectResult($"Hello, {jsonContent}");
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
