# Get started sending and receiving messages from ServiceBus queues using QueueClient

In order to run the sample in this directory, replace the following bracketed values in the `Program.cs` file.

```csharp
// Connection String for the namespace can be obtained from the Azure portal under the 
// `Shared Access policies` section.
const string ServiceBusConnectionString = "{ServiceBus connection string}";
const string EntityPath = "{Queue Name or Subscription Path}";
```

Once you replace the above values run the following from a command prompt:
   
```
dotnet restore
dotnet build
dotnet run
```

## The Sample Program
To keep things reasonably simple, the sample program has only the code for receiving dead letter messages.
Typically in real world applications, dead letter messages are processed in the same application as the one 
processing messages from a queue or a topic subscription. This application processes dead letter messages immediately
as they arrive. Another approach is to check dead letter messages on predefined configurable intervals.

For further information on how to create this sample on your own, follow the rest of the tutorial.

## What will be accomplished
In this tutorial, we will write a console application to receive dead lettered messages for a ServiceBus queue/subscription using a MessageReceiver.

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
    const string ServiceBusConnectionString = "{ServiceBus connection string}";
    const string EntityPath = "{Queue Name or Subscription Path}";
    static IMessageReceiver deadLetterReceiver;
    ```

1. Create a new Task called `ProcessDeadLetterQueueMessagesAsync` that knows how to handle received dead letter messages with the following code:

	```csharp
    static async Task ProcessDeadLetterQueueMessagesAsync(Message message, CancellationToken token)
    {
        // Process the message
        // This could include retries or logging for further troubleshooting
        Console.WriteLine($"Received DLQ message: SequenceNumber:{message.SystemProperties} Body:{Encoding.UTF8.GetString(message.Body)}");

        // Complete the message so that it is not received again.
        // This can be done only if the deadLetterReceiver is created in ReceiveMode.PeekLock mode (which is default).
        await deadLetterReceiver.CompleteAsync(message.SystemProperties.LockToken);
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

1. Create a new method called 'RegisterDeadLetterQueueMessageHandler' to register the `ProcessDeadLetterQueueMessagesAsync` and the 
`ExceptionReceivedHandler` with the necessary `MessageHandlerOptions` parameters to start receiving messages

	```csharp
    static void RegisterDeadLetterQueueMessageHandler()
    {
        // Configure the MessageHandler Options in terms of exception handling, number of concurrent messages to deliver etc.
        var messageHandlerOptions = new MessageHandlerOptions(ExceptionReceivedHandler)
        {
            // Maximum number of Concurrent calls to the callback `ProcessMessagesAsync`, set to 1 for simplicity.
            // Set it according to how many messages the application wants to process in parallel.
            MaxConcurrentCalls = 1,

            // Indicates whether MessagePump should automatically complete the messages after returning from User Callback.
            // False below indicates the Complete will be handled by the User Callback as in `ProcessMessagesAsync` below.
            AutoComplete = false
        };

        // Register the function that will process messages
        deadLetterReceiver.RegisterMessageHandler(ProcessDeadLetterQueueMessagesAsync, messageHandlerOptions);
    }
	```

1. Create a new method called `MainAsync` with the following code:
   
    ```csharp
    static async Task MainAsync()
    {
        deadLetterReceiver = new MessageReceiver(ServiceBusConnectionString, EntityNameHelper.FormatDeadLetterPath(EntityPath), ReceiveMode.PeekLock);

        Console.WriteLine("==========================================================================");
        Console.WriteLine("Press any key to exit after processing all the dead letter queue messages.");
        Console.WriteLine("==========================================================================");
            
        // Register QueueClient's DLQ MessageHandler 
        RegisterDeadLetterQueueMessageHandler();
                        
        Console.ReadKey();
            
        await deadLetterReceiver.CloseAsync();
    }
    ```

1. Add the following code to the `Main` method:
    
    ```csharp
    MainAsync(args).GetAwaiter().GetResult();
    ```

Congratulations! You have now received dead letter messages for a ServiceBus queue/subscription.
