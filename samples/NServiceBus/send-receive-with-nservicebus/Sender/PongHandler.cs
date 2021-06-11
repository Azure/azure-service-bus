using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NServiceBus;
using Shared;

namespace Sender
{
    public class PongHandler : IHandleMessages<Pong>
    {
        private readonly ILogger<PongHandler> logger;

        public PongHandler(ILogger<PongHandler> logger)
        {
            this.logger = logger;
        }

        public Task Handle(Pong message, IMessageHandlerContext context)
        {
            logger.LogInformation($"Processing Pong message: {message.Acknowledgement}");

            return Task.CompletedTask;
        }
    }
}