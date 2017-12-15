using System;
using System.Threading.Tasks;
using System.Threading;
using System.Configuration;
using Microsoft.Azure.Management.ServiceBus;
using Microsoft.Azure.Management.ServiceBus.Models;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;

namespace SBGeoDR2
{
    class Program
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
        static readonly string alternateName = ConfigurationManager.AppSettings["alternatePrimaryName"];

        static void Main(string[] args)
        {
            //MainAsync().GetAwaiter().GetResult();

            Console.WriteLine ("Choose an action:");
            Console.WriteLine ("[A] Create or update namespaces, pair them and create a few entities");
            Console.WriteLine ("[B] Failover");
            Console.WriteLine ("[C] Break pairing");            
            Console.WriteLine ("[D] Delete Alias after Failover.");

            Char key = Console.ReadKey(true).KeyChar;
            String keyPressed = key.ToString().ToUpper();            

            switch (keyPressed)
            {
                case "A":
                    CreatePairing().GetAwaiter().GetResult();
                    break;
                case "B":
                    ExecuteFailover().GetAwaiter().GetResult();
                    break;
                case "C":
                    BreakPairing().GetAwaiter().GetResult();
                    break;
                case "D":
                    DeleteAliasSec().GetAwaiter().GetResult();
                    break;
                default:
                    Console.WriteLine("Unknown command, press enter to exit");
                    Console.ReadLine();
                    break;
            }
        }

        static async Task CreatePairing()
        {            
            // https://msdn.microsoft.com/en-us/library/azure/dn790557.aspx#bk_portal
            string token = await GetAuthorizationHeaderAsync().ConfigureAwait(false);

            TokenCredentials creds = new TokenCredentials(token);
            ServiceBusManagementClient client = new ServiceBusManagementClient(creds) { SubscriptionId = subscriptionId };
            
            // 1. Create Primary Namespace (optional)        
            Console.WriteLine("Create or update namespace 1");
            var namespaceParams = new SBNamespace
            {
                Location = "South Central US",
                Sku = new SBSku
                {                    
                    Name = SkuName.Premium,
                    Capacity = 1
                }
            };
            var namespace1 = await client.Namespaces.CreateOrUpdateAsync(resourceGroupName, geoDRPrimaryNS, namespaceParams)
                .ConfigureAwait(false);

            // 2. Create Secondary Namespace (optional if you already have an empty namespace available)
            Console.WriteLine("Create or update namespace 2");
            var namespaceParams2 = new SBNamespace
            {
                Location = "North Central US",
                Sku = new SBSku
                {
                    Name = SkuName.Premium,
                    Capacity = 1
                }
            };

            // If you re-run this program while namespaces are still paired this operation will fail with a bad request.
            // this is because we block all updates on secondary namespaces once it is paired
            var namespace2 = await client.Namespaces.CreateOrUpdateAsync(resourceGroupName, geoDRSecondaryNS, namespaceParams2)
                .ConfigureAwait(false);

            // 3. Pair the namespaces to enable DR.   
            Console.WriteLine("Starting Pairing");
            ArmDisasterRecovery drStatus = await client.DisasterRecoveryConfigs.CreateOrUpdateAsync(
                resourceGroupName,
                geoDRPrimaryNS,
                alias,
                new ArmDisasterRecovery { PartnerNamespace = namespace2.Id, AlternateName = alternateName }) 
                // Note: The additional, optional parameter AlternateName is resposible for using the namespace name as alias and renaming the primary namespace.
                .ConfigureAwait(false);

            while (drStatus.ProvisioningState != ProvisioningStateDR.Succeeded)
            {
                Console.WriteLine("Waiting for DR to be setup. Current State: " + drStatus.ProvisioningState);

                drStatus = client.DisasterRecoveryConfigs.Get(
                    resourceGroupName,
                    geoDRPrimaryNS,
                    alias);

                Thread.CurrentThread.Join(TimeSpan.FromSeconds(30));
            }

            Console.WriteLine("Creating test entities to show pairing.");
            await client.Topics.CreateOrUpdateAsync(resourceGroupName, geoDRPrimaryNS, "myTopic", new SBTopic())
                .ConfigureAwait(false);
            await client.Subscriptions.CreateOrUpdateAsync(resourceGroupName, geoDRPrimaryNS, "myTopic", "myTopic-Sub1", new SBSubscription())
                .ConfigureAwait(false);

            // Sleeping to allow metadata to sync across primary and secondary
            await Task.Delay(TimeSpan.FromSeconds(60));

            Console.WriteLine("Initial setup complete. Please see in the portal if all resources have been created as expected.");
            Console.WriteLine("Try creating a few additional entities in the primary in the portal.");
            Console.WriteLine("Press enter to exit.");
            Console.ReadLine();            
        }

        static async Task ExecuteFailover()
        {
            // https://msdn.microsoft.com/en-us/library/azure/dn790557.aspx#bk_portal
            string token = await GetAuthorizationHeaderAsync().ConfigureAwait(false);

            TokenCredentials creds = new TokenCredentials(token);
            ServiceBusManagementClient client = new ServiceBusManagementClient(creds) { SubscriptionId = subscriptionId };            

            // Failover. Note that this Failover operations is ALWAYS run against the secondary ( because primary might be down at time of failover )
            Console.WriteLine("Initiating failover. Management operations can take 1-2 minutes to take effect.");
            client.DisasterRecoveryConfigs.FailOver(resourceGroupName, geoDRSecondaryNS, alias);

            // Sleeping to allow the break pairing to happen
            Console.WriteLine("Waiting for failover to complete.");
            await Task.Delay(TimeSpan.FromSeconds(60));
            Console.WriteLine("Failover Complete, press enter to exit.");
            Console.ReadLine();
        }

        static async Task BreakPairing()
        {
            // https://msdn.microsoft.com/en-us/library/azure/dn790557.aspx#bk_portal
            string token = await GetAuthorizationHeaderAsync().ConfigureAwait(false);

            TokenCredentials creds = new TokenCredentials(token);
            ServiceBusManagementClient client = new ServiceBusManagementClient(creds) { SubscriptionId = subscriptionId };

            // Break Pairing
            Console.WriteLine("Disabling pairing. Management operations can take 1-2 minutes to take effect.");
            client.DisasterRecoveryConfigs.BreakPairing(resourceGroupName, geoDRPrimaryNS, alias);

            // sleeping to allow the break pairing to happen
            Console.WriteLine("Waiting for break pairing to complete.");
            await Task.Delay(TimeSpan.FromSeconds(60));
            Console.WriteLine("Break pairing complete. Press enter to exit.");
            Console.ReadLine();
        }

        static async Task DeleteAliasPrim()
        {
            // https://msdn.microsoft.com/en-us/library/azure/dn790557.aspx#bk_portal
            string token = await GetAuthorizationHeaderAsync().ConfigureAwait(false);

            TokenCredentials creds = new TokenCredentials(token);
            ServiceBusManagementClient client = new ServiceBusManagementClient(creds) { SubscriptionId = subscriptionId };

            Console.WriteLine("Deleting the alias. Management operations can take 1-2 minutes to take effect.");
            client.DisasterRecoveryConfigs.Delete(resourceGroupName, geoDRPrimaryNS, alias);

            Console.WriteLine("Wait for the alias to be deleted.");
            await Task.Delay(TimeSpan.FromSeconds(60));
            Console.WriteLine("Alias deleted. Press enter to exit.");
            Console.ReadLine();
        }

        static async Task DeleteAliasSec()
        {
            // https://msdn.microsoft.com/en-us/library/azure/dn790557.aspx#bk_portal
            string token = await GetAuthorizationHeaderAsync().ConfigureAwait(false);

            TokenCredentials creds = new TokenCredentials(token);
            ServiceBusManagementClient client = new ServiceBusManagementClient(creds) { SubscriptionId = subscriptionId };

            Console.WriteLine("Deleting the alias. Management operations can take 1-2 minutes to take effect.");
            client.DisasterRecoveryConfigs.Delete(resourceGroupName, geoDRSecondaryNS, alias);

            Console.WriteLine("Wait for the alias to be deleted.");
            await Task.Delay(TimeSpan.FromSeconds(60));
            Console.WriteLine("Alias deleted. Press enter to exit.");
            Console.ReadLine();
        }

        private static async Task<string> GetAuthorizationHeaderAsync()
        {
            var context = new AuthenticationContext($"{activeDirectoryAuthority}/{tenantId}");

            var result = await context.AcquireTokenAsync(
                resourceManagerUrl,
                new ClientCredential(clientId, clientSecret))
                .ConfigureAwait(false);

            if (result == null)
            {
                throw new InvalidOperationException("Failed to obtain the JWT token.");
            }

            return result.AccessToken;
        }
    }
}
