//   
//   Copyright � Microsoft Corporation, All Rights Reserved
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
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using System.Transactions;
    using Microsoft.ServiceBus.Messaging;
    using Newtonsoft.Json;

    static class TravelBookingHandlers
    {
        const string ContentTypeApplicationJson = "application/json";
        const string TravelBookingLabel = "TravelBooking";

        public static async Task BookFlight(BrokeredMessage message, MessageSender nextStepQueue, MessageSender compensatorQueue)
        {
            try
            {
                using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
                {
                    var via = (message.Properties.ContainsKey("Via")
                        ? ((string) message.Properties["Via"] + ",")
                        : string.Empty) +
                              "bookflight";

                    if (message.Label != null &&
                        message.ContentType != null &&
                        message.Label.Equals(TravelBookingLabel, StringComparison.InvariantCultureIgnoreCase) &&
                        message.ContentType.Equals(ContentTypeApplicationJson, StringComparison.InvariantCultureIgnoreCase))
                    {
                        var body = message.GetBody<Stream>();
                        dynamic travelBooking = DeserializeTravelBooking(body);


                        // do we want to book a flight? No? Let's just forward the message to
                        // the next destination via transfer queue
                        if (travelBooking.flight == null)
                        {
                            await nextStepQueue.SendAsync(CreateForwardMessage(message, travelBooking, via));
                            // done with this job
                            await message.CompleteAsync();
                        }
                        else
                        {
                            lock (Console.Out)
                            {
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine("Booking Flight");
                                Console.ResetColor();
                            }

                            // now we're going to simulate the work of booking a flight,
                            // which usually involves a call to a third party

                            // every 9th flight booking sadly goes wrong
                            if (message.SequenceNumber%9 == 0)
                            {
                                await message.DeadLetterAsync(
                                    new Dictionary<string, object>
                                    {
                                        {"DeadLetterReason", "TransactionError"},
                                        {"DeadLetterErrorDescription", "Failed to perform flight reservation"},
                                        {"Via", via}
                                    });
                            }
                            else
                            {
                                // every operation executed in the first 3 secs of any minute 
                                // tanks completely (simulates some local or external unexpected issue) 
                                if (DateTime.UtcNow.Second <= 3)
                                {
                                    throw new Exception("O_o");
                                }

                                // let's pretend we booked something
                                travelBooking.flight.reservationId = "A1B2C3";

                                await nextStepQueue.SendAsync(CreateForwardMessage(message, travelBooking, via));
                                // done with this job
                                await message.CompleteAsync();
                            }
                        }
                    }
                    else
                    {
                        await message.DeadLetterAsync(
                           new Dictionary<string, object>
                                    {
                                        {"DeadLetterReason", "BadMessage"},
                                        {"DeadLetterErrorDescription", "Unrecognized input message"},
                                        {"Via", via}
                                    });
                    }
                    scope.Complete();
                }
            }
            catch (Exception e)
            {
                Trace.TraceError(e.ToString());
                await message.AbandonAsync();
            }
        }

        public static async Task BookHotel(BrokeredMessage message, MessageSender nextStepQueue, MessageSender compensatorQueue)
        {
            try
            {
                var via = (message.Properties.ContainsKey("Via")
                    ? ((string) message.Properties["Via"] + ",")
                    : string.Empty) +
                          "bookhotel";

                using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
                {
                    if (message.Label != null &&
                        message.ContentType != null &&
                        message.Label.Equals(TravelBookingLabel, StringComparison.InvariantCultureIgnoreCase) &&
                        message.ContentType.Equals(ContentTypeApplicationJson, StringComparison.InvariantCultureIgnoreCase))
                    {
                        var body = message.GetBody<Stream>();
                        dynamic travelBooking = DeserializeTravelBooking(body);

                        // do we want to book a hotel? No? Let's just forward the message to
                        // the next destination via transfer queue
                        if (travelBooking.hotel == null)
                        {
                            await nextStepQueue.SendAsync(CreateForwardMessage(message, travelBooking, via));
                            // done with this job
                            await message.CompleteAsync();
                        }
                        else
                        {
                            lock (Console.Out)
                            {
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine("Booking Hotel");
                                Console.ResetColor();
                            }

                            // now we're going to simulate the work of booking a hotel,
                            // which usually involves a call to a third party

                            // every 11th hotel booking sadly goes wrong
                            if (message.SequenceNumber%11 == 0)
                            {
                                await message.DeadLetterAsync(
                                   new Dictionary<string, object>
                                    {
                                        {"DeadLetterReason", "TransactionError"},
                                        {"DeadLetterErrorDescription", "Failed to perform hotel reservation"},
                                        {"Via", via}
                                    });
                            }
                            else
                            {
                                // every operation executed in the first 3 secs of any minute 
                                // tanks completely (simulates some local or external unexpected issue) 
                                if (DateTime.UtcNow.Second <= 3)
                                {
                                    throw new Exception("O_o");
                                }

                                // let's pretend we booked something
                                travelBooking.hotel.reservationId = "5676891234321";

                                await nextStepQueue.SendAsync(CreateForwardMessage(message, travelBooking, via));

                                // done with this job
                                await message.CompleteAsync();
                            }
                        }
                    }
                    else
                    {
                        await message.DeadLetterAsync(
                            new Dictionary<string, object>
                                    {
                                        {"DeadLetterReason", "BadMessage"},
                                        {"DeadLetterErrorDescription", "Unrecognized input message"},
                                        {"Via", via}
                                    });
                    }
                    scope.Complete();
                }
            }
            catch (Exception e)
            {
                Trace.TraceError(e.ToString());
                await message.AbandonAsync();
            }
        }

        public static async Task BookRentalCar(BrokeredMessage message, MessageSender nextStepQueue, MessageSender compensatorQueue)
        {
            try
            {
                var via = (message.Properties.ContainsKey("Via")
                    ? ((string) message.Properties["Via"] + ",")
                    : string.Empty) +
                          "bookcar";

                using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
                {
                    if (message.Label != null &&
                        message.ContentType != null &&
                        message.Label.Equals(TravelBookingLabel, StringComparison.InvariantCultureIgnoreCase) &&
                        message.ContentType.Equals(ContentTypeApplicationJson, StringComparison.InvariantCultureIgnoreCase))
                    {
                        var body = message.GetBody<Stream>();
                        dynamic travelBooking = DeserializeTravelBooking(body);

                        // do we want to book a flight? No? Let's just forward the message to
                        // the next destination via transfer queue
                        if (travelBooking.car == null)
                        {
                            await nextStepQueue.SendAsync(CreateForwardMessage(message, travelBooking, via));
                            // done with this job
                            await message.CompleteAsync();
                        }
                        else
                        {
                            lock (Console.Out)
                            {
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine("Booking Rental Car");
                                Console.ResetColor();
                            }

                            // now we're going to simulate the work of booking a car,
                            // which usually involves a call to a third party

                            // every 13th car booking sadly goes wrong
                            if (message.SequenceNumber%13 == 0)
                            {
                                await message.DeadLetterAsync(
                                    new Dictionary<string, object>
                                    {
                                        {"DeadLetterReason", "TransactionError"},
                                        {"DeadLetterErrorDescription", "Failed to perform rental car reservation"},
                                        {"Via", via}
                                    });
                            }
                            else
                            {
                                // every operation executed in the first 3 secs of any minute 
                                // tanks completely (simulates some local or external unexpected issue) 
                                if (DateTime.UtcNow.Second <= 3)
                                {
                                    throw new Exception("O_o");
                                }

                                // let's pretend we booked something
                                travelBooking.car.reservationId = "QP271713299R";

                                await nextStepQueue.SendAsync(CreateForwardMessage(message, travelBooking, via));

                                // done with this job
                                await message.CompleteAsync();
                            }
                        }
                    }
                    else
                    {
                        await message.DeadLetterAsync(
                            new Dictionary<string, object>
                                    {
                                        {"DeadLetterReason", "BadMessage"},
                                        {"DeadLetterErrorDescription", "Unrecognized input message"},
                                        {"Via", via}
                                    });
                    }
                    scope.Complete();
                }
            }
            catch (Exception e)
            {
                Trace.TraceError(e.ToString());
                await message.AbandonAsync();
            }
        }

        public static async Task CancelFlight(BrokeredMessage message, MessageSender nextStepQueue, MessageSender compensatorQueue)
        {
            try
            {
                var via = (message.Properties.ContainsKey("Via")
                    ? ((string) message.Properties["Via"] + ",")
                    : string.Empty) +
                          "cancelflight";

                using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
                {
                    if (message.Label != null &&
                        message.ContentType != null &&
                        message.Label.Equals(TravelBookingLabel, StringComparison.InvariantCultureIgnoreCase) &&
                        message.ContentType.Equals(ContentTypeApplicationJson, StringComparison.InvariantCultureIgnoreCase))
                    {
                        var body = message.GetBody<Stream>();
                        dynamic travelBooking = DeserializeTravelBooking(body);

                        // do we want to book a flight? No? Let's just forward the message to
                        // the next destination via transfer queue
                        if (travelBooking.flight != null &&
                            travelBooking.flight.reservationId != null)
                        {
                            lock (Console.Out)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Cancelling Flight");
                                Console.ResetColor();
                            }

                            // undo the reservation (or pretend to fail)
                            if (DateTime.UtcNow.Second <= 3)
                            {
                                throw new Exception("O_o");
                            }

                            // reset the id
                            travelBooking.flight.reservationId = null;

                            // forward
                            await nextStepQueue.SendAsync(CreateForwardMessage(message, travelBooking, via));
                        }
                        else
                        {
                            await nextStepQueue.SendAsync(CreateForwardMessage(message, travelBooking, via));
                        }
                        // done with this job
                        await message.CompleteAsync();
                    }
                    else
                    {
                        await message.DeadLetterAsync(
                            new Dictionary<string, object>
                                    {
                                        {"DeadLetterReason", "BadMessage"},
                                        {"DeadLetterErrorDescription", "Unrecognized input message"},
                                        {"Via", via}
                                    });
                    }
                    scope.Complete();
                }
            }
            catch (Exception e)
            {
                Trace.TraceError(e.ToString());
                await message.AbandonAsync();
            }
        }

        public static async Task CancelHotel(BrokeredMessage message, MessageSender nextStepQueue, MessageSender compensatorQueue)
        {
            lock (Console.Out)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Cancelling Hotel");
                Console.ResetColor();
            }

            try
            {
                var via = (message.Properties.ContainsKey("Via")
                    ? ((string) message.Properties["Via"] + ",")
                    : string.Empty) +
                          "cancelhotel";

                using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
                {
                    if (message.Label != null &&
                        message.ContentType != null &&
                        message.Label.Equals(TravelBookingLabel, StringComparison.InvariantCultureIgnoreCase) &&
                        message.ContentType.Equals(ContentTypeApplicationJson, StringComparison.InvariantCultureIgnoreCase))
                    {
                        var body = message.GetBody<Stream>();
                        dynamic travelBooking = DeserializeTravelBooking(body);

                        // Did we want to book a hotel?Did we succeed and have work to undo?  
                        // If not, let's just forward the message to the next destination via transfer queue
                        if (travelBooking.hotel != null &&
                            travelBooking.hotel.reservationId != null)
                        {
                            // undo the reservation (or pretend to fail)
                            if (DateTime.UtcNow.Second <= 3)
                            {
                                throw new Exception("O_o");
                            }

                            // reset the id
                            travelBooking.hotel.reservationId = null;

                            // forward
                            await nextStepQueue.SendAsync(CreateForwardMessage(message, travelBooking, via));
                        }
                        else
                        {
                            await nextStepQueue.SendAsync(CreateForwardMessage(message, travelBooking, via));
                        }
                        // done with this job
                        await message.CompleteAsync();
                    }
                    else
                    {
                        await message.DeadLetterAsync(
                            new Dictionary<string, object>
                                    {
                                        {"DeadLetterReason", "BadMessage"},
                                        {"DeadLetterErrorDescription", "Unrecognized input message"},
                                        {"Via", via}
                                    });
                    }
                    scope.Complete();
                }
            }
            catch (Exception e)
            {
                Trace.TraceError(e.ToString());
                await message.AbandonAsync();
            }
        }

        static BrokeredMessage CreateForwardMessage(BrokeredMessage message, dynamic travelBooking, string via)
        {
            var brokeredMessage = new BrokeredMessage(SerializeTravelBooking(travelBooking))
            {
                ContentType = ContentTypeApplicationJson,
                Label = message.Label,
                TimeToLive = message.ExpiresAtUtc - DateTime.UtcNow
            };
            foreach (var prop in message.Properties)
            {
                brokeredMessage.Properties[prop.Key] = prop.Value;
            }
            brokeredMessage.Properties["Via"] = via;
            return brokeredMessage;
        }

        public static async Task CancelRentalCar(BrokeredMessage message, MessageSender nextStepQueue, MessageSender compensatorQueue)
        {
            try
            {
                var via = (message.Properties.ContainsKey("Via")
                    ? ((string) message.Properties["Via"] + ",")
                    : string.Empty) +
                          "cancelcar";

                using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
                {
                    if (message.Label != null &&
                        message.ContentType != null &&
                        message.Label.Equals(TravelBookingLabel, StringComparison.InvariantCultureIgnoreCase) &&
                        message.ContentType.Equals(ContentTypeApplicationJson, StringComparison.InvariantCultureIgnoreCase))
                    {
                        var body = message.GetBody<Stream>();
                        dynamic travelBooking = DeserializeTravelBooking(body);

                        // do we want to book a flight? No? Let's just forward the message to
                        // the next destination via transfer queue
                        if (travelBooking.car != null &&
                            travelBooking.car.reservationId != null)
                        {
                            lock (Console.Out)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Cancelling Rental Car");
                                Console.ResetColor();
                            }

                            // undo the reservation (or pretend to fail)
                            if (DateTime.UtcNow.Second <= 3)
                            {
                                throw new Exception("O_o");
                            }

                            // reset the id
                            travelBooking.car.reservationId = null;

                            // forward
                            await nextStepQueue.SendAsync(CreateForwardMessage(message, travelBooking, via));
                        }
                        else
                        {
                            await nextStepQueue.SendAsync(CreateForwardMessage(message, travelBooking, via));
                        }
                        // done with this job
                        await message.CompleteAsync();
                    }
                    else
                    {
                        await message.DeadLetterAsync(
                            new Dictionary<string, object>
                                    {
                                        {"DeadLetterReason", "BadMessage"},
                                        {"DeadLetterErrorDescription", "Unrecognized input message"},
                                        {"Via", via}
                                    });
                    }
                    scope.Complete();
                }
            }
            catch (Exception e)
            {
                Trace.TraceError(e.ToString());
                await message.AbandonAsync();
            }
        }

        static MemoryStream SerializeTravelBooking(dynamic travelBooking)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(travelBooking)));
        }

        static object DeserializeTravelBooking(Stream body)
        {
            return JsonConvert.DeserializeObject(new StreamReader(body, true).ReadToEnd());
        }
    }
}