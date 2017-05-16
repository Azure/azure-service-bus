# Deferral

This sample illustrates and explains the use of the Deferral feature in Service Bus  

# What is Deferral

When a Queue or Subscription client receives a message that it is willing to process, but for which processing is 
not currently possible, it has the option of "deferring" retrieval of the message to a later point. The 
API gesture is ```BrokeredMessage.Defer```/```BrokeredMessage.DeferAsync```.

Deferred messages remain in the main queue along with all other active messages (unlike [Deadletter](../Deadletter)
messages that sit in a sub-queue), but they can no longer be received using the regular ```Receive```/```ReceiveAsync``` 
functions.   

Instead, the "owner" of a deferred message is responsible for remembering the ```SequenceNumber``` of the deferred 
message and can then, at the appropriate time, receive this message explicitly with ```Receive(sequenceNumber)```.

Deferring messages does not impact message's expiration, meaning that deferred messages can still expire. 

## Why would I use it?
 
Deferral is a feature specifically created for workflow processing scenarios. Workflow frameworks may 
require certain operations to complete in a particular order, and postpone processing of some received
messages until prescribed prior work that is informed by other messages has been completed.

Ultimately, the feature aids in re-ordering messages from the arrival order into an order in which they can be 
processed, while leaving those messages safely in the message store for which processing needs to be postponed.

The [SessionState](../SessionState) concept and sample builds on this sample and shows how to use 
the broker's session state feature to keep track of which messages were deferred in a particular context.

## Prerequisites and Setup

All samples share the same basic setup, explained in the main [README](../README.md) file. There are no extra setup steps for this sample.
The application entry points are in [Main.cs](../common/Main.md), which is shared across all samples. The sample implementations generally
reside in *Program.cs*, starting with *Run()*.

You can build the sample from the command line with the [build.bat](build.bat) or [build.ps1](build.ps1) scripts. This assumes that you
have the .NET Build tools in the path. You can also open up the [Deferral.sln](Deferral.sln) solution file with Visual Studio and build.
With either option, the NuGet package manager should download and install the **WindowsAzure.ServiceBus** package containing the
Microsoft.ServiceBus.dll assembly, including dependencies.

## The Sample

The sample is a variation of the [ReceiveLoop](../ReceiveLoop) sample.   

The send-side of the sample takes an ordered list of workflow steps (Shop, Unpack, Prepare, Cook, Eat) and 
puts them into the queue. The way we're shuffling these into a random order is by adding a random delay of a few 
milliseconds (```Task.Delay(rnd.Next(30)```) before each send operation. Sending is done when all send tasks 
are done.   

```C#

dynamic data = new[]
{
    new {step = 1, title = "Shop"},
    new {step = 2, title = "Unpack"},
    new {step = 3, title = "Prepare"},
    new {step = 4, title = "Cook"},
    new {step = 5, title = "Eat"},
};

var rnd = new Random();
var tasks = new List<Task>();
for (int i = 0; i < data.Length; i++)
{
    var message = new BrokeredMessage(new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data[i]))))
    {
        ContentType = "application/json",
        Label = "RecipeStep",
        MessageId = i.ToString(),
        TimeToLive = TimeSpan.FromMinutes(2)
    };

    tasks.Add(Task.Delay(rnd.Next(30)).ContinueWith(
            async (t) =>
            {
                await sender.SendAsync(message);
            }));
}
await Task.WhenAll(tasks);
``` 

More interesting is the receive-side of the sample. 

The method ```ReceiveMessagesAsync``` has two consecutive loops. Mind that using Deferral in this way and with a 
regular queue is somewhat contrived for the purpose of the sample, but the goal of the sample is to show the feature. The 
[SessionState](../SessionState) sample builds on the foundation we lay here, and puts deferral in the context 
of sessions.

What we're expecting for processing our workflow are instructions for 5 steps (Shop, Unpack, Prepare, Cook, Eat) to 
arrive, and we can only process them in order. We could start preparing the meal before we have shopped for extra 
ingredients we're missing, but the outcome might be disappointing.  

To make sure we're doing things in the prescribed order, we keep track of the last step we executed 
in ```lastProcessedRecipeStep```.

The beginning of the loop should be familiar from the [ReceiveLoop](../ReceiveLoop) sample   

``` C#
    int lastProcessedRecipeStep = 0;
    var deferredSteps = new Dictionary<int, long>();

    while (true)
    {
        try
        {
            //receive messages from Queue
            var message = await receiver.ReceiveAsync(TimeSpan.FromSeconds(5));
            if (message != null)
            {
                if (message.Label != null &&
                    message.ContentType != null &&
                    message.Label.Equals("RecipeStep", StringComparison.InvariantCultureIgnoreCase) &&
                    message.ContentType.Equals("application/json", StringComparison.InvariantCultureIgnoreCase))
                {
                    var body = message.GetBody<Stream>();

                    dynamic recipeStep = JsonConvert.DeserializeObject(new StreamReader(body, true).ReadToEnd());
```

Once we have a recipe step, we check whether it's the step we're expecting to process next. If that is the case, we 
handle it and complete the message.   
                    
``` C#                    
                    if (recipeStep.step == lastProcessedRecipeStep + 1)
                    {
                        ... print the message ... 
                        await message.CompleteAsync();
                        lastProcessedRecipeStep = recipeStep.step;
                    }
                    else
                    {
```

If the message arrives out of the expected order, we first remember the message's ```BrokeredMessage.SequenceNumber``` and then call
```Defer```/```DeferAsync``` on the message. 

This causes the message to be "put on the side" inside the queue. As explained in the [MessageBrowse](../MessageBrowse) sample,
the message remains in the main queue, but the message's ```BrokeredMessage.State``` is set to ```MessageState.Deferred```, which makes the
message ineligible for regular delivery.       

``` C#
                        
                        deferredSteps.Add((int)recipeStep.step, (long)message.SequenceNumber);
                        await message.DeferAsync();
                    }
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
```

Once we're done with all the messages that were put into the queue, which is simply determined by the queue remaining empty for 5 seconds, 
we now take care of the deferred messages and process the remaining steps in order. While we have deferred steps, we look for the next 
one due:    
 
```C# 
    
    while (deferredSteps.Count > 0)
    {
        long step;

        if (deferredSteps.TryGetValue(lastProcessedRecipeStep + 1, out step))
        {
``` 

Once we find the desired message, we ask the Queue to give it to us, using the previously saved ```BrokeredMessage.SequenceNumber```. 
The ```Receive```/```ReceiveAsync``` overload that accepts a ```SequenceNumber``` (the data type is ```long```) will only work for 
messages with ```BrokeredMessage.State``` set to ```MessageState.Deferred```.

As the deferred message is retrieved, it returns to the normal ```MessageState.Active``` state and handled like any other message.       

```             
            
            var message = await receiver.ReceiveAsync(step);
            var body = message.GetBody<Stream>();
            dynamic recipeStep = JsonConvert.DeserializeObject(new StreamReader(body, true).ReadToEnd());
           
            ... print the message ... 
           
            await message.CompleteAsync();
            lastProcessedRecipeStep = lastProcessedRecipeStep + 1;
            deferredSteps.Remove(lastProcessedRecipeStep);
        }
    }
``` 

What this sample doesn't show is how to recover from crashes when the app is handling deferred messages. The [SessionState](../SessionState)
sample explains the built-in facility Service Bus has for failover of the required information between competing receivers.    

