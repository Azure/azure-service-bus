# Priority Subscriptions

This sample illustrates how do use topic subscriptions and filters for splitting up a message streams into multiple distinct streams 
based on certain conditions. The concrete example use-case shown here is prioritization, where we split the message stream into 
three distinct streams, with processing priorities 1 and 2 having their own subscriptions, and priorities 3 and below having a 
shared subscription. Splitting up the message stream for routing to particular consumers for any other reason will look 
quite similar. 
 
## Prerequisites and Setup

All samples share the same basic setup, explained in the main [README](../README.md) file. There are no extra setup steps for this sample.
The application entry points are in [Main.cs](../common/Main.md), which is shared across all samples. The sample implementations generally
reside in *Program.cs*, starting with *Run()*.

You can build the sample from the command line with the [build.bat](build.bat) or [build.ps1](build.ps1) scripts. This assumes that you
have the .NET Build tools in the path. You can also open up the [TopicFilters.sln](TopicFilters.sln) solution file with Visual Studio and build.
With either option, the NuGet package manager should download and install the **WindowsAzure.ServiceBus** package containing the
Microsoft.ServiceBus.dll assembly, including dependencies.

## The Sample

The [TopicsGettingStarted](../TopicsGettingStarted) and [TopicFilter](../TopicFilters) samples already show most of the relevant 
foundations required fro using topics and subscriptions. We'll therefore focus on the particular pattern used here and how
and application might leverage it.

The application first creates a namespace manager and then tests whether a topic exists with that name, cleans it away 
if that is the case, and then sets up a new topic with three subscriptions. The setup of the three subscriptions can 
be done asynchronously and in parallel as shown below.

The SQL filters set for the subscriptions split up the value space of the 'Priority' property such that priorities 1 and 2 have their 
own subscriptions and all values above 2 go into the third subscription.  

``` C#
   await namespaceManager.CreateTopicAsync(topicDescription);
            await Task.WhenAll(
                // this sub receives messages for Priority = 1
                namespaceManager.CreateSubscriptionAsync(
                    new SubscriptionDescription(TopicName, "Priority1Subscription"),
                    new RuleDescription(new SqlFilter("Priority = 1"))),
                // this sub receives messages for Priority = 2
                namespaceManager.CreateSubscriptionAsync(
                    new SubscriptionDescription(TopicName, "Priority2Subscription"),
                    new RuleDescription(new SqlFilter("Priority = 2"))),
                // this sub receives messages for Priority Less than 2
                namespaceManager.CreateSubscriptionAsync(
                    new SubscriptionDescription(TopicName, "PriorityLessThan2Subscription"),
                    new RuleDescription(new SqlFilter("Priority > 2")))
                );
```

> **It is discouraged for applications to routinely set up and tear down topics and subscriptions as a part of regular message processing.**  

 Once the subscriptions are set up, we seed the topic with a 100 messages with random priorities:
 
 ``` C#
    var rand = new Random();
    for (var i = 0; i < 100; ++i)
    {
        var msg = new BrokeredMessage()
        {
            TimeToLive = TimeSpan.FromMinutes(2),
            Properties =
            {
                { "Priority", rand.Next(1, 4) }
            }
        };

        await topicClient.SendAsync(msg);
    }
 ``` 

## Receiver Strategies

There are several strategies for how to leverage this topology in an application. 

### What we do in the sample 

This sample uses a very simple consumption model that busily drains all three subscriptions in order of priority 
and then promptly exits. This is *not* something you will want to do in an application except precisely for 
draining, since all receive operations are configured to roundtrip to receive any existing message in the subscription, 
but to not wait for messages to arrive (that is what *TimeSpan.Zero* causes). 

The receive operation will first check for any available message on the first subscription, then on the second and 
finally on the third:    

``` C#
    while (true)
    {
        try
        {
            var message = await subClient1.ReceiveAsync(TimeSpan.Zero) ??
                            (await subClient2.ReceiveAsync(TimeSpan.Zero) ?? 
                            await subClient3.ReceiveAsync(TimeSpan.Zero));

            if (message != null)
            {
                this.OutputMessageInfo("Received: ", message);
            }
            else
            {
                break;
            }
        }
        catch (MessageNotFoundException)
        {
            Console.WriteLine("Got MessageNotFoundException, waiting for messages to be available");
        }
        catch (MessagingException e)
        {
            Console.WriteLine("Error: " + e.Message);
        }
    }
```

The result is that the input set of messages will be printed in priority order as the first subscription is
dranied first, the second is drained second, and the third is drained last. If you were to use this scheme 
in an application, your application would be executing a tight loop against Service Bus **and you 
will be charged a message operation for every empty roundtrip**, so your app may do things a bit differently,
but showing that in full detail is a bit difficult to show within the scope of a little feature sample,
so we'll just discuss it:

### What you might do in your application

If you need priority processing, you will want to set aside reserved resources in your system. You might have 
one or multiple processing nodes that have pending receivers on the highest priority subscription at all
times and process only the highest priority jobs. For smaller systems, instead of dedicating an entire node 
(machine), ypou might have a dedicated process for high priority processing that enjoys exclusive usage 
of a processor core.

In parallel, you may then have a shared resource pool for processing lower priority jobs, with a pending 
receiver on the second priority and another receiver on the lower priorities for each running processing
node or process. 

Thus, for a topology as we show here in this example, the consumers that pull from the subscriptions 
will typically be spread across multiple different machines and/or processes that are configured to 
process the work jobs carried by the messages with appropriate priority. 

A potential layout might be like shown here, with processors always doubled-up for availability. The 
lowest priority tier may share resources with different work. 

```
                       <--- Hi-Pri Processor 1 [dedicated large VM]
    +--- Sub1 / Pri1   <--- Hi-Pri Processor 2 [dedicated large VM]
    |                  <--- Hi-Pri Processor 3 [dedicated large VM]
    |
    |
T --+--- Sub2 / Pri2   <--- Mid-Pri Processor 1 [dedicated midsize VM]
    |                  <--- Mid-Pri Processor 2 [dedicated midsize VM] 
    |
    |                  <--- Low-Pri Processor 1 [VM process/thread]
    +--- Sub3 / Pri3+  <--- Low-Pri Processor 2 [VM process/thread]
                       <--- Low-Pri Processor 3 [VM process/thread] 

```   




   








  