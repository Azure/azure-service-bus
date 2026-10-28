using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.ServiceBus;
using Azure.ResourceManager.ServiceBus.Models;

internal static class GeoDrSample
{
    private static readonly string[] Commands =
        ["create-namespaces", "pair", "status", "failover", "break-pairing", "delete-primary-alias", "delete-secondary-alias"];

    public static async Task RunAsync(string[] args, bool useNamespaceNameAsAlias)
    {
        if (args is ["--self-test"])
        {
            await CheckStatusFailuresAsync();
            return;
        }
        if (args.Length == 0 || args is ["--help"])
        {
            Console.WriteLine($"Commands: {string.Join(", ", Commands)}");
            Console.WriteLine("All mutations require: <command> --confirm-alias <SERVICEBUS_ALIAS>");
            Console.WriteLine("Read README.md before using disposable Premium namespaces. No automatic failover or cleanup.");
            Console.WriteLine("--self-test checks independent status failure/timeout handling without Azure.");
            return;
        }
        if (!Commands.Contains(args[0], StringComparer.Ordinal))
            throw new ArgumentException("Unknown command. Use --help.");
        bool readOnly = args[0] == "status";
        if (readOnly ? args.Length != 1 : args.Length != 3 || args[1] != "--confirm-alias")
            throw new ArgumentException("Use status alone, or <command> --confirm-alias <alias>.");

        var subscription = Required("AZURE_SUBSCRIPTION_ID");
        var resourceGroup = Required("SERVICEBUS_RESOURCE_GROUP");
        var primaryName = Required("SERVICEBUS_PRIMARY_NAMESPACE");
        var secondaryName = Required("SERVICEBUS_SECONDARY_NAMESPACE");
        var alias = Required("SERVICEBUS_ALIAS");
        if (primaryName.Equals(secondaryName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Primary and secondary namespaces must differ.");
        if (!useNamespaceNameAsAlias && (alias.Equals(primaryName, StringComparison.OrdinalIgnoreCase) ||
            alias.Equals(secondaryName, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("This variant requires an alias distinct from both namespace names.");
        if (!readOnly && args[2] != alias)
            throw new ArgumentException("Confirmation must exactly match SERVICEBUS_ALIAS.");
        if (args[0] == "pair" && useNamespaceNameAsAlias &&
            !alias.Equals(primaryName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("For this variant, SERVICEBUS_ALIAS must match SERVICEBUS_PRIMARY_NAMESPACE.");

        var primaryId = ServiceBusNamespaceResource.CreateResourceIdentifier(subscription, resourceGroup, primaryName);
        var secondaryId = ServiceBusNamespaceResource.CreateResourceIdentifier(subscription, resourceGroup, secondaryName);
        Console.WriteLine($"Action: {args[0]}\nPrimary: {primaryId}\nSecondary: {secondaryId}\nAlias: {alias}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var token = timeout.Token;
        var arm = new ArmClient(new DefaultAzureCredential());
        var primary = arm.GetServiceBusNamespaceResource(primaryId);
        var secondary = arm.GetServiceBusNamespaceResource(secondaryId);
        var primaryAlias = arm.GetServiceBusDisasterRecoveryResource(
            ServiceBusDisasterRecoveryResource.CreateResourceIdentifier(subscription, resourceGroup, primaryName, alias));
        var secondaryAlias = arm.GetServiceBusDisasterRecoveryResource(
            ServiceBusDisasterRecoveryResource.CreateResourceIdentifier(subscription, resourceGroup, secondaryName, alias));

        switch (args[0])
        {
            case "create-namespaces":
                var primaryLocation = Required("SERVICEBUS_PRIMARY_LOCATION");
                var secondaryLocation = Required("SERVICEBUS_SECONDARY_LOCATION");
                if (primaryLocation.Equals(secondaryLocation, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Choose different Azure regions.");
                var group = arm.GetResourceGroupResource(ResourceGroupResource.CreateResourceIdentifier(subscription, resourceGroup));
                var namespaces = group.GetServiceBusNamespaces();
                if ((await namespaces.ExistsAsync(primaryName, token)).Value ||
                    (await namespaces.ExistsAsync(secondaryName, token)).Value)
                    throw new InvalidOperationException("Refusing to overwrite an existing namespace. Use pair for existing namespaces.");
                foreach (var (name, location) in new[] { (primaryName, primaryLocation), (secondaryName, secondaryLocation) })
                {
                    Console.WriteLine($"Creating Premium namespace {name}; this incurs charges.");
                    await namespaces.CreateOrUpdateAsync(WaitUntil.Completed, name,
                        new ServiceBusNamespaceData(new AzureLocation(location))
                        {
                            Sku = new ServiceBusSku(ServiceBusSkuName.Premium) { Capacity = 1 }
                        }, token);
                }
                Console.WriteLine("Namespaces created. Pairing is a separate command.");
                break;
            case "pair":
                var primaryData = (await primary.GetAsync(token)).Value.Data;
                var secondaryData = (await secondary.GetAsync(token)).Value.Data;
                if (primaryData.Sku.Name != ServiceBusSkuName.Premium || secondaryData.Sku.Name != ServiceBusSkuName.Premium)
                    throw new InvalidOperationException("Metadata Geo-DR requires Premium namespaces.");
                if (primaryData.Location == secondaryData.Location)
                    throw new InvalidOperationException("Choose primary and secondary namespaces in different regions.");
                if (!string.IsNullOrEmpty(secondaryData.AlternateName))
                    throw new InvalidOperationException("The secondary namespace must not have an alternate name from an earlier pairing.");
                if (await primary.GetServiceBusDisasterRecoveries().GetAllAsync(token).AnyAsync(token) ||
                    await secondary.GetServiceBusDisasterRecoveries().GetAllAsync(token).AnyAsync(token))
                    throw new InvalidOperationException("A namespace is already paired or has an alias. Refusing to replace it.");
                var data = new ServiceBusDisasterRecoveryData { PartnerNamespace = secondaryId.ToString() };
                if (useNamespaceNameAsAlias)
                    data.AlternateName = Required("SERVICEBUS_ALTERNATE_PRIMARY_NAME");
                await primary.GetServiceBusDisasterRecoveries().CreateOrUpdateAsync(WaitUntil.Completed, alias, data, token);
                Console.WriteLine("Pairing operation completed. Inspect status and pending replication before any failover.");
                break;
            case "status":
                await PrintBothStatusAsync(
                    cancellation => PrintStatusAsync(primaryAlias, cancellation),
                    cancellation => PrintStatusAsync(secondaryAlias, cancellation),
                    TimeSpan.FromMinutes(2));
                break;
            case "failover":
                // The secondary must remain usable even when the primary region is unavailable.
                await secondaryAlias.FailOverAsync(cancellationToken: token);
                Console.WriteLine("Failover request accepted by the secondary. Run status to observe completion; messages are NOT replicated.");
                break;
            case "break-pairing":
                await primaryAlias.BreakPairingAsync(token);
                Console.WriteLine("Break-pairing request accepted. Run status before deleting an alias.");
                break;
            case "delete-primary-alias":
                if (useNamespaceNameAsAlias)
                    throw new InvalidOperationException("Do not delete the namespace-name alias on the primary after breaking pairing.");
                await primaryAlias.DeleteAsync(WaitUntil.Completed, token);
                Console.WriteLine("Primary alias deletion request acknowledged. Verify alias absence independently; this command does not delete namespaces or messages.");
                break;
            case "delete-secondary-alias":
                await secondaryAlias.DeleteAsync(WaitUntil.Completed, token);
                Console.WriteLine("Secondary alias deletion request acknowledged. Verify alias absence independently; this command does not delete namespaces or messages.");
                break;
        }
    }

    private static async Task PrintBothStatusAsync(Func<CancellationToken, Task> primary,
        Func<CancellationToken, Task> secondary, TimeSpan budget)
    {
        async Task<Exception?> ReadSideAsync(string side, Func<CancellationToken, Task> read)
        {
            using var timeout = new CancellationTokenSource(budget);
            try
            {
                await read(timeout.Token).WaitAsync(timeout.Token);
                return null;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"{side} status failed: {e.Message}");
                return e;
            }
        }
        var results = await Task.WhenAll(ReadSideAsync("Primary", primary), ReadSideAsync("Secondary", secondary));
        var failures = results.OfType<Exception>().ToArray();
        if (failures.Length != 0)
            throw new AggregateException("Status is incomplete; each side was queried independently.", failures);
    }

    private static async Task CheckStatusFailuresAsync()
    {
        foreach (bool useTimeout in new[] { false, true })
        {
            bool secondaryRead = false;
            try
            {
                await PrintBothStatusAsync(
                    token => useTimeout ? Task.Delay(Timeout.Infinite, token) : Task.FromException(new RequestFailedException(503, "offline primary failure")),
                    _ => { secondaryRead = true; return Task.CompletedTask; },
                    TimeSpan.FromMilliseconds(25));
                throw new InvalidOperationException("Expected incomplete status.");
            }
            catch (AggregateException e)
            {
                if (!secondaryRead || e.InnerExceptions.Count != 1)
                    throw new InvalidOperationException("An inaccessible primary must not hide the secondary.");
            }
        }
        Console.WriteLine("PASS: 2 independent primary-failure/timeout status checks. No Azure calls.");
    }

    private static string Required(string name) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))
            ? Environment.GetEnvironmentVariable(name)!
            : throw new InvalidOperationException($"Set {name}.");

    private static async Task<bool> AnyAsync<T>(this IAsyncEnumerable<T> source, CancellationToken token)
    {
        await foreach (var _ in source.WithCancellation(token))
            return true;
        return false;
    }

    private static async Task PrintStatusAsync(ServiceBusDisasterRecoveryResource alias, CancellationToken token)
    {
        try
        {
            var data = (await alias.GetAsync(token)).Value.Data;
            Console.WriteLine($"{alias.Id}: role={data.Role}, state={data.ProvisioningState}, pending={data.PendingReplicationOperationsCount}");
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            Console.WriteLine($"{alias.Id}: alias does not exist.");
        }
    }
}
