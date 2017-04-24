###########################################################
# Start - Initialization - Invocation, Logging etc
###########################################################
$VerbosePreference = "SilentlyContinue"
$ErrorActionPreference = "Stop"

$scriptPath = $MyInvocation.MyCommand.Path
$scriptDir = Split-Path $scriptPath

& "$scriptDir\scripts\init.ps1"
if(-not $?)
{
    throw "Initialization failure."
}
###########################################################
# End - Initialization - Invocation, Logging etc
###########################################################

#Try to delete as much as we can
#$ErrorActionPreference = "SilentlyContinue"

$exclusions = @("*.spec", "*.zip")
& "$scriptDir\scripts\cleanup.ps1" "$scriptDir" -Exclusions $exclusions

