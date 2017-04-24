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

& "$scriptDir\..\scripts\build\buildCSharp.bat" "$scriptDir"

if($LASTEXITCODE -eq 0)
{
    Write-SpecialLog "Build Complete for '$scriptDir!'" (Get-ScriptName) (Get-ScriptLineNumber)
}
else
{
    Write-ErrorLog "Build returned non-zero exit code: $LASTEXITCODE. Please check if the project built successfully before you can launch examples." (Get-ScriptName) (Get-ScriptLineNumber)
    throw "Build returned non-zero exit code: $LASTEXITCODE. Please check if the project built successfully before you can launch examples."
}