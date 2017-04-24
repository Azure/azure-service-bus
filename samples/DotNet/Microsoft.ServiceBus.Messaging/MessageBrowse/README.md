# Message Browsing (Peek)

This sample shows how to enumerate messages residing in a Queue or Topic subscription. This feature is typically used for diagnostic and troubleshooting 
purposes and/or for tooling built on top of Service Bus. 

## How does Peek work?

The ```Peek```/```PeekAsync``` and ```PeekBatch```/```PeekAsync``` methods exist on all receiver objects: ```MessageReceiver```, ```MessageSession```, ```QueueClient```, and ```SubscriptionClient```.
Peek works on all queues and subscriptions and their respective deadletter queues.

When called repeatedly, the ```Peek``` method enumerates all messages that exist in the queue's or subscription's log, in sequence number order, from the 
lowest available sequence number to the highest. This is the order in which messges were enqueued, it is not the order in which messages might 
eventually be retrieved. 

> The ```SequenceNumber``` property that is set on each brokered message as it is accepted into the message log, 
> is a monotonically increasing and gapless sequence number. The sequence number is authoritiative for determining order of arrival. 
> For partitioned entities, the lower 48 bits hold the per-partition sequence number, the upper 16 bits hold the partition number.

You can also seed an overload of the method with a sequence number to start at, and then call the parameterless method overload to enumerate further. 
```PeekBatch``` functions equivalently, but retrieves a set of messages at once.    

Peek returns *all* messages that exist in the queue's or subscription's message log, not only those available for immediate acquisition with 
```Receive()```. The ```State``` property of each message tells you whether the message is *Active* (available to be received), *Deferred* (see [Deferral](../Deferral)) 
or *Scheduled* (see [ScheduledMessages](.../ScheduledMessages)).   

Garbage collection work on a log occurs asynchronously and not necessarily exactly when messages expire, and therefore ```Peek``` may also 
return messages that have already expired and will be removed or deadlettered when receive is next invoked on the queue or 
subscription (by anyone). This is especially important to keep in mind when attempting to recover deferred messages from the queue.
 A message for which the ```ExpiresAtUtc``` instant has passed can no longer be retrieved or operated on, even when it is being returned by 
 ```Peek```. Returning these messages is deliberate as Peek is a diagnostics tool reflecting the current state of the log.              

Peek will also return messages that have been locked and are being processed by other receivers, but have not yet been completed. Whether a 
message is indeed locked cannot be observed on peeked messages, and the ```LockedUntilUtc``` and ```LockToken``` properties will throw an 
```InvalidOperationException``` when the application attempts to read them.

## Prerequisites and Setup

All samples share the same basic setup, explained in the main [README](../README.md) file. There are no extra setup steps for this sample.
The application entry points are in [Main.cs](../common/Main.md), which is shared across all samples. The sample implementations generally
reside in *Program.cs*, starting with *Run()*.

You can build the sample from the command line with the [build.bat](build.bat) or [build.ps1](build.ps1) scripts. This assumes that you
have the .NET Build tools in the path. You can also open up the [MessageBrowse.sln](MessageBrowse.sln) solution file with Visual Studio and build.
With either option, the NuGet package manager should download and install the **WindowsAzure.ServiceBus** package containing the
Microsoft.ServiceBus.dll assembly, including dependencies.

## Sample Code

The sample is a variation of the [ReceiveLoop](../ReceiveLoop) sample and we will assume that you worked through that sample and already 
understand the structure and most API elements.

What's new in this sample is that we obviously don't ```Receive``` but ```Peek```. When you run the sample repeatedly, you will see that messages
accumulate in the log as we don't receive and remove them. You will also observe that expired messages (we send with a 2 minute 
time-to-live setting) may hang around past their expiration time.

The method ```PeekMessagesAsync``` implements the browse loop that iterates once through the log:

```C#
   async Task PeekMessagesAsync(string namespaceAddress, string queueName, string receiveToken)
    {
        var receiverFactory = MessagingFactory.Create(
            namespaceAddress,
            new MessagingFactorySettings
            {
                TransportType = TransportType.NetMessaging, // Peek doesn't yet work on AMQP
                TokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(receiveToken)
            });
        
        var receiver = await receiverFactory.CreateMessageReceiverAsync(queueName, ReceiveMode.PeekLock);
        Console.WriteLine("Browsing messages from Queue...");
        while (true)
        {
            try
            {
```

The ```Peek``` operation behaves exactly like ```Receive``` in that it returns ```null``` when no message is available and/or the end of the log
has been reached.  

```C#
                //peek messages from Queue
                var message = await receiver.PeekAsync();
                if (message != null)
                {
                    var body = new StreamReader(message.GetBody<Stream>(), true).ReadToEnd();
                    lock (Console.Out)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
```

The one extra property we're writing out to the console in this sample is the ```State``` property, reflecting whether the message is 
active, deferred, or scheduled. 

```C#                        
                        Console.WriteLine(
                            "\t\t\t\tMessage peeked: \n\t\t\t\t\t\tMessageId = {0}, \n\t\t\t\t\t\tSequenceNumber = {1}, \n\t\t\t\t\t\tEnqueuedTimeUtc = {2}," +
                            "\n\t\t\t\t\t\tExpiresAtUtc = {5}, \n\t\t\t\t\t\tContentType = \"{3}\", \n\t\t\t\t\t\tSize = {4}, \n\t\t\t\t\t\tState = {6}, "+
                            "\n\t\t\t\t\t\tContent: [ {7} ]",
                            message.MessageId,
                            message.SequenceNumber,
                            message.EnqueuedTimeUtc,
                            message.ContentType,
                            message.Size,
                            message.ExpiresAtUtc,
                            message.State, 
                            body);
                        Console.ResetColor();
                    }
                }
                else
                {
                    //no more messages in the queue
                    break;
                }
            }
            catch (MessagingException e)
            {
                if (!e.IsTransient)
                {
                    Console.WriteLine(e.Message);
                    throw;
                }
            }
        }
        await receiver.CloseAsync();
        await receiverFactory.CloseAsync();
    }
```

## Using PeekBatch

```PeekBatch``` gets multiple messages and returns them as an enumeration. If no messages 
are available, the enumeration object is empty, not ```null```. A ```PeekBatch``` variation of the above loop will therefore 
keep track if any messages have been returned, at all, and terminate based on that observation. 

The count of ```20``` we pass into ```PeekBatchAsync``` for how many messages we'd like to obtain is an upper bound. The service 
may return any number of messages, up to 20 in this case, but will return at least one message if messages are 
available past the latest read sequence number.  **At most 256 kByte** of cumulative message size will be returned in 
one batch call.     

```C#
        while (true)
        {
            try
            {
                //peek messages from Queue
                int messagesRead = 0;
                var messages = await receiver.PeekBatchAsync(20);
                foreach (var message in messages)
                {
                    messagesRead++;
                    
                    ... output ...
                }
                if (messagesRead == 0)
                    break;

            }
            catch (MessagingException e)
            {
                if (!e.IsTransient)
                {
                    Console.WriteLine(e.Message);
                    throw;
                }
            }
        }
```
 

