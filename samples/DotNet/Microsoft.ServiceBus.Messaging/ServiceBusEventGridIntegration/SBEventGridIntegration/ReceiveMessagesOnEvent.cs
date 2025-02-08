using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Newtonsoft.Json;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using System.IO;
using Microsoft.AspNetCore.Mvc;

namespace SBEventGridIntegration
{
    public static class ReceiveMessagesOnEvent
    {
        const string ServiceBusConnectionString = "YOUR CONNECTION STRING";
        const int numberOfMessages = 10; // Choose the amount of messages you want to receive. Note that this is receive batch and there is no guarantee you will get all the messages.        
        static IMessageReceiver messageReceiver;

        [FunctionName("ReceiveMessagesOnEvent")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");
            var content = req.Body;
            string jsonContent = await new StreamReader(content).ReadToEndAsync();
            log.LogInformation($"Received Event with payload: {jsonContent}");

            IEnumerable<string> headerValues;
            headerValues = req.Headers.GetCommaSeparatedValues("Aeg-Event-Type");

            if (headerValues.Count() != 0)
            {
                var validationHeaderValue = headerValues.FirstOrDefault();
                if (validationHeaderValue == "SubscriptionValidation")
                {
                    var events = JsonConvert.DeserializeObject<GridEvent[]>(jsonContent);
                    var code = events[0].Data["validationCode"];
                    log.LogInformation("Validation code: {code}");
                    return (ActionResult)new OkObjectResult(new { validationResponse = code });
                }
                // React to new messages and receive
                else
                {
                    ReceiveAndProcess(log, JsonConvert.DeserializeObject<GridEvent[]>(jsonContent)).GetAwaiter().GetResult();
                }
            }


            return jsonContent == null
         ? new BadRequestObjectResult("Please pass a name on the query string or in the request body")
         : (ActionResult)new OkObjectResult($"Hello, {jsonContent}");
        }

        static async Task ReceiveAndProcess(ILogger log, GridEvent[] ge)
        {
            log.LogInformation($"TopicName: {ge[0].Data["topicName"]} : SubscriptionName: {ge[0].Data["subscriptionName"]}");
            // Get entity path, at this point you would in case you want to use Event Grid to monitor and react to deadletter messages likely also look for that.
            string EntityPath = $"{ge[0].Data["topicName"]}/subscriptions/{ge[0].Data["subscriptionName"]}";// e.g.: topicname/subscriptions/subscriptionname

            // Create MessageReceiver
            messageReceiver = new MessageReceiver(ServiceBusConnectionString, EntityPath, ReceiveMode.PeekLock, null, numberOfMessages);

            // Receive messages
            await ReceiveMessagesAsync(numberOfMessages, log);
            await messageReceiver.CloseAsync();
        }

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
