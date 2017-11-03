# Sessions

This sample illustrates the Session handling feature of Azure Service Bus. 

Service Bus sessions are unbounded sequences of related messages that allwo for ordered delivery and multiplexing.

[Read more about sessions in the documentation](https://docs.microsoft.com/azure/service-bus-messaging/message-sessions)

## Prerequisites and Setup

Refer to the main [README](../README.md) document for setup instructions. All samples share and require the same setup
before they can be run.

## Sample Code 

The sample is documented inline in the [Program.cs](Program.cs) C# file.

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
async Task SendMessagesAsync(string sessionId, string connectionString, string queueName, string sendToken)
{
    var senderFactory = MessagingFactory.Create(
        connectionString,
        new MessagingFactorySettings
        {
            TransportType = TransportType.Amqp,
            TokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(sendToken)
        });

    var sender = new MessageSender(connectionString,queueName);
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
        var message = new Message(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data[i])))
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
        this.SendMessagesAsync(Guid.NewGuid().ToString(), connectionString, queueName, sendToken),
        this.SendMessagesAsync(Guid.NewGuid().ToString(), connectionString, queueName, sendToken),
        this.SendMessagesAsync(Guid.NewGuid().ToString(), connectionString, queueName, sendToken),
        this.SendMessagesAsync(Guid.NewGuid().ToString(), connectionString, queueName, sendToken));

``` 

### Session Handling

The session handler implements the ```IMessageSessionAsyncHandler``` interface that we previously 
discussed. The implementation is on a class named ```SessionHandler``` that's nested inside the 
```Program``` class. 

The ```RegisterMessageHandler``` method is invoked with the ```MessageSession``` instance and the message to
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
    public async Task RegisterMessageHandler(MessageSession session, Message message)
    {
        if (message.Label != null &&
            message.ContentType != null &&
            message.Label.Equals("RecipeStep", StringComparison.InvariantCultureIgnoreCase) &&
            message.ContentType.Equals("application/json", StringComparison.InvariantCultureIgnoreCase))
        {
            var body = message.Body;

            dynamic recipeStep = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(body));
            .... print to console ...
            await receiveClient.CompleteAsync(message.SystemProperties.LockToken);

            if (recipeStep.step == 5)
            {
                // end of the session!
                await session.CloseAsync();
            }
        }
        else
        {
            await receiver.DeadLetterAsync(message.SystemProperties.LockToken, "BadMessage", "Unexpected message");
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
