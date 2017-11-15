# Duplicate Detection

This sample illustrates the "duplicate detection" feature of Azure Service Bus.

The sample is specifically crafted to demonstrate the effect of duplicate
detection when enabled on a queue or topic. The default setting is for duplicate
detection to be turned off. 

For setup instructions, please refer back to the main [README](../README.md) file.

## What is duplicate detection?

Enabling duplicate detection will keep track of the ```MessageId``` of all
messages sent into a queue or topic [during a defined time window][1]. 

If any new message is sent that carries a ```MessageId``` that has already been
logged during the time window, the message will be reported as being accepted
(the send operation succeeds), but the newly sent message will be instantly
ignored and dropped. No other parts of the message are considered.

[Read more about duplicate detection in the documentation.][2]

## Sample Code 

The sample sends two messages that have the same ```MessageId``` and shows that
only one of those messages is being enqueued and retrievable, if the queue has
the duplicate-detection flag set. 

The setup template creates the queue for this example by setting the
```requiresDuplicateDetection``` flag, which enables the feature, and it sets
the ```duplicateDetectionHistoryTimeWindow``` to 10 minutes.


``` JSON
{
    "apiVersion": "[variables('apiVersion')]",
    "name": "DupdetectQueue",
    "type": "queues",
    "dependsOn": [
        "[concat('Microsoft.ServiceBus/namespaces/', 
            parameters('serviceBusNamespaceName'))]",
    ],
    "properties": {
	   "requiresDuplicateDetection": true,
       "duplicateDetectionHistoryTimeWindow" :  "T10M"
    },
    "resources": []
},
```

The sample is further documented inline in the [DuplicateDetection.java](.\src\main\java\com\microsoft\azure\servicebus\samples\duplicatedetection\DuplicateDetection.java) file.

[1]: https://docs.microsoft.com/azure/service-bus-messaging/duplicate-detection#enable-duplicate-detection
[2]: https://docs.microsoft.com/azure/service-bus-messaging/duplicate-detection