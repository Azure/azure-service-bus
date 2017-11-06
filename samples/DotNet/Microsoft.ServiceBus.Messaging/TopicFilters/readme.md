# Topic Subscription Filters

This sample illustrates creating filtered subscriptions for topics. It shows a
simple *true-filter* that lets all messages pass, a filter with a composite
SQL-like condition, a rule combining a filter with a set of actions, and a
correlation filter condition.  

[Read more about topic filters in the documentation.](https://docs.microsoft.com/azure/service-bus-messaging/topic-filters)

Refer to the main [README](../README.md) document for setup instructions.

## Sample Code 

The sample is documented inline in the [Program.cs](Program.cs) C# file.

The setup template creates the topic for this example with 4 subscriptions. 

``` JSON
{
    "apiVersion": "[variables('apiVersion')]",
    "name": "TopicFilterSampleTopic",
    "type": "topics",
    "dependsOn": [
    "[concat('Microsoft.ServiceBus/namespaces/', 
           parameters('serviceBusNamespaceName'))]",
    ],
    "properties": {},
    "resources": [
    

```

The first subscription selects all messages with a "true" filter, which is a SQL
filter with an expression that is trivially true:

``` JSON
 {
    "apiVersion": "[variables('apiVersion')]",
    "name": "AllOrders",
    "type": "subscriptions",
    "dependsOn": [ "TopicFilterSampleTopic" ],
    "properties": {},
    "resources": [
      {
         "apiVersion": "[variables('apiVersion')]",
         "name": "rule1",
         "type": "rules",
         "dependsOn": [ "AllOrders" ],
         "properties": {
            "filterType": "SqlFilter",
            "sqlFilter": {
               "sqlExpression": "1=1",
               "requiresPreprocessing": false
            }
         }
      }
     ]
   },
```

The second subscription selects all messages with a SQL filter with the user
property 'color' having the value 'blue' and the 'quantity' user property having
the value 10. 

``` JSON
    {
        "apiVersion": "[variables('apiVersion')]",
        "name": "ColorBlueSize10Orders",
        "type": "subscriptions",
        "dependsOn": [ "TopicFilterSampleTopic" ],
        "properties": {},
        "resources": [
         {
            "apiVersion": "[variables('apiVersion')]",
            "name": "rule1",
            "type": "rules",
            "dependsOn": [ "ColorBlueSize10Orders" ],
            "properties": {
            "filterType": "SqlFilter",
               "sqlFilter": {
                  "sqlExpression": "color = 'blue' AND quantity = 10",
                  "requiresPreprocessing": false
               }
            }
         }
        ]
    },
```

The third subscription selects all messages with a SQL filter with the user
property 'color' having the value 'red'. 

``` JSON

    {
        "apiVersion": "[variables('apiVersion')]",
        "name": "ColorRed",
        "type": "subscriptions",
        "dependsOn": [ "TopicFilterSampleTopic" ],
        "properties": {
        },
        "resources": [
         {
            "apiVersion": "[variables('apiVersion')]",
            "name": "rule1",
            "type": "rules",
            "dependsOn": [ "ColorRed" ],
            "properties": {
            "filterType": "SqlFilter",
               "sqlFilter": {
                  "sqlExpression": "color = 'red'",
                  "requiresPreprocessing": false
               },
               "action": {
                  "sqlExpression": "SET quantity = quantity / 2; 
                                    REMOVE priority; 
                                    SET sys.CorrelationId = 'low';"
               }
            }
         }
        ]
    },
```

The forth subscription selects all messages using a correlation filter with the
```Label``` property having the value 'red' and the ```CorrelationId``` property
having the value 'high' 

``` JSON

    {
        "apiVersion": "[variables('apiVersion')]",
        "name": "HighPriorityOrders",
        "type": "subscriptions",
        "dependsOn": [ "TopicFilterSampleTopic" ],
        "properties": {
        },
        "resources": [
        {
            "apiVersion": "[variables('apiVersion')]",
            "name": "rule1",
            "type": "rules",
            "dependsOn": [ "HighPriorityOrders" ],
            "properties": {
            "filterType": "CorrelationFilter",
            "correlationFilter": {
                "label": "red",
                "correlationId": "high"
            }
            }
        }
        ]
    }
    ]
}
```


