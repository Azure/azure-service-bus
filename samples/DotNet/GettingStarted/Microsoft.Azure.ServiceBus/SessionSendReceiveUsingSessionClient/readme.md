# Get started sending and receiving session based messages from Service Bus queues using SessionClient

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
In this tutorial, we will write a console application to send and receive sessionful messages to a ServiceBus queue using a SessionClient.
Sending session based messages to a queue using MessageSender is same as sending other messages but the messages are stamped with an additional 
`SessionId` property. SessionClient offers a more granular control to the user for receiving Session based messages than `QueueClient`. The User 
can explicitly choose to accept sessions with a particular `SessionId`, defer messages received from a session and accept deffered messages on that session.
But this also means the User has to write more code to accept MessageSessions, renew session locks, complete messages and 
define how to achieve a basic degree of concurrency while processing sessions.

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
    static IMessageSender messageSender;
    static ISessionClient sessionClient;
	const string SessionPrefix = "session-prefix";
    ```

1. Create a new Task called `ReceiveSessionMessagesAsync` that knows how to receive messages from a session using SessionClient with the following code:

	```csharp
	static async Task ReceiveSessionMessagesAsync(int numberOfSessions, int messagesPerSession)
    {
		Console.WriteLine("===================================================================");
        Console.WriteLine("Accepting sessions in the reverse order of sends for demo purposes");
        Console.WriteLine("===================================================================");

		for (int i = 0; i < numberOfSessions; i++)
		{
			int messagesReceivedPerSession = 0;
			
			// AcceptMessageSessionAsync(i.ToString()) as below with session id as parameter will try to get a session with that sessionId.
            // AcceptMessageSessionAsync() without any messages will try to get any available session with messages associated with that session.
            IMessageSession session = await sessionClient.AcceptMessageSessionAsync(SessionPrefix + i.ToString());

            if(session != null)
            {
				// Messages within a session will always arrive in order.
				Console.WriteLine("=====================================");
				Console.WriteLine($"Received Session: {session.SessionId}");
				Console.WriteLine($"Receiving all messages for this Session");

				while(messagesReceivedPerSession++ < messagesPerSession)
                {
					Message message = await session.ReceiveAsync();
					Console.WriteLine($"Received message: SequenceNumber:{message.SystemProperties.SequenceNumber} Body:{Encoding.UTF8.GetString(message.Body)}");

                    // Complete the message so that it is not received again.
                    // This can be done only if the queueClient is created in ReceiveMode.PeekLock mode (which is default).
                    await session.CompleteAsync(message.SystemProperties.LockToken);
				}

				Console.WriteLine($"Received all messages for Session: {session.SessionId}");
                Console.WriteLine("=====================================");

                // Close the Session after receiving all messages from the session
                await session.CloseAsync();
            }
        }
    }
	```

1. Create a new method called `SendSessionMessagesAsync` that sends sessionful messages to the queue with the following code:

    ```csharp
	static async Task SendSessionMessagesAsync(int numberOfSessions, int messagesPerSession)
    {
        if (numberOfSessions == 0 || messagesPerSession == 0)
        {
			await Task.FromResult(false);
        }

        for (int i = numberOfSessions - 1; i >= 0; i--)
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

		Console.WriteLine("=====================================");
        Console.WriteLine($"Sent {messagesPerSession} messages each for {numberOfSessions} sessions.");
		Console.WriteLine("=====================================");
    }
    ```

1. Create a new method called `MainAsync` with the following code:
   
    ```csharp
    static async Task MainAsync(string[] args)
    {
		const int numberOfSessions = 5;
        const int numberOfMessagesPerSession = 3;

        messageSender = new MessageSender(ServiceBusConnectionString, QueueName);
        sessionClient = new SessionClient(ServiceBusConnectionString, QueueName);

		Console.WriteLine("======================================================");
        Console.WriteLine("Press any key to exit after receiving all the messages.");
        Console.WriteLine("======================================================");

		// Send messages with sessionId set
        await SendSessionMessagesAsync(numberOfSessions, numberOfMessagesPerSession);      

		// Receive all Session based messages using SessionClient
        await ReceiveSessionMessagesAsync(numberOfSessions, numberOfMessagesPerSession);

        Console.ReadKey();

		await messageSender.CloseAsync();
        await sessionClient.CloseAsync();
    }
    ```

1. Add the following code to the `Main` method:
    
    ```csharp
    MainAsync(args).GetAwaiter().GetResult();
    ```

Congratulations! You have now sent and received session based messages to a ServiceBus queue, using SessionClient.
