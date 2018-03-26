#Step 1 – Login to your Azure subscription
Login-AzureRmAccount

# Optional – you need this if you do not have the Service Bus module already installed
Install-Module AzureRM.ServiceBus

#Step 2 – following parameters are used while creating the resources
$location1 = “East US”
$location2 = “West US”
$resourceGroup = “servicebusgeodrresourcegroup”
$primarynamespace = “servicebusprimarynamespace1”
$secondarynamespace = “servicebussecondarynamespace1”
$aliasname = “servicebusalias1”

#Step 3 - Create Resource Group
New-AzureRmResourceGroup -Name $resourceGroup -Location $location1

#Step 4 - Create Service Bus primary namespace
New-AzureRmServiceBusNamespace –ResourceGroupName $resourceGroup -NamespaceName $primarynamespace -Location $location1 -SkuName Premium

#Step 5 - Create Service Bus secondary namespace - Copy the ARM ID from the output as you need it later as -PartnerPartnernamespace. It usually starts with /subscriptions...
New-AzureRmServiceBusNamespace –ResourceGroupName $resourceGroup –NamespaceName $secondarynamespace -Location $location2 -SkuName Premium

#Step 6 – Create an alias and pairs the namespaces
New-AzureRmServiceBusGeoDRConfiguration -ResourceGroupName $resourcegroup -Name $aliasname -Namespace $primarynamespace -PartnerNamespace "---ARM ID from step before---"

#Optional – you can obtain the alias details using this cmdlet, here you can also check if the provisioning is done. Provisioning state should change from Accepted to Succeeded when any of the below or above operations are completed. Note that after failover you want to add the secondary namespace instead of the primary namespace.
Get-AzureRmServiceBusGeoDRConfiguration -ResourceGroup $resourcegroup -Name $aliasname -Namespace $primarynamespace

#Optional – you can break the pairing between the two namespaces if you desire to associate a different pairing. Break pair is initiated over primary only
Set-AzureRmServiceBusGeoDRConfigurationBreakPair -ResourceGroupName $resourcegroup -Name $aliasname -Namespace $primarynamespace

#Optional – create entities to test the meta data sync between primary and secondary namespaces
#Here we are creating a queue
New-AzureRmServiceBusQueue -ResourceGroupName $resourcegroup -Namespace $primarynamespace -Name “testqueue1” 

#Optional – check if the created queue was replicated
Get-AzureRmServiceBusQueue -ResourceGroupName $resourcegroup -Namespace $secondarynamespace -Name “testqueue1”

#Step 7 – Initiate a failover. This will break the pairing and alias will now point to the secondary namespace. Failover is initiated over secondary only
Set-AzureRmServiceBusGeoDRConfigurationFailOver -ResourceGroupName $resourcegroup -Name $aliasname -Namespace $secondarynamespace

#Step 8 – Remove the Geo-pairing and its configuration. This cmdlet would delete the alias that is associated with the Geo-paired namespaces
# Deleting the alias would then break the pairing and at this point primary and secondary namespaces are no more in sync. you need to add the namespacename of either primary or secondary depending on if you failed over or not. Below shows the state after failover.
Remove-AzureRmServiceBusGeoDRConfiguration -ResourceGroupName $resourcegroup -Name $aliasname -Namespace $secondarynamespace

#Optional – clean up the created resoucegroup
Remove-AzureRmResourceGroup -Name $resourcegroup