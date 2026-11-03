#requires -Version 7.4
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][string]$SubscriptionId,
    [Parameter(Mandatory)][string]$ResourceGroupName,
    [Parameter(Mandatory)][string]$NamespaceName,
    [Parameter(Mandatory)][string]$Location,
    [string]$QueueName = 'sample-queue'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if (-not $PSCmdlet.ShouldProcess("$SubscriptionId/$ResourceGroupName/$NamespaceName", "Create a Standard namespace and queue '$QueueName'")) { return }
$existing = az servicebus namespace list --subscription $SubscriptionId --resource-group $ResourceGroupName --only-show-errors --output json | ConvertFrom-Json
if ($existing | Where-Object name -EQ $NamespaceName) { throw 'Refusing to overwrite an existing namespace.' }
az servicebus namespace create --subscription $SubscriptionId --resource-group $ResourceGroupName --name $NamespaceName --location $Location --sku Standard --only-show-errors
az servicebus queue create --subscription $SubscriptionId --resource-group $ResourceGroupName --namespace-name $NamespaceName --name $QueueName --only-show-errors
Write-Output 'Resources remain billable until explicitly deleted. No access keys are printed and no cleanup is automatic.'