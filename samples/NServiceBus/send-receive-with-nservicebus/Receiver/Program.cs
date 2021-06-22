using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NServiceBus;

namespace Receiver
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
                    var endpointConfiguration = new EndpointConfiguration("Receiver");

                    var transport = endpointConfiguration.UseTransport<AzureServiceBusTransport>();
                    var connectionString = context.Configuration.GetConnectionString("AzureServiceBusConnectionString");
                    transport.ConnectionString(connectionString);

                    endpointConfiguration.AuditProcessedMessagesTo("audit");
                    endpointConfiguration.SendFailedMessagesTo("error");

                    // Operational scripting: https://docs.particular.net/transports/azure-service-bus/operational-scripting
                    endpointConfiguration.EnableInstallers();

                    return endpointConfiguration;
                })
                .Build();

            await host.RunAsync();
        }
    }
}
