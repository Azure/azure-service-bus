# Azure Service Bus samples

This repository contains the official set of samples for the Azure Service Bus service (Standard and Premium), illustrating all core 
features of Service Bus Queues and Service Bus Topics.  This samples all use the `WindowsAzure.ServiceBus` NuGet package.

## Requirements and Setup

These samples run against the cloud service and require that you have an active Azure subscription available 
for use. If you do not have a subscription, [sign up for a free trial](https://azure.microsoft.com/pricing/free-trial/), 
which will give you **ample** credit to experiment with Service Bus Messaging. 
  
The samples assume that you are running on a supported Windows version and have a .NET Framework 4.5+ build environment available. 
[Visual Studio 2015](https://www.visualstudio.com/) is recommended to explore the samples; the free community edition will work just fine.    

To run the samples, you must perform a few setup steps, including creating and configuring a Service Bus namespace. 
For the required [setup.ps1](setup.ps1) and [cleanup.ps1](cleanup.ps1) scripts, **you must have Azure Powershell installed** 
([if you don't here's how](https://azure.microsoft.com/en-us/documentation/articles/powershell-install-configure/)) and properly
configured and run these scripts from the Azure Powershell environment.

Mind that this set of samples does presently use the "Azure Service Management" interface, so you need to initialize your environment access with 
[Add-AzureAccount](https://msdn.microsoft.com/de-de/library/azure/dn790372.aspx) from the Azure Powershell command line. 

``` PS
PS C:\> Add-AzureAccount
``` 

This will prompt you to log in with the account associated with your Azure subscription(s). The cached access token will eventually expire; when 
that happens you will be asked to run Add-AzureAccount again.  

### Setup      
The [setup.ps1](setup.ps1) script will either use the account and subscription you have previously configured for your Azure Powershell environment
or prompt you to log in and, if you have multiple subscriptions associated with your account, select a subscription. 

The script will then create a new Azure Service Bus namespace for running the samples and configure it with shared access signature (SAS) rules
granting send, listen, and management access to the new namespace. The configuration settings are stored in the file "azure-msg-config.properties", 
which is placed into the user profile directory on your machine. All samples use the same [entry-point boilerplate code](common/Main.cs) that 
retrieves the settings from this file and then launches the sample code. The upside of this approach is that you will never have live credentials 
left in configuration files or in code that you might accidentally check in when you fork this repository and experiment with it.   

### Cleanup

The [cleanup.ps1](cleanup.ps1) script removes the created Service Bus namespace and deletes the "azure-msg-config.properties" file from 
your user profile directory.
 
## Common Considerations

Most samples use shared [entry-point boilerplate code](common/Main.cs) that loads the configuration and then launches the sample's 
**Program.Run()** instance methods. 

Except for the samples that explicitly demonstrate security capabilities, all samples are invoked with an externally issued SAS token 
rather than a connection string or a raw SAS key. The security model design of Service Bus generally prefers clients to handle tokens 
rather than keys, because tokens can be constrained to a particular scope and can be issued to expire at a certain time. 
More about SAS and tokens can be found [here](https://azure.microsoft.com/documentation/articles/service-bus-shared-access-signature-authentication/).               

All samples use the asynchronous, task-based programming model of the .NET Framework and therefore the *xAsync* overloads of the 
respective Service Bus API methods. Since nearly all Service Bus operations result in network I/O, using the asynchronous programming
model is strongly encouraged at all times as it yields significantly more efficient execution at runtime.      

> As you are exploring the samples, you should keep in mind that these samples are not aiming to show the simplest way to 
> use the Service Bus API, but rather the **recommended**, most robust, and most efficient way to use the Service Bus API.
> The samples are therefore more explicit and take more lines of code than the simplest use of the API would. Distributed 
> systems, and especially cloud systems, are dynamic environments and the samples reflect this reality.     

## Samples

### Getting Started

* **Getting Started with Queues** - The [QueuesGettingStarted](./QueuesGettingStarted) sample illustrates the basic send and receive gestures 
  for interacting with a previously provisioned Service Bus Queue. Most other samples in this repository are derivatives of this basic sample. 
* **Getting Started with Topics** - The [TopicsGettingStarted](./TopicsGettingStarted) sample illustrates the basic gestures for sending
  messages into Topics and receiving them from Subscriptions.
  
### Message Handling

* **Senders and Receivers with Queues** - The [SendersReceiversWithQueues](./SendersReceiversWithQueues) sample shows how to use the 
  ```MessagingFactory```for explicit connection management and the generic ```MessageSender``` and ```MessageReceiver``` abstractions with queues. 
* **Senders and Receivers with Topics** - The [SendersReceiversWithTopics](./SendersReceiversWithTopics) sample is a variation of 
   the [SendersReceiversWithQueues](./SendersReceiversWithQueues) sample and shows how nearly identical code can be use with Queues and Topics
   when using the ```MessageSender``` and ```MessageReceiver``` abstractions.  
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
* **Priority Subscriptions** - the sample [PrioritySubscriptions](./PrioritySubscriptions) shows how to model a "priority queue" pattern
  with a Topic, with each priority tier having its own Topic Subscription.
  
### Partitioned Entities

* **Partitioned Queues** - [PartitionedQueues](./PartitionedQueues) are largely identical in handling to "regular" Queues (and are the default 
  option when creating new Queues via teh Azure Portal), but are more resilient against slowdowns in the backend storage system. 
  This sample illustrates some special considerations to keep in mind for partitioned queues.   

### Error and Transaction Handling

* **Deadletter Queues** - The [DeadletterQueue](./DeadletterQueue) sample shows how to use the deadletter queue for setting aside 
  messages that cannot be processed, and how to receive from the deadletter queue to inspect, repair, and resubmit such messages.
* **Time To Live** - The [TimeToLive](./TimeToLive) example shows the basic functionality of the TimeToLive option for messages as
  well as handling of the deadletter queue where messages can optionally be stored by the system as they expire.
* **Atomic Transactions** - Service Bus supports wrapping [AtomicTransactions](./AtomicTransactions) scopes around a range of 
  operations, allowing for such groups of operations to either jointly succeed or fail, enabling creating more robust business 
  applications in the cloud.
* **Durable Senders** - The [DurableSender](./DurableSender) sample shows how to make client applications robust against frequent
  network link failures.
* **Geo Replication** - The [GeoReplication](./GeoReplication) sample illustrates how to route messages through two distinct 
  entities, possibly located in different namespaces in differentr datacenters, to limit the application's availability risk.      
  
### Session and Workflow Management Features

* **Sessions** - The [Sessions](./Sessions) sample shows how to enforce strict ordered processing for messages originating from 
  a particular context, and how to multiplex multiple distinct contexts over a single Queue or a Subscription.   
* **Deferral** - The [Deferral](./Deferral) sample shows how to postpone processing of received messages by deferral, which 
  allows pushing messages back into a Queue or Subscription so that they can be picked up directly as the processor is 
  ready to handle them.    
* **Session State** - The [SessionState](./SessionState) sample shows how to keep track of processing a workflow using 
  the session state feature. 
 
### Windows Communication Foundation (WCF) Binding
  
* **NetMessagingBinding** - The [NetMessagingBinding](./NetMessagingBinding) sample shows how to use Service Bus Queues 
   and Topics seamlessly the context of WCF applications using the NetMessagingBinding.
* **Sessions with the NetMessagingBinding** - The [NetMessagingSession](./NetMessagingSession) sample shows how to use Service Bus
  sessions with the NetMessagingBinding.
                              
 