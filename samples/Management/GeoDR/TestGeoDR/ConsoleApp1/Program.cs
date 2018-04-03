using System;
using System.Threading.Tasks;
using System.Text;
using System.Configuration;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.Management.ServiceBus;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;


namespace ConsoleApp1
{
    static class Program
    {
        static readonly string subscriptionId = ConfigurationManager.AppSettings["subscriptionId"];
        static readonly string resourceGroupName = ConfigurationManager.AppSettings["resourceGroupName"];
        static readonly string activeDirectoryAuthority = ConfigurationManager.AppSettings["activeDirectoryAuthority"];
        static readonly string resourceManagerUrl = ConfigurationManager.AppSettings["resourceManagerUrl"];
        static readonly string tenantId = ConfigurationManager.AppSettings["tenantId"];
        static readonly string clientId = ConfigurationManager.AppSettings["clientId"];
        static readonly string clientSecret = ConfigurationManager.AppSettings["clientSecret"];

        static readonly string geoDRPrimaryNS = ConfigurationManager.AppSettings["geoDRPrimaryNS"];
        static readonly string geoDRSecondaryNS = ConfigurationManager.AppSettings["geoDRSecondaryNS"];
        static readonly string alias = ConfigurationManager.AppSettings["alias"];

        static void Main(string[] args)
        {
            MainAsync().GetAwaiter().GetResult();
        }

        static async Task MainAsync()
        {
            string token = await GetAuthorizationHeaderAsync()
                .ConfigureAwait(false);

            TokenCredentials creds = new TokenCredentials(token);
            ServiceBusManagementClient client = new ServiceBusManagementClient(creds) { SubscriptionId = subscriptionId };

            // Get alias connstring and Create Service and Consumer Groups. 
            // Note: In a real world scenario you would do this operations outside of your client and then add the Alias connection strings to your client.            
            String aliasPrimaryConnectionString;
            String aliasSecondaryConnectionString;

            try
            {
               var accessKeys = client.DisasterRecoveryConfigs.ListKeys(resourceGroupName, geoDRPrimaryNS, alias, "RootManageSharedAccessKey");
               aliasPrimaryConnectionString = accessKeys.AliasPrimaryConnectionString;
               aliasSecondaryConnectionString = accessKeys.AliasSecondaryConnectionString;

            }
            catch
            {
                var accessKeys = client.DisasterRecoveryConfigs.ListKeys(resourceGroupName, geoDRSecondaryNS, alias, "RootManageSharedAccessKey");
                aliasPrimaryConnectionString = accessKeys.AliasPrimaryConnectionString;
                aliasSecondaryConnectionString = accessKeys.AliasSecondaryConnectionString;
            }                                 
        
            var ServiceBusConnectionString = aliasPrimaryConnectionString;
            var topicName = "mytopic";

            var topicClient = new TopicClient(ServiceBusConnectionString,topicName);
            try
            {
                for (var i = 0; i < 10; i++)
                {
                    // Create a new message to send to the queue
                    string messageBody = $"Message {i}";
                    var message = new Message(Encoding.UTF8.GetBytes(messageBody));

                    // Write the body of the message to the console
                    Console.WriteLine($"Sending message: {messageBody}");

                    // Send the message to the queue
                    await topicClient.SendAsync(message);
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine($"{DateTime.Now} :: Exception: {exception.Message}");
            }

            Console.WriteLine("Sending done. Press enter to exit.");
            Console.ReadLine();
        }

        private static async Task<string> GetAuthorizationHeaderAsync()
        {
            var context = new AuthenticationContext($"{activeDirectoryAuthority}/{tenantId}");

            AuthenticationResult result = await context.AcquireTokenAsync(
                resourceManagerUrl,
                new ClientCredential(clientId, clientSecret))
                .ConfigureAwait(false);

            if (result == null)
            {
                throw new InvalidOperationException("Failed to obtain the JWT token");
            }

            return result.AccessToken;
        }
    }
}
