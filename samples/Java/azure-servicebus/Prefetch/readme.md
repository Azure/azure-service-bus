# Prefetch
This sample illustrates the the "prefetch" feature of the Service Bus client.

The sample is specifically crafted to demonstrate the throughput difference
between receiving messages with prefetch turned on and prefetch turned off. The
default setting is for prefetch to be turned off. 

Refer to the main [README](../README.md) document for setup instructions.

[Read more about the prefetch feature in the documentation.][1]

## Sample Code 

The sample performs two send and receive sequences, once with prefetch turned on
and once with prefetch turned off. You will observe that the variant with
prefetch turned on yields higher throughput, and therefore a shorter execution
time. 

The sample is further documented inline in the [Prefetch.java](.\src\main\java\com\microsoft\azure\servicebus\samples\prefetch\Prefetch.java) file.

[1]: https://docs.microsoft.com/azure/service-bus-messaging/service-bus-prefetch