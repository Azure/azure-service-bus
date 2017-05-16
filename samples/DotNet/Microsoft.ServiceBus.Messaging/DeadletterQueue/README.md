# Dead-Letter Queues

This sample shows how to move messages to the Dead-letter queue, how to retrieve messages from it, and resubmit corrected message back into the main queue.

## What is a Dead-Letter Queue?

All Service Bus Queues and Subscriptions have a secondary sub-queue, called the *Dead-Letter Queue*. This sub-queue does not need to be explicitly 
created and cannot be deleted or otherwise managed independent of the main entity.

The purpose of the Dead-Letter Queue (DLQ) is accept and hold messages that cannot be delivered to any receiver or messages that could not be processed.
Messages can then be taken out of the DLQ and inspected. An application might, potentially with help of an operator, correct issues and 
resubmit the message, log the fact that there was an error, or take corrective action. (The latter is shown in the [AtomicTransactions](../AtomicTransactions) 
sample where DLQs are used to initiate compensating work of a Saga)

From an API and protocol perspective, the DLQ is mostly like any other queue, except that messages can only be submitted via the 
dead-letter-gesture of the parent entity, that time-to-live is not observed, and that you can't dead-letter from a DLQ. The dead-letter
queue fully supports peek-lock delivery and transactional operations.

**Important:** There is no automatic cleanup of the DLQ. Messages remain in the DLQ until they are   

## How do messages get into the DLQ?

There are several activities in Service Bus that cause messages to get pushed to the DLQ from within the messaging engine itself. The
application can also push messages to the DLQ explicitly. 

As the message gets moved by the broker, two properties are added to the message as the broker calls its internal version of the 
```void DeadLetter( string deadLetterReason, string deadLetterErrorDescription)``` method on the message.  

| Property Name              | Description                                               |
|----------------------------|-----------------------------------------------------------|
| DeadLetterReason           | System-defined or application-defined text code declaring |
|                            | why the message has been dead-lettered. System-defined    |
|                            | codes are:                                                | 
|                            | * MaxDeliveryCountExceeded - max delivery count reached   |
|                            | * TTLExpiredException - time-to-live expired              |
| DeadLetterErrorDescription | Human readable description of the reason code             |

Applications can define their own codes for the ```DeadLetterReason``` property.

### Exceeding MaxDeliveryCount 

Queues and subscriptions have a ```QueueDescription.MaxDeliveryCount```/```SubscriptionDescription.MaxDeliveryCount``` setting; the default value is 10. 
Whenever a message has been delivered under a lock (ReceiveMode.PeekLock), but has been either explicitly abandoned or the lock has expired, the message's
```BrokeredMessage.DeliveryCount``` is incremented. When the DeliveryCount exceeds the ```MaxDeliveryCount```, the message gets moved to the DLQ 
specifying the ``MaxDeliveryCountExceeded``` reason code.

This behavior cannot be turned off, but the ```MaxDeliveryCount``` can set to a very large number. 

### Exceeding TimeToLive

When the ```QueueDescription.EnableDeadLetteringOnMessageExpiration```/```SubscriptionDescription.EnableDeadLetteringOnMessageExpiration``` property is
set to *true* (the default is *false*), all expiring messages are moved to the DLQ, specifying the ``TTLExpiredException``` reason code.

Mind that expired messages are only purged and therefore moved to the DLQ when there is at least one active receiver pulling on the 
main Queue or Subscription; that behavior is by design.

### Errors while processing Subscription rules 

When ```SubscriptionDescriptionEnableDeadLetteringOnFilterEvaluationExceptions```is turned on for a subscription, any errors that occur while a
subscription's SQL filter rule executes are being captured in the DLQ along with the offending message.

### Application-Level Dead-Lettering

In addition to these system-provided dead-lettering features, applications can use the DLQ explicitly to reject unacceptable messages. 
This may include messages that cannot be properly processed due to any sort of system issue, messages that hold malformed payloads, or messages that fail 
authentication when some message-level security scheme is used.

## The Sample

The sample illustrates system-initiated dead-lettering after exceeding the default ```QueueDescription.MaxDeliveryCount``` of 10 and
an application-initiated dead-lettering action.

The sample is based on the [SendersReceiversWithQueues](../SendersReceiversWithQueues) baseline sample where the basic elements are explained; 
the shown API gestures also work with Topic subscriptions.

### MaxDelivery Count Scenario 

For the delivery count scenario, we first send a single message, and then pick it up and abandon it until is "disappears" from the queue. 
Then we fetch the message from the DLQ and inspect it. That's all done in ```ExceedMaxDelivery()```   


``` C#

    // MaxDeliveryCount scenario
    await this.SendMessagesAsync(sender, 1);
    await this.ExceedMaxDelivery(receiverFactory, queueName);

    async Task ExceedMaxDelivery(MessagingFactory receiverFactory, string queueName)
    {
```

In the receive loop on the main queue, we pick up the message with ```Receive(TimeSpan.Zero)```, which asks the 
broker to instantly return any message readily available or return with no result. If we get a message, 
we immediately abandon it, which increments the ```DeliveryCount```. Once the system moves the message to 
the DLQ, the main queue is empty and the loop exists as Receive returns *null*.    

```C#
        var receiver = await receiverFactory.CreateMessageReceiverAsync(queueName, ReceiveMode.PeekLock);
        while(true)
        {
            var msg = await receiver.ReceiveAsync(TimeSpan.Zero);
            if (msg != null)
            {
                Console.WriteLine("Picked up message; DeliveryCount {0}", msg.DeliveryCount);
                await msg.AbandonAsync();
            }
            else
            {
                break;
            }
        }
```

For picking up the message from a DLQ, we make a receiver just like for a regular queue. The required path 
can be constructed with the ```QueueClient.FormatDeadLetterPath()``` helper method, and always follows the 
pattern ```{entity}/$DeadLetterQueue```, meaning that for a Queue "Q1", the path is ```Q1/$DeadLetterQueue``` and
for a topic "T1" and subscription "S1", the path is ```T1/Subscriptions/S1/$DeadLetterQueue```. To construct the 
DLQ path for a subscription, you can also use ```SuubscriptionClient.FormatDeadLetterPath()```.  

```C#        

        var dead-letterReceiver = await receiverFactory.CreateMessageReceiverAsync(
                QueueClient.FormatDeadLetterPath(queueName), ReceiveMode.PeekLock);
        while (true)
        {
            var msg = await dead-letterReceiver.ReceiveAsync(TimeSpan.Zero);
            if (msg != null)
            {
                foreach (var prop in msg.Properties)
                {
                    Console.WriteLine("{0}={1}", prop.Key, prop.Value);
                }
                await msg.CompleteAsync();
            }
            else
            {
                break;
            }
        }
    }
```

         

    



    


 
    
 