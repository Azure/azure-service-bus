# Java Samples for Azure Service Bus

This is the official set of Java samples for Azure Service Bus. The samples demonstrate basics 
such as sending and receiving operations in the "quick starts", and more advanced scenarios in 
the feature-oriented samples. All samples are simple command line applications with minimal extra 
ceremony. 

The Java samples are split into two distinct sets. One set is built with the native Azure Service Bus 
SDK (azure-servicebus), the other set is built with the Apache Qpid JMS (Java Message Service) AMQP client.

## Azure Service Bus API 

The native Azure Service Bus SDK is fully supported by Microsoft (says: you can file service requests through 
the Azure portal to get immediate help) and it provides unfiltered and easy access to all Service Bus features. 

Samples:
* [QueueClientQuickstart](./azure-servicebus/QueueClientQuickstart) - Get started using Service Bus Queues
* [TopicClientQuickstart](./azure-servicebus/TopicClientQuickstart) - Get started using Service Bus Topic
* [MessageReceiverQuickstart](./azure-servicebus/MessageReceiverQuickstart) - Use the imperative message receiver to build your own receive loops
* [ManagingTopicRules](./azure-servicebus/ManagingTopicRules) - Manage rules on topic subscriptions

## Apache Qpid JMS

The Qpid JMS client is a third party open source component managed by the Apache Qpid project. It 
is compatible with Service Bus via its AMQP 1.0 proptocol support and can be used as an 
"lowest common denominator" alternative when the JMS API has been chosen for an existing application
that is being moved onto Azure Service Bus. Mind that JMS 2.0 gestures that change the namespace topology, 
like creating durable subscriptions or temporary queues, are not supported with Azure Service Bus via 
JMS at this time. 

Samples:
* [JmsQueueQuickstart](./qpid-jms-client/JmsQueueQuickstart) - Get started using Service Bus Queues with JMS
* [JmsTopicQuickstart](./qpid-jms-client/JmsTopicQuickstart) - Get started using Service Bus Topics with JMS

# Setup 

First, clone this git repository locally. 

The samples require [creating an Azure subscription](https://azure.microsoft.com/free/) if you don't have one. You also need  
a [Service Bus namespace](https://docs.microsoft.com/azure/service-bus-messaging/service-bus-fundamentals-hybrid-solutions), 
and a simple basic topology of a few exemplary queues, topics, and subscriptions. To set those up, 
with an Azure Service Bus "Standard" namespace, just click the button below and follow the further instructions 
on the Azure Portal:

<a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fclemensv%2Fazure-service-bus%2Fmaster%2Fsamples%2FJava%2Fscripts%2Fazuredeploy.json" target="_blank">
    <img src="http://azuredeploy.net/deploybutton.png"/>
</a>

The free Azure subscription offer includes a service credit that will take you very far with all your 
experiments. The prorated [monthly base fee](https://azure.microsoft.com/pricing/details/service-bus/) 
for Service Bus Standard includes a generous allocation of message operations, and you can even run a 
large [Service Bus Premium namespace](https://docs.microsoft.com/azure/service-bus-messaging/service-bus-premium-messaging) 
with 4 Messaging Units for several days.

You can also deploy the resource manager template from the command line:

## Setup using the Azure CLI

With the Azure CLI, [you first create a named resource group](https://docs.microsoft.com/en-us/azure/azure-resource-manager/xplat-cli-azure-resource-manager) in an Azure region, selected by the *--location* argument, and then [deploy the template into the resource group](https://docs.microsoft.com/en-us/azure/azure-resource-manager/resource-group-template-deploy-cli). The template file 
with the Service Bus topology for these samples is located in the *scripts* subdirectory. The namespace name argument becomes the leftmost portion of the fully qualifed domain name of your Service Bus namespace (e.g. *{name}.servicebus.windows.net* or *{name}.servicebus.cloudapi.de*) and must therefore be globally unique. There will be error feedback if you pick a name that already exists.

```azurecli
az group create --name {rg-name} --location "Central US"
az group deployment create --name {deployment-name} \
                           --resource-group {rg-name} \
                           --template-file scripts/azuredeploy.json \
                           --parameters serviceBusNamespaceName={service-bus-namespace-name}
```


## Setup using PowerShell

The PowerShell setup is functionally equivalent. You first [create a resource group](https://docs.microsoft.com/azure/azure-resource-manager/powershell-azure-resource-manager) and then [deploy the resource manager template](https://docs.microsoft.com/azure/azure-resource-manager/resource-group-template-deploy):

```powershell
New-AzureRmResourceGroup -Name {rg-name} -Location "Central US"
New-AzureRmResourceGroupDeployment \
      -Name {deployment-name} \
      -ResourceGroupName {rg-name}
      -TemplateFile scripts/azuredeploy.json \
      -serviceBusNamespaceName {service-bus-namespace-name} 
```

# Building and exploring the samples

All samples are individual Maven projects. JDK 1.8 or higher is required. There is a top-level Maven project
umbrella that allows to build all samples in one go, but you can also build the projects singly. Each 
project yields a console application packaged into a JAR along with all required dependencies, so that you
don't have to fiddle around with the Java classpath. 

## Exploring and running the samples

The samples are preconfigured for use with [Visual Studio Code](https://code.visualstudio.com/) and the [Red Hat 
Java language support](https://marketplace.visualstudio.com/items?itemName=redhat.java) and [Java debugging](https://marketplace.visualstudio.com/items?itemName=donjayamanne.javadebugger) extensions. 
You can either open the Java sample root directory or the individual sample directories. 

All samples have similar command line usage and accept a Service Bus connection string via option *-c {connection-string}*, and the names of the Service Bus entities they interact with, e.g. *-q {queue-name}*. 

To make running the samples straightforward, there are scripts for Bash ([Azure CLI](https://docs.microsoft.com/en-us/azure/azure-resource-manager/xplat-cli-azure-resource-manager)) and Powershell ([Azure PS](https://docs.microsoft.com/azure/azure-resource-manager/powershell-azure-resource-manager)) in *scripts* that will obtain the namespace connection string from your current Azure subscription, assume the entity names configured in the deployed templates, and export those into environment variables, eliminating the need to pass those arguments on the command line.

The Bash script is *scripts/setupenv.sh*, the Powershell equivalent is *scripts/setupenv.ps1*. Either needs to be called with the name of the Resource Group and the Service Bus namespace name as ordinal arguments. The Bash version runs a (re-)deployment of the template to obtain the required keys. For parsing
the returned JSON file, the Bash script relies on the [./jq](https://stedolan.github.io/jq/) package that can be installed with ```sudo apt install jq```.

Run the Powershell script from the scripts directory with

```bash
./setupenv.ps1 {rg-name} {service-bus-namespace-name} 
```

Run the Bash script with 
```bash
eval `./sampleenv.sh {rg-name} {service-bus-namespace-name}`
```

The scripts initialize the following environment variables:

* SB_SAMPLES_CONNECTIONSTRING - Service Bus connection string
* SB_SAMPLES_QUEUE - Default queue name
* SB_SAMPLES_TOPIC - Default topic name
* SB_SAMPLES_SUBSCRIPTION - Default subscription name

The samples build into the *target* folder of the respective sample subdirectory and can be run from there, using 

```bash
java -jar {name]-jar-with-dependencies.jar 
```

## Building all samples 

To build all samples, run a Maven build from the samples root directory:

```bash
mvn -B package
```

