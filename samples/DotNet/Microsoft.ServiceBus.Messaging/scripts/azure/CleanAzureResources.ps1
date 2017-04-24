[CmdletBinding(PositionalBinding=$True)]
Param(
    [parameter(Mandatory=$true)]
    [string] $ExampleDir,
    [parameter(Mandatory=$true)]
    [string] $configFile
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

$startTime = Get-Date

Write-SpecialLog "Deleting Azure resources for example: $ExampleDir" (Get-ScriptName) (Get-ScriptLineNumber)

$config = & "$scriptDir\..\config\ReadConfig.ps1" $configFile

$config.Keys | sort | % { if(-not ($_.Contains("PASSWORD") -or $_.Contains("KEY"))) { Write-SpecialLog ("Key = " + $_ + ", Value = " + $config[$_]) (Get-ScriptName) (Get-ScriptLineNumber) } }

$subName = $config["AZURE_SUBSCRIPTION_NAME"]
Write-SpecialLog "Using subscription '$subName'" (Get-ScriptName) (Get-ScriptLineNumber)
Select-AzureSubscription -SubscriptionName $subName

#Changing Error Action to Continue here onwards to have maximum resource deletion
$ErrorActionPreference = "Continue"

$success = $true

Write-InfoLog "Deleting ServiceBus" (Get-ScriptName) (Get-ScriptLineNumber)
& "$scriptDir\ServiceBus\DeleteServiceBusResources.ps1" $config["SERVICEBUS_NAMESPACE"] $config["SERVICEBUS_ENTITY_PATH"]
$success = $success -and $?

if($success)
{
    Write-SpecialLog "Deleting configuration.properties file" (Get-ScriptName) (Get-ScriptLineNumber)
    Remove-Item $configFile
    $totalSeconds = ((Get-Date) - $startTime).TotalSeconds
    Write-SpecialLog "Deleted Azure resources, completed in $totalSeconds seconds" (Get-ScriptName) (Get-ScriptLineNumber)
}
else
{
    Write-ErrorLog "One or more errors occurred during Azure resource deletion. Please check logs for error information." (Get-ScriptName) (Get-ScriptLineNumber)
    Write-ErrorLog "Please retry and delete your configuration file manually from: $configFile" (Get-ScriptName) (Get-ScriptLineNumber)
    throw "One or more errors occurred during Azure resource deletion. Please check logs for error information."
}