#requires -Version 7.4
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
$PSNativeCommandUseErrorActionPreference = $true
$target = if ($Action -in @('status-secondary', 'failover', 'delete-secondary-alias')) { $SecondaryNamespace } else { $PrimaryNamespace }
if ($Action -notlike 'status-*' -and -not $PSCmdlet.ShouldProcess("$SubscriptionId/$ResourceGroupName/$target/$Alias", $Action)) { return }
$scope = @('--subscription', $SubscriptionId, '--resource-group', $ResourceGroupName, '--namespace-name', $target, '--alias', $Alias, '--only-show-errors')
switch ($Action) {
    'pair' {
        $partner = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroupName/providers/Microsoft.ServiceBus/namespaces/$SecondaryNamespace"
        az servicebus georecovery-alias set @scope --partner-namespace $partner
    }
    'status-primary' { az servicebus georecovery-alias show @scope }
    'status-secondary' { az servicebus georecovery-alias show @scope }
    'failover' { az servicebus georecovery-alias fail-over @scope }
    'break-pairing' { az servicebus georecovery-alias break-pair @scope }
    'delete-primary-alias' { az servicebus georecovery-alias delete @scope }
    'delete-secondary-alias' { az servicebus georecovery-alias delete @scope }
}
Write-Output 'Inspect alias status before the next action. Metadata-only Geo-DR does not replicate messages; no namespace is deleted.'
