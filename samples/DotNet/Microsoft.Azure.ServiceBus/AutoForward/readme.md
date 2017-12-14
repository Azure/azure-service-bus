# Auto-Forward

This sample demonstrates how to automatically forward messages from a queue,
subscription, or deadletter queue into another queue or topic. 

Refer to the main [README](../README.md) document for setup instructions. 

## What is Auto Forwarding?

The Auto-Forwarding feature enables you to chain the a Topic Subscription or a
Queue to destination Queue or Topic that is part of the same Service Bus
namespace. When the feature is enabled, Service Bus automatically moves any
messages arriving in the source Queue or Subscription into the destination Queue
or Topic. 

Auto-Forwarding allows for a range of powerful routing patterns inside Service
Bus, including decoupling of send and receive locations, fan-in, fan-out, and
application-defined partitioning.  

[Read more about auto-forwarding in the documentation.][1]

## Sample code

The sample generates 2 messages: M1, and M2. M1 is sent to a source topic
with one subscription, from which it is forwarded to a destination queue. M2 is
sent to the destination queue directly. 

The setup template creates the topology for this example as shown here. Note
that the topic whose subscription auto-forwards into the target queue is made
dependent on the target queue, so that the queue is created first. The
connection between the two entitries is made with the ```forwardTo```property of
the subscription pointing to the target queue. 

``` JSON
{
   "apiVersion": "[variables('apiVersion')]",
   "name": "AutoForwardSourceTopic",
   "type": "topics",
   "dependsOn": [
     "[concat('Microsoft.ServiceBus/namespaces/', 
            parameters('serviceBusNamespaceName'))]",
     "AutoForwardTargetQueue",
   ],
   "properties": {},
   "resources": [
     {
       "apiVersion": "[variables('apiVersion')]",
       "name": "Forwarder",
       "type": "subscriptions",
       "dependsOn": [ "AutoForwardSourceTopic" ],
       "properties": {
         "forwardTo": "AutoForwardTargetQueue"
       },
       "resources": []
     }
   ]
},
{
   "apiVersion": "[variables('apiVersion')]",
   "name": "AutoForwardTargetQueue",
   "type": "queues",
   "dependsOn": [
     "[concat('Microsoft.ServiceBus/namespaces/', 
            parameters('serviceBusNamespaceName'))]",
   ],
   "properties": {},
   "resources": []
        }
      ]
    }
```


The sample is documented further inline in the [Program.cs](Program.cs) C# file.

[1]: https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-auto-forwarding