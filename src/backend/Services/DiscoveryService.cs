using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.NetApp;
using AzFilesOptimizer.Backend.Models;
using Microsoft.Extensions.Logging;

namespace AzFilesOptimizer.Backend.Services;

public class DiscoveryService
{
    private readonly ILogger _logger;

    public DiscoveryService(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<DiscoveryResult> DiscoverResourcesAsync(
        string subscriptionId,
        string? resourceGroupName,
        TokenCredential credential)
    {
        _logger.LogInformation("Starting discovery for subscription: {SubscriptionId}", subscriptionId);

        var result = new DiscoveryResult();
        var armClient = new ArmClient(credential);

        try
        {
            var subscription = await armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}")).GetAsync();

            // Discover Azure Files shares
            _logger.LogInformation("Discovering Azure Files shares...");
            result.AzureFileShares = await DiscoverAzureFilesSharesAsync(
                subscription.Value, resourceGroupName);

            // Discover ANF volumes
            _logger.LogInformation("Discovering ANF volumes...");
            result.AnfVolumes = await DiscoverAnfVolumesAsync(
                subscription.Value, resourceGroupName);

            _logger.LogInformation("Discovery completed. Found {FileSharesCount} Azure Files shares and {AnfCount} ANF volumes",
                result.AzureFileShares.Count, result.AnfVolumes.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during discovery");
            throw;
        }
    }

    private async Task<List<DiscoveredAzureFileShare>> DiscoverAzureFilesSharesAsync(
        Azure.ResourceManager.Resources.SubscriptionResource subscription,
        string? resourceGroupFilter)
    {
        var shares = new List<DiscoveredAzureFileShare>();

        try
        {
            // Get all storage accounts in subscription
            await foreach (var storageAccount in subscription.GetStorageAccountsAsync())
            {
                // Apply resource group filter if specified
                if (resourceGroupFilter != null && 
                    !storageAccount.Id.ResourceGroupName.Equals(resourceGroupFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _logger.LogInformation("Checking storage account: {AccountName}", storageAccount.Data.Name);

                try
                {
                    // Get file shares for this storage account
                    var fileServices = storageAccount.GetFileService();
                    var fileServiceResource = await fileServices.GetAsync();
                    
                    await foreach (var share in fileServiceResource.Value.GetFileShares().GetAllAsync())
                    {
                        shares.Add(new DiscoveredAzureFileShare
                        {
                            ResourceId = share.Id.ToString(),
                            StorageAccountName = storageAccount.Data.Name,
                            ShareName = share.Data.Name,
                            ResourceGroup = storageAccount.Id.ResourceGroupName ?? "",
                            SubscriptionId = storageAccount.Id.SubscriptionId ?? "",
                            Location = storageAccount.Data.Location.Name,
                            Tier = share.Data.AccessTier?.ToString() ?? "Unknown",
                            QuotaGiB = share.Data.ShareQuota,
                            Tags = storageAccount.Data.Tags?.ToDictionary(t => t.Key, t => t.Value) ?? new()
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get file shares for storage account {AccountName}", 
                        storageAccount.Data.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering Azure Files shares");
            throw;
        }

        return shares;
    }

    private async Task<List<DiscoveredAnfVolume>> DiscoverAnfVolumesAsync(
        Azure.ResourceManager.Resources.SubscriptionResource subscription,
        string? resourceGroupFilter)
    {
        var volumes = new List<DiscoveredAnfVolume>();

        try
        {
            // Get all NetApp accounts in subscription
            await foreach (var netAppAccount in subscription.GetNetAppAccountsAsync())
            {
                // Apply resource group filter if specified
                if (resourceGroupFilter != null && 
                    !netAppAccount.Id.ResourceGroupName.Equals(resourceGroupFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _logger.LogInformation("Checking NetApp account: {AccountName}", netAppAccount.Data.Name);

                try
                {
                    // Get all capacity pools
                    await foreach (var capacityPool in netAppAccount.GetCapacityPools().GetAllAsync())
                    {
                        // Get all volumes in this capacity pool
                        await foreach (var volume in capacityPool.GetNetAppVolumes().GetAllAsync())
                        {
                            volumes.Add(new DiscoveredAnfVolume
                            {
                                ResourceId = volume.Id.ToString(),
                                VolumeName = volume.Data.Name,
                                NetAppAccountName = netAppAccount.Data.Name,
                                CapacityPoolName = capacityPool.Data.Name,
                                ResourceGroup = netAppAccount.Id.ResourceGroupName ?? "",
                                SubscriptionId = netAppAccount.Id.SubscriptionId ?? "",
                                Location = netAppAccount.Data.Location.Name,
                                ServiceLevel = capacityPool.Data.ServiceLevel.ToString(),
                                ProvisionedSizeBytes = volume.Data.UsageThreshold,
                                ProtocolTypes = volume.Data.ProtocolTypes?.Select(p => p.ToString()).ToArray() ?? Array.Empty<string>(),
                                Tags = netAppAccount.Data.Tags?.ToDictionary(t => t.Key, t => t.Value) ?? new()
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get volumes for NetApp account {AccountName}", 
                        netAppAccount.Data.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering ANF volumes");
            throw;
        }

        return volumes;
    }
}
