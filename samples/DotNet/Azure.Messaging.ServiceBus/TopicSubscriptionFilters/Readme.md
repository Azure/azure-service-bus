# Topic Subscription Filters
This sample illustrates creating filtered subscriptions for topics. 

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
Use a disposable Standard or Premium namespace; Basic does not support topics.
Both applications use Microsoft Entra ID through `DefaultAzureCredential`.
Sign in with `az login` for local development, or configure a managed identity
in Azure. In PowerShell, set
`$env:SERVICEBUS_FULLY_QUALIFIED_NAMESPACE = "example.servicebus.windows.net"`.
Setup needs **Azure Service Bus Data Owner** on the namespace; the send/receive
application needs **Azure Service Bus Data Sender** and **Azure Service Bus Data
Receiver** on the topic. See the [catalog's RBAC setup](../README.md#existing-topic-examples).
Allow up to five minutes for role assignments to propagate. No SAS fallback
is configured. The sample creates
`topicfiltersampletopic` and four subscriptions and does not delete them.

From this folder:

```powershell
dotnet run --project .\CreateTopicAndSubscriptions
dotnet run --project .\SendAndReceiveMessages
```

## CreateTopicAndSubscription

1. Set `SERVICEBUS_FULLY_QUALIFIED_NAMESPACE` in your environment.
1. Build the project.
1. Run the app, which creates the following Service Bus entities:

    1. A topic named `topicfiltersampletopic`.
    2. Subscriptions to the above topic with the following settings. 

       | Subscription name |  Filter type |  Filter expression | Action | 
       | --------- | ----------- | ------------------ | ------ | 
       | AllOrders | True Rule filter | `1=1` | |
       | ColorBlueSize10Orders | SQL filter | `color = 'blue' AND quantity = 10` | | 
       | ColorRed | SQL filter | `color = 'red'` | `SET quantity = quantity / 2;REMOVE priority; SET sys.CorrelationId = 'low'`| 
       | HighPriorityRedOrders | Correlation filter | `"label": "red", "correlationId": "high"` | |

## SendAndReceiveMessages

1. Set `SERVICEBUS_FULLY_QUALIFIED_NAMESPACE` in your environment.
1. Build the project.
1. Run the app and inspect the output. The following transcript is illustrative,
   not live-run evidence. Send, delivery and property ordering can vary.

    ```bash    
    Sending orders to topic.
    Sent order with Color=yellow, Quantity=5, Priority=low
    Sent order with Color=blue, Quantity=10, Priority=low
    Sent order with Color=blue, Quantity=5, Priority=high
    Sent order with Color=blue, Quantity=5, Priority=low
    Sent order with Color=red, Quantity=5, Priority=low
    Sent order with Color=yellow, Quantity=5, Priority=low
    Sent order with Color=yellow, Quantity=10, Priority=high
    Sent order with Color=, Quantity=0, Priority=
    Sent order with Color=blue, Quantity=10, Priority=low
    Sent order with Color=red, Quantity=10, Priority=low
    Sent order with Color=red, Quantity=10, Priority=high
    Sent order with Color=yellow, Quantity=10, Priority=low
    Sent order with Color=red, Quantity=5, Priority=low
    All messages sent.
    
    Receiving messages from subscription AllOrders.
    color=blue,quantity=5,priority=low,CorrelationId=low
    color=red,quantity=10,priority=high,CorrelationId=high
    color=yellow,quantity=5,priority=low,CorrelationId=low
    color=blue,quantity=10,priority=low,CorrelationId=low
    color=blue,quantity=5,priority=high,CorrelationId=high
    color=blue,quantity=10,priority=low,CorrelationId=low
    color=red,quantity=5,priority=low,CorrelationId=low
    color=red,quantity=10,priority=low,CorrelationId=low
    color=red,quantity=5,priority=low,CorrelationId=low
    color=yellow,quantity=10,priority=high,CorrelationId=high
    color=yellow,quantity=5,priority=low,CorrelationId=low
    color=yellow,quantity=10,priority=low,CorrelationId=low
    color=,quantity=0,priority=,CorrelationId=
    Received 13 messages from subscription AllOrders.
    
    Receiving messages from subscription ColorBlueSize10Orders.
    color=blue,quantity=10,priority=low,CorrelationId=low
    color=blue,quantity=10,priority=low,CorrelationId=low
    Received 2 messages from subscription ColorBlueSize10Orders.
    
    Receiving messages from subscription ColorRed.
    color=red,quantity=5,RuleName=RedOrdersWithAction,CorrelationId=low
    color=red,quantity=2,RuleName=RedOrdersWithAction,CorrelationId=low
    color=red,quantity=5,RuleName=RedOrdersWithAction,CorrelationId=low
    color=red,quantity=2,RuleName=RedOrdersWithAction,CorrelationId=low
    Received 4 messages from subscription ColorRed.
    
    Receiving messages from subscription HighPriorityRedOrders.
    color=red,quantity=10,priority=high,CorrelationId=high
    Received 1 messages from subscription HighPriorityRedOrders.    
    ```

## Next steps
[Read more about topic filters in the documentation.](https://docs.microsoft.com/azure/service-bus-messaging/topic-filters)
