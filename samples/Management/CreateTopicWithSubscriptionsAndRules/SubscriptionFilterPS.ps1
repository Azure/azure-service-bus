# Logon
Login-AzureRmAccount

# Change your subscription if needed
Select-AzureRmSubscription -SubscriptionName "your subscription"

# Create Resource Group
New-AzureRmResourceGroup –Name FilterTest –Location westus2

# Create Namespace
New-AzureRmServiceBusNamespace -ResourceGroup FilterTest -NamespaceName FilterTest -Loc
ation westus2

# Create Topic
New-AzureRmServiceBusTopic -ResourceGroupName FilterTest -NamespaceName FilterTest -Nam
e FilterTest -EnablePartitioning $False

# Create Subscription 1
New-AzureRmServiceBusSubscription -ResourceGroupName FilterTest -NamespaceName FilterTe
st -TopicName FilterTest -Name S1

# Create Subscription 2
New-AzureRmServiceBusSubscription -ResourceGroupName FilterTest -NamespaceName FilterTe
st -TopicName FilterTest -Name S2

# Create Subscription 3
New-AzureRmServiceBusSubscription -ResourceGroupName FilterTest -NamespaceName FilterTe
st -TopicName FilterTest -Name S3

# Sample using SQL filter
New-AzureRmServiceBusRule -ResourceGroupName FilterTest -Namespace FilterTest -Topic Fi
lterTest -Subscription S1 -Name MyFilter -SqlExpression "StoreID = 'Store1'"

# Sample using SQL filter
New-AzureRmServiceBusRule -ResourceGroupName FilterTest -Namespace FilterTest -Topic Fi
lterTest -Subscription S2 -Name MyFilter -SqlExpression "StoreID = 'Store2'"

# Sample using Header field
New-AzureRmServiceBusRule -ResourceGroupName FilterTest -Namespace FilterTest -Topic Fi
lterTest -Subscription S3 -Name MyFilter -SqlExpression "sys.To = 'Store3'"

# Get access key to connect to namespace later on
Get-AzureRmServiceBusKey -ResourceGroupName FilterTest -NamespaceName FilterTest -Name
RootManageSharedAccessKey