# Getting Started with Service Bus Queues

This sample shows the essential API elements for interacting with messages and a Service Bus Queue.

You will learn how to establish a connection, and to send and receive messages, and you will learn about the most important 
properties of Service Bus messages.

## Prerequisites and Setup

All samples share the same basic setup, explained in the main [README](../README.md) file. There are no extra setup steps for this sample.
The application entry points are in [Main.cs](../common/Main.md), which is shared across all samples. The sample implementations generally
reside in *Program.cs*, starting with ```Run()```.

You can build the sample from the command line with the [build.bat](build.bat) or [build.ps1](build.ps1) scripts. This assumes that you
have the .NET Build tools in the path. You can also open up the [QueuesGettingStarted.sln](QueuesGettingStarted.sln) solution file with Visual Studio and build.
With either option, the NuGet package manager should download and install the **WindowsAzure.ServiceBus** package containing the
Microsoft.ServiceBus.dll assembly, including dependencies.

## The Program

To keep things reasonably simple, the sample program keeps message sender and message receiver code within a single hosting application,
even though these roles are often spread across applications, services, or at least across independently deployed and run tiers of applications
or services. For clarity, the send and receive activities are kept as separate as if they were different apps and share no API object instances.

### Sending Messages

The simplest way to interact with a Service Bus Queue is to create a ```QueueClient```instance from a Service Bus connection string, which is
what this sample shows. The connection string for a Service Bus namespace can be easily obtained from the portal as illustrated in the 
[main Service Bus documentation](https://azure.microsoft.com/en-us/documentation/articles/service-bus-dotnet-how-to-use-queues/).    

> The connection string contains information about the Service Bus namespace address and commonly also holds the name and key for
> a [Shared Access Signature (SAS) rule](https://azure.microsoft.com/en-us/documentation/articles/service-bus-authentication-and-authorization/).
> Using the rule name and rule key and embedding the key inside an application or its configuration is largely similar to 
> a username/password scheme, except that the SAS key is never directly transferred to the server, but used to create a signed token instead. 
> For simple scenarios that have very few senders and receivers, and where the key, inside the connection string, can be well protected by the 
> app, using connecting strings is the simplest choice. 
> For scenarios with many senders and receivers, or scenarios where the key cannot be well protected, clients will not hold the key, but will 
> be handling a previously issued, time-limited token as you can see in most of the other samples in this repository. 

For the send operation, we create a new ```QueueClient``` and pass the connection string and the queue name. 

```C#
 this.sendClient = QueueClient.CreateFromConnectionString(connectionString, queueName);
```

As you can see, the client object reference is assigned to a field of the class, which is done here intentionally to signal 
that applications **shall hold on to any Service Bus client objects for as long as possible** and preferably for the lifetime 
of the application. 

We **discourage** usage patterns where a QueueClient is created to send a single message and then torn 
down again, just to do this again for the next message. It's very tempting to do this in request handler methods for web sites and 
services since it looks easy, but the recommendation is to anchor a shared object on the web application state or object. The 
```QueueClient``` object can be safely used for sending messages from concurrent asynchronous operations and multiple threads.    

All client objects are automatically initialized with a default "retry policy" and will automatically perform
retries when transient errors occur.  

With the ```QueueClient``` created, we proceed to make a few messages (snippet below is abridged) and send them.

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

All these properties are optional to set.

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
        await sendClient.SendAsync(message);
    }
```

## Receiving Messages

The message receiver side also requires a ```QueueClient``` that we construct just like for the sender. The reason we 
create two identical objects here is that we show in one compact program what would usually happen in two distinct
places. It's uncommon (except in very special and advanced cases discussed in a [different sample](../AtomicTransactions))
for one application module or service to send and receive messages from the same queue, so we keep those paths separate here 
as well.  

```C#
  this.receiveClient = QueueClient.CreateFromConnectionString(connectionString, queueName, ReceiveMode.PeekLock);
```

Note that the ```QueueClient``` is created with the ```PeekLock``` receive mode. This mode will pass the message to the receiver while the broker maintains a lock on
the message and hold on to the message. If the message has not been completed, deferred, deadlettered, or abandoned during the
lock timeout period (all concepts explained in this set of samples), the message will again appear in the Queue (or the Topic Subscription) for retrieval. 

This is different from the ```ReceiveAndDelete``` alternative where the message has already been deleted as it arrives at the receiver. 

Now we start the message receive loop. As explicit receive loops (see the [ReceiveLoop](../ReceiveLoop) sample) can be tricky,
the vast majority of applications will *and should* instead use the ```OnMessage``` or ```OnMessageAsync``` API. 

> **Unless you have good reason to create your own receive loop, you should use the ```OnMessage```/```OnMessageAsync``` API**

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
* The ```AutoRenewTimeout``` option (not shown here) allows adjusting the lock renewal timeout. Messages processed in the OnMessage callback are automatically renewed while the callback remains pending and the application processes the message.
   
The asynchronous variant ```OnMessageAsync``` used here accepts an asynchronous callback method, it is not an asynchronous, awaitable method by itself. 
**You should prefer the asynchronous variant whenever possible**. 

> The callback acts as a throttle for message acquisition. In principle, the receive loop will receive one message, then call 
> the callback with that message for processing, handle completion of the message, and only then receive the next message. With 
> ```MaxConcurrentCalls``` set to a value greater than 1, each concurrent logical thread will follow that pattern. Even though the programming 
> model is *push-like*, the pace at which messages are acquired therefore directly depends on the pace of processing and not on
> the achievable message flow rate. 

The receive loop will be started as soon as ```OnMessageAsync``` is invoked and will continue in the background when the method 
has returned. ```OnMessage```/```OnMessageAsync``` can only be called once on any receiver. The receive loop stops when the ```QueueClient``` is 
closed via ```Close()```/```CloseAsync()```. 

> The callback is not invoked on the calling thread. For applications that need synchronization with an UI thread or similar, like 
> applications written for Windows Forms or WPF, all message handling should be done in the callback and the *result* of the 
> message processing should then be marshaled to the UI thread using the appropriate mechanism.  

Here's the ```OnMessageAsync``` invocation as used in this sample. The callback code is explained below.

``` C#
    receiveClient.OnMessageAsync(
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
  as it is processed by the broker. The sequence number is authoritiative for determining order of arrival. For partitioned
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

Now that we're done with "processing" the message, we tell the broker about that being the case. The ```Complete```/```CompleteAsync```
operation will settle the message transfer with the broker and remove it from the Queue. 


``` C#
    await message.CompleteAsync();
```

## Run()

The ```Run()``` method that is invoked by the common sample entrypoint and is where the client objects get intialized as 
previously discussed. The receive client gets created and is then initialized with the ``OnMessageAsync`` callback, which 
starts a receive loop. The send client gets created and then asynchronously sends a few messages. 

```C#
 public async Task Run(string queueName, string connectionString)
{
    Console.WriteLine("Press any key to exit the scenario");

    this.receiveClient = QueueClient.CreateFromConnectionString(connectionString, queueName, ReceiveMode.PeekLock);
    this.InitializeReceiver();

    this.sendClient = QueueClient.CreateFromConnectionString(connectionString, queueName);
    var sendTask = this.SendMessagesAsync();
```

As the user presses any key, the ```receiveClient``` is closed, which stops the ```OnMessageAsync``` receive loop. If 
sending the messages has not yet completed, we'll wait for the send task to cleanly exit and then close the send client.    


```C#
    Console.ReadKey();

    // shut down the receiver, which will stop the OnMessageAsync loop
    await this.receiveClient.CloseAsync();

    // wait for send work to complete if required
    await sendTask;

    await this.sendClient.CloseAsync();
}
```

## Running the sample

You can run the application from Visual Studio or on the command line from the sample's root directory by starting ```bin/debug/QueuesGettingStarted.exe```
