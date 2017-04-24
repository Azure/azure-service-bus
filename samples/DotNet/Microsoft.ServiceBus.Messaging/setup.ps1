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


& "$scriptDir\scripts\prepare.ps1"

$buildList=@()
$buildErrorList=@()

$projects = gci -Directory $scriptDir
Write-SpecialLog "Building Projects" (Get-ScriptName) (Get-ScriptLineNumber)
Write-SpecialLog "=================" (Get-ScriptName) (Get-ScriptLineNumber)
$projects | % {
    if(Test-Path (Join-Path $_ "build.ps1"))
    {
        Write-SpecialLog ("Building Project: " + $_) (Get-ScriptName) (Get-ScriptLineNumber)
        pushd $_
        $projectName=$_.FullName
        try
        {
            .\build.ps1
            if($LASTEXITCODE -ne 0) { $buildErrorList += $projectName } else { $buildList += $projectName };
        }
        catch
        {
            $buildErrorList += $projectName
            Write-ErrorLog "An exception has occurred while building: $projectName" (Get-ScriptName) (Get-ScriptLineNumber) $_
        }
        finally
        {
            popd
        }
    }
}

if($buildErrorList.Count -ne 0)
{
    Write-ErrorLog "ERROR: One or more projects failed to build:" (Get-ScriptName) (Get-ScriptLineNumber)
    $buildErrorList | % { Write-ErrorLog $_ (Get-ScriptName) (Get-ScriptLineNumber) }
    Write-SpecialLog "Projects built successfully:" (Get-ScriptName) (Get-ScriptLineNumber)
    $buildList | % { Write-SpecialLog $_  (Get-ScriptName) (Get-ScriptLineNumber) }
    throw "Some projects failed to build, please check build output for error information."
}
else
{
    Write-SpecialLog "SUCCESS: All projects built successfully!" (Get-ScriptName) (Get-ScriptLineNumber)
    $buildList | % { Write-SpecialLog $_  (Get-ScriptName) (Get-ScriptLineNumber) }
}

