# AMQP transactions and send Via sample.

This sample illustrates the use of AMQP transactions and AMQP transactions and send Via. It shows you how to run a regular transaction, a transaction that rolls back, or a transaction spanning two entities using send via.

The documentation can be found here: 
* [https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-transactions](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-transactions)
* [https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-amqp-protocol-guide](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-amqp-protocol-guide) 

To run the sample do the following steps:
1. Open a Power Shell Admin console and navigate to the "TransactionsAndSendVia" folder which contains the "TransactionsAndSendVia.sln" file.
2. Type dotnet build.
3. Navigate to the TransactionsAndSendVia\bin\debug\netcoreapp2.0 folder.
4. You can now run any of the below three combinations:

**Note:** To examine the first sample in full run it two times.

* For a regular transaction type: dotnet .\TransactionsAndSendVia.dll -ConnectionString "Your Service Bus Connection String." -QueueName "Your Queue Name" -Ex "false"
* For a transaction hitting an exception type: dotnet .\TransactionsAndSendVia.dll -ConnectionString "Your Service Bus Connection String." -QueueName "Your Queue Name" -Ex "true"
* For a transaction using send via to span multiple entities type: dotnet .\TransactionsAndSendVia.dll -ConnectionString "Your Service Bus Connection String." -QueueName "Your Queue Name" -SendViaQueue "Your send via Queue Name" -Ex "false"

To get an indepth understanding also read through the comments in the code.


