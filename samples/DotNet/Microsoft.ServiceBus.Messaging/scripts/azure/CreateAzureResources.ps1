[CmdletBinding(PositionalBinding=$True)]
Param(
    [parameter(Mandatory=$true)]
    [string]$ExampleDir,
    [parameter(Mandatory=$true)]
    [string]$configFile
    )

###########################################################
# Start - Initialization - Invocation, Logging etc
###########################################################
$VerbosePreference = "SilentlyContinue"
$ErrorActionPreference = "Stop"

$scriptPath = $MyInvocation.MyCommand.Path
$scriptDir = Split-Path $scriptPath

& "$scriptDir\..\init.ps1"
if(-not $?)
{
    throw "Initialization failure."
    exit -9999
}
###########################################################
# End - Initialization - Invocation, Logging etc
###########################################################

###########################################################
# Main Script
###########################################################

# Make sure you run this in Microsoft Azure Powershell prompt
if(-not (& "$scriptDir\CheckAzurePowershell.ps1"))
{
    Write-ErrorLog "Check Azure Powershell Failed! You need to run this script from Azure Powershell." (Get-ScriptName) (Get-ScriptLineNumber)
    throw "Check Azure Powershell Failed! You need to run this script from Azure Powershell."
}

###########################################################
# Get Run Configuration
###########################################################
# Make sure you run this in Microsoft Azure Powershell prompt
if(-not (Test-Path $configFile))
{
    Write-ErrorLog "No run configuration file found at '$configFile'" (Get-ScriptName) (Get-ScriptLineNumber)
    throw "No run configuration file found at '$configFile'"
}
$config = & "$scriptDir\..\config\ReadConfig.ps1" $configFile

###########################################################
# Add Azure Account
###########################################################
$account = Get-AzureAccount
if($account -eq $null)
{
    $account = Add-AzureAccount
    if($account -eq $null)
    {
        Write-ErrorLog "Failed to add Azure Account." (Get-ScriptName) (Get-ScriptLineNumber)
        throw "Failed to add Azure Account."
    }
}
Write-SpecialLog ("Using Azure Account: " + $account.Name) (Get-ScriptName) (Get-ScriptLineNumber)

$subscriptions = Get-AzureSubscription
$subName = ($subscriptions | ? { $_.SubscriptionName -eq $config["AZURE_SUBSCRIPTION_NAME"] } | Select-Object -First 1 ).SubscriptionName
if($subName -eq $null)
{
    $subNames = $subscriptions | % { "`r`n" + $_.SubscriptionName + " - " + $_.SubscriptionId}
    Write-InfoLog ("Available Subscription Names (Name - Id):" + $subNames) (Get-ScriptName) (Get-ScriptLineNumber)

    $subName = Read-Host "Enter subscription name"

    #Update the Azure Subscription Id in config
    & "$scriptDir\..\config\ReplaceStringInFile.ps1" $configFile $configFile @{AZURE_SUBSCRIPTION_NAME=$subName}
    
    ###########################################################
    # Refresh Run Configuration
    ###########################################################
    $config = & "$scriptDir\..\config\ReadConfig.ps1" $configFile
}

Write-SpecialLog "Current run configuration:" (Get-ScriptName) (Get-ScriptLineNumber)
$config.Keys | sort | % { if(-not ($_.Contains("PASSWORD") -or $_.Contains("KEY"))) { Write-SpecialLog ("Key = " + $_ + ", Value = " + $config[$_]) (Get-ScriptName) (Get-ScriptLineNumber) } }

Write-SpecialLog ("Using subscription: " + $config["AZURE_SUBSCRIPTION_NAME"]) (Get-ScriptName) (Get-ScriptLineNumber)
Select-AzureSubscription -SubscriptionName $config["AZURE_SUBSCRIPTION_NAME"]

###########################################################
# Check Azure Resource Creation List
###########################################################


$startTime = Get-Date


###########################################################
# Create Azure Resources
###########################################################


Write-SpecialLog "Creating ServiceBus Resources" (Get-ScriptName) (Get-ScriptLineNumber)
        
Select-AzureSubscription -SubscriptionName $subName
& "$scriptDir\..\init.ps1"
Write-InfoLog "Creating Service Bus Resources" (Get-ScriptName) (Get-ScriptLineNumber)
$sbKeys = & "$scriptDir\ServiceBus\CreateServiceBusResources.ps1" $config["SERVICEBUS_NAMESPACE"] $config["SERVICEBUS_ENTITY_PATH"] $config["AZURE_LOCATION"] $config["SERVICEBUS_SEND_KEY"] $config["SERVICEBUS_LISTEN_KEY"] $config["SERVICEBUS_MANAGE_KEY"] 
if($sbKeys)
{
    & "$scriptDir\..\config\ReplaceStringInFile.ps1" $configFile $configFile @{SERVICEBUS_SEND_KEY=$sbKeys["samplesend"]}
    & "$scriptDir\..\config\ReplaceStringInFile.ps1" $configFile $configFile @{SERVICEBUS_LISTEN_KEY=$sbKeys["samplelisten"]}
    & "$scriptDir\..\config\ReplaceStringInFile.ps1" $configFile $configFile @{SERVICEBUS_MANAGE_KEY=$sbKeys["samplemanage"]}
}

$finishTime = Get-Date
$totalSeconds = ($finishTime - $startTime).TotalSeconds
Write-InfoLog "Azure resources created, completed in $totalSeconds seconds." (Get-ScriptName) (Get-ScriptLineNumber)