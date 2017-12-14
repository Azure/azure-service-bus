# Message Senders and Receivers with Service Bus Topics

This sample shows how to interact with a Service Bus Topic via the ```MessageSender``` 
and ```MessageReceiver``` clients, as an alternative to the ``TopicClient`` and ``SubscriptionClient`` class introduced in 
the basic [TopicGettingStarted](../TopicsGettingStarted) sample. 

The sample is quasi identical to the [SendersReceiversWithQueues](../SendersReceiversWithQueues) sample since 
the API gestures for interacting with queues and topics through those API elements are the same. Showing that is the point 
of these two samples and in this document we will therefore focus on the few differences. 

## Prerequisites and Setup

Refer to the main [README](../README.md) document for setup instructions. All samples share and require the same setup
before they can be run.

## Sample Code 

The sample is documented inline in the [Program.cs](Program.cs) C# file.

The send-side of the sample is identical to the [SendersReceiversWithQueues](../SendersReceiversWithQueues) sample and therefore shows that
queues and topics can be used interchangeably, and that an application's messaging topology can indeed be flexibly adjusted while 
limiting or avoiding code churn.   

The *only* difference in the sender portion of this sample is that we're passing the name of a topic instead the name of a queue:

```C#
    var sender = new MessageSender(connectionString,topicName);
```

The receive side is also nearly identical. The ```Run()``` function passes the name of the subscription in addition to the topic name, 
and also gets to pass a different console color option for displaying the messages received on that subscription. 

``` C#

    async Task ReceiveMessagesAsync(string connectionString, string topicName, string subscriptionName, 
                                    CancellationToken cancellationToken, ConsoleColor color)
    {
        ... create factory ...

```

The only truly noteworthy difference is that we are constructing the ```MessageReceiver``` not over the path of the main entity 
as we do with queues, but first format a path to the subscription from the topic and subscription names, and then construct 
the ```MessageReceiver``` using the composite path. From the ```MessageReceiver``` perspective, the resulting path is completely 
interchangeable with any queue's path.

The static helper method ```SubscriptionClient.FormatSubscriptionPath()``` returns a path of the form ```{topic-name}/Subscription/{subscription-name}```

``` C#
>       var subscriptionPath = SubscriptionClient.FormatSubscriptionPath(topicName, subscriptionName);
        var receiver = new MessageReceiver(connectionString,subscriptionPath, ReceiveMode.PeekLock);
```

Generally, If you want to retain flexibility for your application's messaging topology, you will manage the path from which you receive 
messages for a component or service separately, likely in configuration. 
   
You can obviously also easily create a ```SubscriptionClient``` through the ```MessagingFactory``` as follows and in a 
single line:

```C#
    var receiver = new SubscriptionClient(topicName, subscriptionName);
``` 

The ```SubscriptionClient``` class differs from the regular receiver in that it has specific support for managing 
subscription rules at runtime. More on this in the [TopicFilters](../TopicFilters) sample. 

## Run()

The ```Run()``` method that is invoked by the common sample entry point first sends a few messages and kicks off the receivers for 
three subscriptions in parallel. The messages received from the subscriptions will differ in color, depending on which
subscription they were received from. 

The cancellation token passed to the receiver method is being triggered when the user presses any key sometime after sender and receiver have been kicked off. 

```C#
    public async Task Run(string connectionString, string topicName, string sendToken)
    {
        var cts = new CancellationTokenSource();

        await this.SendMessagesAsync(connectionString, topicName, sendToken);

        var allReceives = Task.WhenAll(
            this.ReceiveMessagesAsync(connectionString, topicName, "Subscription1", receiveToken, cts.Token, ConsoleColor.Cyan),
            this.ReceiveMessagesAsync(connectionString, topicName, "Subscription2", receiveToken, cts.Token, ConsoleColor.Green),
            this.ReceiveMessagesAsync(connectionString, topicName, "Subscription3", receiveToken, cts.Token, ConsoleColor.Yellow));
        Console.WriteLine("\nEnd of scenario, press any key to exit.");
        Console.ReadKey();

        cts.Cancel();
        await allReceives;
    }
```

## Running the sample

You can run the application from Visual Studio or on the command line from the sample's root directory by starting <code>bin/debug/SendersReceiversWithTopics.exe</code>
