# Duplicate Detection

This sample illustrates the "duplicate detection" feature of the Service Bus client.

The sample is specifically crafted to demonstrate the effect of duplicate detection when
enabled on a queue or topic. The default setting is for duplicate detection to be turned off. 

## What is duplicate detection?

Enabling duplicate detection will keep track of the ```MessageId``` of all messages sent into 
a queue or topic during a defined time window. If any new message is sent that carries a 
```MessageId``` that has already been logged during the time window, the message will be reported
as being accepted (the send operation succeeds), but the newly sent message will be instantly 
ignored and dropped. No other parts of the message are considered.

## Why would I need this?

Simply stated, if a client wanted to send a message, and erroneously believes it couldn't send the message due 
to some error condition, a retry will cause the same message to end up in the system twice. 

It is quite possible that a message gets committed into the queue and acknowledgment can't 
be returned to the sender. Duplicate detection takes the doubt out of this situation by letting
the sender re-send the same message and the queue tossing out any duplicate copy.

## How do I turn it on?

The feature can be turned on setting [```QueueDescription.RequiresDuplicateDetection```](https://msdn.microsoft.com/library/azure/microsoft.servicebus.messaging.queuedescription.requiresduplicatedetection.aspx) or
[```TopicDescription.RequiresDuplicateDetection```]() to ```true``` when creating a queue or topic.  

The sample's setup script creates a queue with this property turned on and this sample uses that queue.

You can configure the size of the duplicate detection window during which message-ids are being
retained with the expressively named [```QueueDescription.DuplicateDetectionHistoryTimeWindow```](https://msdn.microsoft.com/en-us/library/azure/microsoft.servicebus.messaging.queuedescription.duplicatedetectionhistorytimewindow.aspx) property. The default
value is 10 minutes. 

> Mind that the enabling duplicate detection and size of the window will directly impacts a queue's (and a topic's) throughgput.
> Keeping the window small, means that fewer message-ids must be retained and matched and throughput is impacted less. For 
> high throughput entities that require duplicate detection, you should keep the window as small as feasible for the use-case.     
   
## Prerequisites and Setup

All samples share the same basic setup, explained in the main [README](../README.md) file. There are no extra setup steps for this sample.
The application entry points are in [Main.cs](../common/Main.md), which is shared across all samples. The sample implementations generally
reside in *Program.cs*, starting with *Run()*.

You can build the sample from the command line with the [build.bat](build.bat) or [build.ps1](build.ps1) scripts. This assumes that you
have the .NET Build tools in the path. You can also open up the [QueuesGettingStarted.sln](QueuesGettingStarted.sln) solution file with Visual Studio and build.
With either option, the NuGet package manager should download and install the **WindowsAzure.ServiceBus** package containing the
Microsoft.ServiceBus.dll assembly, including dependencies.

## Sample Code

Having worked through [QueuesGettingStarted](../QueuesGettingStarted) and [ReceiveLoop](../ReceiveLoop), you
will be familiar with the majority of API gestures we use here, so we're not going to through all of 
those again.

The sample really just sends two messages that have the same MessageId set:  

``` C#
    // Send messages to queue
    Console.WriteLine("\tSending messages to {0} ...", queueName);
    var message = new BrokeredMessage
    {
        MessageId = "ABC123",
        TimeToLive = TimeSpan.FromMinutes(1)
    };
    await sender.SendAsync(message);
    Console.WriteLine("\t=> Sent a message with messageId {0}", message.MessageId);

    var message2 = new BrokeredMessage
    {
        MessageId = "ABC123",
        TimeToLive = TimeSpan.FromMinutes(1)
    };
    await sender.SendAsync(message2);
```

Following that is a simple loop that receives messages until the queue is empty:

``` C#
    while (true)
    {
        var receivedMessage = await receiver.ReceiveAsync(TimeSpan.FromSeconds(10));

        if (receivedMessage == null)
        {
            break;
        }
        Console.WriteLine("\t<= Received a message with messageId {0}", receivedMessage.MessageId);
        await receivedMessage.CompleteAsync();
        if (receivedMessageId.Equals(receivedMessage.MessageId, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\t\tRECEIVED a DUPLICATE MESSAGE");
        }

        receivedMessageId = receivedMessage.MessageId;
    }
``` 

When you execute the sample you will find that the second message is not being received. As expected.