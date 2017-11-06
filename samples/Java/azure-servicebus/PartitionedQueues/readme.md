# Partitioned Queues

This sample illustrates the specifics of partitioned queues. Service Bus creates 
"partitioned" queues by default, which means that the queue log is distributed across
multiple storage backends for minimizing availability risks. This behavior has impact on 
the order in which messages will be retrieved, and it also has impact on the sequence
numbering scheme. This sample illustrates this.

[Read more on partitioning in the documentation][1].

Refer to the main [README](../README.md) document for setup instructions. 

## Sample Code 

The sample is documented inline in the [PartitionedQueues.java](.\src\main\java\com\microsoft\azure\servicebus\samples\partitionedqueues\PartitionedQueues.java) file.

[1]: https://docs.microsoft.com/azure/service-bus-messaging/service-bus-partitioning