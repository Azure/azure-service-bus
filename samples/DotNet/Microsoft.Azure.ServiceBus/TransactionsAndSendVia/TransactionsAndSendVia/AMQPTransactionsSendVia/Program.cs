using System;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;

namespace TransactionsAndSendVia
{
    class Program
    {
        static void Main(string[] args)
        {
            string ServiceBusConnectionString = "";
            string QueueName = "";
            string SendViaQueueName = "";
            string Ex = "";

            for (int i = 0; i < args.Length; i++)
            {                
                if (args[i] == "-ConnectionString")
                {
                    Console.WriteLine($"ConnectionString: {args[i + 1]}");
                    ServiceBusConnectionString = args[i + 1]; // Alternatively enter your connection string here.
                }
                else if (args[i] == "-QueueName")
                {
                    Console.WriteLine($"Queue Name: {args[i + 1]}");
                    QueueName = args[i + 1]; // Alternatively enter your queue name here.
                }
                else if (args[i] == "-SendViaQueue")
                {
                    Console.WriteLine($"Send Via Queue Name: {args[i + 1]}");
                    SendViaQueueName = args[i + 1]; // Alternatively enter your queue name here.
                }
                else if (args[i] == "-Ex")
                {
                    Console.WriteLine($"Exception true / false: {args[i + 1]}");
                    Ex = args[i + 1]; // Alternatively enter your queue name here.
                }
            }

            var myProgram = new Program();

            if (ServiceBusConnectionString != "" && QueueName != "" && SendViaQueueName == "" && Ex == "false")
            {
                myProgram.TransactionSample(ServiceBusConnectionString, QueueName).GetAwaiter().GetResult();
            }
            else if (ServiceBusConnectionString != "" && QueueName != "" && Ex == "true")
            {
                myProgram.TransactionError(ServiceBusConnectionString, QueueName, Ex).GetAwaiter().GetResult();
            }
            else if(ServiceBusConnectionString != "" && QueueName != "" && SendViaQueueName != "")
            {
                myProgram.TransactionSendViaSample(ServiceBusConnectionString, QueueName, SendViaQueueName).GetAwaiter().GetResult();
            }
            else
            {
                Console.WriteLine("Specify -Connectionstring and -QueueName to execute the example.");
                Console.WriteLine("If you want to execute the Send Via sample add the parameter -SendViaQueue.");
                Console.WriteLine("If you want to simulate an exception set -ex to \"true\".");
                Console.ReadKey();
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
        }

        // This sample shows a successful transaction.
        private async Task TransactionSample(string SBCS, string QueueName)
        {
            // Note: A transaction cannot span more than one connection, hence you need to create your connection object
            // always before and then pass it to sender and receiver.
            var connection = new ServiceBusConnection(SBCS);
            var sender = new MessageSender(connection, QueueName);
            var receiver = new MessageReceiver(connection, QueueName);
            // Receive not part of transaction. Only operations which actually do something with the message on the broker are part of the transaction.
            // These are: Send, Complete, Deadletter, Defer. Receive itself already utilizes the peeklock concept on the broker.
            // Note the receive timeout of 2 seconds is just for demo purposes to not let the user wait in case no message is there.
            // Run twice to see the sample in full.

            var receivedMessage = await receiver.ReceiveAsync(TimeSpan.FromSeconds(5));
            if(receivedMessage != null)
            {
                var msg = receivedMessage.DeserializeMsg<MyMessage>();
                Console.WriteLine($"MessageId: {receivedMessage.MessageId} \n Name: {msg.Name} \n Address: {msg.Address} \n ZipCode {msg.ZipCode}");
            }
            else
                Console.WriteLine($"No message received.");


            using (var ts = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            {                
                try
                {
                    if (receivedMessage != null)
                        await receiver.CompleteAsync(receivedMessage.SystemProperties.LockToken);

                    var myMsgBody = new MyMessage
                    {
                        Name = "Some name",
                        Address = "Some street address",
                        ZipCode = "Some zip code"
                    };

                    var message = myMsgBody.AsMessage();
                    await sender.SendAsync(message).ConfigureAwait(false);
                    Console.WriteLine("Message has been sent");
                    
                    ts.Complete();
                }
                catch(Exception ex)
                {
                    // This rolls back send and complete in case an exception happens
                    ts.Dispose();
                    Console.WriteLine(ex.ToString());
                }                                
            }

            await sender.CloseAsync();
            await receiver.CloseAsync();            
        }

        // This shows an unsuccsessful transaction. No message is actually send nor received properly, meaning complete is being rolled back. 
        // Meaning If you executed the first sample first once or twice you will always get the same message again in this sample.
        private async Task TransactionError(string SBCS, string QueueName,string Error)
        {
            var connection = new ServiceBusConnection(SBCS);
            var sender = new MessageSender(connection, QueueName);
            var receiver = new MessageReceiver(connection, QueueName);
            // Receive not part of transaction. Only operations which actually do something with the message on the broker are part of the transaction.
            // These are: Send, Complete, Deadletter, Defer. Receive itself already utilizes the peeklock concept on the broker.
            // Note the receive timeout of 2 seconds is just for demo purposes to not let the user wait in case no message is there.
            var receivedMessage = await receiver.ReceiveAsync();
            if (receivedMessage != null)
            {
                var msg = receivedMessage.DeserializeMsg<MyMessage>();
                Console.WriteLine($"MessageId: {receivedMessage.MessageId} \n Name: {msg.Name} \n Address: {msg.Address} \n ZipCode {msg.ZipCode}");
            }
            else
                Console.WriteLine($"No message received.");

            using (var ts = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            {
                try
                {
                    if (receivedMessage != null)
                        await receiver.CompleteAsync(receivedMessage.SystemProperties.LockToken);

                    var myMsgBody = new MyMessage
                    {
                        Name = "Some name",
                        Address = "Some street address",
                        ZipCode = "Some zip code"
                    };

                    var message = myMsgBody.AsMessage();
                    await sender.SendAsync(message).ConfigureAwait(false);
                    Console.WriteLine("Message has been sent");

                    // This is just to show an error case
                    throw new Exception(Error);

                    // this code is never reached in this example
                    // ts.Complete();
                }
                catch (Exception ex)
                {
                    // This rolls back send and complete in case an exception happens
                    ts.Dispose();
                    Console.WriteLine(ex.ToString());
                }
            }

            await sender.CloseAsync();
            await receiver.CloseAsync();
        }

        // This scenario handles if you want to have a transaction spanning multiple entities.
        // Note: We send two messages here to show that we send to the viaQueue and to a seperate
        // queue and handle operations on both within the same transaction block. Hence we are sending to the
        // destination queue via the viaQueue.
        private async Task TransactionSendViaSample(string SBCS, string viaQueueName, string destinationQueueName)
        {
            var connection = new ServiceBusConnection(SBCS);
            var viaQueueSender = new MessageSender(connection, viaQueueName);
            var viaQueueReceiver = new MessageReceiver(connection, viaQueueName);            
            var destinationViaSender = new MessageSender(connection, destinationQueueName, viaEntityPath: viaQueueName);

            var myMsgBody = new MyMessage
            {
                Name = "Some name",
                Address = "Some street address",
                ZipCode = "Some zip code"
            };

            var msgBody = myMsgBody.AsBody();

            // Note: If you use a partitioned queue to send or receive messages via a transaction you will need to specify the correct partition key. 
            // Those partition keys are always ignored in case you send to a non partitioned queue.
            var message1 = new Message(msgBody) { MessageId = "1", PartitionKey = "pk1" };
            var message2 = new Message(msgBody) { MessageId = "2", PartitionKey = "pk2", ViaPartitionKey = "pk1" };

            await viaQueueSender.SendAsync(message1);
            var receivedMessage = await viaQueueReceiver.ReceiveAsync();
            var msg = receivedMessage.DeserializeMsg<MyMessage>();
            Console.WriteLine($"MessageId: {receivedMessage.MessageId} \n Name: {msg.Name} \n Address: {msg.Address} \n ZipCode {msg.ZipCode}");

            using (var ts = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            {
                try
                {
                    await viaQueueReceiver.CompleteAsync(receivedMessage.SystemProperties.LockToken);
                    await destinationViaSender.SendAsync(message2);

                    ts.Complete();
                }
                catch(Exception ex)
                {
                    // This rolls back send and complete in case an exception happens
                    ts.Dispose();
                    Console.WriteLine(ex.ToString());
                }  
            }

            Console.WriteLine("Second Message has been send to via queue...");
        }
    }
}
