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

[Read more about duplicate detection in the documentation.](https://docs.microsoft.com/azure/service-bus-messaging/duplicate-detection)

## Prerequisites and Setup

Refer to the main [README](../README.md) document for setup instructions. All samples share and require the same setup
before they can be run.

## Sample Code 

The sample is documented inline in the [Program.cs](Program.cs) C# file.

The sample really just sends two messages that have the same MessageId set:  

``` C#
    // Send messages to queue
    Console.WriteLine("\tSending messages to {0} ...", queueName);
    var message = new Message
    {
        MessageId = "ABC123",
        TimeToLive = TimeSpan.FromMinutes(1)
    };
    await sender.SendAsync(message);
    Console.WriteLine("\t=> Sent a message with messageId {0}", message.MessageId);

    var message2 = new Message
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
        await receivedreceiveClient.CompleteAsync(message.SystemProperties.LockToken);
        if (receivedMessageId.Equals(receivedMessage.MessageId, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\t\tRECEIVED a DUPLICATE MESSAGE");
        }

        receivedMessageId = receivedMessage.MessageId;
    }
``` 

When you execute the sample you will find that the second message is not being received. As expected.