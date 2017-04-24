###########################################################
# Start - Initialization - Invocation, Logging etc
###########################################################
$VerbosePreference = "SilentlyContinue"
$ErrorActionPreference = "Stop"

$scriptPath = $MyInvocation.MyCommand.Path
$scriptDir = Split-Path $scriptPath

& "$scriptDir\..\..\init.ps1"
if(-not $?)
{
    throw "Initialization failure."
    exit -9999
}
###########################################################
# End - Initialization - Invocation, Logging etc
###########################################################

$nugetExe = Join-Path $scriptDir "..\..\..\tools\nuget\nuget.exe"
$install = & "$nugetExe" install WindowsAzure.ServiceBus -version 3.0 -OutputDirectory "$scriptDir\..\..\..\packages"

$sbNuget = (gci "$scriptDir\..\..\..\packages\WindowsAzure.ServiceBus.*")[0].FullName

$sbDll = Join-Path $sbNuget "lib\net45-full\Microsoft.ServiceBus.dll"

if(-not (Test-Path $sbDll))
{
    throw "ERROR: $sbDll not found. Please make sure you have WindowsAzure.ServiceBus nuget package available."
}

return $sbDll