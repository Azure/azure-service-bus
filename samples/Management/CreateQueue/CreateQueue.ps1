[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][string]$SubscriptionId,
    [Parameter(Mandatory)][string]$ResourceGroupName,
    [Parameter(Mandatory)][string]$NamespaceName,
    [Parameter(Mandatory)][string]$Location,
    [string]$QueueName = 'sample-queue'
)

$ErrorActionPreference = 'Stop'
if (-not $PSCmdlet.ShouldProcess("$SubscriptionId/$ResourceGroupName/$NamespaceName", "Create a Standard namespace and queue '$QueueName'")) { return }
$scope = @{ SubscriptionId = $SubscriptionId; ResourceGroupName = $ResourceGroupName }
if (Get-AzServiceBusNamespace @scope | Where-Object Name -EQ $NamespaceName) {
    throw 'Refusing to overwrite an existing namespace. Choose a new disposable namespace name.'
}
New-AzServiceBusNamespace @scope -Name $NamespaceName -Location $Location -SkuName Standard
New-AzServiceBusQueue @scope -NamespaceName $NamespaceName -Name $QueueName
Write-Output 'Resources remain billable until explicitly deleted. No access keys are printed and no cleanup is automatic.'
