using Microsoft.Azure.ServiceBus;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MessageSender
{
    class Program
    {
        const string ServiceBusConnectionString = "YOUR CONNECTION STRING";
        const string TopicName = "YOUR TOPIC NAME";
        static ITopicClient TopicClient;

        static void Main(string[] args)
        {
            MainAsync().GetAwaiter().GetResult();
        }

        static async Task MainAsync()
        {
            const int numberOfMessages = 1;
            TopicClient = new TopicClient(ServiceBusConnectionString, TopicName);

            Console.WriteLine("================================================");
            Console.WriteLine("Press any key to exit after sending the message.");
            Console.WriteLine("================================================");            

            // Send Messages
            await SendMessagesAsync(numberOfMessages);

            Console.ReadKey();

            await TopicClient.CloseAsync();
        }               

        static async Task SendMessagesAsync(int numberOfMessagesToSend)
        {
            try
            {
                for (var i = 1; i <= numberOfMessagesToSend; i++)
                {
                    // Create a new message to send to the queue
                    string messageBody = $"Message {i}";
                    var message = new Message(Encoding.UTF8.GetBytes(messageBody));

                    // Write the body of the message to the console
                    Console.WriteLine($"Sending message: {messageBody}");

                    // Send the message to the queue
                    await TopicClient.SendAsync(message);
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine($"{DateTime.Now} :: Exception: {exception.Message}");
            }
        }
    }
}
