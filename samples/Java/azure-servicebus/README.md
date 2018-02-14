# Java Samples for Azure Service Bus

This is the official set of Java samples for Azure Service Bus. The samples demonstrate basics 
such as sending and receiving operations in the "quick starts", and more advanced scenarios in 
the feature-oriented samples. All samples are simple command line applications with minimal extra 
ceremony. 

The samples in this directory are built with the native Azure Service Bus SDK (azure-servicebus). The native Azure 
Service Bus SDK is fully supported by Microsoft (says: you can file service requests through 
the Azure portal to get immediate help) and it provides unfiltered and easy access to all Service Bus features. 

### Getting Started

* **Getting Started with Queues** - The [QueuesGettingStarted](./QueuesGettingStarted) sample illustrates the basic send and receive gestures 
  for interacting with a previously provisioned Service Bus Queue. Most other samples in this repository are derivatives of this basic sample. 
* **Getting Started with Topics** - The [TopicsGettingStarted](./TopicsGettingStarted) sample illustrates the basic gestures for sending
  messages into Topics and receiving them from Subscriptions.

### Message Handling

* **Receive Loop** - [ReceiveLoop](./ReceiveLoop) shows how to use an explicit receive loop with a queues instead of the 
   recommended, callback-based OnMessage(Async) API used in the "getting started" sample.
* **Message Prefetching** - The [Prefetch](./Prefetch) sample shows the difference between having "prefetch" turned on or off for the receiver. 
  Prefetch is a background receive operation that acquires messages into a buffer before the application itself calls *Receive* and therefore 
  optimizes and often accelerates the message flow.
* **Duplicate Detection** - The sample for [DuplicateDetection](./DuplicateDetection) illustrates how Service Bus suppresses the secound and all 
  further messages sent with an identical *MessageId* when sent during a defined duplicate detection time window when the *RequiresDuplicateDetection*
  flag is turned on for a Queue or Topic.
* **Message Browsing** - [MessageBrowse](./MessageBrowse) shows how to enumerate all messages residing in a Queue or Subscription without receiving
  or locking them. This method also allows finding deferred and scheduled messages.
* **Auto Forward** - [AutoForward](./AutoForward) illustrates how and why to use automatic forwarding between entities in Service Bus.
  
### Topics and Subscriptions

* **Topic Filters** - The [TopicFilters](./TopicFilters) sample illustrates how to create and configure filters on Topic Subscriptions.
  
### Partitioned Entities

* **Partitioned Queues** - [PartitionedQueues](./PartitionedQueues) are largely identical in handling to "regular" Queues (and are the default 
  option when creating new Queues via teh Azure Portal), but are more resilient against slowdowns in the backend storage system. 
  This sample illustrates some special considerations to keep in mind for partitioned queues.   

### Error and Transaction Handling

* **Deadletter Queues** - The [DeadletterQueue](./DeadletterQueue) sample shows how to use the deadletter queue for setting aside 
  messages that cannot be processed, and how to receive from the deadletter queue to inspect, repair, and resubmit such messages.
* **Time To Live** - The [TimeToLive](./TimeToLive) example shows the basic functionality of the TimeToLive option for messages as
  well as handling of the deadletter queue where messages can optionally be stored by the system as they expire.
  
See the main samples [README](../readme.md) for setup and build instructions.
