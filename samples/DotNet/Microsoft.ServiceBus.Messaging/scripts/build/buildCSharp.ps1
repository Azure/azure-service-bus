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

$nugetExePath = Join-Path $scriptDir "..\..\tools\nuget"
$env:Path = $env:Path + ";" + $nugetExePath

$buildList=@()
$buildErrorList=@()

$csharpProjects = gci -Recurse -Filter *.sln
Write-SpecialLog "Building CSharp Projects" (Get-ScriptName) (Get-ScriptLineNumber)
Write-SpecialLog "========================" (Get-ScriptName) (Get-ScriptLineNumber)
$csharpProjects | % {
    Write-SpecialLog ("Building CSharp Project: " + $_.Directory) (Get-ScriptName) (Get-ScriptLineNumber)
    pushd $_.Directory
    $projectName=$_.FullName
    try
    {
       nuget-restore -verbosity detailed $projectName
       msbuild.exe /m /fl /flp:"Verbosity=Detailed" /clp:verbosity="Minimal;Summary" /t:"Clean;Build" /p:configuration="Debug" $projectName
       if($LASTEXITCODE -ne 0) { $buildErrorList += $projectName } else { $buildList += $projectName };
    }
    catch [System.Management.Automation.CommandNotFoundException]
    {
        $buildErrorList += $projectName
        Write-ErrorLog "An exception has occurred while building: $projectName" (Get-ScriptName) (Get-ScriptLineNumber) $_
    }
    finally
    {
        popd
    }
}

Write-SpecialLog "`r`nProject building complete!`r`n" (Get-ScriptName) (Get-ScriptLineNumber)
Write-SpecialLog "Build Summary:" (Get-ScriptName) (Get-ScriptLineNumber)
Write-SpecialLog "======================" (Get-ScriptName) (Get-ScriptLineNumber)

if($buildErrorList.Count -ne 0)
{
    Write-ErrorLog "ERROR: One or more projects failed to build:" (Get-ScriptName) (Get-ScriptLineNumber)
    $buildErrorList | % { Write-ErrorLog $_  (Get-ScriptName) (Get-ScriptLineNumber) }
    Write-SpecialLog "Projects built successfully:" (Get-ScriptName) (Get-ScriptLineNumber)
    $buildList | % { Write-SpecialLog $_  (Get-ScriptName) (Get-ScriptLineNumber) }
    throw "Some projects failed to build, please check build output for error information."
}
else
{
    Write-SpecialLog "SUCCESS: All projects built successfully!" (Get-ScriptName) (Get-ScriptLineNumber)
    $buildList | % { Write-SpecialLog $_  (Get-ScriptName) (Get-ScriptLineNumber) }
}