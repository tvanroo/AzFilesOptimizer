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
    private readonly JobLogService? _jobLogService;
    private readonly string? _jobId;

    public DiscoveryService(ILogger logger, JobLogService? jobLogService = null, string? jobId = null)
    {
        _logger = logger;
        _jobLogService = jobLogService;
        _jobId = jobId;
    }

    private async Task LogProgressAsync(string message)
    {
        _logger.LogInformation(message);
        if (_jobLogService != null && !string.IsNullOrEmpty(_jobId))
        {
            await _jobLogService.AddLogAsync(_jobId, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {message}");
        }
    }

    public async Task<DiscoveryResult> DiscoverResourcesAsync(
        string subscriptionId,
        string? resourceGroupName,
        TokenCredential credential,
        string? tenantId = null)
    {
        await LogProgressAsync($"Starting discovery for subscription: {subscriptionId}");

        var result = new DiscoveryResult();
        var armClient = new ArmClient(credential);

        try
        {
            await LogProgressAsync("Connecting to Azure Resource Manager...");
            var subscription = await armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}")).GetAsync();

            // Discover Azure Files shares
            await LogProgressAsync("Step 1/2: Discovering Azure Files shares...");
            result.AzureFileShares = await DiscoverAzureFilesSharesAsync(
                subscription.Value, resourceGroupName, tenantId);
            await LogProgressAsync($"✓ Found {result.AzureFileShares.Count} Azure Files shares");

            // Discover ANF volumes
            await LogProgressAsync("Step 2/2: Discovering Azure NetApp Files volumes...");
            result.AnfVolumes = await DiscoverAnfVolumesAsync(
                subscription.Value, resourceGroupName);
            await LogProgressAsync($"✓ Found {result.AnfVolumes.Count} ANF volumes");

            await LogProgressAsync($"Discovery completed successfully. Total: {result.AzureFileShares.Count} shares, {result.AnfVolumes.Count} volumes");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during discovery");
            await LogProgressAsync($"ERROR during discovery: {ex.Message}");
            throw;
        }
    }

    private async Task<List<DiscoveredAzureFileShare>> DiscoverAzureFilesSharesAsync(
        Azure.ResourceManager.Resources.SubscriptionResource subscription,
        string? resourceGroupFilter,
        string? tenantId = null)
    {
        var shares = new List<DiscoveredAzureFileShare>();
        int storageAccountCount = 0;

        try
        {
            await LogProgressAsync("  • Enumerating storage accounts...");
            
            // Get all storage accounts in subscription
            await foreach (var storageAccount in subscription.GetStorageAccountsAsync())
            {
                storageAccountCount++;
                
                // Apply resource group filter if specified
                if (resourceGroupFilter != null && 
                    !storageAccount.Id.ResourceGroupName.Equals(resourceGroupFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await LogProgressAsync($"  • Checking storage account: {storageAccount.Data.Name} (RG: {storageAccount.Id.ResourceGroupName})");

                try
                {
                    // Get file shares for this storage account
                    var fileServices = storageAccount.GetFileService();
                    var fileServiceResource = await fileServices.GetAsync();
                    
                    int shareCountForAccount = 0;
                    await foreach (var share in fileServiceResource.Value.GetFileShares().GetAllAsync())
                    {
                        shareCountForAccount++;
                        
                        // Collect comprehensive metadata
                        var discoveredShare = new DiscoveredAzureFileShare
                        {
                            // Hierarchy
                            TenantId = tenantId ?? "",
                            SubscriptionId = storageAccount.Id.SubscriptionId ?? "",
                            ResourceGroup = storageAccount.Id.ResourceGroupName ?? "",
                            StorageAccountName = storageAccount.Data.Name,
                            ShareName = share.Data.Name,
                            
                            // Resource identification
                            ResourceId = share.Id.ToString(),
                            Location = storageAccount.Data.Location.Name,
                            
                            // Storage Account properties
                            StorageAccountSku = storageAccount.Data.Sku?.Name.ToString() ?? "",
                            StorageAccountKind = storageAccount.Data.Kind?.ToString() ?? "",
                            EnableHttpsTrafficOnly = storageAccount.Data.EnableHttpsTrafficOnly,
                            MinimumTlsVersion = storageAccount.Data.MinimumTlsVersion?.ToString(),
                            AllowBlobPublicAccess = storageAccount.Data.AllowBlobPublicAccess,
                            AllowSharedKeyAccess = storageAccount.Data.AllowSharedKeyAccess,
                            
                            // File Share properties
                            AccessTier = share.Data.AccessTier?.ToString() ?? "Unknown",
                            AccessTierChangeTime = null, // Not available in current SDK version
                            AccessTierStatus = share.Data.AccessTierStatus,
                            ShareQuotaGiB = share.Data.ShareQuota,
                            ShareUsageBytes = share.Data.ShareUsageBytes,
                            EnabledProtocols = share.Data.EnabledProtocol != null ? 
                                new[] { share.Data.EnabledProtocol.Value.ToString() } : null,
                            RootSquash = share.Data.RootSquash?.ToString(),
                            
                            // Performance properties (Premium shares)
                            ProvisionedIops = share.Data.SignedIdentifiers?.Count,
                            
                            // Lease properties
                            LeaseStatus = share.Data.LeaseStatus?.ToString(),
                            LeaseState = share.Data.LeaseState?.ToString(),
                            LeaseDuration = share.Data.LeaseDuration?.ToString(),
                            
                            // Snapshot properties
                            SnapshotTime = share.Data.SnapshotOn?.UtcDateTime,
                            IsSnapshot = share.Data.SnapshotOn.HasValue,
                            
                            // Soft delete properties
                            IsDeleted = share.Data.IsDeleted,
                            DeletedTime = share.Data.DeletedOn?.UtcDateTime,
                            RemainingRetentionDays = share.Data.RemainingRetentionDays,
                            Version = share.Data.Version,
                            
                            // Metadata and tags
                            Metadata = share.Data.Metadata?.ToDictionary(m => m.Key, m => m.Value),
                            Tags = storageAccount.Data.Tags?.ToDictionary(t => t.Key, t => t.Value),
                            
                            // Timestamps
                            LastModifiedTime = share.Data.LastModifiedOn?.UtcDateTime,
                            DiscoveredAt = DateTime.UtcNow
                        };
                        
                        shares.Add(discoveredShare);
                    }
                    
                    if (shareCountForAccount > 0)
                    {
                        await LogProgressAsync($"    → Found {shareCountForAccount} file share(s) in {storageAccount.Data.Name}");
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
            await LogProgressAsync("  • Enumerating NetApp accounts...");
            
            // Get all NetApp accounts in subscription
            await foreach (var netAppAccount in subscription.GetNetAppAccountsAsync())
            {
                // Apply resource group filter if specified
                if (resourceGroupFilter != null && 
                    !netAppAccount.Id.ResourceGroupName.Equals(resourceGroupFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await LogProgressAsync($"  • Checking NetApp account: {netAppAccount.Data.Name} (RG: {netAppAccount.Id.ResourceGroupName})");

                try
                {
                    // Get all capacity pools
                    await foreach (var capacityPool in netAppAccount.GetCapacityPools().GetAllAsync())
                    {
                        await LogProgressAsync($"    • Checking capacity pool: {capacityPool.Data.Name}");
                        
                        int volumeCount = 0;
                        // Get all volumes in this capacity pool
                        await foreach (var volume in capacityPool.GetNetAppVolumes().GetAllAsync())
                        {
                            volumeCount++;
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
                        
                        if (volumeCount > 0)
                        {
                            await LogProgressAsync($"      → Found {volumeCount} volume(s) in pool {capacityPool.Data.Name}");
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
