#Step 1 – Login to your Azure subscription
az login
az account set --subscription "Your subscription"

#Step 2 – following parameters are used while creating the resources. The variable declaration assumes you run the commands from a Powershell Window. You may need to change in case you ran e.g. from the portal.
$location1 = “East US”
$location2 = “West US”
$resourceGroup = “servicebusgeodrresourcegroup”
$primarynamespace = “servicebusprimarynamespace1”
$secondarynamespace = “servicebussecondarynamespace1”
$aliasname = “servicebusalias1”

#Step 3 - Create Resource Group
az group create --name $resourceGroup --location $location1

#Step 4 - Create Service Bus primary namespace
az servicebus namespace create --name $primarynamespace --resource-group $resourceGroup --location $location1 --sku Premium

#Step 5 - Create Service Bus secondary namespace - Copy the ARM ID from the output as you need it later as -PartnerPartnernamespace. It usually starts with /subscriptions...
az servicebus namespace create --name $secondarynamespace --resource-group $resourceGroup --location $location2 --sku Premium

#Step 6 – Create an alias and pairs the namespaces
az servicebus georecovery-alias set --resource-group $resourceGroup --alias $aliasname --namespace-name $primarynamespace --partner-namespace "---ARM ID from step before---"

#Optional – you can obtain the alias details using this cmdlet, here you can also check if the provisioning is done. Provisioning state should change from Accepted to Succeeded when any of the below or above operations are completed. Note that after failover you want to add the secondary namespace instead of the primary namespace.
az servicebus georecovery-alias show --resource-group $resourceGroup --alias $aliasname --namespace-name $primarynamespace

#Optional – you can break the pairing between the two namespaces if you desire to associate a different pairing. Break pair is initiated over primary only
az servicebus georecovery-alias break-pair --resource-group $resourceGroup --alias $aliasname --namespace-name $primarynamespace

#Optional – create entities to test the meta data sync between primary and secondary namespaces
#Here we are creating a queue
az servicebus queue create --resource-group $resourceGroup --namespace-name $primarynamespace --name Q1

#Optional – check if the created queue was replicated
az servicebus queue show --resource-group $resourceGroup --namespace-name $secondarynamespace --name Q1

#Step 7 – Initiate a failover. This will break the pairing and alias will now point to the secondary namespace. Failover is initiated over secondary only
az servicebus georecovery-alias fail-over --resource-group $resourceGroup --alias $aliasname --namespace-name $secondarynamespace

#Step 8 – Remove the Geo-pairing and its configuration. This cmdlet would delete the alias that is associated with the Geo-paired namespaces
# Deleting the alias would then break the pairing and at this point primary and secondary namespaces are no more in sync. you need to add the namespacename of either primary or secondary depending on if you failed over or not. Below shows the state after failover.
az servicebus georecovery-alias delete --resource-group $resourceGroup --alias $aliasname --namespace-name $secondarynamespace

#Optional – clean up the created resoucegroup
az group delete --name $resourceGroup