# Prefetch
This sample illustrates the the "prefetch" feature of the Service Bus client.

The sample is specifically crafted to demonstrate the throughput difference between receiving 
messages with prefetch turned on and prefetch turned off. The default setting is for prefetch to be
turned off. 

## What is Prefetch?

The Prefetch feature is turned on by setting the ```PrefetchCount``` property of a ```MessageReceiver```,
```QueueClient```, or ```SubscriptionClient``` to a number greater than zero. Setting the value to zero 
turns prefetch off.

``` C#
     var receiver = await factory.CreateMessageReceiverAsync( ... )
     receiver.PrefetchCount = 10; // on, buffer size 10
     
     ... or ...
     
     receiver.PrefetchCount = 0; // off, default setting
```

You can easily add this setting to the receive-side of the [QueuesGettingStarted](../QueuesGettingStarted) or 
[ReceiveLoop](../ReceiveLoop) settings to see the effect in those contexts. 

When Prefetch is enabled, the receiver will quietly acquire more messages, up to the ```PrefetchCount``` 
limit, than what the application immediately asks for. A single initial ```Receive```/```ReceiveAsync``` call will 
therefore acquire a message for immediate consumption that will be returned as son as available, 
and the client will proceed to acquire further messages to fill the prefetch buffer in the background.

While messages are available in the prefetch buffer, any subsequent ```Receive```/```ReceiveAsync``` calls will be 
immediately satisfied from the buffer, and the buffer is replenished in the background as space 
becomes available. If there are no messages available for delivery, the receive operation will drain 
the buffer and then wait or block as expected.

Prefetch also works equivalently with the ```OnMessage``` and ```OnMessageAsync``` APIs.         
    
## If it is faster, why is Prefetch not the default option?

Prefetch speeds up the message flow by aiming to have a message readily available for local 
retrieval when and before the application asks for one.

This throughput gain is the result of a tradeoff decision that the application author needs to make 
explicitly:

* With the ```ReceiveAndDelete``` receive mode, all messages that are acquired into the prefetch buffer
  will no longer be available in the queue and will only reside in the in-memory prefetch buffer
  until they have been received into the application through the ```Receive```/```ReceiveAsync``` 
  or ```OnMessage```/```OnMessageAsync``` APIs. If the application terminates before the messages 
  have been received into the application, those messages are irrecoverably lost. 
* In the ```PeekLock``` receive mode, messages fetched into the Prefetch buffer will be acquired into 
  the buffer in a locked state and will have the timeout clock for the lock ticking. If the prefetch 
  buffer is large, and processing takes so long that message locks expire while residing in the 
  prefetch buffer or even expire while the application is processing the message, there might be some 
  confusing effects for the application to handle. 
  * The application might acquire a message with an expired or imminently expiring lock. If that is the
    case, the application might process the message, but then find that it cannot complete it due to 
    a lock expiration. The application can check the ```LockedUntilUtc``` property (which is subject to 
    clock skew between the broker and local machine's clock). If the message lock is expired, the 
    application must ignore the message; no API call on or with the message should be made. 
    If the message is not expired but expiration is imminent, the lock can be renewed and extended 
    by another default lock period by calling ```message.RenewLock()```
 *  If the lock silently expires in the prefetch buffer, the message is being treated as abandoned and
    is again made available for retrieval from the queue. That might then again cause it to be fetched 
    into the prefetch buffer; placed at the end. If the prefetch buffer cannot usually be worked through 
    during the message expiration, this will cause messages to be repeatedly prefetched but never 
    effectively delivered in a usable (validly locked) state and will eventually be tossed into the 
    deadletter queue once the maximum delivery count is exceeded.
    
 If you need a high degree of reliability for message processing and processing takes significant work
 and therefore time, it is recommended to use the prefetch feature very conservatively or not at all.
 
 If you need high throughout and message processing is commonly cheap, prefetch will yield 
 significant throughput benefits. 
 
 The maximum prefetch count and the lock duration configured on the queue or subscription need to 
 be balanced such that the lock timeout at least exceeds the cumulative expected message processing 
 time for  the maximum size of the prefetch buffer, plus one message. At the same time, the lock
 timeout ought not to be so long that messages can exceed their maximum TimeToLive when they are 
 accidentally dropped and require their lock to expire for being redelivered.                             
 

## Prerequisites and Setup

All samples share the same basic setup, explained in the main [README](../README.md) file. There are no extra setup steps for this sample.
The application entry points are in [Main.cs](../common/Main.md), which is shared across all samples. The sample implementations generally
reside in *Program.cs*, starting with *Run()*.

You can build the sample from the command line with the [build.bat](build.bat) or [build.ps1](build.ps1) scripts. This assumes that you
have the .NET Build tools in the path. You can also open up the [Prefetch.sln](Prefetch.sln) solution file with Visual Studio and build.
With either option, the NuGet package manager should download and install the **WindowsAzure.ServiceBus** package containing the
Microsoft.ServiceBus.dll assembly, including dependencies.

## Sample Code

Having worked through [QueuesGettingStarted](../QueuesGettingStarted) and [ReceiveLoop](../ReceiveLoop), you
will be familiar with the majority of API gestures we use here, so we're not going to through all of 
those again.

The sample sets up a very simple comparative performance test between two runs of the same method, once
with and once without prefetch enabled, end writes out the time difference in the end. For each run we
set up a fresh receiver.   

``` C#
    // Run 1
    var receiver = await receiverMessagingFactory.CreateMessageReceiverAsync(queueName, ReceiveMode.PeekLock);
    receiver.PrefetchCount = 0;
    // Send and Receive messages with prefetch OFF
    var timeTaken1 = await this.SendAndReceiveMessages(sender, receiver, 100);
    
    receiver.Close();
    
    // Run 2
    receiver = await receiverMessagingFactory.CreateMessageReceiverAsync(queueName, ReceiveMode.PeekLock);
    receiver.PrefetchCount = 10;
    // Send and Receive messages with prefetch ON
    var timeTaken2 = await this.SendAndReceiveMessages(sender, receiver, 100);
    
    receiver.Close();

    // Calculate the time difference
    var timeDifference = timeTaken1 - timeTaken2;

    Console.WriteLine("\nTime difference = {0} milliseconds", timeDifference);

    Console.WriteLine();
    Console.WriteLine("Press [Enter] to quit...");
    Console.ReadLine();
```

The core of the ```SendAndReceiveMessages``` method is a loop that picks up the previously sent 
messages and measures the time taken.

``` C#
        var stopWatch = new Stopwatch();
        stopWatch.Start();

        var receivedMessage = await receiver.ReceiveAsync(TimeSpan.FromSeconds(5));
        while (receivedMessage != null )
        {
            // here's where you'd do any work

            // complete (roundtrips)
            await receivedMessage.CompleteAsync();

            if (--messageCount <= 0)
                break;

            // now get the next message
            receivedMessage = await receiver.ReceiveAsync(TimeSpan.FromSeconds(5));
        }
        // Stop the stopwatch
        stopWatch.Stop();
``` 

When you execute the sample you will find that the prefetch variant will yield significantly
higher throughput, even though each message is explicitly being completed with another roundtrip 
network gesture.  