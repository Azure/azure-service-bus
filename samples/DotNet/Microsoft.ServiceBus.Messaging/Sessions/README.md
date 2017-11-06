# Sessions

This sample illustrates the Session handling feature of Azure Service Bus. 

Service Bus sessions are unbounded sequences of related messages that allows for
ordered delivery and multiplexing.

[Read more about sessions in the documentation][1]

Refer to the main [README](../README.md) document for setup instructions. 

## Sample Code 

This sample shows how to prevent out-of-order delivery using sessions, given
that the messages originate from the same sender, and how to de-multiplex
interleaved message streams. 

We initiate an interleaved send of four independent message sequences, each
labeled with a unique ```SessionId```. The interleaved send causes the messages
from those four sequences to show up interleaved representing the actual send
order.

The message "processing" is very similar to most other samples; we inspect the
message for whether it meets our expectations and then proceed to handle the
payload. 

The only special case is when we reach the end of the expected sequence. When
the fifth job step arrives, we close the session object, which indicates that
the session handler is done with this session and does not expect any further
messages. 

The sample is documented inline in the [Program.cs](Program.cs) C# file.

[1]: https://docs.microsoft.com/azure/service-bus-messaging/message-sessions