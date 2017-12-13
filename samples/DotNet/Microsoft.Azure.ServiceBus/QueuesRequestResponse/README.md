# Request/Response Pattern with Queues

The request/response message exchange pattern is very common, and with many protocols, including the 
dominant HTTP protocol, it is the only supported pattern: The client sends a request, and the server 
replies with a response. 

This sample shows how to implement the request/response pattern over a pair of Service Bus Queues.

## Considerations

We assume that the requesting party and the responding party are not part of the same application 
and may indeed use request and reply queues on different Service Bus namespaces in different 
datacenters. That assumption influences how we will deal with access control.

We will also assume that the client expects a somewhat timely response within its current application 
instance lifetime. "Timely" may mean under a second, but it may also mean 15 minutes. The point of 
running a request/response pattern over queues is commonly that the transfers need to be reliable, 
and that the work required to satisfy the request is non-trivial.  

To keep the sample complexity manageable, we will not persist the information about pending requests;
responses that arrive back at the requesting party and that cannot be matched to requests of the 
current application instance will simply be dead-lettered. 

## Modeling Request/Response

Service Bus Queues are one-way communication entities that route messages from a sender to a receiver
via the Service Bus message broker. To create a feedback path from receiver back to the sender, we 
must therefore use a separate queue. 

[TBD...]


   
  

    