function Get-LogFile()
{
    if([String]::IsNullOrWhiteSpace($LogFile))
    {
        $LogDir = "log"
        if(-not (Test-Path $LogDir))
        {
            mkdir $LogDir -ErrorAction SilentlyContinue
        }
        $LogFile = Join-Path $LogDir ("servicebus-msg-samples-" + [System.DateTime]::Now.ToString("yyyyMMddHH") + ".log")
    }
 
    return $LogFile
}

function Get-ScriptName()
{
    if ([String]::IsNullOrWhiteSpace($MyInvocation.ScriptName))
    {
        return $MyInvocation.InvocationName
    }
    else
    {
        return Split-Path -Leaf $MyInvocation.ScriptName
    }
}

function Get-ScriptLineNumber()
{
    return $MyInvocation.ScriptLineNumber
}

function Format-LogMessage([string] $level, [string] $msg, [string] $scriptName = $null, [string] $scriptLineNumber = $null, [object] $pipelineObject = $null)
{
    $msg = ("{0} [{1}] [{3}(ln:{4})] - {2}" -f [System.DateTime]::Now.ToString("T"), $level, $msg, $scriptName, $scriptLineNumber)
    if($pipelineObject -ne $null)
    {
        $msg += "`r`n" + $($pipelineObject | Out-String)
    }
    return $msg;
}

function Write-LogToFile([string] $logFile, [string] $msg)
{
    #Multi-threaded protection with retry
    $mutexName = Split-Path -Leaf $logFile
    $mutex = new-object System.Threading.Mutex $false,$mutexName
    $mutex.WaitOne() > $null
    $done = $false
    $retry = 1
    while((-not $done) -and ($retry -le 5))
    {
        try
        {
            Out-File -FilePath $logFile -InputObject $msg -Append -Encoding utf8
            $done = $true
        }
        catch 
        {
            $retry++
            sleep -Milliseconds 50
        }
    }
    $mutex.ReleaseMutex()
}

function Write-Log([string] $level, [string] $msg, [string] $scriptName = $null, [string] $scriptLineNumber = $null, [object] $pipelineObject = $null)
{
    if([String]::IsNullOrWhiteSpace($logFile))
    {
        $logFile = Get-LogFile
    }
    
    $special = $false
    if($level.Equals("SPECIAL", [System.StringComparison]::OrdinalIgnoreCase))
    {
        $level = "INFO"
        $special = $true
    }

    $msg = Format-LogMessage $level $msg $scriptName $scriptLineNumber $pipelineObject
    Write-LogToFile $logFile $msg;

    switch($level.ToUpperInvariant())
	{
		"ERROR" 
		{
            Write-Host $msg -ForegroundColor Red;
			break;
		}

        "WARN" 
		{
            Write-Host $msg -ForegroundColor Yellow;
			break;
		}
            
		"INFO"
        {
            if($special)
            {
                Write-Host $msg -ForegroundColor Cyan
            }
            else
            {
                Write-Host $msg
            }
            break;
        }

		default
		{
            Write-Host $msg
		}
	}
}

function Write-InfoLog([string] $msg, [string] $scriptName = $null, [string] $scriptLineNumber = $null)
{
    Write-Log "INFO" $msg $scriptName $scriptLineNumber
}

function Write-SpecialLog([string] $msg, [string] $scriptName = $null, [string] $scriptLineNumber = $null)
{
    Write-Log "SPECIAL" $msg $scriptName $scriptLineNumber
}

function Write-WarnLog([string] $msg, [string] $scriptName = $null, [string] $scriptLineNumber = $null, [object] $pipelineObject = $null)
{
    Write-Log "WARN" $msg $scriptName $scriptLineNumber $pipelineObject
}

function Write-ErrorLog([string] $msg, [string] $scriptName = $null, [string] $scriptLineNumber = $null, [object] $pipelineObject = $null)
{
    Write-Log "ERROR" $msg $scriptName $scriptLineNumber $pipelineObject
}