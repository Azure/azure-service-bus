if(Get-Command Add-AzureAccount -errorAction SilentlyContinue)
{
    $True
}
else
{
    Write-ErrorLog "You need to run these scripts in Azure Powershell. Please follow the guide to install Azure Powershell: http://azure.microsoft.com/en-us/documentation/articles/install-configure-powershell/#Install" (Get-ScriptName) (Get-ScriptLineNumber)
    $False
}