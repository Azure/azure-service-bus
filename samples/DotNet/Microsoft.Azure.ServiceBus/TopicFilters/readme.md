# Topic Subscription Filters

This sample illustrates creating filtered subscriptions for topics. It shows a simple *true-filter* that lets all messages pass,
a filter with a composite SQL-like condition, a rule combining a filter with a set of actions, and a correlation filter 
condition.  

[Read more about topic filters in the documentation.](https://docs.microsoft.com/azure/service-bus-messaging/topic-filters)

## Prerequisites and Setup

Refer to the main [README](../README.md) document for setup instructions. All samples share and require the same setup
before they can be run.

## Sample Code 

The sample is documented inline in the [Program.cs](Program.cs) C# file.

The sample code shows how to create subscriptions with filters using the Service Bus management API (via the ```NamespaceManager``` class)
and also shows the effects of those filters at runtime.

> **It is DISCOURAGED for applications to routinely set up and tear down topics and subscriptions as a part of regular message processing.**
>
> Managing topics and subscriptions should be treated as a system (re-)configuration operation and therefore only executed when the 
> application is being set up, removed, or reconfigured. This recommendation includes *all* operations on the *NamespaceManager*, 
> including the *Queue/Topic/SubscriptionExists* and *GetQueue/Topic/Subscription* operations. These operations should specifically 
> **NOT** be used to determine whether an entity exists before sending or receiving, which will throw an appropriate exception, or 
> to determine availability of messages before attempting to receive them.   

Most of the key functionality to look through is right in the sample's ```Run()``` method that is invoked by the boilerplate code
with a namespace address and a token that gives the sample full management permissions on the namespace. Those permissions are 
required to work with most operations on the ```NamespaceManager``` class.

```C#    
      // Create messaging factory and ServiceBus namespace client.
      var sharedAccessSignatureTokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(manageToken);
      var namespaceManager = new NamespaceManager(connectionString, sharedAccessSignatureTokenProvider);
``` 

The sample first checks whether the topic still exists if it hasn't been cleaned up by an earlier run. If the topic exists
we delete it, which also deletes all related subscriptions, so that we have a clean environment. An application would 
typically *not* toss out existing entities and rather change them; you can see how to change rules on existing 
subscriptions in the [SubscriptionRules](../SubscriptionRules) sample. Then we create the topic:

```C#
     if (await namespaceManager.TopicExistsAsync(TopicName))
     {
        await namespaceManager.DeleteTopicAsync(TopicName);
     }
     var topicDescription = await namespaceManager.CreateTopicAsync(TopicName);
```

On the topic, we create four different kinds of subscriptions with different filter options:


The first subscription has a simple *TrueFilter*, which is also applied if you omit the filter 
parameter altogether:
 
```C#
    await namespaceManager.CreateSubscriptionAsync(topicDescription.Path, SubscriptionAllMessages, new TrueFilter());
```

The next subscription implements a SQL filter using the expression ```color = 'blue' AND quantity = 10``` that is applied
for all arriving messages. If an arriving message has both properties and both properties have the required values, 
the message gets selected for the subscription.  
 
```C# 
    await namespaceManager.CreateSubscriptionAsync(
        topicDescription.Path,
        SubscriptionColorBlueSize10Orders,
        new SqlFilter("color = 'blue' AND quantity = 10"));
```

The following subscription is created with a full ```RuleDescription```, that includes a filter checking for ```color = 'red'```
and an action that will modify each matched message. Each matched message will have the 'quantity' property numeric value 
halved, the 'priority' property removed, and the ```CorrelationId``` system property set to "low".

``` C#      
    await namespaceManager.CreateSubscriptionAsync(
        topicDescription.Path,
        SubscriptionColorRed,
        new RuleDescription
        {
            Name = "RedRule",
            Filter = new SqlFilter("color = 'red'"),
            Action = new SqlRuleAction(
                "SET quantity = quantity / 2;" +
                "REMOVE priority;" +
                "SET sys.CorrelationId = 'low';")
        });
```

Finally, we create a subscription that uses a ```CorrelationFilter```, which checks for the values of the ```Label``` and
```CorrelationId``` properties.  

``` C#
    namespaceManager.CreateSubscription(topicDescription.Path, SubscriptionHighPriorityOrders, 
        new CorrelationFilter { Label = "red", CorrelationId = "high"});
```

The application will then send several messages, receive them through the subscriptions we created, and 
print out the relevant message properties.

The sample sends these messages:

```
Sent order with Color=red, Quantity=10, Priority=high
Sent order with Color=yellow, Quantity=5, Priority=low
Sent order with Color=blue, Quantity=5, Priority=low
Sent order with Color=blue, Quantity=10, Priority=low
Sent order with Color=blue, Quantity=5, Priority=high
Sent order with Color=blue, Quantity=10, Priority=low
Sent order with Color=red, Quantity=5, Priority=low
Sent order with Color=red, Quantity=10, Priority=low
Sent order with Color=yellow, Quantity=5, Priority=low
Sent order with Color=red, Quantity=5, Priority=low
Sent order with Color=yellow, Quantity=10, Priority=high
Sent order with Color=yellow, Quantity=10, Priority=low
Sent order with Color=, Quantity=0, Priority=
```

From the first, "AllOrders" subscription we get all of those back:

```
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
```
On the "ColorBlueSize10Orders" subscription we only get the expected matches:

```C#
color=blue,quantity=10,priority=low,CorrelationId=low
color=blue,quantity=10,priority=low,CorrelationId=low
```

On the "ColorRed" subscription you can see how the matched input is modified
by the rule's action:

```C#
color=red,quantity=5,RuleName=RedRule,CorrelationId=low
color=red,quantity=2,RuleName=RedRule,CorrelationId=low
color=red,quantity=5,RuleName=RedRule,CorrelationId=low
color=red,quantity=2,RuleName=RedRule,CorrelationId=low
``` 
And the "HighPriorityOrders" subscription has a correlation filter for high priority and 
label value "red" that occurs only once ans thus:

``` 
color=red,quantity=10,priority=high,CorrelationId=high
``` 

## Running the sample

You can run the application from Visual Studio or on the command line from the sample's root 
directory by starting <code>bin/debug/sample.exe</code>
