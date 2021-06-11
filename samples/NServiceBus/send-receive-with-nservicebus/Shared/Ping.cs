using NServiceBus;

namespace Shared
{
    public class Ping : ICommand
    {
        public int Round { get; set; }
    }
}
