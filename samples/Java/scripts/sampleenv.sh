resourceGroup=$1
namespaceName=$2
ruleName=$3

if [ "$ruleName" == "" ]; then  
   ruleName="SendListen" 
fi

if [ "$resourceGroup" == "" ] || [ "$namespaceName" == "" ]; then
    echo "sampleenv.sh {resource-group} {namespace-name}"
    exit 1 
fi

cx=`az group deployment create --name azuredeploy --resource-group $resourceGroup --template-file ./azuredeploy.json --parameters serviceBusNamespaceName=$namespaceName | jq ".properties.outputs.sendListenConnectionString.value"` 
echo export SB_SAMPLES_CONNECTIONSTRING="$cx"
echo export SB_SAMPLES_QUEUENAME="myqueue"
echo export SB_SAMPLES_TOPICNAME="mytopic"
echo export SB_SAMPLES_SUBSCRIPTIONNAME="mysub"
