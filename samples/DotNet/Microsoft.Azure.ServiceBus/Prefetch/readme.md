# Prefetch
This sample illustrates the the "prefetch" feature of the Service Bus client.

The sample is specifically crafted to demonstrate the throughput difference between receiving 
messages with prefetch turned on and prefetch turned off. The default setting is for prefetch to be
turned off. 

[Read more about the prefetch feature in the documentation.](https://docs.microsoft.com/azure/service-bus-messaging/service-bus-prefetch)

## Prerequisites and Setup

Refer to the main [README](../README.md) document for setup instructions. All samples share and require the same setup
before they can be run.

## Sample Code 

The sample is documented inline in the [Program.cs](Program.cs) C# file.

Having worked through [QueuesGettingStarted](../QueuesGettingStarted) and [ReceiveLoop](../ReceiveLoop), you
will be familiar with the majority of API gestures we use here, so we're not going to through all of 
those again.

The sample sets up a very simple comparative performance test between two runs of the same method, once
with and once without prefetch enabled, end writes out the time difference in the end. For each run we
set up a fresh receiver.   

``` C#
    // Run 1
    var receiver = new MessageReceiver(connectionString, queueName, ReceiveMode.PeekLock);
    receiver.PrefetchCount = 0;
    // Send and Receive messages with prefetch OFF
    var timeTaken1 = await this.SendAndReceiveMessages(sender, receiver, 100);
    
    receiver.Close();
    
    // Run 2
    receiver = new MessageReceiver(connectionString, queueName, ReceiveMode.PeekLock);
    receiver.PrefetchCount = 10;
    // Send and Receive messages with prefetch ON
    var timeTaken2 = await this.SendAndReceiveMessages(sender, receiver, 100);
    
    receiver.Close();

    // Calculate the time difference
    var timeDifference = timeTaken1 - timeTaken2;

    Console.WriteLine("\nTime difference = {0} milliseconds", timeDifference);

    Console.WriteLine();
    Console.WriteLine("Press [Enter] to quit...");
    Console.ReadLine();
```

The core of the ```SendAndReceiveMessages``` method is a loop that picks up the previously sent 
messages and measures the time taken.

``` C#
        var stopWatch = new Stopwatch();
        stopWatch.Start();

        var receivedMessage = await receiver.ReceiveAsync(TimeSpan.FromSeconds(5));
        while (receivedMessage != null )
        {
            // here's where you'd do any work

            // complete (roundtrips)
            await receivedreceiveClient.CompleteAsync(message.SystemProperties.LockToken);

            if (--messageCount <= 0)
                break;

            // now get the next message
            receivedMessage = await receiver.ReceiveAsync(TimeSpan.FromSeconds(5));
        }
        // Stop the stopwatch
        stopWatch.Stop();
``` 

When you execute the sample you will find that the prefetch variant will yield significantly
higher throughput, even though each message is explicitly being completed with another roundtrip 
network gesture.  