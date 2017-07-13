# Atomic Transactions with Service Bus

This sample illustrates how to use Azure Service Bus atomic transaction support by implementing a 
travel booking scenario using the [Saga pattern](http://kellabyte.com/2012/05/30/clarifying-the-saga-pattern/)
first formulated by [Hector Garcia Molina and Kenneth Salem [PDF]](http://www.cs.cornell.edu/andru/cs711/2002fa/reading/sagas.pdf) 
in 1987 as a form of a long-lived transaction. 

The Saga model we will implement combines local transactions scoped to a single work-step with a model that 
allows achieving a consistent outcome of a set of activities that span those transactions and cannot be executed 
in a single transaction scope in a large distributed systems that may also span multiple owners.        

Mind that the sample is of substantial complexity and  aimed at developers building frameworks leaning 
on Azure Service Bus for creating robust foundations for business applications in the cloud. Therefore, the sample 
code is very intentionally not "frameworked-over" with a smooth abstraction for hosting the simulated business logic,
since the focus is on showing the interactions with the platform. 

You can most certainly use the presented capabilities directly in a business application if you wish.

In this document we will discuss the transactional capabilities of Service Bus first, then briefly discuss Sagas (you 
are encouraged to review the blog article and the paper linked above for more depth). Then we look at how to project the concept
onto Service Bus, and then we'll take a look at the code.  

## What are Transactions?

"Transactions" are execution scopes in the context of [transaction processing](https://en.wikipedia.org/wiki/Transaction_processing).
A transaction groups two or more operations together. The goal of a transaction is that the result of this group of operations has a common outcome. 
A transaction coordinator or a transaction framework therefore ensures that the operations belonging to the group of operations 
either jointly fail or jointly succeed and in this respect "act as one" - which is referred to as atomicity. 

Transaction theory is rich and defines further properties (such such as consistency, isolation and durability) that relate to how the 
participants in the transaction ought to handle their resources during a transaction and after the transaction succeeds or fails. We'll 
touch on some of those below, but diving into all details is well beyond the scope of this document. There are a several excellent books 
and many papers for exploring theory and history of transaction processing. These two classics might be worth your time:

* Jim Gray, Andreas Reuter, Transaction Processing — Concepts and Techniques, 1993, Morgan Kaufmann, [ISBN 1-55860-190-2](https://en.wikipedia.org/wiki/Special:BookSources/1558601902)
* Philip A. Bernstein, Eric Newcomer, Principles of Transaction Processing, 1997, Morgan Kaufmann, [ISBN 1-55860-415-4](https://en.wikipedia.org/wiki/Special:BookSources/1558604154))
  
## How does Service Bus support transactions?

Azure Service Bus is a transactional message broker and ensures transactional integrity for all internal operations 
against its message stores and the respective indices. All transfers of messages inside of Service Bus, such as moving 
messages to a [Dead-Letter Queue](/../Deadletter) or [automatic forwarding](../AutoForward) of messages between entities are 
transactional.What that means is that if Service Bus reports a message as accepted it has already been stored and labeled with 
a sequence number, and from there onwards, any transfers inside of Service Bus are coordinated operations across entities, and will 
neither lead to loss (source succeeds and target fails) or to duplication (source fails and target succeeds) of the message. 

From a consumer perspective, Service Bus supports grouping of certain operations against a **single entity** in the scope 
of a transaction. You can, for instance, send several messages to one Queue from within a transaction scope, and the 
messages will only be committed to the Queue's log when the transaction successfully completes.

The operations that can be performed within a transaction scope are:

* `QueueClient`, `MessageSender`, `TopicClient`: 
    * `Send`, `SendAsync`
    * `SendBatch`, `SendBatchAsync`
* `BrokeredMessage`:  
    * `Complete`, `CompleteAsync`
    * `Abandon`, `AbandonAsync`
    * `Deadletter`, `DeadletterAsync`
    * `Defer`, `DeferAsync`
    * `RenewLock`, `RenewLockAsync`
* `MessageSession`:
    * `SetState`, `SetStateAsync`
    * `GetState`, `GetStateAsync`
    
Quite apparently missing are all receive operations. The assumption made for Service Bus transactions is that the application
acquires messages, using the ReceiveMode.PeekLock mode, inside some receive loop or with an OnMessage callback, and only then 
opens a transaction scope for processing the message. 

The disposition of the message (complete, abandon, dead-letter, defer) then occurs within the scope of and dependent on 
the overall outcome of the transaction.

> Client transactions are currently only supported over the *NetMessaging* protocol and using the .NET Framework client. 

Service Bus does **not** support enlistment into distributed 2-phase-commit transactions via MS DTC or other transaction 
coordinators, so you cannot perform an operation against SQL Server or Azure SQL DB and Service Bus from within the same 
transaction scope. This is also true for the on-premises variant of Azure Service Bus, Service Bus for Windows Server 1.1.

> Send operations can be enlisted in a MS DTC transaction when the operation is bridged, on the client, via 
> the Microsoft Message Queue (MSMQ). The [DurableSender](../DurableSender) sample illustrates how to do this.    

Azure Service Bus *does* support .NET Framework transactions [which enlist volatile participants](https://msdn.microsoft.com/en-us/library/ms172153(v=vs.85).aspx) 
into a transaction scope. Whether a set of Service Bus operations will become effective can therefore be made dependent 
on the outcome of independently enlisted, parallel local work.

Transactions that span multiple Service Bus entities require using a special API gesture. You may, for instance, want to 
receive/complete a message from one queue and send to a different queue from within a single transaction scope. If you 
use the regular API operations against two or more entities from within a single transaction scope, the operation will fail.  

## From Queue to Work to Queue

To allow transactional handover of data from a queue to a processor and then onwards to another queue, Service Bus supports 
"transfers". 

```
        /---\         +-----+        +-----+
       |  P  | =====> |  T  | =====> |  Q  |
        \---/         +-----+        +-----+
``` 

In a transfer operation, a sender (or transaction processor, `P`) first sends a message to a "transfer queue" (`T`) and the 
transfer queue immediately proceeds to move the message to the intended destination queue (`Q`) using the same robust transfer 
implementation that the [auto-forward](../AutoForward) capability relies on. The message is never committed to the transfer 
queue's log in a way that it becomes visible for the transfer queue's consumers.

This transfer model becomes a powerful tool for transactional applications when the transfer queue is source of the 
processor's input messages:  

```
       +-----+         /---\         +-----+        +-----+
       |  T  | =====> |  P  | =====> |  T  | =====> |  Q  |
       +-----+         \---/         +-----+        +-----+
```
   
or, illustrated differently:

```
        /---\  <===== +-----+        +-----+
       |  P  |        |  T  |        |  Q  |
        \---/  =====> +-----+ =====> +-----+
```

It may initially look a bit odd to post a message back to the queue from which your process receives, but it enables 
Service Bus to execute the operation to complete (or defer or dead-letter) the input message and the operation to 
capture the resulting output message on the same message log in a single atomic operation.

```
        /---\  [M1] == Receive() <==== +-----+                  +-----+
       |  P  | [M2] == Send() =======> |  T  | [M2] == Fwd ===> |  Q  |
        \---/  [M1] == Complete() ===> +-----+                  +-----+
          :                                                        ^
          :.................. effective transfer path .............: 
        
```

The way you sent up such transfers is by creating a message sender that targets the destination queue "via" the 
transfer queue. You will obviously also have a receiver that pulls messages from that same queue:  

```C#
    var sender = this.messagingFactory.CreateMessageSender(destinationQueue, myQueueName);
    var receiver = this.messagingFactory.CreateMessageReceiver(myQueueName);
```   

A simple transaction then uses these elements as follows:

```C#
   var msg = receiver.Receive();
   
   using ( scope = new TransactionScope() )
   {
       // do whatever work is required 
       
       var newmsg = ... package the result ... 
        
       msg.Complete(); // mark the message as done
       sender.Send( newmsg ); // forward the result
       
       scope.Complete(); // declare the transaction done
   } 
   
```

## A Quick Introduction To Sagas

As you can learn from the blog post and paper linked from the introduction above, a Saga is a chain of separate 
transactional work-steps that form a chain, similar to what workflow frameworks provide.

If there is a failure, the result of each work-step can be reversed using the work-step's associated *compensator*. 
The compensator generally does not annihilate the outcome of a previously successful execution of its work-step, but
performs explicit work that compensates for the work-step's effect. That is also the principle by which most 
business systems work. In any accounting application, it's outright illegal to delete erroneously-created records; 
it is required to create a further record that corrects the effect of the erroneously-created record. The error 
stays in the books.            

This sample implements a classic example for illustrating the principle: We'll try to book a travel trip that 
includes reserving a rental car, reserving a hotel room, and booking a flight ticket. 

While these three operations are independent, it's not going to make us happy to hold a car and hotel reservation
on a fabulous vacation island without having been able to secure seats on a flight (or ship) to get there.

We therefore form a work-flow that will first try to reserve a car, then try to reserve a hotel, and then try to 
book a flight. The logic behind that order is that flights are the most limited resource and have the greatest failure 
potential. Also, cancelling a flight reservation is more likely to carry a financial penalty (airlines offer 
penalty-free short-term reservation holds before any ticket is issued, so we should probably model two steps here, but 
we're not *really* building a reservation system).

The resulting flow looks like this:

```

           [Start] --> [ Book Rental Car ] --> [ Book Hotel ] --> [ Book Flight ] --+
                               |                     |                   |          |
                             Error                 Error               Error        |
                               |                     |                   |          |
                               V                     V                   V          |
                   +-- [Cancel Rental Car] <-- [Cancel Hotel] <-- [Cancel Flight]   | <~~~ [Undo]
                   |                                                                |
                   +-------->--Fail---------------+    +---------OK---<-------------+
                                                  V    V
                                                 [Result]    
        
```

Each of these operations is a local transaction that needs to yield a consistent and predictable outcome. But there 
is no coordination required all across these three steps as long as it is ensured that the outcome of each partial 
transaction is preserved and progress is made. The overall outcome is either a completely booked itinerary, or a 
failure report. The case we don't cover in the sample is a cancellation after success, which would run all compensators.

## The Sample

The sample code first sets up a messaging topology for the saga, and then runs the scenario. Afterwards the topology is 
cleaned up. Mind *your* application **should not create/delete such topologies at runtime**; we do this here to limit the 
sample's complexity. Creating a topology should be a configuration or provisioning task associated with your application
that is executed very rarely and clearly separated from the runtime path that uses the topology.

``` C#
var namespaceManager = new NamespaceManager(
    namespaceAddress,
    TokenProvider.CreateSharedAccessSignatureTokenProvider(manageKeyName, manageKey));

var queues = await this.SetupSagaTopologyAsync(namespaceManager);
await RunScenarioAsync(namespaceAddress, manageKeyName, manageKey);
await this.CleanupSagaTopologyAsync(namespaceManager, queues);
```

### Topology setup

Creating the topology is done with the Service Bus API, but could also be done with an ARM template. Since
Service Bus allows for hierarchical structures inside a namespace, we create the following structure for this 
"Saga Type 1", reflected in the constants of the sample    

```
    / Service Bus Namespace Root
    |
    +- /sagas
         +- /1          
            +- /input  (SagaInputQueueName)
            +- /Ta     (BookRentalCarQueueName)
            +- /Tb     (BookHotelQueueName)
            +- /Tc     (BookFlightQueueName)
            +- /Ca     (CancelRentalCarQueueName)
            +- /Cb     (CancelHotelQueueName)
            +- /Cc     (CancelFlightQueueName)
            +- /output (SagaOutputQueueName)
```

With that input, the `SetupSagaTopologyAsync` method is quite repetitive, so we're no going to reproduce 
that here in full, but just point out the two interesting details.

The work-step queues are configured with automatic forwarding from their dead-letter queues into their respective 
compensator's queues. That means the processor of the work hands any explicit failures to the compensation path
by dead-lettering the input message. What's nice about this approach is that it works for explicit failures of
the operation (hotel is booked out) and also catches any technical issues (repeated crashes of the worker, 
message expiration).       

``` C#
        new QueueDescription(BookFlightQueueName)
        {
            // on failure, we move deadletter messages off to the flight 
            // booking compensator's queue
            EnableDeadLetteringOnMessageExpiration = true,
            ForwardDeadLetteredMessagesTo = CancelFlightQueueName
        }),
```                 

The "input" queue is wired to the first step of the Saga, also by using auto-forwarding. That allows the 
clients to remain stable and not change the submission address even if the Saga structure were changed. It 
also would allow for the input to be differently secured than the private in-saga flow (which we don't show here).

Doing this in the topology setup and having the flow defined in the following step is a bit of a "leaky 
abstraction", but the declared intent for this sample is not to hide too much detail with pretty framework:   

``` C#
        new QueueDescription(SagaInputQueueName)
        {
            // book car is the first step
            ForwardTo = BookRentalCarQueueName
        })   
```

### Configuring Flow and Running the Saga

The `RunScenarioAsync` method is the scenario host. It creates and initializes the receiver on the 
"output" queue where all results land, then initializes and hosts the Saga (see below), and then proceeds to
submitting the booking jobs into the Saga.  The `SendBookingRequests` method creates some input example 
data and submits it as messages into the "input" queue. The jobs don't need to always request car and hotel 
and air together, but can also request any other combination.       

```C#
static async Task RunScenarioAsync(string namespaceAddress, string manageKeyName, string manageKey)
{
    ... omitted factory setup ...
        
    var resultsReceiver = await RunResultsReceiver(receiverMessagingFactory);

    var sagaTerminator = new CancellationTokenSource();
    var saga = RunSaga(workersMessagingFactory, sagaTerminator);

    await SendBookingRequests(senderMessagingFactory);

    Console.ReadKey();
    sagaTerminator.Cancel();
    await saga.Task;

    resultsReceiver.Close();
    senderMessagingFactory.Close();
    receiverMessagingFactory.Close();
    workersMessagingFactory.Close();
}
```

The `RunSaga` method sets up execution of the Saga workers and compensators via a helper class as follows: 

``` C#
static SagaTaskManager RunSaga(MessagingFactory workersMessageFactory, CancellationTokenSource terminator)
{
    var saga = new SagaTaskManager(workersMessageFactory, terminator.Token)
    {
        {BookRentalCarQueueName, TravelBookingHandlers.BookRentalCar, BookHotelQueueName, CancelRentalCarQueueName},
        {CancelRentalCarQueueName, TravelBookingHandlers.CancelRentalCar, SagaResultQueueName, string.Empty},
        {BookHotelQueueName, TravelBookingHandlers.BookHotel, BookFlightQueueName, CancelHotelQueueName},
        {CancelHotelQueueName, TravelBookingHandlers.CancelHotel, CancelRentalCarQueueName, string.Empty},
        {BookFlightQueueName, TravelBookingHandlers.BookFlight, SagaResultQueueName, CancelFlightQueueName},
        {CancelFlightQueueName, TravelBookingHandlers.CancelFlight, CancelHotelQueueName, string.Empty}
    };
    return saga;
}
``` 

Each of the rows in the initialization of the object is equivalent to a call to a method on the 
helper class that is shown below. The C# compiler turns the rows into calls to the `Add` method. 

The initialization wires up the message handlers and define the message paths for positive and 
negative outcomes to arrive at the flow graph shown above. 

The helper creates a receiver object for `taskQueueName`, and sender objects for the 
`nextStepQueue` and `compensatorQueue`. Notice that these senders send **via** the
`taskQueueName`, as discussed above.   

It then registers an `OnMessageAsync` lambda on the receiver which will dispatch to the supplied 
callback method when a message has been obtained. The callback is invoked with the received message, and the 
sender objects for next step and compensator, from which the invoked method can choose for how it wants to make progress.

The method also registers with the `CancellationToken` handled by the containing object to ensure proper shutdown.

``` C#
public void Add(
    string taskQueueName,
    Func<BrokeredMessage, MessageSender, MessageSender, Task> doWork,
    string nextStepQueue,
    string compensatorQueue)
{
    var tcs = new TaskCompletionSource<bool>();
    var rcv = this.messagingFactory.CreateMessageReceiver(taskQueueName);
    var nextStepSender = this.messagingFactory.CreateMessageSender(nextStepQueue, taskQueueName);
    var compensatorSender = this.messagingFactory.CreateMessageSender(compensatorQueue, taskQueueName);

    this.cancellationToken.Register(
        () =>
        {
            rcv.Close();
            tcs.SetResult(true);
        });
    rcv.OnMessageAsync(m => doWork(m, nextStepSender, compensatorSender), new OnMessageOptions {AutoComplete = false});
    this.tasks.Add(tcs.Task);
} 
```

Once `RunSaga` returns, all receivers are active.      
 
### Business Transactions 

The callback functions that represent the busines transactions are fairly uniform since we don't perform 
true work in this sample. We will therefore only dissect one of the workers and one of the compensators.

We'll pick `BookHotel` and `CancelHotel`, because they are both in the middle of the flow.

```C#
public static async Task BookHotel(BrokeredMessage message, MessageSender nextStepQueue, MessageSender compensatorQueue)
{
    try
    {
```

To start, we'll pick up the "via" property from the incoming job message and add the id of this 
job so that we can track the job progress.
 
```C#        
var via = (message.Properties.ContainsKey("Via")
            ? ((string) message.Properties["Via"] + ",")
            : string.Empty) +
                    "bookhotel";
```

Then we open a transaction scope, and check whether this is a messaage we can handle, and decode it.

```C#
        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            if (message.Label != null &&
                message.ContentType != null &&
                message.Label.Equals(TravelBookingLabel, StringComparison.InvariantCultureIgnoreCase) &&
                message.ContentType.Equals(ContentTypeApplicationJson, StringComparison.InvariantCultureIgnoreCase))
            {
                var body = message.GetBody<Stream>();
                dynamic travelBooking = DeserializeTravelBooking(body);
```

If the booking does not contain a hotel reservation request, we hand the job to the next step and declare success 

```C#
                // do we want to book a hotel? No? Let's just forward the message to
                // the next destination via transfer queue
                if (travelBooking.hotel == null)
                {
                    await nextStepQueue.SendAsync(CreateForwardMessage(message, travelBooking, via));
                    // done with this job
                    await message.CompleteAsync();
                }
                else
                {
```

Otherwise we simulate doing the work of booking a hotel, which will usually involve calling some external 
service(s). We simulate a failure of the business transaction below by having every 11th attempt to book 
a hotel go wrong, and the input to be dead-lettered so that it gets picked up by the compensation task 
though the auto-forward path that we set up earlier.    

```C#
                    lock (Console.Out)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine("Booking Hotel");
                        Console.ResetColor();
                    }

                    // now we're going to simulate the work of booking a hotel,
                    // which usually involves a call to a third party

                    // every 11th hotel booking sadly goes wrong
                    if (message.SequenceNumber%11 == 0)
                    {
                        await message.DeadLetterAsync(
                            new Dictionary<string, object>
                            {
                                {"DeadLetterReason", "TransactionError"},
                                {"DeadLetterErrorDescription", "Failed to perform hotel reservation"},
                                {"Via", via}
                            });
                    }
                    else
                    {
```

If the transaction occurs in the first 3 seconds of any minute, we just simulate an outright crash

```C#
                        if (DateTime.UtcNow.Second <= 3)
                        {
                            throw new Exception("O_o");
                        }


```

When we get past all failure opportunities, we pretend we have a valid booking and and progress to 
the next step and complete the input message. 

```C#
                        // let's pretend we booked something
                        travelBooking.hotel.reservationId = "5676891234321";
                        await nextStepQueue.SendAsync(CreateForwardMessage(message, travelBooking, via));

                        // done with this job
                        await message.CompleteAsync();
                    }
                }
            }
            else
            {
```

If we don't know how to decode the message we also push it out to the dead-letter queue

```C#                
                await message.DeadLetterAsync(
                  new Dictionary<string, object>
                        {
                            {"DeadLetterReason", "BadMessage"},
                            {"DeadLetterErrorDescription", "Unrecognized input message"},
                            {"Via", via}
                        });
            }
```

Finally and **importantly**, we tell the transaction framework that we're done. With the pending 
transaction scope, none of the interactions with the task queue are yet realized and that will only 
happen once we call ```Complete``` and the transaction concludes. If we leave the transaction scope 
without completing, none of the work on the queue is done. That means that eventually the lock 
on the input message is going to expire and the operation will be retried. 

The underlying assumption is that the external work that is being performed can detect duplicate
jobs, meaning that if the agency issues the same booking with the same agency booking code twice,
the second attempt will return the resuklt of the prior job.               

```C#
            scope.Complete();
        }
    }
```

Lastly, if anything went wrong that we did not accoutn for, we will log that fact and abandon the 
input message. 

```C#
    catch (Exception e)
    {
        Trace.TraceError(e.ToString());
        await message.AbandonAsync();
    }
}

```

### Compensators

The hotel booking's compensator doesn't do any real work except for resetting the reservation-id if one was set
by the worker. In an actual implementation it would call an external service to cancel the reservation.

```C#
public static async Task CancelHotel(BrokeredMessage message, MessageSender nextStepQueue, MessageSender compensatorQueue)
{
    lock (Console.Out)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Cancelling Hotel");
        Console.ResetColor();
    }
    try
    {
        var via = (message.Properties.ContainsKey("Via")
            ? ((string) message.Properties["Via"] + ",")
            : string.Empty) +
                    "cancelhotel";
```

The cancellation is also done in a transaction scope. We check whether we can understand the message first. 
If we can we porocess, if we can't, we dead-letter. That means that bad input messages coming from the workers 
will trickle down into the dead-letter queue of the compensator. That's a choice this implementation makes. The 
compensator could also attempt to fix the message and then resubmit it into the worker path.

``` C#

        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            if (message.Label != null &&
                message.ContentType != null &&
                message.Label.Equals(TravelBookingLabel, StringComparison.InvariantCultureIgnoreCase) &&
                message.ContentType.Equals(ContentTypeApplicationJson, StringComparison.InvariantCultureIgnoreCase))
            {
                var body = message.GetBody<Stream>();
                dynamic travelBooking = DeserializeTravelBooking(body);
```

Did we want to book a hotel? Did we succeed and have work to undo?

``` C#
                if (travelBooking.hotel != null &&
                    travelBooking.hotel.reservationId != null)
                {
                    // undo the reservation (or pretend to fail)
                    if (DateTime.UtcNow.Second <= 3)
                    {
                        throw new Exception("O_o");
                    }
```

This is where the work happens. We just reset the reservation-id and forward 

``` C#

                    // reset the id
                    travelBooking.hotel.reservationId = null;

                    // forward
                    await nextStepQueue.SendAsync(CreateForwardMessage(message, travelBooking, via));
                }
                else
                {
                    await nextStepQueue.SendAsync(CreateForwardMessage(message, travelBooking, via));
                }
                // done with this job
                await message.CompleteAsync();
            }
            else
            {
                await message.DeadLetterAsync(
                    new Dictionary<string, object>
                            {
                                {"DeadLetterReason", "BadMessage"},
                                {"DeadLetterErrorDescription", "Unrecognized input message"},
                                {"Via", via}
                            });
            }
            scope.Complete();
        }
    }
    catch (Exception e)
    {
        Trace.TraceError(e.ToString());
        await message.AbandonAsync();
    }
}
```  


   
