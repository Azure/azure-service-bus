param (
    [Parameter(Mandatory=$true)][string]$resourceGroup,
    [Parameter(Mandatory=$true)][string]$namespaceName,
    [string]$ruleName="SendListen"
 )

$env:SB_SAMPLES_CONNECTIONSTRING=(Get-AzureRmServiceBusKey -ResourceGroupName $resourceGroup -Namespace $namespaceName -Name $rulename).PrimaryConnectionString
$env:SB_SAMPLES_QUEUENAME="myqueue"
$env:SB_SAMPLES_TOPICNAME="mytopic"
$env:SB_SAMPLES_SUBSCRIPTIONNAME="mysub"
