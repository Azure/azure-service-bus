az login

az account set --subscription Azure_subscription_name

# Create a resource group
az group create --name my-resourcegroup --location eastus

# Create a Messaging namespace
az servicebus namespace create --name namespace-name --resource-group my-resourcegroup -l eastus2

# Create a queue
az servicebus queue create --resource-group my-resourcegroup --namespace-name namespace_name --name queue-name

# Get the connection string
az servicebus namespace authorization-rule keys list --resource-group my-resourcegroup --namespace-name namespace-name --name RootManageSharedAccessKey

# Delete the created resources if desired
az group delete --resource-group my-resourcegroup