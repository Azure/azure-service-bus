# Priority Subscriptions

This sample illustrates how do use topic subscriptions and filters for splitting
up a message streams into multiple distinct streams based on certain conditions.

The concrete example use-case shown here is prioritization, where we split the
message stream into three distinct streams, with processing priorities 1 and 2
having their own subscriptions, and priorities 3 and below having a shared
subscription. Splitting up the message stream for routing to particular
consumers for any other reason will look quite similar. 
 
## Prerequisites and Setup

Refer to the main [README](../README.md) document for setup instructions. 

## Sample Code 

The sample is documented inline in the [Program.cs](Program.cs) C# file.
  