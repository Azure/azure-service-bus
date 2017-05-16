# Getting Started with Service Bus Topics

This sample shows how to interact with the essential API elements for interacting with a Service Bus *Topic*.

The sample is nearly identical to the [QueuesGettingStarted](../QueuesGettingStarted) sample since 
the API gestures for interacting with *Queues* and *Topics* are largely the same. In this document we will 
therefore focus on the few differences between the samples. 

## Prerequisites and Setup

All samples share the same basic setup, explained in the main [README](../README.md) file. There are no extra setup steps for this sample.
The application entry points are in [Main.cs](../common/Main.md), which is shared across all samples. The sample implementations generally
reside in *Program.cs*, starting with ```Run()```.

You can build the sample from the command line with the [build.bat](build.bat) or [build.ps1](build.ps1) scripts. This assumes that you
have the .NET Build tools in the path. You can also open up the [TopicsGettingStarted.sln](TopicsGettingStarted.sln) solution file with Visual Studio and build.
With either option, the NuGet package manager should download and install the **WindowsAzure.ServiceBus** package containing the
Microsoft.ServiceBus.dll assembly, including dependencies.

## What is a Topic?

*Topics* are very similar to *Queues*. A Topic has one "tail" for submitting messages, exactly like a Queue, and it has zero or more named, 
user-configurable, durably created, service-side "heads", called *Subscriptions*, from which messages can be retrieved and which each act 
like independent *Queues*. 

Conceptually, every existing subscription receives a copy of each message that is sent into the topic, so that each subscriber can independently 
consume the complete message stream. Whether a message is selected into the subscription is determined by a filter condition; the default filter 
condition allows any message. Filters are further illustrated in the [TopicFilters](../TopicFilters) sample.      

> The factual implementation in Service Bus is more efficient than this conceptual idea. The message bodies are stored just once and 
> only a subset of the message properties are copied for each subscription. If you are sending many messges with larger messages bodies, 
> each of those message bodies therefore only counts once towards the topic's cumulative size quota.

## The Program

The send-side of the sample very similar to the [QueuesGettingStarted](../QueuesGettingStarted) sample. The only difference in the sender portion 
of this sample is that we're creating a ``TopicClient`` with a topic instead the name of a ```QueueClient```.

```C#
      this.sendClient = TopicClient.CreateFromConnectionString(connectionString, topicName);
```

The receive side is also not all that different, but since we're using a preconfigured *Topic* with three existing subscriptions, we set up and 
initialize three parallel receivers. To receive from a topic subscription, the simplest option is to use a ```SubscriptionClient``` initialized
with a connection string.  

``` C#

    this.subscription1Client = SubscriptionClient.CreateFromConnectionString(connectionString, topicName, "Subscription1");
    this.subscription2Client = SubscriptionClient.CreateFromConnectionString(connectionString, topicName, "Subscription2");
    this.subscription3Client = SubscriptionClient.CreateFromConnectionString(connectionString, topicName, "Subscription3");

    this.InitializeReceiver(this.subscription1Client, ConsoleColor.Cyan);
    this.InitializeReceiver(this.subscription2Client, ConsoleColor.Green);
    this.InitializeReceiver(this.subscription3Client, ConsoleColor.Yellow);

```

The way the receive loop is set up also echoes the sample in that it sets up an ```OnMessaageAsync```callback and with that starts receiving

```C#
void InitializeReceiver(SubscriptionClient receiver, ConsoleColor color)
{
    receiver.OnMessageAsync(
        async message =>
        {
            ... handle the message ...
            
            await message.CompleteAsync();
        },
        new OnMessageOptions { AutoComplete = false, MaxConcurrentCalls = 1 });
}
```

## Run()

The ```Run()``` method that is invoked by the common sample entry point first sends a few messages and kicks off the receivers for 
three *Subscriptions* in parallel. The messages received from the *Subscriptions* will differ in color depending on which
subscription tzhey were received from. 

## Running the sample

You can run the application from Visual Studio or on the command line from the sample's root directory by starting <code>bin/debug/TopicsGettingStarted.exe</code>
