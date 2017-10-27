param (
    [Parameter(Mandatory=$true)][string]$resourceGroup,
    [Parameter(Mandatory=$true)][string]$namespaceName,
    [string]$ruleName="SendListen"
 )

$env:SB_SAMPLES_CONNECTIONSTRING=(Get-AzureRmServiceBusKey -ResourceGroupName $resourceGroup -Namespace $namespaceName -Name $rulename).PrimaryConnectionString
$env:SB_SAMPLES_MANAGE_CONNECTIONSTRING=(Get-AzureRmServiceBusKey -ResourceGroupName $resourceGroup -Namespace $namespaceName -Name "RootManageSharedAccessKey").PrimaryConnectionString
$env:SB_SAMPLES_QUEUENAME="BasicQueue"
$env:SB_SAMPLES_TOPICNAME="BasicTopic"
$env:SB_SAMPLES_SUBSCRIPTIONNAME="Subscription1"
