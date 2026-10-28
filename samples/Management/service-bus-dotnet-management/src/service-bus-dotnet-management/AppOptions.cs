// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.ServiceBus;
using Azure.ResourceManager.ServiceBus.Models;

namespace AzureServiceBusDotNetManagement;

public sealed class AppOptions
{
    public string SubscriptionId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string Credential { get; set; } = "";
    public string ManagedIdentityClientId { get; set; } = "";
    public string DataCenterLocation { get; set; } = "eastus";
    public string ServiceBusSku { get; set; } = "Standard";
    public int PremiumCapacity { get; set; } = 1;
    public int PremiumMessagingPartitions { get; set; } = 1;
    public string ResourceGroupPrefix { get; set; } = "sb-management";
    public string NamespacePrefix { get; set; } = "sb-management";
    public int OperationTimeoutSeconds { get; set; } = 900;
    public int CleanupTimeoutSeconds { get; set; } = 300;

    public bool EnablePartitioning => ServiceBusSku == "Standard" ||
        (ServiceBusSku == "Premium" && PremiumMessagingPartitions > 1);

    public static AppOptions Load(string path, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        AppOptions options;
        try
        {
            options = JsonSerializer.Deserialize<AppOptions>(File.ReadAllText(path), new JsonSerializerOptions
            {
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            }) ?? throw new SampleException("Configuration must be a JSON object.");
        }
        catch (JsonException)
        {
            throw new SampleException("Invalid configuration JSON or unknown property. Use the documented schema; secrets are not supported.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new SampleException("Configuration file could not be read. Supply an accessible nonsecret JSON file with --config.");
        }
        options.SubscriptionId = environment("SB_SAMPLE_SUBSCRIPTION_ID") ?? options.SubscriptionId;
        options.TenantId = environment("SB_SAMPLE_TENANT_ID") ?? options.TenantId;
        options.Credential = environment("SB_SAMPLE_CREDENTIAL") ?? options.Credential;
        options.ManagedIdentityClientId = environment("SB_SAMPLE_MANAGED_IDENTITY_CLIENT_ID") ?? options.ManagedIdentityClientId;
        options.DataCenterLocation = environment("SB_SAMPLE_LOCATION") ?? options.DataCenterLocation;
        options.ServiceBusSku = environment("SB_SAMPLE_SKU") ?? options.ServiceBusSku;
        return options;
    }

    public void Validate()
    {
        if (!Guid.TryParse(SubscriptionId, out var subscription) || subscription == Guid.Empty)
            throw new SampleException("SubscriptionId must be a nonempty subscription GUID.");
        if (!Regex.IsMatch(DataCenterLocation ?? "", "^[a-z][a-z0-9]{1,30}$"))
            throw new SampleException("DataCenterLocation must be an Azure location name such as eastus.");
        if (ServiceBusSku is not ("Standard" or "Premium"))
            throw new SampleException("ServiceBusSku must be Standard or Premium; Basic cannot run the topic scenario.");
        if (PremiumMessagingPartitions is not (1 or 2 or 4) ||
            PremiumCapacity is not (1 or 2 or 4 or 8 or 16) ||
            PremiumCapacity < PremiumMessagingPartitions)
            throw new SampleException("Premium partitions must be 1, 2 or 4; capacity must be 1, 2, 4, 8 or 16 and at least the partition count.");
        if (ServiceBusSku == "Standard" && (PremiumCapacity != 1 || PremiumMessagingPartitions != 1))
            throw new SampleException("Premium settings cannot be customized for Standard.");
        if (!Regex.IsMatch(ResourceGroupPrefix ?? "", "^[a-z][a-z0-9-]{2,15}$") ||
            !Regex.IsMatch(NamespacePrefix ?? "", "^[a-z][a-z0-9-]{2,15}$"))
            throw new SampleException("Resource prefixes must be 3-16 lowercase letters, digits or hyphens, starting with a letter.");
        if (OperationTimeoutSeconds is < 1 or > 3600 || CleanupTimeoutSeconds is < 1 or > 1800)
            throw new SampleException("OperationTimeoutSeconds must be 1-3600; CleanupTimeoutSeconds must be 1-1800 per resource.");
        switch (Credential)
        {
            case "azure-cli":
                if (!Guid.TryParse(TenantId, out var tenant) || tenant == Guid.Empty || !string.IsNullOrEmpty(ManagedIdentityClientId))
                    throw new SampleException("azure-cli requires a TenantId GUID and no ManagedIdentityClientId.");
                break;
            case "managed-identity":
                if (!string.IsNullOrEmpty(TenantId) ||
                    (!string.IsNullOrEmpty(ManagedIdentityClientId) &&
                     (!Guid.TryParse(ManagedIdentityClientId, out var identity) || identity == Guid.Empty)))
                    throw new SampleException("managed-identity uses the host tenant; optionally set a ManagedIdentityClientId GUID, not TenantId.");
                break;
            default:
                throw new SampleException("Explicitly select Credential: azure-cli or managed-identity.");
        }
    }

    public TokenCredential CreateCredential()
    {
        Validate();
        return Credential switch
        {
            "azure-cli" => new AzureCliCredential(new AzureCliCredentialOptions
            {
                TenantId = TenantId,
                ProcessTimeout = TimeSpan.FromSeconds(30)
            }),
            "managed-identity" => new ManagedIdentityCredential(
                string.IsNullOrEmpty(ManagedIdentityClientId)
                    ? ManagedIdentityId.SystemAssigned
                    : ManagedIdentityId.FromUserAssignedClientId(ManagedIdentityClientId)),
            _ => throw new SampleException("Unsupported credential.")
        };
    }

    public ServiceBusNamespaceData CreateNamespaceData(string runId)
    {
        Validate();
        var sku = ServiceBusSku == "Standard"
            ? new ServiceBusSku(ServiceBusSkuName.Standard) { Tier = ServiceBusSkuTier.Standard }
            : new ServiceBusSku(ServiceBusSkuName.Premium) { Tier = ServiceBusSkuTier.Premium, Capacity = PremiumCapacity };
        var data = new ServiceBusNamespaceData(new AzureLocation(DataCenterLocation))
        {
            Sku = sku,
            DisableLocalAuth = true,
            MinimumTlsVersion = ServiceBusMinimumTlsVersion.Tls1_2
        };
        if (ServiceBusSku == "Premium")
            data.PremiumMessagingPartitions = PremiumMessagingPartitions;
        data.Tags.Add(ResourcePlan.OwnershipTag, runId);
        return data;
    }
}

public sealed class SampleException(string message) : Exception(message);
