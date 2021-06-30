using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NServiceBus;
using Shared;

namespace Receiver
{
    public class PingHandler : IHandleMessages<Ping>
    {
        private readonly ILogger<PingHandler> logger;

        public PingHandler(ILogger<PingHandler> logger)
        {
            this.logger = logger;
        }

        public async Task Handle(Ping message, IMessageHandlerContext context)
        {
            logger.LogInformation($"Processing Ping message #{message.Round}");

            var reply = new Pong { Acknowledgement = $"Ping #{message.Round} processed at {DateTimeOffset.UtcNow:s}" };

            await context.Reply(reply);
            
            // throw new Exception("BOOM");
        }
    }
}
