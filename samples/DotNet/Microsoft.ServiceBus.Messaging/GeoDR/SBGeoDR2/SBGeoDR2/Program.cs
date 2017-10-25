using System;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Azure.Management.ServiceBus;
using Microsoft.Azure.Management.ServiceBus.Models;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;

namespace SBGeoDR2
{
    static class Program
    {
        static string subscriptionId = "your subscription id"; // Pick existing subscription
        static string resourceGroupName = "your resource group name"; // Pick existing resource group       

        static string activeDirectoryAuthority = "https://login.microsoftonline.com";
        static string resourceManagerUrl = "https://management.azure.com/";

        // Use the following link to learn how to setup an client app in access Active Directory
        // and grant that app rights to your azure subscription
        // https://msdn.microsoft.com/en-us/library/azure/dn790557.aspx#bk_portal
        // Respectively follow this: https://docs.microsoft.com/en-us/azure/azure-resource-manager/resource-group-create-service-principal-portal
        // To get the below three values. Make sure to add the application as owner in your resource group via "Access control (IAM)".

        static string tenantId = "your tenant / directory id";     // Directory ID in portal
        static string clientId = "your client / application id";    //Application ID in portal
        static string clientSecret = "your clientSecret / key"; // Key in portal

        
        static string geoDRPrimaryNS = "your primary ns";
        static string geoDRSecondaryNS = "your secondary ns";
        static string alias = "your alias";

        static void Main(string[] args)
        {
            MainAsync().GetAwaiter().GetResult();
        }

        static async Task MainAsync()
        {
            // https://msdn.microsoft.com/en-us/library/azure/dn790557.aspx#bk_portal
            string token = await GetAuthorizationHeaderAsync().ConfigureAwait(false);

            TokenCredentials creds = new TokenCredentials(token);
            ServiceBusManagementClient client = new ServiceBusManagementClient(creds) { SubscriptionId = subscriptionId };
            
            //// 1. Create Primary Namespace (optional)            
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

            //// 2. Create Secondary Namespace (optional if you already have an empty namespace available)
            var namespaceParams2 = new SBNamespace
            {
                Location = "North Central US",
                Sku = new SBSku
                {
                    Name = SkuName.Premium,
                    Capacity = 1
                }
            };

            //// If you re-run this program while namespaces are still paired this operation will fail with a bad request.
            //// this is because we block all updates on secondary namespaces once it is paired
            var namespace2 = await client.Namespaces.CreateOrUpdateAsync(resourceGroupName, geoDRSecondaryNS, namespaceParams2)
                .ConfigureAwait(false);

            // 3. Pair the namespaces to enable DR.            
            ArmDisasterRecovery drStatus = await client.DisasterRecoveryConfigs.CreateOrUpdateAsync(
                resourceGroupName,
                geoDRPrimaryNS,
                alias,
                new ArmDisasterRecovery { PartnerNamespace = geoDRSecondaryNS })
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

            await client.Topics.CreateOrUpdateAsync(resourceGroupName, geoDRPrimaryNS, "myTopic", new SBTopic())
                .ConfigureAwait(false);
            await client.Subscriptions.CreateOrUpdateAsync(resourceGroupName, geoDRPrimaryNS, "myTopic", "myTopic-Sub1", new SBSubscription())
                .ConfigureAwait(false);

            // sleeping to allow metadata to sync across primary and secondary
            await Task.Delay(TimeSpan.FromSeconds(60));
            
            // 6. Failover. Note that this Failover operations is ALWAYS run against the secondary ( because primary might be down at time of failover )
            // client.DisasterRecoveryConfigs.FailOver(resourceGroupName, geoDRSecondaryNS, alias);            

            // other possible DR operations

            // 7. Break Pairing
            // client.DisasterRecoveryConfigs.BreakPairing(resourceGroupName, geoDRPrimaryNS, alias);

            // 8. Delete DR config (alias)
            // note that this operation needs to run against the namespace that the alias is currently pointing to
            // client.DisasterRecoveryConfigs.Delete(resourceGroupName, geoDRPrimaryNS, alias);
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
