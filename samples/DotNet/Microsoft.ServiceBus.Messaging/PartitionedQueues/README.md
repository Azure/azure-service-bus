#Partitioned Queues

This sample illustrates Partitioned Queues

## Prerequisites and Setup

All samples share the same basic setup, explained in the main [README](../README.md) file. There are no extra setup steps for this sample.
The application entry points are in [Main.cs](../common/Main.md), which is shared across all samples. The sample implementations generally
reside in *Program.cs*, starting with *Run()*.

You can build the sample from the command line with the [build.bat](build.bat) or [build.ps1](build.ps1) scripts. This assumes that you
have the .NET Build tools in the path. You can also open up the [TopicFilters.sln](TopicFilters.sln) solution file with Visual Studio and build.
With either option, the NuGet package manager should download and install the **WindowsAzure.ServiceBus** package containing the
Microsoft.ServiceBus.dll assembly, including dependencies.
