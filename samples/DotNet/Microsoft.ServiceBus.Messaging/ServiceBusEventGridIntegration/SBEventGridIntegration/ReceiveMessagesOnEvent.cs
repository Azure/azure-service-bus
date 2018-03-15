using System.Linq;
using System.Net;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using System.Collections.Generic;

namespace SBEventGridIntegration
{
    public static class ReceiveMessagesOnEvent
    {
        const string ServiceBusConnectionString = "YOUR CONNECTION STRING";
        const int numberOfMessages = 10; // Choose the amount of messages you want to receive. Note that this is receive batch and there is no guarantee you will get all the messages.        
        static IMessageReceiver messageReceiver;

        [FunctionName("ReceiveMessagesOnEvent")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");
            // parse query parameter
            var content = req.Content;            

            // Get content
            string jsonContent = await content.ReadAsStringAsync();
            log.Info($"Received Event with payload: {jsonContent}");

            IEnumerable<string> headerValues;
            if (req.Headers.TryGetValues("Aeg-Event-Type", out headerValues))
            {
                // Handle Subscription validation (Whenever you create a new subscription we send a new validation message)
                var validationHeaderValue = headerValues.FirstOrDefault();
                if (validationHeaderValue == "SubscriptionValidation")
                {
                    var events = JsonConvert.DeserializeObject<GridEvent[]>(jsonContent);
                    var code = events[0].Data["validationCode"];
                    return req.CreateResponse(HttpStatusCode.OK,
                    new { validationResponse = code });
                }
                // React to new messages and receive
                else
                {
                    ReceiveAndProcess(log, JsonConvert.DeserializeObject<GridEvent[]>(jsonContent)).GetAwaiter().GetResult();
                }
            }
            
            return jsonContent == null
            ? req.CreateResponse(HttpStatusCode.BadRequest, "Please pass a name on the query string or in the request body")
            : req.CreateResponse(HttpStatusCode.OK, "Hello " + jsonContent);
        }

        static async Task ReceiveAndProcess(TraceWriter log, GridEvent[] ge)
        {            
            log.Info($"TopicName: {ge[0].Data["topicName"]} : SubscriptionName: {ge[0].Data["subscriptionName"]}");
            // Get entity path, at this point you would in case you want to use Event Grid to monitor and react to deadletter messages likely also look for that.
            string EntityPath = $"{ge[0].Data["topicName"]}/subscriptions/{ge[0].Data["subscriptionName"]}";// e.g.: topicname/subscriptions/subscriptionname

            // Create MessageReceiver
            messageReceiver = new MessageReceiver(ServiceBusConnectionString, EntityPath, ReceiveMode.PeekLock, null, numberOfMessages);

            // Receive messages
            await ReceiveMessagesAsync(numberOfMessages, log);            
            await messageReceiver.CloseAsync();
        }

        static async Task ReceiveMessagesAsync(int numberOfMessagesToReceive, TraceWriter tw)
        {            
            // Receive the message
            IList<Message> receiveList = await messageReceiver.ReceiveAsync(numberOfMessagesToReceive);
            foreach (Message msg in receiveList)
            {
                tw.Info($"Received message: SequenceNumber:{msg.SystemProperties.SequenceNumber} Body:{Encoding.UTF8.GetString(msg.Body)}");
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
