# Azure Service Bus .NET Standard samples

This repository contains the official set of .NET Standard samples for the Azure Service Bus service (Standard and Premium), illustrating all core 
features of Service Bus Queues and Service Bus Topics.  This samples all use the `Microsoft.Azure.ServiceBus` NuGet package for
.NET Standard.

# Setup

First, clone this git repository locally. 

The samples require [creating an Azure subscription](https://azure.microsoft.com/free/) if you don't have one. You also need  
a [Service Bus namespace](https://docs.microsoft.com/azure/service-bus-messaging/service-bus-fundamentals-hybrid-solutions), 
and a simple basic topology of a few exemplary queues, topics, and subscriptions. To set those up, 
with an Azure Service Bus "Standard" namespace, just click the button below and follow the further instructions 
on the Azure Portal:

<a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fclemensv%2Fazure-service-bus%2Fmaster%2Fsamples%2FDotNet%2FMicrosoft.ServiceBus.Messaging%2Fscripts%2Fazuredeploy.json" target="_blank">
    <img src="http://azuredeploy.net/deploybutton.png"/>
</a>

The free Azure subscription offer includes a service credit that will take you very far with all your 
experiments. The prorated [monthly base fee](https://azure.microsoft.com/pricing/details/service-bus/) 
for Service Bus Standard includes a generous allocation of message operations, and you can even run a 
large [Service Bus Premium namespace](https://docs.microsoft.com/azure/service-bus-messaging/service-bus-premium-messaging) 
with 4 Messaging Units for several days.

You can also deploy the resource manager template from the command line:

## Setup using PowerShell

The PowerShell setup is functionally equivalent. You first [create a resource group](https://docs.microsoft.com/azure/azure-resource-manager/powershell-azure-resource-manager) and then [deploy the resource manager template](https://docs.microsoft.com/azure/azure-resource-manager/resource-group-template-deploy):

```powershell
New-AzureRmResourceGroup -Name {rg-name} -Location "Central US"
New-AzureRmResourceGroupDeployment \
      -Name {deployment-name} \
      -ResourceGroupName {rg-name}
      -TemplateFile scripts/azuredeploy.json \
      -serviceBusNamespaceName {service-bus-namespace-name} 
```

## Exploring and running the samples

To make running the samples straightforward, there is a Powershell ([Azure PS](https://docs.microsoft.com/azure/azure-resource-manager/powershell-azure-resource-manager)) script 
in *scripts* that will obtain the namespace connection string from your current Azure subscription, assume the entity names configured in the deployed templates, 
and export those into a configuration file in your user directory, eliminating the need to pass arguments on the command line.

The Powershell script is *scripts/setupenv.ps1*. It needs to be called with the name of the Resource Group and the Service Bus namespace name as ordinal arguments. 

Run the Powershell script from the scripts directory with

```bash
./setupenv.ps1 {rg-name} {service-bus-namespace-name} 
```

## Common Considerations

Most samples use shared [entry-point boilerplate code](common/Main.cs) that loads the configuration and then launches the sample's 
**Program.Run()** instance methods. 

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
 
### Management Operations

* **Managing entities** - The [QueueCRUD](./ManagingEntities/QueueCRUD) sample shows how to create a new entity, retrieve an existing
  entity and its properties, update the properties (which can be udpated), and also delete the entity.
* **SASAuthorizationRule** - The [SASAuthorizationRule](./ManagingEntities/SASAuthorizationRule) sample shows how to create a new 
  SAS authentication policy for a particular entity with a limited scope of Send or Listen.
  
### External Samples

* **Authentication using Managed Service Identity** - The [MSI_Authentication](https://github.com/Azure-Samples/app-service-msi-servicebus-dotnet) sample shows how to send and receive data from Azure Service Bus Queue at run-time from an App Service with a Managed Service Identity (MSI) 
* **High throughput performance sample** - The [PerformanceSample](https://github.com/Azure-Samples/service-bus-dotnet-messaging-performance) can be used to help benchmark Service Bus premium messaging, and can be used for performance best practices. 
