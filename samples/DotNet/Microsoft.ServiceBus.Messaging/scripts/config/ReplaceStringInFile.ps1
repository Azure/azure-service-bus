[CmdletBinding(PositionalBinding=$True)]
Param(
    [string]
    [parameter( Position=0, Mandatory=$true)]
    $TempFilePath,
    [string]
    [parameter( Position=1, Mandatory=$true)]
    $FilePath,
    [hashtable]
    [parameter( Position=2, Mandatory=$true)]
    $Config
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

Write-SpecialLog "Replacing configurations in $FilePath" (Get-ScriptName) (Get-ScriptLineNumber)

$content = Get-Content $TempFilePath
foreach( $key in $config.Keys )
{
    $newVal = $config[$key]
    if($content -like "*{$key}*")
    {
        Write-InfoLog ("Updating $key with value: " + $config[$key]) (Get-ScriptName) (Get-ScriptLineNumber)
    }
    $content = $content -replace "{$key}", $newVal
}

$fileFolder = Split-Path $FilePath
if(-not (Test-Path $fileFolder))
{
    mkdir $fileFolder -ErrorAction SilentlyContinue
}

#Multi-threaded protection with retry
$fileName = Split-Path -Leaf $FilePath
$mutex = new-object System.Threading.Mutex $false,$fileName
$mutex.WaitOne() > $null
$done = $false
$retry = 1
while((-not $done) -and ($retry -le 5))
{
    try
    {
        Set-Content -path $FilePath -value $content
        $done = $true
    }
    catch 
    {
        $retry++
        sleep -Milliseconds 50
    }
}
$mutex.ReleaseMutex()
