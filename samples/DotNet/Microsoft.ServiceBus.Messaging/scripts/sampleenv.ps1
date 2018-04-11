param (
    [Parameter(Mandatory=$true)][string]$resourceGroup,
    [Parameter(Mandatory=$true)][string]$namespaceName,
    [string]$ruleName="SendListen"
 )

function Get-ScriptDirectory
{
$Invocation = (Get-Variable MyInvocation -Scope 1).Value
Split-Path $Invocation.MyCommand.Path
}

$configFile = Join-Path Get-ScriptDirectory "../common/azure-msg-config.properties" 
"SB_SAMPLES_CONNECTIONSTRING="+(Get-AzureRmServiceBusKey -ResourceGroupName $resourceGroup -Namespace $namespaceName -Name $rulename).PrimaryConnectionString > $configFile
"SB_SAMPLES_QUEUENAME=BasicQueue" >> $configFile
"SB_SAMPLES_TOPICNAME=BasicTopic" >> $configFile
"SB_SAMPLES_SUBSCRIPTIONNAME=Subscription1" >> $configFile
