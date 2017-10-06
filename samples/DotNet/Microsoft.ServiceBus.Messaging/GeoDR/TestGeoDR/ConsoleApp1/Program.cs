using System;
using System.Threading;
using Microsoft.ServiceBus.Messaging;
using Microsoft.Azure.Management.ServiceBus;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;

namespace ConsoleApp1
{
    class Program
    {
        public static string subscriptionId = "your subscription id"; // Pick existing subscription
        public static string resourceGroupName = "your resource group name"; // Pick existing resource group       

        public static string activeDirectoryAuthority = "https://login.microsoftonline.com";
        public static string resourceManagerUrl = "https://management.azure.com/";

        // Use the following link to learn how to setup an client app in access Active Directory
        // and grant that app rights to your azure subscription
        // https://msdn.microsoft.com/en-us/library/azure/dn790557.aspx#bk_portal
        // Respectively follow this: https://docs.microsoft.com/en-us/azure/azure-resource-manager/resource-group-create-service-principal-portal
        // To get the below three values. Make sure to add the application as owner in your resource group via "Access control (IAM)".

        public static string tenantId = "your tenant / directory id";     // Directory ID in portal
        public static string clientId = "your client / application id";    //Application ID in portal
        public static string clientSecrets = "your clientSecrets / key"; // Key in portal


        static string geoDRPrimaryNS = "your primary ns";
        static string geoDRSecondaryNS = "your secondary ns";
        static string alias = "your alias";

        static void Main(string[] args)
        {
            string token = GetAuthorizationHeader();

            TokenCredentials creds = new TokenCredentials(token);
            ServiceBusManagementClient client = new ServiceBusManagementClient(creds) { SubscriptionId = subscriptionId };

            // Get alias connstring and Create Service and Consumer Groups
            var accessKeys = client.Namespaces.ListKeys(resourceGroupName, geoDRPrimaryNS, "RootManageSharedAccessKey");
            var aliasPrimaryConnectionString = accessKeys.AliasPrimaryConnectionString;
            var aliasSecondaryConnectionString = accessKeys.AliasSecondaryConnectionString;

            if(aliasPrimaryConnectionString == null)
            {
                accessKeys = client.Namespaces.ListKeys(resourceGroupName, geoDRSecondaryNS, "RootManageSharedAccessKey");
                aliasPrimaryConnectionString = accessKeys.AliasPrimaryConnectionString;
                aliasSecondaryConnectionString = accessKeys.AliasSecondaryConnectionString;
            }

            var connectionString = aliasPrimaryConnectionString;
            var topicName = "mytopic";

            var clientSR = TopicClient.CreateFromConnectionString(connectionString, topicName);
            var message = new BrokeredMessage("This is a test message!");

            Console.WriteLine(String.Format("Message id: {0}", message.MessageId));

            clientSR.Send(message);

            Console.WriteLine("Message successfully sent! Press ENTER to exit program");
            Console.ReadLine();
        }

        private static string GetAuthorizationHeader()
        {
            AuthenticationResult result = null;

            var context = new AuthenticationContext(string.Format("{0}/{1}", activeDirectoryAuthority, tenantId));

            var thread = new Thread(() =>
            {
                result = context.AcquireTokenAsync(
                    resourceManagerUrl,
                    new ClientCredential(clientId, clientSecrets)).Result;
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Name = "AquireTokenThread";
            thread.Start();
            thread.Join();

            if (result == null)
            {
                throw new InvalidOperationException("Failed to obtain the JWT token");
            }

            string token = result.AccessToken;
            return token;
        }
    }
}
