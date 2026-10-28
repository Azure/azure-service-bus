using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ =>
{
    var fullyQualifiedNamespace = builder.Configuration["ServiceBusFullyQualifiedNamespace"];
    if (string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
        throw new InvalidOperationException("Set ServiceBusFullyQualifiedNamespace to <namespace>.servicebus.windows.net.");
    return new ServiceBusClient(fullyQualifiedNamespace, new DefaultAzureCredential());
});
await builder.Build().RunAsync();
