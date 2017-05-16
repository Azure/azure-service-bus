# Auto Forward
This sample demonstrates how to automatically forward messages from a queue, subscription, or deadletter queue into another queue or topic. 

## What is Auto Forwarding?

The Auto-Forwarding feature enables you to chain the a Topic Subscription or a Queue to destination Queue or Topic that is part of the same Service Bus namespace. 
When the feature is enabled, Service Bus automatically moves any messages arriving in the source Queue or Subscription into the destination Queue or Topic. 

The forwarding implementation is transactional and prevents duplication or message loss. The destination Queue or Topic can still be sent to by other senders; 
the source Queue or Subscription can no longer be used for client receive operations.    

To forward messages from a Queue or Subscription to a destination, set the ```QueueDescription.ForwardTo``` or ```SubscriptionDescription.ForwardTo``` property to the path of the 
destination entity. 

> Note that the destination Queue/Topic must already exist at the time you create or update the source Queue/Subscription. When 
> creating the forwarding rule, the caller must have "Manage" rights on both entities. When sending messages, the sender does 
> not need any permissions on any chained destination Queues.

Auto-Forwarding is also available for dead-letter queues, by setting the ```QueueDescription.ForwardDeadLetteredMessagesTo``` or ```SubscriptionDescription.ForwardDeadLetteredMessagesTo``` properties. 

## Why would I use it?

Auto-Forwarding allows for a range of powerful routing patterns inside Service Bus.

> In the following, the term "topology" refers to the way Service Bus entities are laid
> out and related inside a Service bus namespace

### Drop Chute
Drop Chute Queues are introduced to completely decouple the send-side of a Service Bus topology
from the receive-side of the topology.

A Drop Chute Queue (D) that auto-forwards to a destination queue (Q1) 

```
       QueueDescription.ForwardTo = "Q1"

       +-----+        +-----+
       |  D  | =====> | Q1  |
       +-----+        +-----+
```     

can be easily retargeted to a different destination queue (Q2) or even a destination
topic with an update to the queue description:

```
       QueueDescription.ForwardTo = "Q2"

       +-----+        +-----+
       |  D  | =====> | Q2  |
       +-----+        +-----+
```     

The sender will not have to make any changes, at all.

A Drop Chute Queue also allows cutting off or suspending specific senders very effectively.

When messages just from the senders associated with the Drop Chute shall be rejected temporarily,
the Drop Chute Queue can be disabled (```QueueDescription.Status = EntityStatus.Disabled```) 
while the destination queue remains operative.  

If a sender only has permissions to send into the Drop Chute queue and the owner changes 
the keys for the Shared Access signature (SAS) rule the sender is using, the sender is 
instantly locked out. The Drop Chute Queue can also be deleted completely without impacting
the remainder of the topology.

### Fan-In Pattern

The Fan-In Pattern allows receiving messages from many sources and consolidating them into a single entity for retrieval. 

> For receiving event-style data from very many concurrent senders and at high throughput,
> you should **always** first consider using [Azure Event Hubs](https://azure.microsoft.com/services/event-hubs/) over a
> fan-in topology created with Service Bus Queues and Topics. Azure Event Hubs is significantly 
> better optimized for these scenarios and more cost efficient.     

Building on the previous discussion, the most obvious Fan-In scenario uses multiple Drop Chute Queues that point to the 
same destination queue:  

```
       (D1) QueueDescription.ForwardTo = "Q"
       +-----+        
       | D1  |  
       +-----+ =====> +-----+
                      |  Q  |
       +-----+ =====> +-----+
       | D2  | 
       +-----+ 
       (D2) QueueDescription.ForwardTo = "Q"
```     

This allows messages from different senders with different access control requirements (including temporary disabling of 
the send path) to be consolidated into a single queue.

> Mind that using the fan-in pattern *does not* yield an increase the overall message throughput. The throughput 
> remains gated by the capacity of the shared destination queue and by the capacity associated with the namespace, 
> which is explicitly controlled through "messaging units" in Service Bus Premium namespaces. 
 
Another use of Fan-In is to collect raw messages from different Topics, for instance for consolidation of messages
into a shared Audit Queue that captures all raw messages:

```
                      +-----+
                      | Sub | ====> Regular processing
       +-----+ =====> +------      
       | T1  | 
       +-----+ =====> +-----+ 
                      |Audit| 
                      +-----+ =====> +-----+
                                     |  Q  |  Audit Queue
                      +-----+ =====> +-----+
                      |Audit| 
       +-----+ =====> +-----+      
       | T2  | 
       +-----+ =====> +-----+
                      | Sub | ====> Regular processing
                      +-----+ 
       
```     

For Topics with a large or fairly dynamic number of subscriptions, the Fan-In pattern is very handy for consolidation
of the dead-letter queues, of which one exists for each subscription.

```
                      SubscriptionDescription.ForwardDeadLetteredMessagesTo = "Q" 
                      +-----+
                 +==> | S1  | =====> Regular processing
                 |    +---DLQ ....
                 |               :
                 |    +-----+    :
                 +==> | S2  | =====> Regular processing
       +-----+   |    +---DLQ ....
       |  T  | ==+               :
       +-----+   |    +-----+    :
                 +==> | S3  | =====> Regular processing
                 |    +---DLQ .... 
                 |               :
                 |    +-----+    :
                 +==> | S4  | =====> Regular processing
                      +---DLQ ....    
                                 :    +-----+
                                 :..> |  Q  | Consolidated DLQ
                                      +-----+
```       
  
### Partitioning 

The Partitioning pattern allows splitting one message stream into several distinct sub-streams 
before making it available for processing. An example were to split message streams by content
criteria, like distributing orders by their postal code prefix, assuming the postal code has
been promoted into a message property for performing this sort of triage. 

Typically, this is done just with Topics and Subscriptions, but if each partition requires some 
distinct sub-topology, Auto-Forward is a very helpful tool:

```
                                                  +-----+
                 {Partitioning Rule}              | S1  |
                      +-----+        +-----+ ===> +-----+ 
                 +==> | S1  | =====> |  T  |
                 |    +-----+        +-----+ ===> +-----+
                 |                                | S2  |
                 |    +-----+                     +-----+    
                 +==> | S2  | =====>    "
       +-----+   |    +-----+
       |  T  | ==+               
       +-----+   |    +-----+    
                 +==> | S3  | =====>    "
                 |    +-----+ 
                 :               
```
  
### Fan-Out 

The Fan-Out pattern is, quite apparently, the opposite of Fan-In and allows sending messages to 
larger numbers of receivers. The Partitioning pattern above is already an example of Fan-Out, 
but each subscription is constrained to a subset of the message stream.

An unconstrained Fan-Out distribution propagates all messages arriving in the source topic into 
all target topics.  

```
                                                  +-----+
                                     >a,b,c       | s21 | >a,b
                      +-----+        +-----+ ===> +-----+ 
                 +==> | s11 | =====> | T2  | 
                 |    +-----+        +-----+ ===> +-----+
                 |                                | s21 | >c
                 |    +-----+        +-----+      +-----+    
                 +==> | s12 | =====> |  Q  |
       +-----+   |    +-----+        +-----+      +-----+
a,b,c> | T1  | ==+                                | s31 | >a,b,c
       +-----+   |    +-----+        +-----+ ===> +-----+ 
                 +==> | s13 | =====> | T3  | 
                 |    +-----+        +-----+ ===> +-----+
                 :                   >a,b,c       | s32 | >a,b,c
                                                  +-----+
```   

The purpose of Fan-Out trees is to propagate information like notifications into distinct areas of 
the application. It's quite possible to span a Fan-Out tree over destination Queues or Topics 
that are used by the application for regular messages and inject special notifications into the 
message streams that are denoted by a special ```BrokeredMessage.Label``` value. 

As a single Topic is limited to 2000 subscriptions, a Fan-Out tree might also help with distributing
individual messages to an audience that is larger than 2000 subscribers. However, keep in mind that 
the limit for concurrent, active receivers on a Service Bus namespace is intentionally limited to 5000. 

> If you need to distribute notifications to very large audiences in a timely fashion, you should consider 
> using [Azure Notification Hubs](https://azure.microsoft.com/services/notification-hubs/) for mobile apps 
> or [a combination of Service Bus Topics and SignalR for distribution of the messages into web clients](http://www.asp.net/signalr/overview/performance/scaleout-with-windows-azure-service-bus)
> before creating Fan-Out trees in Service Bus.             

## Cost Implications

Auto-Forwarding is charged exactly as if a user-application were to perform the operation, 
meaning that the cost on Service Bus Standard is two message operations (receive and send); 
with Service Bus Premium there is no extra cost.

## The Sample

The sample generates 3 messages: M1, M2 and M3. M1 is sent to a source topic with one subscription, from which 
it is forwarded to a destination queue. M2 is sent to the destination queue via a transfer queue. M3 is sent to 
a source topic with two subscriptions. One subscription forwards M3 to the destination queue. The second subscription 
deadletters M3. Service Bus forwards this copy of M3 to the destination queue.

## Description

```C#
QueueDescription sourceQueueDescription = new QueueDescription(SourceQueueName); 
sourceQueueDescription.ForwardTo = DestinationQueueName; 
namespaceManager.CreateQueue(sourceQueueDescription);
```

To forward messages from a dead-letter queue, set the QueueDescription.ForwardDeadLetteredMessagesTo property 
to the path of destination queue or topic. Again, Service Bus requires the sender to attach a token that indicates 
that the sender has send permissions on the source topic. The sender does not need any permissions on the destination queue.

``` C#
QueueDescription sourceQueueDescription = new QueueDescription(SourceQueueName);  
sourceQueueDescription.ForwardDeadLetteredMessagesTo = DestinationQueue.Path;  
namespaceManager.CreateQueue(sourceQueueDescription);
```

