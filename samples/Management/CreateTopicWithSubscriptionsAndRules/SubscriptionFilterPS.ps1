[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][string]$SubscriptionId,
    [Parameter(Mandatory)][string]$ResourceGroupName,
    [Parameter(Mandatory)][string]$NamespaceName,
    [Parameter(Mandatory)][string]$Location,
    [string]$TopicName = 'sample-topic'
)

$ErrorActionPreference = 'Stop'
if (-not $PSCmdlet.ShouldProcess("$SubscriptionId/$ResourceGroupName/$NamespaceName", "Create Standard namespace, topic '$TopicName' and three filtered subscriptions")) { return }
$scope = @{ SubscriptionId = $SubscriptionId; ResourceGroupName = $ResourceGroupName }
if (Get-AzServiceBusNamespace @scope | Where-Object Name -EQ $NamespaceName) {
    throw 'Refusing to overwrite an existing namespace. Choose a new disposable namespace name.'
}
New-AzServiceBusNamespace @scope -Name $NamespaceName -Location $Location -SkuName Standard
New-AzServiceBusTopic @scope -NamespaceName $NamespaceName -Name $TopicName
for ($i = 1; $i -le 3; $i++) {
    $subscription = "S$i"
    $ruleScope = $scope + @{ NamespaceName = $NamespaceName; TopicName = $TopicName; SubscriptionName = $subscription }
    New-AzServiceBusSubscription @scope -NamespaceName $NamespaceName -TopicName $TopicName -Name $subscription
    New-AzServiceBusRule @ruleScope -Name 'StoreFilter' -FilterType SqlFilter -SqlExpression "StoreId = 'Store$i'"
    # Leaving the default true rule would make the new filter ineffective.
    Remove-AzServiceBusRule @ruleScope -Name '$Default'
}
Write-Output 'Created S1/S2/S3 for Store1/Store2/Store3. Resources remain until manually deleted; no keys are printed.'
