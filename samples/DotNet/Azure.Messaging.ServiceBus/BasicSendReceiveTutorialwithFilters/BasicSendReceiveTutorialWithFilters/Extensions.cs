using System.Collections;
using System.Collections.Generic;
using System.Text;
//using Microsoft.Azure.ServiceBus;
using Newtonsoft.Json;
using Azure.Messaging.ServiceBus;
using System;

namespace BasicSendReceiveTutorialWithFilters
{
    public static class Extensions
    {
        public static T As<T>(this ServiceBusReceivedMessage message) where T : class
        {
            return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(message.Body.ToArray()));
        }
        public static ServiceBusMessage AsMessage(this object obj)
        {
            return new ServiceBusMessage(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj)));
        }

        public static bool Any(this IList<ServiceBusReceivedMessage> collection)
        {
            return collection != null && collection.Count > 0;
        }
    }
}