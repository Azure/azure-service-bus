[CmdletBinding(PositionalBinding=$True)]
Param(
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
# Create Run Configuration
###########################################################

Write-SpecialLog "Step 0: Creating Run Configuration" (Get-ScriptName) (Get-ScriptLineNumber)

$config = @{
    SERVICEBUS_NAMESPACE = "msgSample" + [System.DateTime]::Now.ToString("yyMMddHHmmss");
    AZURE_LOCATION="North Europe";
}

if(-not (Test-Path $configFile))
{
    Write-InfoLog "Creating a new run configuration at $configFile" (Get-ScriptName) (Get-ScriptLineNumber)
    &$scriptDir\ReplaceStringInFile.ps1 "$scriptDir\configurations.properties.template" $configFile $config
}
else
{
    Write-InfoLog "An existing run configuration was found at $configFile, just updating newer entries." (Get-ScriptName) (Get-ScriptLineNumber)
    &$scriptDir\ReplaceStringInFile.ps1 $configFile $configFile $config
}

$config = & "$scriptDir\ReadConfig.ps1" $configFile
$config.Keys | sort | % { if(-not ($_.Contains("PASSWORD") -or $_.Contains("KEY"))) { Write-InfoLog ("Key = " + $_ + ", Value = " + $config[$_]) (Get-ScriptName) (Get-ScriptLineNumber) } }

$configFile