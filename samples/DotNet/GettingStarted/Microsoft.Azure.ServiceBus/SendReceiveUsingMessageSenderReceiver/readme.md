# Get started sending and receiving messages from Service Bus queues using MessageSender and MessageReceiver

In order to run the sample in this directory, replace the following bracketed values in the `Program.cs` file.

```csharp
// Connection String for the namespace can be obtained from the Azure portal under the 
// `Shared Access policies` section.
const string ServiceBusConnectionString = "{Service Bus connection string}";
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
In this tutorial, we will write a console application to send and receive messages to a ServiceBus queue using MessageSender and MessageReceiver.
MessageSender and MessageReceiver APIs offer a more richer API surface in terms of being able to Defer Messages, Receive Deferred Messages, 
Peek Messages etc. Thus it allows the User a more granular control on processing of messages than `QueueClient` but that also means the User has 
to write more code to renew message locks, complete messages and define how to achieve a basic degree of concurrency while processing messages.

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
	static IMessageSender messageSender;
    static IMessageReceiver messageReceiver;
    ```

1. Create a new Task called `ReceiveMessagesAsync` that knows how to handle received messages with the following code:

	```csharp
	static async Task ReceiveMessagesAsync(int numberOfMessagesToReceive)
    {
		while(numberOfMessagesToReceive-- > 0)
        {
			// Receive the message
            Message message = await messageReceiver.ReceiveAsync();

            // Process the message
            Console.WriteLine($"Received message: SequenceNumber:{message.SystemProperties.SequenceNumber} Body:{Encoding.UTF8.GetString(message.Body)}");

            // Complete the message so that it is not received again.
            // This can be done only if the MessageReceiver is created in ReceiveMode.PeekLock mode (which is default).
            await messageReceiver.CompleteAsync(message.SystemProperties.LockToken);
        }
    }
	```
1. Create a new method called `SendMessagesAsync` with the following code:

    ```csharp
    // Sends messages to the queue.
    static async Task SendMessagesAsync(int numberOfMessagesToSend)
    {
		try
        {
			for (var i = 0; i < numberOfMessagesToSend; i++)
			{
				// Create a new message to send to the queue
				string messageBody = $"Message {i}";
				var message = new Message(Encoding.UTF8.GetBytes(messageBody));

				// Write the body of the message to the console
				Console.WriteLine($"Sending message: {messageBody}");

				// Send the message to the queue
				await messageSender.SendAsync(message);
			}
        }
        catch (Exception exception)
        {
			Console.WriteLine($"{DateTime.Now} :: Exception: {exception.Message}");
        }
	}
    ```

1. Create a new method called `MainAsync` with the following code:
   
    ```csharp
    static async Task MainAsync(string[] args)
    {
        const int numberOfMessages = 10;
        messageSender = new MessageSender(ServiceBusConnectionString, QueueName);
        messageReceiver = new MessageReceiver(ServiceBusConnectionString, QueueName, ReceiveMode.PeekLock);

		// Send Messages
        await SendMessagesAsync(numberOfMessages);

		// Receive Messages
        await ReceiveMessagesAsync(numberOfMessages);

		Console.WriteLine("Completed Receiving all messages... Press any key to exit");
		Console.ReadKey();

		// Close the messageSender and messageReceiver after processing all needed messages.
        await messageSender.CloseAsync();
        await messageReceiver.CloseAsync();
    }
    ```

1. Add the following code to the `Main` method:
    
    ```csharp
    MainAsync(args).GetAwaiter().GetResult();
    ```

Congratulations! You have now sent and received messages to a ServiceBus queue using MessageSender and MessageReceiver.
