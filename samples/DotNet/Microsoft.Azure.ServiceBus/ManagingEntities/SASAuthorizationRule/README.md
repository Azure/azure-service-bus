# Managing Azure Service Bus Queues

This sample guides you through SharedAccessSignature(SAS) authentication support provided by the library
to customize authorization to your Service Bus entity.

You will learn
1. how to generate a new SAS policy for a topic which a specific type of claim. (send-only/receive-only).
1. how to generate a SAS token based on an existing SAS policy which only applies to a specific subscription.

Refer to the main [README](../../README.md) document for setup instructions. 

## Sample Code 

The sample is documented inline in the [Program.cs](Program.cs) C# file.

The sample creates a Topic with 2 subscriptions.
We then generate a send-only SAS policy and another receive-only SAS policy.
We then validate
1. send-only SAS could be used to send but not to receive.
2. receive-only SAS could be used to receive but not to send.
3. SAS token generated for subscription1 throws when being used for subscription2.

