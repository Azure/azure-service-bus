[CmdletBinding(PositionalBinding=$True)]
Param(
    [parameter(Mandatory=$true)]
    [string]$ExampleDir,
    [parameter(Mandatory=$false)]
    [string[]]$Exclusions
    )
    
###########################################################
# Start - Initialization - Invocation, Logging etc
###########################################################
$VerbosePreference = "SilentlyContinue"
$ErrorActionPreference = "Stop"

$scriptPath = $MyInvocation.MyCommand.Path
$scriptDir = Split-Path $scriptPath

& "$scriptDir\init.ps1"
if(-not $?)
{
    throw "Initialization failure."
    exit -9999
}
###########################################################
# End - Initialization - Invocation, Logging etc
###########################################################

function Clean-ExampleFolder($cleanupDir)
{
    if(-not (Test-Path $cleanupDir))
    {
        return;
    }

    Write-InfoLog "Cleaning $cleanupDir" (Get-ScriptName) (Get-ScriptLineNumber)

    Get-ChildItem -Directory -Recurse -Path $cleanupDir -Include "bin", "obj", "packages", "target" -Exclude $Exclusions | % `
    {
        Write-InfoLog "Deleting $_" (Get-ScriptName) (Get-ScriptLineNumber)
        Remove-Item -Path $_ -Recurse -Force -ErrorAction SilentlyContinue
    }

    $Inclusions = @("SubmitConfig.xml", "*.spec", "*.zip", "*.suo", "*.user", "*.out", "*.log")

    Get-ChildItem -Recurse -Path $cleanupDir -Include $Inclusions -Exclude $Exclusions | % `
    {
        Write-InfoLog "Deleting $_" (Get-ScriptName) (Get-ScriptLineNumber)
        Remove-Item -Path $_ -Force -ErrorAction SilentlyContinue
    }

    Get-ChildItem -Recurse -Hidden -Path $cleanupDir -Include $Inclusions -Exclude $Exclusions | % `
    {
        Write-InfoLog "Deleting $_" (Get-ScriptName) (Get-ScriptLineNumber)
        Remove-Item -Path $_ -Force -ErrorAction SilentlyContinue
    }
}

#Try to delete as much as we can
#$ErrorActionPreference = "SilentlyContinue"

$ExampleDir = $ExampleDir.Replace("""","")
$runConfigurationFile = Join-Path $env:userprofile "azure-msg-config.properties" 

Write-InfoLog "Checking for a run configuration file. Path: $runConfigurationFile" (Get-ScriptName) (Get-ScriptLineNumber)
if(Test-Path $runConfigurationFile)
{
    Write-InfoLog "Run configuration file found. Path: $runConfigurationFile" (Get-ScriptName) (Get-ScriptLineNumber)
    Write-SpecialLog "===== Azure Resources clean-up =====" (Get-ScriptName) (Get-ScriptLineNumber)
   & "$scriptDir\azure\CleanAzureResources.ps1" "$ExampleDir" "$runConfigurationFile"
   
}
else
{
    Write-WarnLog "Run configuration file not found. Path: $runConfigurationFile" (Get-ScriptName) (Get-ScriptLineNumber)
}

if($Exclusions)
{
    Write-SpecialLog "===== File and folder clean-up =====" (Get-ScriptName) (Get-ScriptLineNumber)
    Write-SpecialLog "Exclusions: $Exclusions" (Get-ScriptName) (Get-ScriptLineNumber)
}

Clean-ExampleFolder "$scriptDir\..\tools"
Clean-ExampleFolder "$ExampleDir"

Get-ChildItem -Directory -Recurse -Path $ExampleDir -Include "run" -Exclude $Exclusions | % `
{
    Write-InfoLog "Deleting $_" (Get-ScriptName) (Get-ScriptLineNumber)
    Remove-Item -Path $_ -Recurse -Force -ErrorAction SilentlyContinue
}

if(Test-Path "$scriptDir\..\packages")
{
    Remove-Item "$scriptDir\..\packages" -Force -Recurse -ErrorAction SilentlyContinue
}

Remove-Module "Logging-ServiceBusMsgSamples.psm1" -Force -ErrorAction SilentlyContinue