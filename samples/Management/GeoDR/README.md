# Azure Service Bus Geo-disaster recovery

To learn more about Azure Service Bus, please visit our [marketing page](https://azure.microsoft.com/en-us/services/service-bus/).

To learn more about our Geo-DR feature in general please follow [this](https://docs.microsoft.com/azure/service-bus-messaging/service-bus-geo-dr) link.

This sample shows how to: 

1. Achieve Geo-DR for an Service Bus namespace. 
2. Create a namespace with live metadata replication between two customer chosen regions

This sample consists of three parts:

1. The main scenario showing management (Setup, failover, remove pairing) of new or existing namespaces sample can be found [here](https://github.com/Azure/azure-service-bus/tree/master/samples/DotNet/Microsoft.ServiceBus.Messaging/GeoDR/SBGeoDR2/SBGeoDR2)
2. The scenario in which you want to use an existing namespace name as alias can be found [here](https://github.com/Azure/azure-service-bus/tree/master/samples/DotNet/Microsoft.ServiceBus.Messaging/GeoDR/SBGeoDR2/SBGeoDR_existing_namespace_name). Make sure to thoroughly look through the comments as this diverges slightly from the main scenario. Examine both, App.config and Program.cs. ***Note:*** If you do not failover but just do break pairing, there is no need to execute delete alias as namespace name and alias are the same. If you do failover you would need to delete the alias if you would want to use the namespace outside of a DR setup.
3. A sample on how to access the alias connection string which can be found [here](https://github.com/Azure/azure-service-bus/tree/master/samples/DotNet/Microsoft.ServiceBus.Messaging/GeoDR/TestGeoDR/ConsoleApp1).

## Getting Started
### Prerequisites

In order to get started using the sample (as it uses the Service Bus management libraries), you must authenticate with Azure Active Directory (AAD). This requires you to authenticate as a Service Principal, which provides access to your Azure resources. 
To obtain a service principal please do the following steps:

1. Go to the Azure Portal and select Azure Active Directory in the left menu.
2. Create a new Application under App registrations / + New application registration.
	1. The application should be of type Web app / API.
	2. You can provide any URL for your application as sign on URL.
	3. Navigate to your newly created application
3. Application or AppId is the client Id. Note it down as you will need it for the sample.
4. Select keys and create a new key. Note down the Value as you won't be able to access it later.
5. Go back to the root Azure Active Directory and select properties.
	1. Note down the Directory ID as this is your TenantId.
6. You must have ‘Owner’ permissions under Role for the resource group that you wish to run the sample on. Regardless if you want to use an existing namespace or create a new one, make sure to add the newly created application as owner under Access Control (IAM).

For more information on creating a Service Principal, refer to the following articles:

*	[Use the Azure Portal to create Active Directory application and service principal that can access resources](https://docs.microsoft.com/azure/azure-resource-manager/resource-group-create-service-principal-portal)
*	[Use Azure PowerShell to create a service principal to access resources](https://docs.microsoft.com/azure/azure-resource-manager/resource-group-authenticate-service-principal)
*	[Use Azure CLI to create a service principal to access resources](https://docs.microsoft.com/azure/azure-resource-manager/resource-group-authenticate-service-principal-cli)

<!-- The above articles helps you to obtain an AppId (ClientId), TenantId, and ClientSecret (Authentication Key), all of which are required to authenticate the management libraries.  Finally, when creating your Active Directory application, if you do not have a sign-on URL to input in the create step, simply input any URL format string e.g. https://contoso.org/exampleapp -->

### Required NuGet packages

1.	Microsoft.Azure.Management.ServiceBus
2.	Microsoft.IdentityModel.Clients.ActiveDirectory - used to authenticate with AAD

### Running the sample

1.	Please use Visual Studio 2017
2.	Make sure all assemblies are in place.
2.	Populate the regarding values in the App.config.
3.	Build the solution.
4.	Make sure to execute on Screen option A before any other option.

The Geo DR actions could be

*	CreatePairing
For creating a paired region. After this, you should see metadata (i.e. Queues, Topics and Subscriptions replicated to the secondary namespace).

*	FailOver
Simulating a failover. After this action, the secondary namespace becomes the primary

*	BreakPairing
For breaking the pairing between a primary and secondary namespace

*	DeleteAlias
For deleting an alias, that contains information about the primary-secondary pairing

*	GetConnectionStrings
In a Geo DR enabled namespace, the Service Bus should be accessed only via the alias. This is because, the alias can point to either the primary Service Bus or the failed over Service Bus. This way, the user does not have to adjust the connection strings in his/her apps to point to a different Service Bus in the case of a failover.    
The way to get the alias connection string is shown in a seperate console app which you can also use to test your newly geo paired namespaces. It can be found [here](https://github.com/Azure/azure-service-bus/tree/master/samples/DotNet/Microsoft.ServiceBus.Messaging/GeoDR/TestGeoDR).
   
***Note:*** The AAD access data for the GeoDR sample must also be used for the Test sample.
