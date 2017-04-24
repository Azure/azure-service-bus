###########################################################
# Start - Initialization - Invocation, Logging etc
###########################################################
$VerbosePreference = "SilentlyContinue"
$ErrorActionPreference = "Stop"

$scriptPath = $MyInvocation.MyCommand.Path
$scriptDir = Split-Path $scriptPath

& "$scriptDir\..\scripts\init.ps1"
if(-not $?)
{
    throw "Initialization failure."
}
###########################################################
# End - Initialization - Invocation, Logging etc
###########################################################

#Create Run Configuration
$configFile = Join-Path $env:userprofile "azure-msg-config.properties" 

& "$scriptDir\..\scripts\config\CreateRunConfig.ps1" $configFile
if(-not $?)
{
    Write-ErrorLog "Creation of Run Configuration failed. Please check the logs for error information or retry." (Get-ScriptName) (Get-ScriptLineNumber)
    throw "Creation of Run Configuration failed. Please check the logs for error information or retry."
}

#Create Azure Resources
& "$scriptDir\..\scripts\azure\CreateAzureResources.ps1" $scriptDir $configFile
if(-not $?)
{
    Write-ErrorLog "Creation of Azure Resources failed. Please check the logs for error information or retry." (Get-ScriptName) (Get-ScriptLineNumber)
    throw "Creation of Azure Resources failed. Please check the logs for error information or retry."
}
