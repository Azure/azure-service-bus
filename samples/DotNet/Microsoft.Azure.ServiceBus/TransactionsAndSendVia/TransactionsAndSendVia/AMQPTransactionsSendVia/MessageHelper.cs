using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.ServiceBus;
using Newtonsoft.Json;



namespace TransactionsAndSendVia
{
    public static class MessageHelper
    {
        public static T DeserializeMsg<T>(this Message message) where T : class
        {
            return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(message.Body));
        }
        public static Message AsMessage(this object obj)
        {
            return new Message(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj)));
        }

        public static byte[] AsBody(this object obj)
        {
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj));
        }
    }
}
