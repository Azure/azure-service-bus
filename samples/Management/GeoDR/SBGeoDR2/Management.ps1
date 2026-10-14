[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][string]$SubscriptionId,
    [Parameter(Mandatory)][string]$ResourceGroupName,
    [Parameter(Mandatory)][string]$PrimaryNamespace,
    [Parameter(Mandatory)][string]$SecondaryNamespace,
    [Parameter(Mandatory)][string]$Alias,
    [ValidateSet('pair', 'status-primary', 'status-secondary', 'failover', 'break-pairing', 'delete-primary-alias', 'delete-secondary-alias')]
    [string]$Action = 'status-primary'
)

$ErrorActionPreference = 'Stop'
if ($PrimaryNamespace -eq $SecondaryNamespace -or $Alias -in @($PrimaryNamespace, $SecondaryNamespace)) {
    throw 'Use distinct primary, secondary and alias names. Use the .NET namespace-name variant for that migration.'
}
$target = if ($Action -in @('status-secondary', 'failover', 'delete-secondary-alias')) { $SecondaryNamespace } else { $PrimaryNamespace }
if ($Action -notlike 'status-*' -and -not $PSCmdlet.ShouldProcess("$SubscriptionId/$ResourceGroupName/$target/$Alias", $Action)) { return }
$scope = @{ SubscriptionId = $SubscriptionId; ResourceGroupName = $ResourceGroupName; NamespaceName = $target; Name = $Alias }
switch ($Action) {
    'pair' {
        $partner = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroupName/providers/Microsoft.ServiceBus/namespaces/$SecondaryNamespace"
        New-AzServiceBusGeoDRConfiguration @scope -PartnerNamespace $partner
    }
    'status-primary' { Get-AzServiceBusGeoDRConfiguration @scope }
    'status-secondary' { Get-AzServiceBusGeoDRConfiguration @scope }
    'failover' { Set-AzServiceBusGeoDRConfigurationFailOver @scope }
    'break-pairing' { Set-AzServiceBusGeoDRConfigurationBreakPair @scope }
    'delete-primary-alias' { Remove-AzServiceBusGeoDRConfiguration @scope }
    'delete-secondary-alias' { Remove-AzServiceBusGeoDRConfiguration @scope }
}
Write-Output 'Inspect alias status before the next action. Metadata-only Geo-DR does not replicate messages; no namespace is deleted.'
