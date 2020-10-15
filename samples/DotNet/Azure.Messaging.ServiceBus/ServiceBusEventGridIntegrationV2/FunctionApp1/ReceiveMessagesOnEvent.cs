using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

//using Microsoft.Azure.ServiceBus;
//using Microsoft.Azure.ServiceBus.Core;
using System.Net;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Azure.EventGrid.Models;

namespace SBEventGridIntegrationV2
{
    public static class ReceiveMessagesOnEvent
    {
        const string ServiceBusConnectionString = "<CONNECTION STRING TO SERVICE BUS NAMESPACE>";
        const int numberOfMessages = 10; // Choose the amount of messages you want to receive. Note that this is receive batch and there is no guarantee you will get all the messages.        
        //static IMessageReceiver messageReceiver;

        [FunctionName("ReceiveMessagesOnEvent")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req, ILogger log)
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
                    ReceiveAndProcess(log, JsonConvert.DeserializeObject<GridEvent[]>(jsonContent)).GetAwaiter().GetResult();
                }
            }

            return jsonContent == null
                ? new BadRequestObjectResult("Please pass a name on the query string or in the request body")
                : (ActionResult)new OkObjectResult($"Hello, {jsonContent}");
        }

        [FunctionName("ReceiveMessagesOnEvent2")]
        public static void EventGridTriggerFunction([EventGridTrigger] EventGridEvent eventGridEvent, ILogger log)
        {
            log.LogInformation("C# Event Grid trigger function processed a request.");
            log.LogInformation(eventGridEvent.Data.ToString());
        }

        static async Task ReceiveAndProcess(ILogger log, GridEvent[] ge)
        {
            log.LogInformation($"TopicName: {ge[0].Data["topicName"]} : SubscriptionName: {ge[0].Data["subscriptionName"]}");

            ServiceBusClient client = new ServiceBusClient(ServiceBusConnectionString);

            // create the receiver
            ServiceBusReceiver receiver = client.CreateReceiver(ge[0].Data["topicName"], ge[0].Data["subscriptionName"]);

            // the received message is a different type as it contains some service set properties
            IReadOnlyList<ServiceBusReceivedMessage> receivedMessages = await receiver.ReceiveMessagesAsync(maxMessages: 100);

            foreach (ServiceBusReceivedMessage receivedMessage in receivedMessages)
            {
                // get the message body as a string
                string body = receivedMessage.Body.ToString();
                Console.WriteLine($"Received: {body} from subscription: {ge[0].Data["subscriptionName"]}");
                // complete the message, thereby deleting it from the service
                await receiver.CompleteMessageAsync(receivedMessage);
            }

            // Create MessageReceiver
            //messageReceiver = new MessageReceiver(ServiceBusConnectionString, EntityPath, ReceiveMode.PeekLock, null, numberOfMessages);

            // Receive messages
            //await ReceiveMessagesAsync(numberOfMessages, log);
            //await messageReceiver.CloseAsync();
        }
        /*
        static async Task ReceiveMessagesAsync(int numberOfMessagesToReceive, ILogger tw)
        {
            // Receive the message
            IList<Message> receiveList = await messageReceiver.ReceiveAsync(numberOfMessagesToReceive);
            foreach (Message msg in receiveList)
            {
                tw.LogInformation($"Received message: SequenceNumber:{msg.SystemProperties.SequenceNumber} Body:{Encoding.UTF8.GetString(msg.Body)}");
                await messageReceiver.CompleteAsync(msg.SystemProperties.LockToken);
            }
        }
        */
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
