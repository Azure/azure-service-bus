param (
    [Parameter(Mandatory=$true)][string]$resourceGroup,
    [Parameter(Mandatory=$true)][string]$namespaceName
 )

#SERVICEBUS configurations

$configFile = Join-Path $env:userprofile "azure-msg-config.properties" 
"SERVICEBUS_NAMESPACE = $namespaceName" > $configFile
"SERVICEBUS_ENTITY_PATH = msgsamples" >> $configFile
"SERVICEBUS_FQDN_ENDPOINT = "+(Get-AzureRmServiceBusNamespace -ResourceGroupName "$resourceGroup" -NamespaceName "$namespaceName").ServiceBusEndpoint >> $configFile
"SERVICEBUS_SEND_KEY = "+(Get-AzureRmServiceBusKey -ResourceGroupName "$resourceGroup" -Namespace "$namespaceName" -Name "samplesend").PrimaryKey >> $configFile
"SERVICEBUS_LISTEN_KEY = "+(Get-AzureRmServiceBusKey -ResourceGroupName "$resourceGroup" -Namespace "$namespaceName" -Name "samplelisten").PrimaryKey >> $configFile 
"SERVICEBUS_MANAGE_KEY = "+(Get-AzureRmServiceBusKey -ResourceGroupName "$resourceGroup" -Namespace "$namespaceName" -Name "samplemanage").PrimaryKey >> $configFile
