# Durable, Transactional Senders with MSMQ

This sample demonstrates how a Windows application that is temporarily
disconnected from the network can continue to send messages to Service Bus.
 
The durable message sender library relies on the local Microsoft Message Queue
(MSMQ) built into Windows, and stores all messages in a local queue until
connectivity is restored. This sample therefore only works on Windows. 

> Be aware that the library does create, but not delete local queues as it is
assumed that a local application will want to continue forwarding already sent
messages as it resumes or restarts 

The library also allows the application to send messages as part of a
distributed DTC transaction via MSMQ into Service Bus.

Refer to the main [README](../README.md) document for setup instructions.

## MSMQ? What is MSMQ? How do I get it?

The Microsoft Message Queue (MSMQ) is a robust, local message queueing
middleware that is built into nearly every version of Windows, including the
consumer versions of Windows 7, 8, 8.1, and 10. 

MSMQ can be installed following the [Install Message Queueing][1] guidance. The
Windows 7 instructions work equivalently for newer versions of Windows. For this
sample and library to function, only the "Microsoft Message Queue (MSMQ) Server
Core" features are required.   

From Windows 8 and Windows Server 2012 onwards, you can also just open up an
elevated Powershell window and use ```Enable-WindowsOptionalFeature -Online
-FeatureName MSMQ-Server -All``` to enable MSMQ.     

## Sample Code 

To allow an application to send messages to a Service Bus Queue or Topic in the
absence of network connectivity, the sent messages need to be stored locally,and
be transmitted to Service Bus in the background after connectivity has been
restored. 

This functionality is implemented by the durable message sender library. The
application calls the ```DurableMessageSender.Send()``` operation of the library
exactly as it would call the methods of the Service Bus client library. The
required MSMQ queues are created and managed by the library.  

Messages are only transferred while the ```DurableMessageSender``` instance is
held by the client application. 


```C#
// Create a MessagingFactory. 
MessagingFactory messagingFactory = MessagingFactory.Create(namespaceUri, tokenProvider); 
 
// Create a durable sender. 
DurableMessageSender durableMessageSender = new DurableMessageSender(messagingFactory, queueName); 
 
// Send message. 
Message msg = new Message("This is a message."); 
 
durableMessageSender.Send(msg);
```
 
The ```DurableMessageSender``` enqueues all of the application’s messages into a local, transactional MSMQ queue. In the 
background, the durable message sender library reads these messages from the MSMQ queue and sends them to the Service Bus 
Queue or Topic. ```DurableMessageSender``` maintains one MSMQ queue per Service Bus Queue or Topic that the application 
wants to send to.

```C#
public void Send(Message sbusMessage) 
{ 
    Message msmqMessage = MsmqHelper.PackSbusMessageIntoMsmqMessage(sbusMessage); 
    SendtoMsmq(this.msmqQueue, msmqMessage); 
} 
 
void SendtoMsmq(MessageQueue msmqQueue, Message msmqMessage) 
{ 
    if (Transaction.Current == null) 
    { 
        msmqQueue.Send(msmqMessage, MessageQueueTransactionType.Single); 
    } 
    else 
    { 
        msmqQueue.Send(msmqMessage, MessageQueueTransactionType.Automatic); 
    } 
} 
``` 
 

If the ```DurableMessageSender``` experiences a temporary failure when sending a message to Service Bus, it waits some time and 
then tries again. The wait time increases exponentially with every failure. The maximum wait time is 60 seconds. After a successful 
transmission to Service Bus, the wait time is reset to its initial value of 50ms. If the ```DurableMessageSender```  experiences 
a permanent failure when sending a message to Service Bus, the message is moved to a MSMQ dead-letter queue.

```C#
Message msmqMessage = null; 
try 
{ 
    msmqMessage = this.msmqQueue.EndPeek(result); 
} 
catch (MessageQueueException ex) 
{ 
    if (ex.MessageQueueErrorCode == MessageQueueErrorCode.IOTimeout) 
    { 
        MsmqPeekBegin(); 
        return; 
    } 
} 
 
if (msmqMessage != null) 
{ 
    Message sbusMessage = MsmqHelper.UnpackSbusMessageFromMsmqMessage(msmqMessage); 
    // Clone Service Bus message in case we need to deadletter it. 
    Message sbusDeadletterMessage = CloneMessage(sbusMessage); 
 
    switch (SendMessageToServiceBus(sbusMessage)) 
    { 
        case SendResult.Success: // Message was successfully sent to Service Bus. Remove MSMQ message from MSMQ queue. 
            this.msmqQueue.BeginReceive(TimeSpan.FromSeconds(60), null, MsmqOnReceiveComplete); 
            break; 
        case SendResult.WaitAndRetry: // Service Bus is temporarily unavailable. Wait. 
            waitAfterErrorTimer = new Timer(ResumeSendingMessagesToServiceBus, null, timerWaitTimeInMilliseconds, Timeout.Infinite); 
            break; 
        case SendResult.PermanentFailure: // Permanent error. Deadletter MSMQ message. 
            DeadletterMessage(this.clonedMessage); 
            this.msmqQueue.BeginReceive(TimeSpan.FromSeconds(60), null, MsmqOnReceiveComplete); 
            break; 
        } 
    } 
}
``` 
  

Unlike sending messages directly to Service Bus, sending messages to a transactional MSMQ queue can be done as part of a distributed 
transaction. ```DurableMessageSender``` therefore allows an application to send messages to a Service Bus Queue or Topic as 
part of a regular or a distributed transaction.

The library maintains message ordering. This means that ```DurableMessageSender``` sends messages to Service Bus in the same 
order in which the application submitted the messages.

In order to avoid message duplication, the destination Service Bus Queue or Topic **must** have duplicate detection enabled, 
meaning the ```QueueDescription.RequiresDuplicateDetection``` property must be set to ```true```.

The ```DurableMessageSender``` does not honor transactional guarantees of message batches. If the application sends multiple 
messages within a single transaction, and then Service Bus returns a permanent error as the messages are sent to Service Bus, 
the message will not be enqueued into the Service Bus Queue or Topic whereas other messages might.

Also note that messages do not expire while they are stored in the MSMQ queue. This implementation of ```DurableMessageSender``` 
sets the ```TimeToBeReceived``` property of the MSMQ message to *infinite*. The ```Message.TimeToLive``` value 
becomes effective when the message is submitted into the Service Bus Queue or Topic.  

## Source Code Files
* *Client.cs*: Implements a Service Bus client that sends and receives messages to Service Bus using the durable sender library.
* *DurableMessageSender.cs*: Implements the durable message sender API. Implements code that converts Service Bus brokered messages 
  to and from MSMQ messages and sends and receives MSMQ messages.
* *MsmqHelper.cs*: Implements queue management and message conversion methods.

The sample is documented inline in the [Program.cs](Program.cs) C# file.



[1]: https://technet.microsoft.com/library/cc730960.aspx