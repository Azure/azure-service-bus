# Get started configuring and managing rules for Subscriptions

In order to run the sample in this directory, replace the following bracketed values in the `Program.cs` file.

```csharp
// Connection String for the namespace can be obtained from the Azure portal under the 
// `Shared Access policies` section.
const string ServiceBusConnectionString = "{Service Bus connection string}";
const string TopicName = "{Topic Name}";

// Simply create 4 default subscriptions (no rules specified explicitly) and provide subscription names. 
// The Rule addition will be done as part of the sample depending on the subscription behavior expected.
const string allMessagesSubscriptionName = "{Subscription 1 Name}";
const string sqlFilterOnlySubscriptionName = "{Subscription 2 Name}";
const string sqlFilterWithActionSubscriptionName = "{Subscription 3 Name}";
const string correlationFilterSubscriptionName = "{Subscription 4 Name}";
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
Topics are similar to Queues for the send side of the application. However unlike Queues, Topic can have zero or more subscriptions,
from which messages can be retrieved and each of subscription act like independent queues. Whether a message is selected into the
subscription is determined by the Filter condition for the subscription. Filters can be one of the following:

1. `TrueFilter` - Selects all messages to subscription, 
2. `FalseFilter` - Selects none of the messages to subscription, 
3. `SqlFilter` - Holds a SQL-like condition expression that is evaluated in the ServiceBus service against the arriving messages'
user-defined properties and system properties and if matched the message is selected for subscription.
4. `CorrelationFilter` - Holds a set of conditions that is evaluated in the ServiceBus service against the arriving messages'
user-defined properties and system properties. A match exists when an arriving message's value for a property is equal to the
value specified in the correlation filter.

In this tutorial, we will write a console application to manage rules on Subscription (`AddRule`, `GetRules`, `RemoveRules`).
We will also explore different forms of subscription filters. Refer to the 
link(https://github.com/Azure/azure-service-bus/tree/master/samples/DotNet/Microsoft.ServiceBus.Messaging/TopicFilters) for a more 
detailed explanation of filters.

## Prerequisites
1. [.NET Core](https://www.microsoft.com/net/core)
2. An Azure subscription.
3. [A ServiceBus namespace](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-create-namespace-portal) 
4. [A ServiceBus Topic](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dotnet-how-to-use-topics-subscriptions#2-create-a-topic-using-the-azure-portal)
5. [ServiceBus Subscriptions](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dotnet-how-to-use-topics-subscriptions)

### Create a console application

- Create a new .NET Core application. Check out [this link](https://docs.microsoft.com/en-us/dotnet/articles/core/getting-started) with help to create a new application on your operating system.

### Add the ServiceBus client reference

1. Add the following to your project.json, making sure that the solution references the `Microsoft.Azure.ServiceBus` project.

    ```json
    "Microsoft.Azure.ServiceBus": "1.0.0"
    ```

### Write some code to send messages to the topic, manage rules and receive messages from the subscription
1. Add the following using statement to the top of the Program.cs file.
   
    ```csharp
    using Microsoft.Azure.ServiceBus;
    ```

1. Add the following variables to the `Program` class, and replace the placeholder values:
    
    ```csharp
    const string ServiceBusConnectionString = "{Service Bus connection string}";
    const string TopicName = "{Topic Name}";
    const string allMessagesSubscriptionName = "{Subscription 1 Name}";
    const string sqlFilterOnlySubscriptionName = "{Subscription 2 Name}";
    const string sqlFilterWithActionSubscriptionName = "{Subscription 3 Name}";
    const string correlationFilterSubscriptionName = "{Subscription 4 Name}";
    ```

1. Create the following methods that will send messages with various combinations to the topic:

    ```csharp
    static async Task SendMessagesAsync()
    {
		Console.WriteLine($"==========================================================================");
        Console.WriteLine("Sending Messages to Topic");
        try
        {
			await Task.WhenAll(
				SendMessageAsync(label: "Red"),
                SendMessageAsync(label: "Blue"),
                SendMessageAsync(label: "Red", correlationId: "important"),
                SendMessageAsync(label: "Blue", correlationId: "important"),
                SendMessageAsync(label: "Red", correlationId: "notimportant"),
                SendMessageAsync(label: "Blue", correlationId: "notimportant"),
                SendMessageAsync(label: "Green"),
                SendMessageAsync(label: "Green", correlationId: "important"),
                SendMessageAsync(label: "Green", correlationId: "notimportant")
            );
        }
        catch (Exception exception)
        {
			Console.WriteLine($"{DateTime.Now} :: Exception: {exception.Message}");
        }
    }

    static async Task SendMessageAsync(string label, string correlationId = null)
    {
		Message message = new Message { Label = label };
        message.UserProperties.Add("Color", label);
		
		if (correlationId != null)
        {
			message.CorrelationId = correlationId;
        }

        await topicClient.SendAsync(message);
        Console.WriteLine($"Sent Message:: Label: {message.Label}, CorrelationId: {message.CorrelationId ?? message.CorrelationId}");
    }
    ```

1. Create a new method Task `ReceiveMessagesAsync` with the following code to process messages from a given subscription:
	```csharp
	static async Task ReceiveMessagesAsync(string subscriptionName)
    {
		string subscriptionPath = EntityNameHelper.FormatSubscriptionPath(TopicName, subscriptionName);
        IMessageReceiver subscriptionReceiver = new MessageReceiver(ServiceBusConnectionString, subscriptionPath, ReceiveMode.ReceiveAndDelete);

        Console.WriteLine($"==========================================================================");
        Console.WriteLine($"{DateTime.Now} :: Receiving Messages From Subscription: {subscriptionName}");
        int receivedMessageCount = 0;
        while (true)
        {
			var receivedMessage = await subscriptionReceiver.ReceiveAsync(TimeSpan.Zero);
            if (receivedMessage != null)
            {
				object colorProperty;
                receivedMessage.UserProperties.TryGetValue("Color", out colorProperty);
                Console.WriteLine($"Color Property = {colorProperty}, CorrelationId = {receivedMessage.CorrelationId ?? receivedMessage.CorrelationId}");
                receivedMessageCount++;
            }
            else
            {
				break;
            }
        }

        Console.WriteLine($"{DateTime.Now} :: Received '{receivedMessageCount}' Messages From Subscription: {subscriptionName}");
        Console.WriteLine($"==========================================================================");
    }
	```

1. Create a new method called `MainAsync` with the following code:
   
    ```csharp
    static async Task MainAsync()
    {
		topicClient = new TopicClient(ServiceBusConnectionString, TopicName);
        allMessagessubscriptionClient = new SubscriptionClient(ServiceBusConnectionString, TopicName, allMessagesSubscriptionName);
        sqlFilterOnlySubscriptionClient = new SubscriptionClient(ServiceBusConnectionString, TopicName, sqlFilterOnlySubscriptionName);
        sqlFilterWithActionSubscriptionClient = new SubscriptionClient(ServiceBusConnectionString, TopicName, sqlFilterWithActionSubscriptionName);
        correlationFilterSubscriptionClient = new SubscriptionClient(ServiceBusConnectionString, TopicName, correlationFilterSubscriptionName);

        // First Subscription is already created with default rule. Leave as is.

        // 2nd Subscription: Add SqlFilter on Subscription 2
        // Delete Default Rule.
        // Add the required SqlFilter Rule
        // Note: Does not apply to this sample but if there are multiple rules configured for a 
        // single subscription, then one message is delivered to the subscription when any of the 
        // rule matches. If more than one rules match and if there is no `SqlRuleAction` set for the
        // rule, then only one message will be delivered to the subscription. If more than one rules
        // match and there is a `SqlRuleAction` specified for the rule, then one message per `SqlRuleAction`
        // is delivered to the subscription.
        Console.WriteLine($"SubscriptionName: {sqlFilterOnlySubscriptionName}, Removing Default Rule and Adding SqlFilter");
        await sqlFilterOnlySubscriptionClient.RemoveRuleAsync(RuleDescription.DefaultRuleName);
        await sqlFilterOnlySubscriptionClient.AddRuleAsync(new RuleDescription
			{
              Filter = new SqlFilter("Color = 'Red'"),
                Name = "RedSqlRule"
            });

        // 3rd Subscription: Add SqlFilter and SqlRuleAction on Subscription 3
        // Delete Default Rule
        // Add the required SqlFilter Rule and Action
        Console.WriteLine($"SubscriptionName: {sqlFilterWithActionSubscriptionName}, Removing Default Rule and Adding SqlFilter and SqlRuleAction");
        await sqlFilterWithActionSubscriptionClient.RemoveRuleAsync(RuleDescription.DefaultRuleName);
        await sqlFilterWithActionSubscriptionClient.AddRuleAsync(new RuleDescription
            {
                Filter = new SqlFilter("Color = 'Blue'"),
                Action = new SqlRuleAction("SET Color = 'BlueProcessed'"),
                Name = "BlueSqlRule"
            });

        // 4th Subscription: Add Correlation Filter on Subscription 4
        Console.WriteLine($"SubscriptionName: {sqlFilterWithActionSubscriptionName}, Removing Default Rule and Adding CorrelationFilter");
        await correlationFilterSubscriptionClient.RemoveRuleAsync(RuleDescription.DefaultRuleName);
        await correlationFilterSubscriptionClient.AddRuleAsync(new RuleDescription
        {
			Filter = new CorrelationFilter() { Label = "Red", CorrelationId = "important" },
            Name = "ImportantCorrelationRule"
        });

        // Get Rules on Subscription, called here only for one subscription as example
        var rules = (await correlationFilterSubscriptionClient.GetRulesAsync()).ToList();
        Console.WriteLine($"GetRules:: SubscriptionName: {correlationFilterSubscriptionName}, CorrelationFilter Name: {rules[0].Name}, Rule: {rules[0].Filter}");

        // Send messages to Topic
        await SendMessagesAsync();

        // Receive messages from 'allMessagesSubscriptionName'. Should receive all 9 messages 
        await ReceiveMessagesAsync(allMessagesSubscriptionName);

        // Receive messages from 'sqlFilterOnlySubscriptionName'. Should receive all messages with Color = 'Red' i.e 3 messages
        await ReceiveMessagesAsync(sqlFilterOnlySubscriptionName);

        // Receive messages from 'sqlFilterWithActionSubscriptionClient'. Should receive all messages with Color = 'Blue'
        // i.e 3 messages AND all messages should have color set to 'BlueProcessed'
        await ReceiveMessagesAsync(sqlFilterWithActionSubscriptionName);

        // Receive messages from 'correlationFilterSubscriptionName'. Should receive all messages  with Color = 'Red' and CorrelationId = "important"
        // i.e 1 message
        await ReceiveMessagesAsync(correlationFilterSubscriptionName);

        Console.WriteLine("=========================================================");
        Console.WriteLine("Completed Receiving all messages... Press any key to exit");
        Console.WriteLine("=========================================================");

        Console.ReadKey();

        await allMessagessubscriptionClient.CloseAsync();
        await sqlFilterOnlySubscriptionClient.CloseAsync();
        await sqlFilterWithActionSubscriptionClient.CloseAsync();
        await correlationFilterSubscriptionClient.CloseAsync();
        await topicClient.CloseAsync();
	}
    ```

1. Add the following code to the `Main` method:
    
    ```csharp
    MainAsync(args).GetAwaiter().GetResult();
    ```

Congratulations! You have now learnt to configure and manage rules for a ServiceBus Topic Subscription.
