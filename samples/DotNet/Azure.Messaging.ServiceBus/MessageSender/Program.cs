using Azure.Messaging.ServiceBus;
using System;
using System.Text;
using System.Threading.Tasks;

namespace MessageSender
{
    class Program
    {
        const string ServiceBusConnectionString = "Endpoint=sb://spsbusegridtutns.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=j1lrBlYD/6QXwC+OqYpByxoq6WZQ7IgMkV37ETlTUUc=";
        const string TopicName = "mytopic";

        static void Main(string[] args)
        {
            MainAsync().GetAwaiter().GetResult();
        }

        static async Task MainAsync()
        {
            const int numberOfMessages = 5;

            Console.WriteLine("================================================");
            Console.WriteLine("Press any key to exit after sending the message.");
            Console.WriteLine("================================================");

            // Send Messages
            await SendMessagesAsync(numberOfMessages);

            Console.ReadKey();
        }               

        static async Task SendMessagesAsync(int numberOfMessagesToSend)
        {
            try
            {
                await using (ServiceBusClient client = new ServiceBusClient(ServiceBusConnectionString))
                {
                    // create the sender
                    ServiceBusSender sender = client.CreateSender(TopicName);

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
            }
            catch (Exception exception)
            {
                Console.WriteLine($"{DateTime.Now} :: Exception: {exception.Message}");
            }
        }
    }
}
