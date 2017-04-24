[CmdletBinding()]
Param(
    [parameter(Mandatory=$true)]
    [string]$FileName
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

Write-SpecialLog "Reading configurations from $FileName" (Get-ScriptName) (Get-ScriptLineNumber)

if(-not (Test-Path $FileName))
{
    Write-ErrorLog "Configuration file $FileName not found!" (Get-ScriptName) (Get-ScriptLineNumber)
    throw "Configuration file not found!"
}

$content = Get-Content $FileName

$runConfig = @{}
foreach($line in $content)
{
    if(-not $line.startsWith("#"))
    {
        $a = $line.split("=", 2)
        if($a.Length -eq 2)
        {
            $runConfig.Add($a[0].trim(), $a[1].trim())
        }
    }
}

if($runConfig.Count -eq 0)
{
    Write-ErrorLog "No run configurations found!" (Get-ScriptName) (Get-ScriptLineNumber)
    throw "No run configurations found!"
}

return $runConfig