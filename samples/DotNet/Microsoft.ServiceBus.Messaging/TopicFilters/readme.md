# Topic Subscription Filters

This sample illustrates creating filtered subscriptions for topics. It shows a simple *true-filter* that lets all messages pass,
a filter with a composite SQL-like condition, a rule combining a filter with a set of actions, and a correlation filter 
condition.  

## Prerequisites and Setup

All samples share the same basic setup, explained in the main [README](../README.md) file. There are no extra setup steps for this sample.
The application entry points are in [Main.cs](../common/Main.md), which is shared across all samples. The sample implementations generally
reside in *Program.cs*, starting with *Run()*.

You can build the sample from the command line with the [build.bat](build.bat) or [build.ps1](build.ps1) scripts. This assumes that you
have the .NET Build tools in the path. You can also open up the [TopicFilters.sln](TopicFilters.sln) solution file with Visual Studio and build.
With either option, the NuGet package manager should download and install the **WindowsAzure.ServiceBus** package containing the
Microsoft.ServiceBus.dll assembly, including dependencies.

## What are Subscription Filters?

Each newly created topic subscription has at least one filter. If you don't explicitly specify one, the applied filter is the 
*true* filter that allows all messages to be selected into the subscription.  

In fact, filters are applied to subscriptions as part of of a *rule*, which is a named entity and combines a condition (the filter)
and an action. A single subscription may have hundreds of such rules. 

Most applications only need filters, and therefore the Service Bus API lets you bypass the added complexity of handling 
rules when you just need a single filter or just a single rule. This sample shows the simpler APIs, 
the [SubscriptionRules](../SubscriptionRules) sample shows how to manage rules for more complex use-cases at runtime.

Service Bus supports three kinds of filters:

* **Boolean filters** - in the form of the ```TrueFilter``` and the ```FalseFilter```. The conditions of these filters either cause all 
  arriving messages (```true```) or none of the arriving messages (```false```) to be selected for the subscription.
* **SQL Filters** - A ```SqlFilter``` holds a [SQL-like condition expression](https://msdn.microsoft.com/library/azure/microsoft.servicebus.messaging.sqlfilter.sqlexpression.aspx)
  that is evaluated in the broker against the arriving messages' user-defined properties and system properties. All system
  properties (which are all properties explicitly listed on the [```BrokeredMessage``` class](https://msdn.microsoft.com/library/microsoft.servicebus.messaging.brokeredmessage_properties.aspx)) 
  must be prefixed with ```sys.``` in the condition expression. The SQL subset implements testing for existence of properties (```EXISTS```), 
  testing for null-values (```IS NULL```), logical ```NOT```/```AND```/```OR```, relational operators, numeric arithmetic, and simple text pattern matching with ```LIKE```.
* **Correlation Filters** - A [```CorrelationFilter```](https://msdn.microsoft.com/library/microsoft.servicebus.messaging.correlationfilter.aspx) holds a
  set of conditions that are matched against one of more of an arriving message's user and system properties. A common use is a match 
  against the ```CorrelationId``` property, but the application can also choose to match against ```ContentType```, ```Label```, ```MessageId```, ```ReplyTo```, ```ReplyToSessionId```, 
  ```SessionId```, ```To```, and any user-defined properties. A match exists when an arriving message's value for a property is equal to the 
  value specified in the correlation filter. For string expressions, the comparison is case-sensitive. When specifying multiple match 
  properties, the filter combines them as a logical AND condition, meaning all conditions must match for the filter to match.   
                                          
All filters evaluate message properties. Filters cannot evaluate the message body.

The cost of choosing complex filter rules is lower overall message throughput at the subscription, topic, and namespace level, since evaluating
rules costs compute time. Whenever possible, applications should choose correlation filters over SQL-like filters since they are much more efficient in 
processing and therefore have less impact on throughput.

### Actions

For SQL filters, you can combine the filter condition with an *action*, that is executed on the message after is has been matched and 
before the message is selected into the topic. [*SqlRuleAction* expressions](https://msdn.microsoft.com/en-us/library/azure/microsoft.servicebus.messaging.sqlruleaction.sqlexpression.aspx) 
also lean on SQL and allow to modify or remove message properties as the message gets selected for the subscription. The 
changes to the message properties are private to the particular subscription. 


## The Sample

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
      var namespaceManager = new NamespaceManager(namespaceAddress, sharedAccessSignatureTokenProvider);
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
