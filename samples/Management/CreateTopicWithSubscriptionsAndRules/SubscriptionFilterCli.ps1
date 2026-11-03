#requires -Version 7.4
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][string]$SubscriptionId,
    [Parameter(Mandatory)][string]$ResourceGroupName,
    [Parameter(Mandatory)][string]$NamespaceName,
    [Parameter(Mandatory)][string]$Location,
    [string]$TopicName = 'sample-topic'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if (-not $PSCmdlet.ShouldProcess("$SubscriptionId/$ResourceGroupName/$NamespaceName", "Create Standard namespace, topic '$TopicName' and three filtered subscriptions")) { return }
$existing = az servicebus namespace list --subscription $SubscriptionId --resource-group $ResourceGroupName --only-show-errors --output json | ConvertFrom-Json
if ($existing | Where-Object name -EQ $NamespaceName) { throw 'Refusing to overwrite an existing namespace.' }
az servicebus namespace create --subscription $SubscriptionId --resource-group $ResourceGroupName --name $NamespaceName --location $Location --sku Standard --only-show-errors
az servicebus topic create --subscription $SubscriptionId --resource-group $ResourceGroupName --namespace-name $NamespaceName --name $TopicName --only-show-errors
for ($i = 1; $i -le 3; $i++) {
    $subscription = "S$i"
    az servicebus topic subscription create --subscription $SubscriptionId --resource-group $ResourceGroupName --namespace-name $NamespaceName --topic-name $TopicName --name $subscription --only-show-errors
    az servicebus topic subscription rule create --subscription $SubscriptionId --resource-group $ResourceGroupName --namespace-name $NamespaceName --topic-name $TopicName --subscription-name $subscription --name StoreFilter --filter-sql-expression "StoreId = 'Store$i'" --only-show-errors
    az servicebus topic subscription rule delete --subscription $SubscriptionId --resource-group $ResourceGroupName --namespace-name $NamespaceName --topic-name $TopicName --subscription-name $subscription --name '$Default' --only-show-errors
}
Write-Output 'Created S1/S2/S3 for Store1/Store2/Store3. Resources remain until manually deleted; no keys are printed.'
