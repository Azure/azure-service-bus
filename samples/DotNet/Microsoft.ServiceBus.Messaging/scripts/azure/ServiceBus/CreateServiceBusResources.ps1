[CmdletBinding(PositionalBinding=$True)]
Param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern("^[a-z0-9-]*$")]
    [String]$Namespace,                             # required    needs to be alphanumeric or '-'
    [Parameter(Mandatory = $true)]
    [ValidatePattern("^[a-z0-9-]*$")]
    [String]$Path,                                  # required    needs to be alphanumeric or '-'
    [Parameter(Mandatory = $true)]                            
    [String]$Location = "West Europe",              # optional    default to "West Europe"
    [String]$SendKey = $null,
    [String]$ListenKey = $null,
    [String]$ManageKey = $null,
    [String]$UserMetadata = $null,                  # optional    default to $null
    [Bool]$CreateACSNamespace = $False              # optional    default to $false
  )


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

$serviceBusDll = & "$scriptDir\GetServiceBusDll.ps1" (Get-ScriptName) (Get-ScriptLineNumber)

Write-InfoLog "Adding the $serviceBusDll assembly to the script..." (Get-ScriptName) (Get-ScriptLineNumber)
Add-Type -Path $serviceBusDll
Write-InfoLog "The $serviceBusDll assembly has been successfully added to the script." (Get-ScriptName) (Get-ScriptLineNumber)

$startTime = Get-Date



if ( $SendKey -eq $null -Or $SendKey.StartsWith("{") ) { $SendKey = [Microsoft.ServiceBus.Messaging.SharedAccessAuthorizationRule]::GenerateRandomKey() }
if ( $ListenKey -eq $null -Or $ListenKey.StartsWith("{") ) { $ListenKey = [Microsoft.ServiceBus.Messaging.SharedAccessAuthorizationRule]::GenerateRandomKey() } 
if ( $SendListenKey -eq $null -Or $SendListenKey.StartsWith("{") ) { $SendListenKey = [Microsoft.ServiceBus.Messaging.SharedAccessAuthorizationRule]::GenerateRandomKey() } 
if ( $ManageKey -eq $null -Or $ManageKey.StartsWith("{") ) { $ManageKey = [Microsoft.ServiceBus.Messaging.SharedAccessAuthorizationRule]::GenerateRandomKey() }

$SendRuleName = "samplesend"
$ListenRuleName = "samplelisten"
$SendListenRuleName = "samplesendlisten"
$ManageRuleName = "samplemanage"

$SendAccessRights = [Microsoft.ServiceBus.Messaging.AccessRights[]]([Microsoft.ServiceBus.Messaging.AccessRights]::Send)
$ListenAccessRights = [Microsoft.ServiceBus.Messaging.AccessRights[]]([Microsoft.ServiceBus.Messaging.AccessRights]::Listen)
$SendListenAccessRights = [Microsoft.ServiceBus.Messaging.AccessRights[]]([Microsoft.ServiceBus.Messaging.AccessRights]::Send,[Microsoft.ServiceBus.Messaging.AccessRights]::Listen)
$ManageAccessRights = [Microsoft.ServiceBus.Messaging.AccessRights[]]([Microsoft.ServiceBus.Messaging.AccessRights]::Manage,[Microsoft.ServiceBus.Messaging.AccessRights]::Send,[Microsoft.ServiceBus.Messaging.AccessRights]::Listen)


# Create Azure Service Bus namespace
$CurrentNamespace = Get-AzureSBNamespace -Name $Namespace

# Check if the namespace already exists or needs to be created
if ($CurrentNamespace)
{
    Write-InfoLog "The namespace: $Namespace already exists in location: $($CurrentNamespace.Region)" (Get-ScriptName) (Get-ScriptLineNumber)
	$ErrorActionPreference = "SilentlyContinue"
	$null = Remove-AzureSBAuthorizationRule -Name "root$SendRuleName" -Namespace $Namespace 
	$null = Remove-AzureSBAuthorizationRule -Name "root$ListenRuleName" -Namespace $Namespace 
	$null = Remove-AzureSBAuthorizationRule -Name "root$SendListenRuleName" -Namespace $Namespace 
	$null = Remove-AzureSBAuthorizationRule -Name "root$ManageRuleName" -Namespace $Namespace 
	$ErrorActionPreference = "Stop"
}
else
{
    Write-InfoLog "The namespace: $Namespace does not exist." (Get-ScriptName) (Get-ScriptLineNumber)
    Write-InfoLog "Creating namespace: $Namespace in location: $Location" (Get-ScriptName) (Get-ScriptLineNumber)
    $CurrentNamespace = New-AzureSBNamespace -Name $Namespace -Location $Location -CreateACSNamespace $CreateACSNamespace -NamespaceType Messaging
    #introduce a delay so that the namespace info can be retrieved
    sleep -s 30
    $CurrentNamespace = Get-AzureSBNamespace -Name $Namespace
    Write-InfoLog "The namespace: $Namespace in location: $Location has been successfully created." (Get-ScriptName) (Get-ScriptLineNumber)
}
$null = New-AzureSBAuthorizationRule -Name "root$SendRuleName" -Namespace $Namespace -Permission $("Send") -PrimaryKey $SendKey
$null = New-AzureSBAuthorizationRule -Name "root$ListenRuleName" -Namespace $Namespace -Permission $("Listen") -PrimaryKey $ListenKey
$null = New-AzureSBAuthorizationRule -Name "root$SendListenRuleName" -Namespace $Namespace -Permission $("Send", "Listen") -PrimaryKey $SendListenKey
$null = New-AzureSBAuthorizationRule -Name "root$ManageRuleName" -Namespace $Namespace -Permission $("Manage", "Listen","Send") -PrimaryKey $ManageKey




# Create the NamespaceManager object
Write-InfoLog "Creating a NamespaceManager object for the namespace: $Namespace" (Get-ScriptName) (Get-ScriptLineNumber)
$NamespaceManager = [Microsoft.ServiceBus.NamespaceManager]::CreateFromConnectionString($CurrentNamespace.ConnectionString);
Write-InfoLog "NamespaceManager object for the namespace: $Namespace has been successfully created." (Get-ScriptName) (Get-ScriptLineNumber)


function Set-Rule([Microsoft.ServiceBus.Messaging.AuthorizationRules] $AuthorizationRules, 
                  [Microsoft.ServiceBus.Messaging.SharedAccessAuthorizationRule] $Rule)
{   
   [Microsoft.ServiceBus.Messaging.SharedAccessAuthorizationRule] $ExistingRule = $null
   if ($AuthorizationRules.TryGetSharedAccessAuthorizationRule($Rule.$KeyName, [ref]$ExistingRule))
    {
        $ExistingRule.PrimaryKey = $Rule.PrimaryKey
    }
    else
    {
        $AuthorizationRules.Add($Rule)
    }
}

function Create-Queue($queuePath, 
          $requiresSession = $false,
          $requiresDuplicateDetection = $false,
          $supportOrdering = $false,
          $enablePartitioning = $true,
          $enableDeadLetteringOnMessageExpiration = $true)
{
    [Microsoft.ServiceBus.Messaging.QueueDescription]$QueueDescription = $null
    
    $queueExists = $NamespaceManager.QueueExists($queuePath)
    if ($queueExists)
    {
        Write-InfoLog "The queue: $queuePath already exists in the namespace: $Namespace." (Get-ScriptName) (Get-ScriptLineNumber)
        $QueueDescription = $NamespaceManager.GetQueue($queuePath)
    }
    else
    {
        Write-InfoLog "Creating the queue: $queuePath in the namespace: $Namespace" (Get-ScriptName) (Get-ScriptLineNumber)
        $QueueDescription = New-Object -TypeName Microsoft.ServiceBus.Messaging.QueueDescription -ArgumentList $queuePath
    }

    $QueueDescription.RequiresSession = $requiresSession
    $QueueDescription.RequiresDuplicateDetection = $requiresDuplicateDetection
    $QueueDescription.SupportOrdering = $supportOrdering
    $QueueDescription.EnablePartitioning = $enablePartitioning
    $QueueDescription.EnableDeadLetteringOnMessageExpiration = $enableDeadLetteringOnMessageExpiration        
    
    Set-Rule $QueueDescription.Authorization (New-Object -TypeName Microsoft.ServiceBus.Messaging.SharedAccessAuthorizationRule -ArgumentList $SendRuleName, $SendKey, $SendAccessRights)
    Set-Rule $QueueDescription.Authorization (New-Object -TypeName Microsoft.ServiceBus.Messaging.SharedAccessAuthorizationRule -ArgumentList $ListenRuleName, $ListenKey, $ListenAccessRights)
    Set-Rule $QueueDescription.Authorization (New-Object -TypeName Microsoft.ServiceBus.Messaging.SharedAccessAuthorizationRule -ArgumentList $ManageRuleName, $ManageKey, $ManageAccessRights)

    if ($queueExists)
    {
         $NamespaceManager.UpdateQueue($QueueDescription)
    }
    else
    {
        $NamespaceManager.CreateQueue($QueueDescription)
    }
}

function Create-Topic($topicPath,
          $requiresDuplicateDetection = $false,
          $supportOrdering = $false,
          $enablePartitioning = $false,
          $enableFilteringMessagesBeforePublishing = $false)
{
    [Microsoft.ServiceBus.Messaging.TopicDescription]$TopicDescription = $null
    
    $topicExists = $NamespaceManager.TopicExists($topicPath)
    if ($topicExists)
    {
        Write-InfoLog "The topic: $topicPath already exists in the namespace: $Namespace." (Get-ScriptName) (Get-ScriptLineNumber)
        $TopicDescription = $NamespaceManager.GetTopic($topicPath)
    }
    else
    {
        Write-InfoLog "Creating the topic: $topicPath in the namespace: $Namespace" (Get-ScriptName) (Get-ScriptLineNumber)
        $TopicDescription = New-Object -TypeName Microsoft.ServiceBus.Messaging.TopicDescription -ArgumentList $topicPath
    }

    $TopicDescription.RequiresDuplicateDetection = $requiresDuplicateDetection
    $TopicDescription.SupportOrdering = $supportOrdering
    $TopicDescription.EnablePartitioning = $enablePartitioning
    $TopicDescription.EnableFilteringMessagesBeforePublishing = $enableFilteringMessagesBeforePublishing        
    
    Set-Rule $TopicDescription.Authorization (New-Object -TypeName Microsoft.ServiceBus.Messaging.SharedAccessAuthorizationRule -ArgumentList $SendRuleName, $SendKey, $SendAccessRights)
    Set-Rule $TopicDescription.Authorization (New-Object -TypeName Microsoft.ServiceBus.Messaging.SharedAccessAuthorizationRule -ArgumentList $ListenRuleName, $ListenKey, $ListenAccessRights)
    Set-Rule $TopicDescription.Authorization (New-Object -TypeName Microsoft.ServiceBus.Messaging.SharedAccessAuthorizationRule -ArgumentList $ManageRuleName, $ManageKey, $ManageAccessRights)

    if ($topicExists)
    {
         $NamespaceManager.UpdateTopic($TopicDescription)
    }
    else
    {
        $NamespaceManager.CreateTopic($TopicDescription)
    }
}

function Create-Subscription($topicPath,
          $subscriptionName,
          $requiresSession = $false,
          $enableDeadLetteringOnMessageExpiration = $true,
          $enableDeadLetteringOnFilterEvaluationExceptions = $true, 
          [Microsoft.ServiceBus.Messaging.Filter]$filter = $null )
{
    [Microsoft.ServiceBus.Messaging.SubscriptionDescription]$SubscriptionDescription = $null
    
    if ( $filter -eq $null )
    {
        $filter = New-Object -TypeName Microsoft.ServiceBus.Messaging.TrueFilter
    }
    
    $subscriptionExists = $NamespaceManager.SubscriptionExists($topicPath, $subscriptionName)
    if ($subscriptionExists)
    {
        Write-InfoLog "The subscription: $subscriptionName already exists in the namespace: $Namespace." (Get-ScriptName) (Get-ScriptLineNumber)
        $SubscriptionDescription = $NamespaceManager.GetSubscription($topicPath, $subscriptionName)
    }
    else
    {
        Write-InfoLog "Creating the subscription: $subscriptionName in the namespace: $Namespace" (Get-ScriptName) (Get-ScriptLineNumber)
        $SubscriptionDescription = New-Object -TypeName Microsoft.ServiceBus.Messaging.SubscriptionDescription -ArgumentList $topicPath, $subscriptionName
    }

    $SubscriptionDescription.RequiresSession = $requiresSession
    $SubscriptionDescription.EnableDeadLetteringOnMessageExpiration = $enableDeadLetteringOnMessageExpiration  
    $SubscriptionDescription.EnableDeadLetteringOnFilterEvaluationExceptions = $enableDeadLetteringOnFilterEvaluationExceptions
    
    if ($subscriptionExists)
    {
         $NamespaceManager.UpdateSubscription($SubscriptionDescription)
    }
    else
    {
        $NamespaceManager.CreateSubscription($SubscriptionDescription, $filter)
    }
}

$r = Create-Queue -queuePath "BasicQueue" -enablePartitioning $false
$r = Create-Queue -queuePath "BasicQueue2" -enablePartitioning $false
$r = Create-Queue -queuePath "SessionQueue" -requiresSession $true -supportOrdering $true -enablePartitioning $false
$r = Create-Queue -queuePath "DupdetectQueue" -requiresDuplicateDetection $true -enablePartitioning $true
$r = Create-Queue -queuePath "PartitionedQueue" -enablePartitioning $true


$r = Create-Topic -topicPath "BasicTopic"
$r = Create-Subscription -topicPath "BasicTopic" -subscriptionName "Subscription1"
$r = Create-Subscription -topicPath "BasicTopic" -subscriptionName "Subscription2"
$r = Create-Subscription -topicPath "BasicTopic" -subscriptionName "Subscription3" 
$r = Create-Topic -topicPath "BasicTopic2"
$r = Create-Subscription -topicPath "BasicTopic2" -subscriptionName "Subscription1"
$r = Create-Subscription -topicPath "BasicTopic2" -subscriptionName "Subscription2"
$r = Create-Subscription -topicPath "BasicTopic2" -subscriptionName "Subscription3" 
$r = Create-Topic -topicPath "DupdetectTopic" -requiresDuplicateDetection $true
$r = Create-Subscription -topicPath "DupdetectTopic" -subscriptionName "Subscription1"
$r = Create-Subscription -topicPath "DupdetectTopic" -subscriptionName "Subscription2"
$r = Create-Subscription -topicPath "DupdetectTopic" -subscriptionName "Subscription3" 
$r = Create-Topic -topicPath "PrePublishFilterTopic" -enableFilteringMessagesBeforePublishing $true
$r = Create-Subscription -topicPath "PrePublishFilterTopic" -subscriptionName "Subscription1"
$r = Create-Subscription -topicPath "PrePublishFilterTopic" -subscriptionName "Subscription2"
$r = Create-Subscription -topicPath "PrePublishFilterTopic" -subscriptionName "Subscription3" 
$r = Create-Topic -topicPath "PartitionedTopic" -supportOrdering $false -enablePartitioning $true
$r = Create-Subscription -topicPath "PartitionedTopic" -subscriptionName "Subscription1"
$r = Create-Subscription -topicPath "PartitionedTopic" -subscriptionName "Subscription2"
$r = Create-Subscription -topicPath "PartitionedTopic" -subscriptionName "Subscription3" 


$finishTime = Get-Date
$totalSeconds = ($finishTime - $startTime).TotalSeconds
Write-InfoLog "CreateRelays completed in $totalSeconds seconds." (Get-ScriptName) (Get-ScriptLineNumber)

$keys = @{
  "$SendRuleName" = "$SendKey";
  "$ListenRuleName" = "$ListenKey";
  "$SendListenRuleName" = "$SendListenKey";
  "$ManageRuleName" = "$ManageKey";
}

Write-InfoLog "keys $keys"

return $keys