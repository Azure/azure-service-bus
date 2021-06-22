using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NServiceBus;
using Shared;

namespace Sender
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureLogging((context, logging) =>
                {
                    logging.AddConfiguration(context.Configuration.GetSection("Logging"));

                    logging.AddConsole();
                })
                .UseConsoleLifetime()
                .UseNServiceBus(context =>
                {
                    var endpointConfiguration = new EndpointConfiguration("Sender");

                    var transport = endpointConfiguration.UseTransport<AzureServiceBusTransport>();
                    var connectionString = context.Configuration.GetConnectionString("AzureServiceBusConnectionString");
                    transport.ConnectionString(connectionString);

                    endpointConfiguration.AuditProcessedMessagesTo("audit");
                    endpointConfiguration.SendFailedMessagesTo("error");

                    transport.Routing().RouteToEndpoint(typeof(Ping), "Receiver");

                    // Operational scripting: https://docs.particular.net/transports/azure-service-bus/operational-scripting
                    endpointConfiguration.EnableInstallers();

                    return endpointConfiguration;
                })
                .ConfigureServices(services => services.AddHostedService<SenderWorker>())
                .Build();

            await host.RunAsync();
        }
    }
}
