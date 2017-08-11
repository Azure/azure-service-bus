# Get started sending and receiving session based messages from Service Bus queues using QueueClient

In order to run the sample in this directory, replace the following bracketed values in the `Program.cs` file.

```csharp
// Connection String for the namespace can be obtained from the Azure portal under the 
// `Shared Access policies` section.
const string ServiceBusConnectionString = "{Service Bus connection string}";
const string QueueName = "{Queue Name of a Queue that supports sessions}";
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
In this tutorial, we will write a console application to send and receive sessionful messages to a ServiceBus queue using a QueueClient.
Sessions are used in scenarios where User requires unbounded sequences of related messages. Messages within a session are always delivered
in a First In First Out Order. Sending session based messages to a queue using QueueClient is same as sending other messages but the 
messages are stamped with an additional `SessionId` property. QueueClient offers a simple SessionPump model to receive messages related 
to a session. Once a session handler is registered as shown below, the User code does not have to write explicit code to receive sessions 
and if configured using `SessionHandlerOptions`, does not have to write explicit code to renew session locks or complete messages or improve 
the degree of concurrency of session processing. Hence the queueClient can be used in scenarios where the User wants to get started quickly
or the scenarios where they need basic session based send/receive and wants to achieve that with as little code writing as possible.

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
    const string QueueName = "{Queue Name of a Queue that supports sessions}";
    static IQueueClient queueClient;
    ```

1. Create a new Task called `ProcessSessionMessagesAsync` that knows how to handle received messages from a session with the following code:

	```csharp
	static async Task ProcessSessionMessagesAsync(IMessageSession session, Message message, CancellationToken token)
    {
		Console.WriteLine($"Received Session: {session.SessionId} message: SequenceNumber: {message.SystemProperties.SequenceNumber} Body:{Encoding.UTF8.GetString(message.Body)}");

        // Complete the message so that it is not received again.
        // This can be done only if the queueClient is created in ReceiveMode.PeekLock mode (which is default).
        await session.CompleteAsync(message.SystemProperties.LockToken);

        // Note: Use the cancellationToken passed as necessary to determine if the queueClient has already been closed.
        // If queueClient has already been Closed, you may chose to not call CompleteAsync() or AbandonAsync() etc. calls 
        // to avoid unnecessary exceptions.
    }
	```

1. Create a new Task called `ExceptionReceivedHandler` to look at the exceptions received on the MessagePump. This will be useful for debugging purposes.

	```csharp
	// Use this Handler to look at the exceptions received on the SessionPump
	static Task ExceptionReceivedHandler(ExceptionReceivedEventArgs exceptionReceivedEventArgs)
    {
		Console.WriteLine($"Message handler encountered an exception {exceptionReceivedEventArgs.Exception}.");
        return Task.CompletedTask;
    }
	```

1. Create a new method called 'RegisterOnSessionHandlerAndReceiveSessionMessages' to register the `ProcessSessionMessagesAsync` and the 
`ExceptionReceivedHandler` with the necessary `SessionHandlerOptions` parameters to start receiving messages from sessions

	```csharp
    static void RegisterOnSessionHandlerAndReceiveSessionMessages()
    {
		// Configure the SessionHandler Options in terms of exception handling, number of concurrent sessions to deliver etc.
        var sessionHandlerOptions =
			new SessionHandlerOptions(ExceptionReceivedHandler)
            {
				// Maximum number of Concurrent calls to the callback `ProcessSessionMessagesAsync`
                // Value 2 below indicates the callback can be called with a message for 2 unique
                // session Id's in parallel. Set it according to how many messages the application 
                // wants to process in parallel.
				MaxConcurrentSessions = 2,

				// Indicates the maximum time the Session Pump should wait for receiving messages for sessions.
                // If no message is received within the specified time, the pump will close that session and try to get messages
                // from a different session. Default is to wait for 1 minute to fetch messages for a session. Set to a 1 second
                // value here to allow the sample execution to finish fast but ideally leave this as 1 minute unless there 
                // is a specific reason to timeout earlier.
                MessageWaitTimeout = TimeSpan.FromSeconds(1),

				// Indicates whether SessionPump should automatically complete the messages after returning from User Callback.
                // False below indicates the Complete will be handled by the User Callback as in `ProcessSessionMessagesAsync`.
                AutoComplete = false
            };

        // Register the function that will process session messages
        queueClient.RegisterSessionHandler(ProcessSessionMessagesAsync, sessionHandlerOptions);
    }
	```

1. Create a new method called `SendSessionMessagesAsync` that sends sessionful messages to the queue with the following code:

    ```csharp
	static async Task SendSessionMessagesAsync(int numberOfSessions, int messagesPerSession)
    {
		const string SessionPrefix = "session";

        if (numberOfSessions == 0 || messagesPerSession == 0)
        {
			await Task.FromResult(false);
        }

        for (int i = 0; i < numberOfSessions; i++)
        {
			var messagesToSend = new List<Message>();
            string sessionId = SessionPrefix + i;
            for (int j = 0; j < messagesPerSession; j++)
            {
				// Create a new message to send to the queue
				string messageBody = "test" + j;
                var message = new Message(Encoding.UTF8.GetBytes(messageBody));
                // Assign a SessionId for the message
                message.SessionId = sessionId;
                messagesToSend.Add(message);

				// Write the sessionId, body of the message to the console
                Console.WriteLine($"Sending SessionId: {message.SessionId}, message: {messageBody}");
            }

            // Send a batch of messages corresponding to this sessionId to the queue
            await queueClient.SendAsync(messagesToSend);
        }

        Console.WriteLine($"Sent {messagesPerSession} messages each for {numberOfSessions} sessions.");
    }
    ```

1. Create a new method called `MainAsync` with the following code:
   
    ```csharp
    static async Task MainAsync(string[] args)
    {
		const int numberOfSessions = 5;
        const int numberOfMessagesPerSession = 3;

        queueClient = new QueueClient(ServiceBusConnectionString, QueueName);

		Console.WriteLine("======================================================");
        Console.WriteLine("Press any key to exit after receiving all the messages.");
        Console.WriteLine("======================================================");

		// Register Session Handler and Receive Session Messages
        RegisterOnSessionHandlerAndReceiveSessionMessages();

		// Send messages with sessionId set
        await SendSessionMessagesAsync(numberOfSessions, numberOfMessagesPerSession);      

        Console.ReadKey();

        await queueClient.CloseAsync();
    }
    ```

1. Add the following code to the `Main` method:
    
    ```csharp
    MainAsync(args).GetAwaiter().GetResult();
    ```

Congratulations! You have now sent and received session based messages to a ServiceBus queue, using QueueClient.
