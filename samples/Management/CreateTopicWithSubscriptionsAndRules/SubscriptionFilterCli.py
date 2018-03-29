az login
az account set --subscription "Your subscription"

# Add resource group
az group create --name FilterTest2 --location westus2

# Add namespace
az servicebus namespace create --name FilterTest2 --resource-group FilterTest2 --location westus2

# Add topic
az servicebus topic create --resource-group FilterTest2 --namespace-name FilterTest2 --
name FilterTest2

# Add subscription 1
az servicebus topic subscription create --resource-group FilterTest2 --namespace-name F
ilterTest2 --topic-name FilterTest2 --name S1

# Add subscription 2
az servicebus topic subscription create --resource-group FilterTest2 --namespace-name F
ilterTest2 --topic-name FilterTest2 --name S2

# Add subscription 3
az servicebus topic subscription create --resource-group FilterTest2 --namespace-name F
ilterTest2 --topic-name FilterTest2 --name S3

# Add filter 1
az servicebus topic subscription rule create --resource-group FilterTest2 --namespace-name FilterTest2 --topic-name FilterTest2 --subscription-name --name MyFilter --filter-sql-expression "StoreId = 'Store1'"

az servicebus topic subscription rule create --resource-group FilterTest2 --namespace-name FilterTest2 --topic-name FilterTest2 --subscription-name S1 --name MyFilter --filter-sql-expression "StoreId = 'Store1'"

# Add filter 2
az servicebus topic subscription rule create --resource-group FilterTest2 --namespace-name FilterTest2 --topic-name FilterTest2 --subscription-name S2 --name MyFilter --filter-sql-expression "StoreId = 'Store2'"

# Add filter 3
az servicebus topic subscription rule create --resource-group FilterTest2 --namespace-name FilterTest2 --topic-name FilterTest2 --subscription-name S3 --name MyFilter --filter-sql-expression "StoreId = 'Store3'"

# Get your access key for later
az servicebus namespace authorization-rule keys list --resource-group FilterTest2 --namespace-name FilterTest2 --name RootManageSharedAccessKey