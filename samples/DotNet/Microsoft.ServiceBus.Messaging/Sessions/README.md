# Sessions

This sample illustrates the Session handling feature of Azure Service Bus. 

## What are Service Bus Sessions?

Service Bus Sessions, also called "Groups" in the AMQP 1.0 protocol, are unbounded sequences of related 
messages. Service Bus is not prescriptive about the nature of the relationship, and also doesn't 
define a particular model for telling where a message sequence starts or ends.

Any sender can "create" a session when submitting messages into a Topic or Queue by setting the 
```BrokeredMessage.SessionId``` property to some application-defined identifier that is unique to 
the session. At the AMQP 1.0 protocol level, this value maps to the ```group-id``` property. 

Sessions come into existence when there is at least one message with the session's ```SessionId``` 
in the Queue or Topic subscription. Once a Session exists, there is no defined moment or gesture 
for when the session expires or disappears.  

Theoretically, a message can be received for a session today, and the next message in a year's time, 
and if the ```SessionId``` matches, the session is the same from the Service Bus perspective.

We say "theoretically", because an application usually has a notion of where a set of related 
messages starts and ends; Service Bus simply doesn't set any specific rules. In this sample we 
show a set of related messages for which there is a clear rule of where the session ends.

The Session *feature* in Service Bus enables a specific kind of receive gesture in form of 
the ```MessageSession```. You turn the feature on by setting ```QueueDescription.RequiresSession``` or
```SubscriptionDescription.RequiresSession``` to *true*. This is required before you attempt to use 
the related API gestures. 

The foundational API gestures for Sessions exist on both the ```QueueClient``` and the 
```SubscriptionClient```. There is an imperative model where you control when sessions and messages 
are received, and a handler-based model, very similar to ```OnMessage``` that hides the 
complexity of managing the receive loop. The handler model is what this sample shows.

## Session Features

The Session feature provides concurrent demultiplexing of interleaved message streams while
preserving and guaranteeing ordered delivery.

To illustrate this, let's look at a picture:

```
Queue
-----------------------
12321423112312133213321
-----------------------
          |
Streams   V                              +--- MessageSession.Receive() -> 1 1 1 1
-----------------------                  |
1   1   11  1 1   1   1  > SessionId=1 --+  
 2 2  2   2  2   2   2   > SessionId=2 ------ MessageSession.Receive() -> 2 2 2 2
  3    3   3   33  33    > SessionId=3 --+
     4                   > SessionId=4   |
-----------------------                  +--- MessageSession.Receive() -> 3 3 3 3
```

A ```MessageSession``` receiver is created by the client accepting a session.
Imperatively, the client calls ```QueueClient.AcceptMessageSession```/
```QueueClient.AcceptMessageSessionAsync```, in the callback model is registers a session handler 
as we'll show below.

When the ```MessageSession``` is accepted and while it is held by a client, that client holds an 
exclusive lock on *all* messages with that session's  ```SessionId``` that exist in the Queue or 
Subscription, and also on all messages that will arrive with that  ```SessionId ``` while the session is held.

The lock is released when ```MessageSession.Close```/```MessageSession.CloseAsync``` is called, or
when the lock expires in case the application is unable to do so. The session lock should be 
treated like an exclusive lock on a file, meaning that the application should make an effort 
to close the session as soon as it no longer needs it.  

When multiple concurrent receivers pull from the queue, the messages belonging to a particular 
session are dispatched to the specific receiver that currently holds the lock for that session.
With that, an interleaved message stream residing in one Queue or Subscription gets cleanly 
de-multiplexed to different receivers and those receivers can also sit on different client machines,
since the lock management happens service-side, inside Service Bus.

The Queue is, however, still a Queue: There is no random access. 

The illustration above shows three concurrent ```MessageSession``` receivers, which of all 
*must* actively take messages off the Queue for every receiver to make progress. 
The Session with ```SessionId=4``` above has no active, owning client, which means that no messages 
will be delivered to anyone, until that message has been taken by a newly created, owning session receiver.  

While that might appear very constraining, a single receiver process can handle very many 
concurrent sessions easily, especially when they are written with strictly asynchronous code; 
juggling several dozen concurrent sessions effectively automatic with the callback model.

The strategy for handling very many concurrent sessions, whereby each session only sporadically 
receives messages is for the handler to drop the session after some idle time and pick up
processing again when the session is accepted as the next session arrives.

The session lock held by the session receiver is an umbrella for the message locks used by the
```ReceiveMode.PeekLock``` mode. A receiver cannot have two messages concurrently "in flight",
but the messages *must* be processed in order. A new message can only be obtained when the prior
message has been completed or dead-lettered. Abandoning a message will cause the same message to 
be served up again with the next receive operation.  

### IMessageSession(Async)Handler

The handler-based model for processing message sessions is similar to the ```OnMessage``` callback-model
introduced in the [QueuesGettingStarted](../QueuesGettingStarted) sample, where the complexity of
handling the receive loop is left to the Service Bus API. For sessions, the receiver client must supply
an implementation of the ```IMessageSessionHandler``` or ```IMessageSessionAsyncHandler``` interfaces.

Both interfaces define semantically identical methods, once in synchronous and one in asynchronous variants:

* ```IMessageSessionHandler.OnMessage```/``IMessageSessionAsyncHandler.OnMessageAsync``` - this method is
  called when a message is available for processing, equivalent to the ```OnMessage``` callback on 
  regular receivers. The method is called with the ```MessageSession``` instance as an extra argument, 
  which allows the handler method to control the session lifetime.
* ```IMessageSessionHandler.OnCloseSession```/``IMessageSessionAsyncHandler.OnCloseSessionAsync``` - this 
  method is invoked when the session is closed by the Service Bus client and allows the application to 
  handle any shutdown work it may need to perform. It is not called when the ```OnMessage```/```OnMessageAsync``` 
  explicitly chooses to close the session.
* ```IMessageSessionHandler.OnSessionLost```/``IMessageSessionAsyncHandler.OnSessionLostAsync``` - this 
  method is invoked when the session lock is lost. The lock can be lost due to the connection to Service Bus
  having been dropped, or some other condition inside the broker that causes the lock to be abandoned. The 
  method is given the exception that has been raised (which will contain a "tracking-id" for Azure product 
  support if the session has been dropped by the service) and allows the client to perform any required
  shutdown work.
  
 The session handler is generally registered with its type via the ```QueueClient.RegisterSessionHandler``` or 
 ```SubscriptionClient.RegisterSessionHandler```, and the Service Bus client will create a 
fresh instance for every new session that it accepts.
  
 ### IMessageSessionHandler(Async)Factory
        
If an application wishes to use a singleton object or control how instances are created, for which we
have no need in this sample, it can alternatively register a factory object.

Such a factory object implements ```IMessageSessionHandlerFactory```/```IMessageSessionHandlerAsyncFactory```,
with ```CreateInstance``` and ```DisposeInstance``` methods.

## Prerequisites and Setup

All samples share the same basic setup, explained in the main [README](../README.md) file. There are no extra setup steps for this sample.
The application entry points are in [Main.cs](../common/Main.md), which is shared across all samples. The sample implementations generally
reside in *Program.cs*, starting with ```Run()```.

You can build the sample from the command line with the [build.bat](build.bat) or [build.ps1](build.ps1) scripts. This assumes that you
have the .NET Build tools in the path. You can also open up the [Sessions.sln](Sessions.sln) solution file with Visual Studio and build.
With either option, the NuGet package manager should download and install the **WindowsAzure.ServiceBus** package containing the
Microsoft.ServiceBus.dll assembly, including dependencies.

## The Program

The send-side of the program is derived from the [Deferral](../Deferral) sample, which shows 
how to reorder an sequence of messages that arrives out-of-order for any reason with the ```Defer()``` 
operation. This sample shows how to prevent out-of-order delivery altogether using sessions, given that the 
messages originate from the same sender, and how to de-multiplex interleaved message streams. 

### Sending

What we do in this sample is to initiate an interleaved send of four independent message sequences, 
each labeled with a unique ```SessionId```. The interleaved send causes the messages from those
four sequences to show up interleaved representing the actual send order, very similar
to the illustration above.

The ```SendMessagesAsync``` method is passed the ```sessionId``` to be used for sending 
a sequence of messages, along with address information and a token for sending the messages.
That information is used to create the messanger sender, which is not at all different from
any other sender. Sending into sessionful Queues or sessionful Topic subscriptions is done
with the regular clients. The sole difference is setting the ```SessionId``` on the outbound
message.      

``` C#
async Task SendMessagesAsync(string sessionId, string namespaceAddress, string queueName, string sendToken)
{
    var senderFactory = MessagingFactory.Create(
        namespaceAddress,
        new MessagingFactorySettings
        {
            TransportType = TransportType.Amqp,
            TokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(sendToken)
        });

    var sender = await senderFactory.CreateMessageSenderAsync(queueName);
```

We send an ordered set of "jobs", and set the ```SessionId``` to the given parameter for 
each message we send.  

``` C#
    dynamic data = new[]
    {
        new {step = 1, title = "Shop"},
        new {step = 2, title = "Unpack"},
        new {step = 3, title = "Prepare"},
        new {step = 4, title = "Cook"},
        new {step = 5, title = "Eat"},
    };

    for (int i = 0; i < data.Length; i++)
    {
        var message = new BrokeredMessage(new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data[i]))))
        {
            SessionId = sessionId,
            ContentType = "application/json",
            Label = "RecipeStep",
            MessageId = i.ToString(),
            TimeToLive = TimeSpan.FromMinutes(2)
        };
        await sender.SendAsync(message);
    }
}
```

Inside the ```Run()``` method, we asynchronously invoke the ```SendMessagesAsync``` 
four times, which will cause the messages to be sent simultaneously and over separate connections 
since we're using separate ```MessagingFactory``` instances. Each session's identifier is
a fresh GUID.    

``` C#
  await Task.WhenAll(
        this.SendMessagesAsync(Guid.NewGuid().ToString(), namespaceAddress, queueName, sendToken),
        this.SendMessagesAsync(Guid.NewGuid().ToString(), namespaceAddress, queueName, sendToken),
        this.SendMessagesAsync(Guid.NewGuid().ToString(), namespaceAddress, queueName, sendToken),
        this.SendMessagesAsync(Guid.NewGuid().ToString(), namespaceAddress, queueName, sendToken));

``` 

### Session Handling

The session handler implements the ```IMessageSessionAsyncHandler``` interface that we previously 
discussed. The implementation is on a class named ```SessionHandler``` that's nested inside the 
```Program``` class. 

The ```OnMessageAsync``` method is invoked with the ```MessageSession``` instance and the message to
process. 

The message "processing" is very similar to most other samples; we inspect the message for
whether it meets our expectations and then proceed to handle the payload. When we're done processing, 
we call ```CompleteAsync``` on the message. if the message doesn't meet out expectations, we 
[dead-letter](../Deadletter) it.    

The only special case is when we reach the end of the expected sequence. When the fifth job step 
arrives, we close the session object, which indicates that the session handler is done with this
session and does not expect any further messages. Once the call returns, the Service Bus API will 
proceed to discard the ```SessionHandler``` instance. 

``` C#
class SessionHandler : IMessageSessionAsyncHandler
{
    public async Task OnMessageAsync(MessageSession session, BrokeredMessage message)
    {
        if (message.Label != null &&
            message.ContentType != null &&
            message.Label.Equals("RecipeStep", StringComparison.InvariantCultureIgnoreCase) &&
            message.ContentType.Equals("application/json", StringComparison.InvariantCultureIgnoreCase))
        {
            var body = message.GetBody<Stream>();

            dynamic recipeStep = JsonConvert.DeserializeObject(new StreamReader(body, true).ReadToEnd());
            .... print to console ...
            await message.CompleteAsync();

            if (recipeStep.step == 5)
            {
                // end of the session!
                await session.CloseAsync();
            }
        }
        else
        {
            await message.DeadletterAsync("BadMessage", "Unexpected message");
        }
    }
```

The ```OnCloseSessionAsync``` and ```OnSessionLostAsync``` methods have no work to do for this sample.

``` C#
            public async Task OnCloseSessionAsync(MessageSession session)
            {
                // nothing to do
            }

            public async Task OnSessionLostAsync(Exception exception)
            {
                // nothing to do
            }
        }        

```
