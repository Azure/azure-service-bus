1. This sample required that you add your Service Bus connection string to 
   the "Microsoft.ServiceBus.ConnectionString" Azure configuration setting.
2. The sample will automatically create a Service Bus queue called ProcessingQueue 
   using the connection string you supplied. If the queue already existed the sample
   will reuse the queue.
3. Make sure Microsoft.ServiceBus.MessagingPerformanceCounters.man and RegisterMessagingPerfCounter.cmd
   is copied as part of deployment by setting 
   - Build Action = Content
   - Copy To Output Directory = Copy if Newer
4. If you intend to run this code as an actual Azure deployment, please make sure
   you also setup a storage account instead of using UseDevelopmentStorage=true.
5. Once your application is deployed and running the Diagnostics monitor will begin collecting 
   performance counters and persisting that data to Azure storage. You can use tools such as 
   Server Explorer in Visual Studio,  Azure Storage Explorer, or Azure Diagnostics Manager by 
   Cerebrata to view the performance counters data in the WADPerformanceCountersTable table. 
   You can also programatically query the Table service to get the data as well. 
   For more information please refer to http://msdn.microsoft.com/en-us/library/azure/dn535595.aspx