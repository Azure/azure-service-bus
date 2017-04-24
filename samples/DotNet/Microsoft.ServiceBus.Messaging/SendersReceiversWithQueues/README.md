#Message Senders and Receivers with Service Bus Queues

This sample shows interacting with Service Bus Queues using a set of API gestures that are more abstract but also a bit more
flexible than the ``QueueClient`` class introduced in the basic [QueuesGettingStarted](../QueuesGettingStarted) sample. 

Specifically, this sample introduces the ```MessagingFactory``` and the ```MessageSender``` and ```MessageReceiver``` clients that 
you can create from the factory. The advantage of using these client classes is that they work interchangeably across Queues 
and Topics for sending and across Queues and Subscriptions for receiving, and therefore provide agility with regards to the 
messaging topology. 

If you initially choose a Queue for a communication path, but later decide to switch to a Topic with multiple subscriptions to allow 
further consumers to get the sender's messages, ```MessageSender``` and ```MessageReceiver``` based code can be used with that 
new topology without changes except for configuring different paths.     

You will learn how to establish a connection, and to send and receive messages, and you will learn about the most important 
properties of Service Bus messages.

## Prerequisites and Setup

All samples share the same basic setup, explained in the main [README](../README.md) file. There are no extra setup steps for this sample.
The application entry points are in [Main.cs](../common/Main.md), which is shared across all samples. The sample implementations generally
reside in *Program.cs*, starting with ```Run()```.

You can build the sample from the command line with the [build.bat](build.bat) or [build.ps1](build.ps1) scripts. This assumes that you
have the .NET Build tools in the path. You can also open up the [SendersReceiversWithQueues.sln](SendersReceiversWithQueues.sln) solution file with Visual Studio and build.
With either option, the NuGet package manager should download and install the **WindowsAzure.ServiceBus** package containing the
Microsoft.ServiceBus.dll assembly, including dependencies.

## The Program

To keep things reasonably simple, the sample program keeps message sender and message receiver code within a single hosting application,
even though these roles are often spread across applications, services, or at least across independently deployed and run tiers of applications
or services. For clarity, the send and receive activities are kept as separate as if they were different apps and share no API object instances.

### Sending Messages

Sending messages requires a connection to Service Bus, which is always managed by a *MessagingFactory*. That's also true when you use the ```QueueClient```
as shown in the "getting started" sample, but in that case, a factory is created and managed for you. 

The *MessagingFactory* serves as an anchor for connection management and as a factory for the various client objects that can interact with Service Bus 
entities. Connections to Service Bus are established "just in time" when required, for instance when the first send or receive operation is initiated. 
The connection is shared across all client objects created from the same ```MessagingFactory```, each having a separate link inside that connection. 
When the ```MessagingFactory``` is closed or aborted, all client operations across all client objects are aborted as well.

For the send operation, we create a new ```MessagingFactory``` and pass the namespace base address (typically ```sb://{namespace-name}.servicebus.windows.net```)
and a set of ```MessagingFactorySettings```. The settings object is configured with the transport protocol type (AMQP 1.0) and with a token provider object
that wraps the SAS send token passed to the sample by the [entry point](../common/Main.md).

```C#
async Task SendMessagesAsync(string namespaceAddress, string queueName, string sendToken)
{
    var senderFactory = MessagingFactory.Create(
        namespaceAddress,
        new MessagingFactorySettings
        {
            TransportType = TransportType.Amqp,
            TokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(sendToken)
        });
```

All client objects created through this factory will be initialized with a default "retry policy" and will automatically perform
retries following the rules of the policy when transient errors occur. The policy can be overridden using the ```RetryPolicy``` property: 

``` C#
// this line is not in the code sample. Only set or override when you have good reason to do so
senderFactory.RetryPolicy = new RetryExponential(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(30), 10);
```

From the factory we next create a ```MessageSender```, which can send messages to Queues and Topics. We could also create a ```QueueClient```,
which is the client object specialized for Queues, but the generic message sender is the more flexible option.

```C#
var sender = await senderFactory.CreateMessageSenderAsync(queueName);
```

With the message sender in hands, we proceed to create a few messages (snippet below is abridged) and send them.

In this example we use the Newtonsoft JSON.NET serializer to turn a dynamic object into JSON format, then encode the resulting
text as UTF-8, and pass the resulting byte stream into the body of the message. We then set the message's ```ContentType``` property
to "application/json" to inform the receiver of the message body format.

We could also pass a serializable .NET object (marked as ```[Serializable]``` or ```[DataContract]```) as the message body object to the
```BrokeredMessage``` constructor. When sending with AMQP as we do here, the object would be serialized in AMQP encoding by default.
When sending with the ```NetMessaging``` transport type, the object would be serialized with the binary .NET data contract serializer.
A further overload of the ```BrokeredMessage``` constructor lets you pass an ```XmlObjectSerializer``` of your own choice.

The example also sets

* the ```Label``` property, which gives the receiver a hint about the purpose of the message and allows for
  dispatching to a handler method without first touching the message body.
* the ```MessageId``` property, which uniquely identifies this particular message and enables features like correlation
  and duplicate detection.
* the ```TimeToLive``` property, which causes the message to expire and be automatically garbage collected from the Queue
  when expired. We set this here so that we don't accumulate many stale messages in the demo queue as you experiment.

All properties are optional to set.

```C#
dynamic data = new[]
{
    new {name = "Einstein", firstName = "Albert"},
    ...
    new {name = "Kopernikus", firstName = "Nikolaus"}
};


for (int i = 0; i < data.Length; i++)
{
    var message = new BrokeredMessage(
                    new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data[i]))))
                    {
                        ContentType = "application/json",
                        Label = "Scientist",
                        MessageId = i.ToString(),
                        TimeToLive = TimeSpan.FromMinutes(2)
                    };
```

And once we have composed a message, we send it into the queue. With the default retry policy,
the asynchronous send operation will automatically retry the send operation in case transient errors occur. 

We deliberately don't wrap this operation into a ```try/catch``` block as one would do in a production application; 
if the sample fails sending to the queue after all retries have been exhausted it will terminate with an exception. 

```C#
        await sender.SendAsync(message);
    }
```

## Receiving Messages

The message receiver side also requires a ```MessagingFactory``` that we construct just like for the sender. The only difference,
not even really apparent, is that we pass a token that confers receive ("Listen") permission on the Queue. Everything else,
except names, is the same as on the send side.

```C#
async Task ReceiveMessagesAsync(string namespaceAddress, string queueName, string receiveToken, CancellationToken cancellationToken)
{
    var receiverFactory = 
        MessagingFactory.Create(
            namespaceAddress,
            new MessagingFactorySettings
            {
                TransportType = TransportType.Amqp,
                TokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(receiveToken)
            });
```

As you might expect, we'll now create a receiver object. Like with send, we could use the ```QueueClient```, but we create a generic
```MessageReceiver``` that could also receive from a Topic Subscription when given the correct path. 

Note that the receiver is created with the ```PeekLock``` receive mode. This mode will pass the message to the receiver while the broker maintains a lock on
the message and hold on to the message. If the message has not been completed, deferred, deadlettered, or abandoned during the
lock timeout period (all concepts explained in this set of samples), the message will again appear in the Queue (or the Topic Subscription) for retrieval. 

This is different from the ```ReceiveAndDelete``` alternative where the message has been deleted as it arrives at the receiver. In this example, the
message is either completed or deadlettered as you will see further below.

```C#
var receiver = await receiverFactory.CreateMessageReceiverAsync(queueName, ReceiveMode.PeekLock);
```

Since we want to illustrate an application here that continuously processes messages, we also need a way for the application
to state when to stop. You may have noticed the ```cancellationToken``` that's passed into the hosting function. We will now register
a callback on that token for signaling when the caller wants the message processing to end, and in that we will close the receiver
and also the messaging factory. We also complete a ```TaskCompletionSource``` instance that will signal when this method can exit
after all work has ended.

``` C#
var doneReceiving = new TaskCompletionSource<bool>();
cancellationToken.Register(
    async () =>
    {
        await receiver.CloseAsync();
        await receiverFactory.CloseAsync();
        doneReceiving.SetResult(true);
    });
```

Now we start the message receive loop. As explicit receive loops (see the [ReceiveLoop](../ReceiveLoop) sample) can be tricky,
the vast majority of applications will *and should* instead use the ```OnMessage(Async)``` API. 

> Unless you have good reason to create your own receive loop, you should use the OnMessage API.

The ```OnMessage(Async)``` method accepts a callback function for handling a single message, and a set of options:

* The ```AutoComplete``` flag controls whether the responsibility for handling the processing outcome lies with the OnMessage
  loop host (```true```) or with the callback (```false```). If set to *true*, returning normally from the callback will automatically
  cause ```Complete()``` to be called on the message. Throwing any exception will cause the message to be abendoned with ```Abandon()```.
  If the flag is set to ```false``` as shown here, the callback retains full control over the outcome. Should the callback throw 
  an exception, the exception is absorbed, but the message remains unabandoned and locked until its lock timeout expires. For the 
  ```ReceiveAndDelete``` receive mode, the setting has no effect.
* ```MaxConcurrentCalls``` controls how many concurrent threads are at most being used for invoking the callback method concurrently.
   Concurrent execution may significantly increase the overall throughput, but you must treat the callback as a multi-threaded 
   operation and appropriately guard all access to shared variables and objects.
* The ```AutoRenewTimeout``` option (not shown here) is discussed in the [LockRenewal](../LockRenewal) sample
   
The asynchronous variant ```OnMessageAsync``` used here accepts an asynchronous callback method, it is not an asynchronous, awaitable method by itself. 
You should prefer the asynchronous variant whenever possible. 

> The callback acts as a throttle for message acquisition. In principle, the receive loop will receive one message, then call 
> the callback with that message for processing, handle completion of the message, and only then receive the next message. With 
> ```MaxConcurrentCalls``` set to a value greater than 1, each concurrent logical thread will follow that pattern. Even though the programming 
> model is *push-like*, the pace at which messages are acquired therefore directly depends on the pace of processing and not on
> the achievable message flow rate. 

The receive loop will be started as soon as ```OnMessage(Async)``` is invoked and will continue in the background when the method 
has returned. OnMessage can only be called once on any receiver. The receive loop stops when the receiver is being closed or when
the spawning MessagingFactory is closed.

> The callback is not invoked on the calling thread. For applications that need synchronization with an UI thread or similar, like 
> applications written for Windows Forms or WPF, all message handling should be done in the callback and the *result* of the 
> message processing should then be marshaled to the UI thread using the appropriate mechanism.  

Here's the ```OnMessageAsync``` invocation as used in this sample. The callback code is explained below.

``` C#
    receiver.OnMessageAsync(
        async message =>
        {
            ... the handling code is explained below ...
        },
        new OnMessageOptions {AutoComplete = false, MaxConcurrentCalls = 1});
```

When we have obtained a message, we'll first check whether it is a message that we can handle. For this example, we check
the ```Label``` and ```ContentType``` properties for whether they contain the expected values indicating that we can successfully
decode and process the message body. If they do, we acquire the body stream and deserialize it:

``` C#
    if (message.Label != null &&
        message.ContentType != null &&
        message.Label.Equals("Scientist", StringComparison.InvariantCultureIgnoreCase) &&
        message.ContentType.Equals("application/json", StringComparison.InvariantCultureIgnoreCase))
    {
        var body = message.GetBody<Stream>();

        dynamic scientist = JsonConvert.DeserializeObject(new StreamReader(body, true).ReadToEnd());
```

Instead of processing the message, the sample code writes out the message properties to the console. Of particular interest are
those properties that the broker sets or modifies as the message passes through:

* the ```SequenceNumber``` property is a monotonically increasing and gapless sequence number assigned to each message
  as it is processed by the broker. The sequence number is authoritative for determining order of arrival. For partitioned
  entities, the lower 48 bits hold the per-partition sequence number, the upper 16 bits hold the partition number.
* the ```EnqueuedTimeUtc``` property reflects the time at which the message has been committed by the processing
  broker node. There may be clock skew from UTC and also between different broker nodes. If you need to determine order
  of arrival refer to the ```SequenceNumber```.
* the ```Size``` property holds the size of the message body, in bytes.
* the ```ExpiresAtUtc``` property holds the absolute instant at which this message will expire (```EnqueuedTimeUtc```+```TimeToLive```)

``` C#
    lock (Console.Out)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(
            "\t\t\t\tMessage received: \n\t\t\t\t\t\tMessageId = {0}, \n\t\t\t\t\t\tSequenceNumber = {1}, \n\t\t\t\t\t\tEnqueuedTimeUtc = {2}," +
            "\n\t\t\t\t\t\tExpiresAtUtc = {5}, \n\t\t\t\t\t\tContentType = \"{3}\", \n\t\t\t\t\t\tSize = {4},  \n\t\t\t\t\t\tContent: [ firstName = {6}, name = {7} ]",
            message.MessageId,
            message.SequenceNumber,
            message.EnqueuedTimeUtc,
            message.ContentType,
            message.Size,
            message.ExpiresAtUtc,
            scientist.firstName,
            scientist.name);
        Console.ResetColor();
    }
```

Now that we're done with "processing" the message, we tell the broker about that being the case. The ```Complete(Async)```
operation will settle the message transfer with the broker and remove it from the Queue. 

If the message does not meet our processing criteria, we will deadletter it, meaning it is put into a special queue for 
handling defective messages. The broker will automatically deadletter the message if delivery has been attempted too many times.
You can find out more about this in the [Deadletter](../Deadletter) sample.

``` C#
        await message.CompleteAsync();
    }
    else
    {
        await message.DeadLetterAsync("ProcessingError", "Don't know what to do with this message");
    }
```

The ```ReceiveMessagesAsync``` method closes with an await statement that will wait for completion of the ```TaskCompletionSource```
we initialized above and that will be set (and therefore unblock this wait) once the cancellation token fires and processing stops.

``` C#
  await doneReceiving.Task;
```

## Run()

The Run() method that is invoked by the common sample entry point starts sender and receiver in parallel and waits for 
both to complete before exiting. The cancellation token passed to the receiver method is being triggered when the 
user presses any key sometime after sender and receiver have been kicked off. 

```C#
    public async Task Run(string namespaceAddress, string queueName, string sendToken, string receiveToken)
    {
        Console.WriteLine("Press any key to exit the scenario");

        var cts = new CancellationTokenSource();

        var sendTask = this.SendMessagesAsync(namespaceAddress, queueName, sendToken);
        var receiveTask = this.ReceiveMessagesAsync(namespaceAddress, queueName, receiveToken, cts.Token);

        Console.ReadKey();
        cts.Cancel();

        await Task.WhenAll(sendTask, receiveTask);
    } 
```

##Running the sample

You can run the application from Visual Studio or on the command line from the sample's root directory by starting <code>bin/debug/sample.exe</code>
