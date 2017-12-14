//   
//   Copyright © Microsoft Corporation, All Rights Reserved
// 
//   Licensed under the Apache License, Version 2.0 (the "License"); 
//   you may not use this file except in compliance with the License. 
//   You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0 
// 
//   THIS CODE IS PROVIDED ON AN *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
//   OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION
//   ANY IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A
//   PARTICULAR PURPOSE, MERCHANTABILITY OR NON-INFRINGEMENT.
// 
//   See the Apache License, Version 2.0 for the specific language
//   governing permissions and limitations under the License. 

namespace AtomicTransactions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;
    using Newtonsoft.Json;

    public class Program : MessagingSamples.Sample
    {
        const string SagaQueuePathPrefix = "sagas/1";
        const string BookRentalCarQueueName = SagaQueuePathPrefix + "/Ta";
        const string CancelRentalCarQueueName = SagaQueuePathPrefix + "/Ca";
        const string BookHotelQueueName = SagaQueuePathPrefix + "/Tb";
        const string CancelHotelQueueName = SagaQueuePathPrefix + "/Cb";
        const string BookFlightQueueName = SagaQueuePathPrefix + "/Tc";
        const string CancelFlightQueueName = SagaQueuePathPrefix + "/Cc";
        const string SagaResultQueueName = SagaQueuePathPrefix + "/result";
        const string SagaInputQueueName = SagaQueuePathPrefix + "/input";
        static int pendingTransactions;

        public async Task Run(Dictionary<string, string> settings)
        {
            // we're going to create a topology for sagas of sequential transactions in this 
            // sample. For each transactional saga step we will have a dedicated input queue. 

            // The saga's sequence is as follows
            //
            //  [Start] --> [ Book Rental Car ] --> [ Book Hotel ] --> [ Book Flight ] --+
            //                      |                     |                   |          |
            //                    Error                 Error               Error        |
            //                      |                     |                   |          |
            //                      V                     V                   V          |
            //          +-- [Cancel Rental Car] <-- [Cancel Hotel] <-- [Cancel Flight]   |
            //          |                                                                |
            //          +-------->---------------------+    +--------------<-------------+
            //                                         V    V
            //                                        [Result]    
            //
            // The error path is set up via the deadletter queues of the booking steps, 
            // which, in turn, auto-forward to the respective cancellation steps. This is 
            // wired up as the queues are created in SetupTopologyAsync . 
            // The remaining paths are set up as we initialize the Saga work and conpensation 
            // tasks in RunSaga.

            var namespaceManager = NamespaceManager.CreateFromConnectionString(settings["SB_SAMPLES_MANAGE_CONNECTIONSTRING"]);
            
            var queues = await this.SetupSagaTopologyAsync(namespaceManager);
            await RunScenarioAsync(settings["SB_SAMPLES_CONNECTIONSTRING"]);
            await this.CleanupSagaTopologyAsync(namespaceManager, queues);
        }

        static async Task RunScenarioAsync(string connectionString)
        {
            var workersMessagingFactory =  MessagingFactory.CreateFromConnectionString(connectionString);
            var receiverMessagingFactory = MessagingFactory.CreateFromConnectionString(connectionString);
            var senderMessagingFactory = MessagingFactory.CreateFromConnectionString(connectionString);

            var resultsReceiver = await RunResultsReceiver(receiverMessagingFactory);

            var sagaTerminator = new CancellationTokenSource();
            var saga = RunSaga(workersMessagingFactory, sagaTerminator);

            await SendBookingRequests(senderMessagingFactory);

            await Task.WhenAny(
                Task.Run(() => Console.ReadKey()),
                Task.Delay(TimeSpan.FromSeconds(10))
            );

            sagaTerminator.Cancel();
            await saga.Task;

            resultsReceiver.Close();
            senderMessagingFactory.Close();
            receiverMessagingFactory.Close();
            workersMessagingFactory.Close();
        }

        static async Task<MessageReceiver> RunResultsReceiver(MessagingFactory receiverMessagingFactory)
        {
            // this receiver reads from the results queue and prints out the message
            var receiver = await receiverMessagingFactory.CreateMessageReceiverAsync(SagaResultQueueName);
            receiver.OnMessage(PrintResultMessage, new OnMessageOptions {AutoComplete = true});
            return receiver;
        }

        static async Task SendBookingRequests(MessagingFactory senderMessagingFactory)
        {
            // and now we'll send some booking requests
            dynamic bookingRequests = new dynamic[]
            {
                new
                {
                    flight = new
                    {
                        bookingClass = "C",
                        legs = new[]
                        {
                            new {flightNo = "XB937", from = "DUS", to = "LHR", date = "2017-08-01"},
                            new {flightNo = "XB49", from = "LHR", to = "SEA", date = "2017-08-01"},
                            new {flightNo = "XB48", from = "SEA", to = "LHR", date = "2017-08-10"},
                            new {flightNo = "XB940", from = "LHR", to = "DUS", date = "2017-08-11"}
                        }
                    },
                    hotel = new {name = "Hopeman", city = "Kirkland", state = "WA", checkin = "2017-08-01", checkout = "2017-08-10"},
                    car = new {vendor = "Hervis", airport = "SEA", from = "2017-08-01T17:00", until = "2017-08-10:17:00"}
                },
                new
                {
                    flight = new
                    {
                        bookingClass = "C",
                        legs = new[]
                        {
                            new {flightNo = "XL75", from = "DUS", to = "FRA", date = "2017-08-01"},
                            new {flightNo = "XL490", from = "FRA", to = "SEA", date = "2017-08-01"},
                            new {flightNo = "XL491", from = "SEA", to = "FRA", date = "2017-08-10"},
                            new {flightNo = "XL78", from = "FRA", to = "DUS", date = "2017-08-11"}
                        }
                    },
                    hotel = new {name = "Eastin", city = "Bellevue", state = "WA", checkin = "2017-08-01", checkout = "2017-08-10"},
                    car = new {vendor = "Avional", airport = "SEA", from = "2017-08-01T17:00", until = "2017-08-10:17:00"}
                },
                new
                {
                    hotel = new {name = "Eastin", city = "Bellevue", state = "WA", checkin = "2017-08-01", checkout = "2017-08-10"}
                },
                new
                {
                    flight = new
                    {
                        bookingClass = "Y",
                        legs = new[]
                        {
                            new {flightNo = "XL75", from = "DUS", to = "FRA", date = "2017-08-01"},
                            new {flightNo = "XL78", from = "FRA", to = "DUS", date = "2017-08-11"}
                        }
                    }
                },
                new
                {
                    car = new {vendor = "Hext", airport = "DUS", from = "2017-08-01T17:00", until = "2017-08-10:17:00"}
                }
            };

            Console.WriteLine("Sending requests");
            var sender = await senderMessagingFactory.CreateMessageSenderAsync(SagaInputQueueName);
            for (int j = 0; j < 5; j++)
            {
                for (int i = 0; i < bookingRequests.Length; i++)
                {
                    var message = new BrokeredMessage(new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(bookingRequests[i]))))
                    {
                        ContentType = "application/json",
                        Label = "TravelBooking",
                        TimeToLive = TimeSpan.FromMinutes(15)
                    };

                    await sender.SendAsync(message);
                    Interlocked.Increment(ref pendingTransactions);
                }
            }
        }

        static SagaTaskManager RunSaga(MessagingFactory workersMessageFactory, CancellationTokenSource terminator)
        {
            var saga = new SagaTaskManager(workersMessageFactory, terminator.Token)
            {
                {BookRentalCarQueueName, TravelBookingHandlers.BookRentalCar, BookHotelQueueName, CancelRentalCarQueueName},
                {CancelRentalCarQueueName, TravelBookingHandlers.CancelRentalCar, SagaResultQueueName, string.Empty},
                {BookHotelQueueName, TravelBookingHandlers.BookHotel, BookFlightQueueName, CancelHotelQueueName},
                {CancelHotelQueueName, TravelBookingHandlers.CancelHotel, CancelRentalCarQueueName, string.Empty},
                {BookFlightQueueName, TravelBookingHandlers.BookFlight, SagaResultQueueName, CancelFlightQueueName},
                {CancelFlightQueueName, TravelBookingHandlers.CancelFlight, CancelHotelQueueName, string.Empty}
            };
            return saga;
        }

        async Task<IEnumerable<QueueDescription>> SetupSagaTopologyAsync(NamespaceManager nm)
        {
            Console.WriteLine("Setup");
            return new List<QueueDescription>
            {
                await nm.QueueExistsAsync(SagaResultQueueName)
                    ? await nm.GetQueueAsync(SagaResultQueueName)
                    : await nm.CreateQueueAsync(SagaResultQueueName),
                await nm.QueueExistsAsync(CancelFlightQueueName)
                    ? await nm.GetQueueAsync(CancelFlightQueueName)
                    : await nm.CreateQueueAsync(new QueueDescription(CancelFlightQueueName)),
                await nm.QueueExistsAsync(BookFlightQueueName)
                    ? await nm.GetQueueAsync(BookFlightQueueName)
                    : await nm.CreateQueueAsync(
                        new QueueDescription(BookFlightQueueName)
                        {
                            // on failure, we move deadletter messages off to the flight 
                            // booking compensator's queue
                            EnableDeadLetteringOnMessageExpiration = true,
                            ForwardDeadLetteredMessagesTo = CancelFlightQueueName
                        }),
                await nm.QueueExistsAsync(CancelHotelQueueName)
                    ? await nm.GetQueueAsync(CancelHotelQueueName)
                    : await nm.CreateQueueAsync(new QueueDescription(CancelHotelQueueName)),
                await nm.QueueExistsAsync(BookHotelQueueName)
                    ? await nm.GetQueueAsync(BookHotelQueueName)
                    : await nm.CreateQueueAsync(
                        new QueueDescription(BookHotelQueueName)
                        {
                            // on failure, we move deadletter messages off to the hotel 
                            // booking compensator's queue
                            EnableDeadLetteringOnMessageExpiration = true,
                            ForwardDeadLetteredMessagesTo = CancelHotelQueueName
                        }),
                await nm.QueueExistsAsync(CancelRentalCarQueueName)
                    ? await nm.GetQueueAsync(CancelRentalCarQueueName)
                    : await nm.CreateQueueAsync(new QueueDescription(CancelRentalCarQueueName)),
                await nm.QueueExistsAsync(BookRentalCarQueueName)
                    ? await nm.GetQueueAsync(BookRentalCarQueueName)
                    : await nm.CreateQueueAsync(
                        new QueueDescription(BookRentalCarQueueName)
                        {
                            // on failure, we move deadletter messages off to the car rental 
                            // compensator's queue
                            EnableDeadLetteringOnMessageExpiration = true,
                            ForwardDeadLetteredMessagesTo = CancelRentalCarQueueName
                        }),
                await nm.QueueExistsAsync(SagaInputQueueName)
                    ? await nm.GetQueueAsync(SagaInputQueueName)
                    : await nm.CreateQueueAsync(
                        new QueueDescription(SagaInputQueueName)
                        {
                            // book car is the first step
                            ForwardTo = BookRentalCarQueueName
                        })
            };
        }

        async Task CleanupSagaTopologyAsync(NamespaceManager namespaceManager, IEnumerable<QueueDescription> queues)
        {
            Console.WriteLine("Cleanup");
            foreach (var queueDescription in queues.Reverse())
            {
                await namespaceManager.DeleteQueueAsync(queueDescription.Path);
            }
        }

        static void PrintResultMessage(BrokeredMessage m)
        {
            lock (Console.Out)
            {
                Console.ForegroundColor = m.Properties.ContainsKey("TransactionError") || m.Properties.ContainsKey("DeadLetterReason")
                    ? ConsoleColor.Magenta
                    : ConsoleColor.Yellow;
                foreach (var prop in m.Properties)
                {
                    Console.WriteLine("{0}={1},", prop.Key, prop.Value);
                }
                Console.WriteLine(
                    "{0}\nPending: {1}",
                    new StreamReader(m.GetBody<Stream>(), true).ReadToEnd(),
                    Interlocked.Decrement(ref pendingTransactions));
                Console.ResetColor();
            }
        }

        public static int Main(string[] args)
        {
            try
            {
                var app = new Program();
                app.RunSample(args, app.Run);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return 1;
            }
            return 0;
        }
    }
}