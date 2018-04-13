using System.Collections;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.ServiceBus;
using Newtonsoft.Json;

namespace BasicSendReceiveTutorialWithFilters
{
    public static class Extensions
    {
        public static T As<T>(this Message message) where T : class
        {
            return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(message.Body));
        }
        public static Message AsMessage(this object obj)
        {
            return new Message(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj)));
        }

        public static bool Any(this IList<Message> collection)
        {
            return collection != null && collection.Count > 0;
        }
    }
}