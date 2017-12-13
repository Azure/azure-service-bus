# Deferral

This sample illustrates and explains the use of the Deferral feature in Service
Bus.  

# What is Deferral

When a Queue or Subscription client receives a message that it is willing to
process, but for which processing is not currently possible, it has the option
of "deferring" retrieval of the message to a later point. 

[Read more about deferral in the documentation.][1]

Refer to the main [README](../README.md) document for setup instructions. 

## The Sample

The send-side of the sample takes an ordered list of workflow steps (Shop,
Unpack, Prepare, Cook, Eat) and puts them into the queue. The way we're
shuffling these into a random order is by adding a random delay of a few
milliseconds (```Task.Delay(rnd.Next(30)```) before each send operation. Sending
is done when all send tasks are done.

What we're expecting for processing our workflow are instructions for 5 steps
(Shop, Unpack, Prepare, Cook, Eat) to arrive, and we can only process them in
order. We could start preparing the meal before we have shopped for extra
ingredients we're missing, but the outcome might be disappointing.  

Once we received a recipe step, we check whether it's the step we're expecting
to process next. If that is the case, we handle it and complete the message. If
the message arrives out of the expected order, we defer the message for later
processing.

This causes the message to be "put on the side" inside the queue. Once we're
done with all the messages that were put into the queue, which is simply
determined by the queue remaining empty, we then take care of the
deferred messages and process the remaining steps in order. 

The sample is documented further inline in the [Program.cs](Program.cs) C# file.


[1]: https://docs.microsoft.com/azure/service-bus-messaging/message-deferral