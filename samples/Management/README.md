# Service Bus management samples

| Sample | Purpose |
| --- | --- |
| [CreateQueue](CreateQueue) | Create a new Standard namespace and queue with Az PowerShell or Azure CLI |
| [CreateTopicWithSubscriptionsAndRules](CreateTopicWithSubscriptionsAndRules) | Create a Standard namespace, topic and three SQL-filtered subscriptions; remove the default match-all rules |
| [GeoDR](GeoDR) | .NET, Az PowerShell and Azure CLI operations for existing metadata-only Geo-DR scenarios |
| [Metrics](Metrics) | Read namespace/entity metrics with the current ARM SDK and Azure Identity |
| [.NET resource management](service-bus-dotnet-management) | Create an exclusive disposable group, Standard or Premium namespace, queue, topic, subscription and SQL rule; inventory and clean up confirmed owned resources |
| [Java publish/subscribe management](service-bus-java-manage-publish-subscribe-with-basic-features) | Create Standard or Premium resources, resize a topic, manage subscriptions and optional authorization keys, and verify fan-out with Entra credentials |
| [Java advanced queue management](service-bus-java-manage-queue-with-advanced-features) | Manage Standard or Premium queue settings, scoped SAS rules, ordered sessions and duplicate detection, with bounded owned-resource cleanup |

The three SDK management samples are ordinary source directories in this
repository, not submodules of their archived original repositories. Their
READMEs retain provenance, licenses and exact prerequisites. Read those
instructions before running: live modes create billable resources and require
an exclusive fresh scope. Offline checks do not validate Azure behavior.

## PowerShell and CLI scripts

Use a currently supported PowerShell 7 release (7.4 or later), the current
`Az.Accounts` / `Az.ServiceBus` modules for the Az scripts, or the current
Azure CLI for the `*Cli.ps1` scripts. The former `.py` files were command snippets,
not Python programs; they now have runnable PowerShell wrappers.

Sign in explicitly with `Connect-AzAccount` or `az login` before execution.
Supply the exact subscription ID and an **existing disposable resource group**.
Contributor scoped to that test group is sufficient for these management
operations; do not grant Owner merely to run a sample.

```powershell
.\CreateQueue\CreateQueue.ps1 -SubscriptionId "<id>" -ResourceGroupName "<group>" -NamespaceName "<new-name>" -Location eastus -WhatIf
.\CreateQueue\CreateQueueCli.ps1 -SubscriptionId "<id>" -ResourceGroupName "<group>" -NamespaceName "<new-name>" -Location eastus -WhatIf
.\CreateTopicWithSubscriptionsAndRules\SubscriptionFilterPS.ps1 -SubscriptionId "<id>" -ResourceGroupName "<group>" -NamespaceName "<new-name>" -Location eastus -WhatIf
.\CreateTopicWithSubscriptionsAndRules\SubscriptionFilterCli.ps1 -SubscriptionId "<id>" -ResourceGroupName "<group>" -NamespaceName "<new-name>" -Location eastus -WhatIf
```

`-WhatIf` on these creation operations performs no Azure calls. Remove it only
after reviewing the target and creation cost; the scripts request confirmation.
Creation refuses to overwrite an existing namespace. Native CLI failures stop
the script. Partial resources may remain after any failure, and cleanup is
deliberately manual. Scripts neither print access keys nor delete resource groups.

The two Geo-DR scripts require existing Premium namespaces and a **separate
alias name**. Read [Geo-DR safety and configuration](GeoDR/README.md) first:

```powershell
.\GeoDR\SBGeoDR2\Management.ps1 -SubscriptionId "<id>" -ResourceGroupName "<group>" -PrimaryNamespace "<primary>" -SecondaryNamespace "<secondary>" -Alias "<alias>" -Action pair -WhatIf
.\GeoDR\SBGeoDR2\ManagementCli.ps1 -SubscriptionId "<id>" -ResourceGroupName "<group>" -PrimaryNamespace "<primary>" -SecondaryNamespace "<secondary>" -Alias "<alias>" -Action pair -WhatIf
```

Choose one explicit action: `pair`, `status-primary`, `status-secondary`,
`failover`, `break-pairing`, `delete-primary-alias` or `delete-secondary-alias`.
Status commands are read-only **Azure calls**, even with `-WhatIf`; they are not
offline previews. Failover targets the secondary and is disruptive. Neither
script automatically creates namespaces, fails over, breaks pairing or deletes
aliases in sequence. Use the .NET variant for the existing namespace-name alias.
