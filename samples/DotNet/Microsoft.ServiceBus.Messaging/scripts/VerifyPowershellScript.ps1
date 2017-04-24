param($path, [switch]$verbose)

if ($verbose) {
    $VerbosePreference = ‘Continue’
}

trap { Write-Host $_; exit 1; continue }
& `
{
	$file = [System.io.File]::Open($path, 'Open', 'Read', 'ReadWrite')
	$reader = New-Object System.IO.StreamReader($file)
	$text = $reader.ReadToEnd()
	$reader.Close()
	$file.Close()

    [void]$ExecutionContext.InvokeCommand.NewScriptBlock($text)
    Write-Host "Parsed $path without errors"
    exit 0
}