using NServiceBus;

namespace Shared
{
    public class Pong : IMessage
    {
        public string Acknowledgement { get; set; }
    }
}