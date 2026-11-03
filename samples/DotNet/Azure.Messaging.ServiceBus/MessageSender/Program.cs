using Azure.Messaging.ServiceBus;
using System;
using System.Text;
using System.Threading.Tasks;
using Azure.Identity;

namespace MessageSender
{
    class Program
    {
        static readonly string FullyQualifiedNamespace =
            Environment.GetEnvironmentVariable("SERVICEBUS_FULLY_QUALIFIED_NAMESPACE") is string host && !string.IsNullOrWhiteSpace(host)
                ? host : throw new InvalidOperationException("Set SERVICEBUS_FULLY_QUALIFIED_NAMESPACE (example.servicebus.windows.net).");
        static readonly string TopicName =
            Environment.GetEnvironmentVariable("SERVICEBUS_TOPIC") is string topic && !string.IsNullOrWhiteSpace(topic)
                ? topic : throw new InvalidOperationException("Set SERVICEBUS_TOPIC.");

        static void Main(string[] args)
        {
            MainAsync().GetAwaiter().GetResult();
        }

        static async Task MainAsync()
        {
            const int numberOfMessages = 5;

            // Send Messages
            await SendMessagesAsync(numberOfMessages);
        }               

        static async Task SendMessagesAsync(int numberOfMessagesToSend)
        {
            try
            {
                await using var client = new ServiceBusClient(FullyQualifiedNamespace, new DefaultAzureCredential());
                // create the sender
                await using ServiceBusSender sender = client.CreateSender(TopicName);

                for (var i = 1; i <= numberOfMessagesToSend; i++)
                {
                    // Create a new message to send to the queue
                    string messageBody = $"Message {i}";
                    var message = new ServiceBusMessage(Encoding.UTF8.GetBytes(messageBody));

                    // Write the body of the message to the console
                    Console.WriteLine($"Sending message: {messageBody}");

                    // Send the message to the queue
                    await sender.SendMessageAsync(message);
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"{DateTime.Now} :: Exception: {exception.Message}");
                throw;
            }
        }
    }
}
