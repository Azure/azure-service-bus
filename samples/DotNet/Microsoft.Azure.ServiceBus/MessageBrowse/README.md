# Message Browsing (Peek)

This sample shows how to enumerate messages residing in a Queue or Topic subscription. This feature is typically used for diagnostic and troubleshooting 
purposes and/or for tooling built on top of Service Bus. 

[Read more about message browsing in the documentation.](https://docs.microsoft.com/azure/service-bus-messaging/message-browsing)

## Prerequisites and Setup

Refer to the main [README](../README.md) document for setup instructions. All samples share and require the same setup
before they can be run.

## Sample Code 

The sample is documented inline in the [Program.cs](Program.cs) C# file.

When you run the sample repeatedly, you will see that messages accumulate in the log as we don't receive and remove them. 
You will also observe that expired messages (we send with a 2 minute time-to-live setting) may hang around past their expiration time.

The method ```PeekMessagesAsync``` implements the browse loop that iterates once through the log:

```C#
   async Task PeekMessagesAsync(string connectionString, string queueName)
    {
        var receiverFactory = MessagingFactory.Create(
            connectionString,
            new MessagingFactorySettings
            {
                TransportType = TransportType.NetMessaging, // Peek doesn't yet work on AMQP
                TokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(receiveToken)
            });
        
        var receiver = new MessageReceiver(connectionString,queueName, ReceiveMode.PeekLock);
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
                    var body = Encoding.UTF8.GetString(message.Body);
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
                            message.SystemProperties.SequenceNumber,
                            message.SystemProperties.EnqueuedTimeUtc,
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
            catch (ServiceBusException e)
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
            catch (ServiceBusException e)
            {
                if (!e.IsTransient)
                {
                    Console.WriteLine(e.Message);
                    throw;
                }
            }
        }
```
 

