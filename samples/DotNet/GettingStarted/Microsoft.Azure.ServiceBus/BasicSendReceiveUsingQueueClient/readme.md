# Get started sending and receiving messages from ServiceBus queues using QueueClient

In order to run the sample in this directory, replace the following bracketed values in the `Program.cs` file.

```csharp
// Connection String for the namespace can be obtained from the Azure portal under the 
// `Shared Access policies` section.
const string ServiceBusConnectionString = "{ServiceBus connection string}";
const string QueueName = "{Queue Name}";
```

Once you replace the above values run the following from a command prompt:
   
```
dotnet restore
dotnet build
dotnet run
```

## The Sample Program
To keep things reasonably simple, the sample program keeps send and receive code within a single hosting application.
Typically in real world applications these roles are often spread across applications, services, or at least across 
independently deployed and run tiers of applications or services. For clarity, the send and receive activities are kept as 
separate methods as if they were different apps.

For further information on how to create this sample on your own, follow the rest of the tutorial.

## What will be accomplished
In this tutorial, we will write a console application to send and receive messages to a ServiceBus queue using a QueueClient.
QueueClient offers a simple API surface to send message(or messages in a batch) and offers a simple MessagePump model to receive messages.
Once a message process handler is registered as shown below, the User code does not have to write explicit code to receive messages and 
if configured using `MessageHandlerOptions`, does not have to write explicit code to renew message locks or complete messages or improve 
the degree of concurrency of message processing. Hence the queueClient can be used in scenarios where the User wants to get started 
quickly or the scenarios where they need basic send/receive and wants to achieve that with as little code writing as possible.

## Prerequisites
1. [.NET Core](https://www.microsoft.com/net/core)
2. An Azure subscription.
3. [A ServiceBus namespace](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-create-namespace-portal) 
4. [A ServiceBus queue](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dotnet-get-started-with-queues#2-create-a-queue-using-the-azure-portal)

### Create a console application

- Create a new .NET Core application. Check out [this link](https://docs.microsoft.com/en-us/dotnet/articles/core/getting-started) with help to create a new application on your operating system.

### Add the ServiceBus client reference

1. Add the following to your project.json, making sure that the solution references the `Microsoft.Azure.ServiceBus` project.

    ```json
    "Microsoft.Azure.ServiceBus": "1.0.0"
    ```

### Write some code to send and receive messages from the queue
1. Add the following using statement to the top of the Program.cs file.
   
    ```csharp
    using Microsoft.Azure.ServiceBus;
    ```

1. Add the following variables to the `Program` class, and replace the placeholder values:
    
    ```csharp
    const string ServiceBusConnectionString = "{Service Bus connection string}";
    const string QueueName = "{Queue Name}";
    static IQueueClient queueClient;
    ```

1. Create a new Task called `ProcessMessagesAsync` that knows how to handle received messages with the following code:

	```csharp
	static async Task ProcessMessagesAsync(Message message, CancellationToken token)
    {
		// Process the message
        Console.WriteLine($"Received message: SequenceNumber:{message.SystemProperties.SequenceNumber} Body:{Encoding.UTF8.GetString(message.Body)}");
		
		// Complete the message so that it is not received again.
        // This can be done only if the queueClient is opened in ReceiveMode.PeekLock mode (which is default).
        await queueClient.CompleteAsync(message.SystemProperties.LockToken);
    }
	```

1. Create a new Task called `ExceptionReceivedHandler` to look at the exceptions received on the MessagePump. This will be useful for debugging purposes.

	```csharp
	// Use this Handler to look at the exceptions received on the MessagePump
	static Task ExceptionReceivedHandler(ExceptionReceivedEventArgs exceptionReceivedEventArgs)
    {
		Console.WriteLine($"Message handler encountered an exception {exceptionReceivedEventArgs.Exception}.");
        return Task.CompletedTask;
    }
	```

1. Create a new method called 'RegisterOnMessageHandlerAndReceiveMessages' to register the `ProcessMessagesAsync` and the 
`ExceptionReceivedHandler` with the necessary `MessageHandlerOptions` parameters to start receiving messages

	```csharp
	static void RegisterOnMessageHandlerAndReceiveMessages()
    {
		// Configure the MessageHandler Options in terms of exception handling, number of concurrent messages to deliver etc.
        var messageHandlerOptions = new MessageHandlerOptions(ExceptionReceivedHandler)
        {
			// Maximum number of Concurrent calls to the callback `ProcessMessagesAsync`, set to 1 for simplicity.
            // Set it according to how many messages the application wants to process in parallel.
			MaxConcurrentCalls = 1,

			// Indicates whether MessagePump should automatically complete the messages after returning from User Callback.
            // False value below indicates the Complete will be handled by the User Callback as seen in `ProcessMessagesAsync`.
            AutoComplete = false
        };

        // Register the function that will process messages
        queueClient.RegisterMessageHandler(ProcessMessagesAsync, messageHandlerOptions);
    }
	```

1. Create a new method called `SendMessagesAsync` with the following code:

    ```csharp
    // Sends messages to the queue.
    static async Task SendMessagesAsync(int numberOfMessagesToSend)
    {
		for (var i = 0; i < numberOfMessagesToSend; i++)
		{
			try
			{
				// Create a new message to send to the queue
				string messageBody = $"Message {i}";
				var message = new Message(Encoding.UTF8.GetBytes(messageBody));

				// Write the body of the message to the console
				Console.WriteLine($"Sending message: {messageBody}");

				// Send the message to the queue
				await queueClient.SendAsync(message);
			}
			catch (Exception exception)
			{
				Console.WriteLine($"{DateTime.Now} :: Exception: {exception.Message}");             
            }
        }
	}
    ```

1. Create a new method called `MainAsync` with the following code:
   
    ```csharp
    static async Task MainAsync(string[] args)
    {
        queueClient = new QueueClient(ServiceBusConnectionString, QueueName);

		// Register QueueClient's MessageHandler and receive messages in a loop
        RegisterOnMessageHandlerAndReceiveMessages();

		// Send Messages
        await SendMessagesAsync(10);        

        Console.WriteLine("Press any key to exit after receiving all the messages.");
        Console.ReadKey();

        await queueClient.CloseAsync();
    }
    ```

1. Add the following code to the `Main` method:
    
    ```csharp
    MainAsync(args).GetAwaiter().GetResult();
    ```

Congratulations! You have now sent and received messages to a ServiceBus queue, using QueueClient.
