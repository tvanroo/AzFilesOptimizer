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
        static int ClampInt(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
        static double ClampDouble(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

        static (int? iops, double? mibps) EstimateSharePerformance(DiscoveredAzureFileShare share)
        {
            var quotaGiB = share.ShareQuotaGiB ?? 0;
            var accessTier = share.AccessTier?.ToLowerInvariant() ?? string.Empty;
            var accountKind = share.StorageAccountKind?.ToLowerInvariant() ?? string.Empty;

            // If provisioned values already populated, keep them
            if (share.ProvisionedIops.HasValue || share.ProvisionedBandwidthMiBps.HasValue)
            {
                return (share.ProvisionedIops, share.ProvisionedBandwidthMiBps);
            }

            // SSD / premium (Provisioned v2 SSD)
            if (accountKind.Contains("filestorage") || accessTier.Contains("premium"))
            {
                var estIops = ClampInt((int)Math.Ceiling(3000 + 1.0 * quotaGiB), 3000, 102400);
                var estBw = ClampDouble(Math.Ceiling(100 + 0.1 * quotaGiB), 100, 10340);
                return (estIops, estBw);
            }

            // HDD / Transaction Optimized (Provisioned v2 HDD or classic standard)
            {
                var estIops = ClampInt((int)Math.Ceiling(1000 + 0.2 * quotaGiB), 500, 50000);
                var estBw = ClampDouble(Math.Ceiling(60 + 0.02 * quotaGiB), 60, 5120);
                return (estIops, estBw);
            }
        }
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
                    // Include snapshots and soft-deleted shares in listing; stats must be fetched via per-share Get(expand:"stats")
                    await foreach (var share in fileServiceResource.Value.GetFileShares().GetAllAsync(expand: "deleted,snapshots"))
                    {
                        shareCountForAccount++;

                        // Try to fetch stats for this share to populate ShareUsageBytes
                        long? usageBytes = null;
                        int? provisionedIops = null;
                        int? provisionedBandwidthMibps = null;
                        try
                        {
                            // Don't fetch stats for soft-deleted shares (service doesn't return stats)
                            if (share.Data.IsDeleted != true)
                            {
                                var withStats = await share.GetAsync(expand: "stats");
                                var detailed = withStats.Value.Data;
                                usageBytes = detailed.ShareUsageBytes;
                                provisionedIops = detailed.ProvisionedIops;
                                provisionedBandwidthMibps = detailed.ProvisionedBandwidthMibps;
                            }
                        }
                        catch (Exception statsEx)
                        {
                            _logger.LogWarning(statsEx, "Failed to fetch stats for share {ShareName} in account {Account}", share.Data.Name, storageAccount.Data.Name);
                        }
                        
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
                            ShareUsageBytes = usageBytes ?? share.Data.ShareUsageBytes,
                            EnabledProtocols = share.Data.EnabledProtocol != null ? 
                                new[] { share.Data.EnabledProtocol.Value.ToString() } : null,
                            RootSquash = share.Data.RootSquash?.ToString(),
                            
                            // Performance properties (Premium shares)
                            ProvisionedIops = provisionedIops ?? share.Data.ProvisionedIops,
                            ProvisionedBandwidthMiBps = provisionedBandwidthMibps ?? share.Data.ProvisionedBandwidthMibps,
                            
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

                        // Derive estimated performance where provisioned values are absent
                        var (estIops, estBw) = EstimateSharePerformance(discoveredShare);
                        discoveredShare.EstimatedIops = estIops;
                        discoveredShare.EstimatedThroughputMiBps = estBw;
                        
                        // Collect snapshot metadata (only for non-snapshot, non-deleted shares)
                        if (!discoveredShare.IsSnapshot && discoveredShare.IsDeleted != true)
                        {
                            try
                            {
                                var snapshotList = new List<(DateTime? snapshotTime, long? usageBytes)>();
                                await foreach (var snapshot in fileServiceResource.Value.GetFileShares().GetAllAsync(expand: "snapshots"))
                                {
                                    if (snapshot.Data.SnapshotOn.HasValue && 
                                        snapshot.Data.Name == share.Data.Name)
                                    {
                                        long? snapshotUsage = null;
                                        try
                                        {
                                            var withStats = await snapshot.GetAsync(expand: "stats");
                                            snapshotUsage = withStats.Value.Data.ShareUsageBytes;
                                        }
                                        catch { /* snapshot stats may not be available */ }
                                        
                                        snapshotList.Add((snapshot.Data.SnapshotOn?.UtcDateTime, snapshotUsage));
                                    }
                                }
                                
                                discoveredShare.SnapshotCount = snapshotList.Count;
                                discoveredShare.TotalSnapshotSizeBytes = snapshotList
                                    .Where(s => s.usageBytes.HasValue)
                                    .Sum(s => s.usageBytes.Value);
                                
                                // Calculate churn rate if we have multiple snapshots with dates
                                var orderedSnapshots = snapshotList
                                    .Where(s => s.snapshotTime.HasValue && s.usageBytes.HasValue)
                                    .OrderBy(s => s.snapshotTime.Value)
                                    .ToList();
                                
                                if (orderedSnapshots.Count >= 2)
                                {
                                    var firstSnapshot = orderedSnapshots.First();
                                    var lastSnapshot = orderedSnapshots.Last();
                                    var daysDiff = (lastSnapshot.snapshotTime.Value - firstSnapshot.snapshotTime.Value).TotalDays;
                                    
                                    if (daysDiff > 0)
                                    {
                                        var sizeGrowth = lastSnapshot.usageBytes.Value - firstSnapshot.usageBytes.Value;
                                        discoveredShare.ChurnRateBytesPerDay = sizeGrowth / daysDiff;
                                    }
                                }
                                
                                // Set monitoring availability (placeholder - would need Azure Monitor API)
                                discoveredShare.MonitoringEnabled = true; // Assume metrics are available
                                discoveredShare.MonitoringDataAvailableDays = 30; // Azure Files keeps 30 days by default
                            }
                            catch (Exception snapshotEx)
                            {
                                _logger.LogWarning(snapshotEx, "Failed to collect snapshot metadata for share {ShareName}", share.Data.Name);
                            }
                        }
                        
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
        static (int? iops, double? mibps) EstimateAnfPerformance(
            string serviceLevel,
            long provisionedBytes,
            double? actualThroughputMibps,
            double? configuredThroughputMibps,
            string? poolQosType,
            bool? coolAccessEnabled,
            string? coolTieringPolicy)
        {
            // If service returns actual or configured throughput, prefer it (Flexible or Manual QoS)
            var chosenThroughput = actualThroughputMibps ?? configuredThroughputMibps;

            double tiB = provisionedBytes / 1099511627776.0;
            var sl = serviceLevel.ToLowerInvariant();
            var qos = poolQosType?.ToLowerInvariant();
            var isManual = qos == "manual"; // Flexible/Manual decouples size and throughput
            var isCool = (coolAccessEnabled ?? false) && (string.Equals(coolTieringPolicy, "auto", StringComparison.OrdinalIgnoreCase) || string.Equals(coolTieringPolicy, "snapshotonly", StringComparison.OrdinalIgnoreCase));

            // Base coeffs per TiB
            double throughputPerTiB = sl switch
            {
                "standard" => 16,
                "premium" => 64,
                "ultra" => 128,
                _ => 16
            };
            // Cool access adjustments apply to Premium/Ultra when on auto/snapshotOnly
            if (!isManual && isCool)
            {
                if (sl == "premium") throughputPerTiB = 36;
                else if (sl == "ultra") throughputPerTiB = 68;
            }

            int iopsPerTiB = sl switch
            {
                "standard" => 128,
                "premium" => 512,
                "ultra" => 1024,
                _ => 128
            };

            var estThroughput = chosenThroughput ?? throughputPerTiB * tiB;
            var estIops = (int)Math.Round(iopsPerTiB * tiB);

            return (estIops, estThroughput);
        }
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
                            // Attempt to capture pool QoS type and volume cool access flags if available
                            string? poolQosType = capacityPool.Data.QosType?.ToString();
                            bool? coolAccessEnabled = null;
                            string? coolTieringPolicy = null;
                            try
                            {
                                // Use reflection to avoid compile errors if properties are absent in current SDK
                                var data = volume.Data;
                                var t = data.GetType();
                                var coolProp = t.GetProperty("CoolAccess") ?? t.GetProperty("CoolAccessEnabled");
                                if (coolProp != null)
                                {
                                    var val = coolProp.GetValue(data);
                                    coolAccessEnabled = val as bool? ?? (val != null ? (bool)val : (bool?)null);
                                }
                                var tieringProp = t.GetProperty("TieringPolicyName") ?? t.GetProperty("TieringPolicy");
                                if (tieringProp != null)
                                {
                                    coolTieringPolicy = tieringProp.GetValue(data)?.ToString();
                                }
                            }
                            catch { /* leave nulls if not available */ }

                            var v = new DiscoveredAnfVolume
                            {
                                ResourceId = volume.Id.ToString(),
                                VolumeName = volume.Data.Name,
                                NetAppAccountName = netAppAccount.Data.Name,
                                CapacityPoolName = capacityPool.Data.Name,
                                ResourceGroup = netAppAccount.Id.ResourceGroupName ?? "",
                                SubscriptionId = netAppAccount.Id.SubscriptionId ?? "",
                                Location = netAppAccount.Data.Location.Name,
                                ServiceLevel = capacityPool.Data.ServiceLevel.ToString(),
                                PoolQosType = poolQosType,
                                ProvisionedSizeBytes = volume.Data.UsageThreshold,
                                ThroughputMibps = volume.Data.ThroughputMibps,
                                ActualThroughputMibps = volume.Data.ActualThroughputMibps,
                                CoolAccessEnabled = coolAccessEnabled,
                                CoolTieringPolicy = coolTieringPolicy,
                                ProtocolTypes = volume.Data.ProtocolTypes?.Select(p => p.ToString()).ToArray() ?? Array.Empty<string>(),
                                Tags = netAppAccount.Data.Tags?.ToDictionary(t => t.Key, t => t.Value) ?? new()
                            };
                            
                            // Try to capture TLS version from NetApp account encryption settings
                            try
                            {
                                var accountData = netAppAccount.Data;
                                var encryptionProp = accountData.GetType().GetProperty("Encryption");
                                if (encryptionProp != null)
                                {
                                    var encryption = encryptionProp.GetValue(accountData);
                                    if (encryption != null)
                                    {
                                        var tlsProp = encryption.GetType().GetProperty("MinimumTlsVersion");
                                        if (tlsProp != null)
                                        {
                                            v.MinimumTlsVersion = tlsProp.GetValue(encryption)?.ToString();
                                        }
                                    }
                                }
                            }
                            catch { /* TLS version may not be available in SDK version */ }
                            
                            // Collect ANF snapshot metadata
                            try
                            {
                                var snapshotList = new List<long?>();
                                await foreach (var snapshot in volume.GetNetAppVolumeSnapshots().GetAllAsync())
                                {
                                    // ANF snapshots don't typically expose size directly in SDK
                                    // Store count for now
                                    snapshotList.Add(null);
                                }
                                
                                v.SnapshotCount = snapshotList.Count;
                                // For ANF, snapshot size calculation would require more complex queries
                                v.TotalSnapshotSizeBytes = null;
                                
                                // Set monitoring availability
                                v.MonitoringEnabled = true; // ANF has metrics
                                v.MonitoringDataAvailableDays = 93; // ANF keeps 93 days
                            }
                            catch (Exception snapshotEx)
                            {
                                _logger.LogWarning(snapshotEx, "Failed to collect snapshot metadata for ANF volume {VolumeName}", volume.Data.Name);
                            }
                            
                            var (estIops, estBw) = EstimateAnfPerformance(
                                v.ServiceLevel,
                                v.ProvisionedSizeBytes,
                                v.ActualThroughputMibps,
                                v.ThroughputMibps,
                                v.PoolQosType,
                                v.CoolAccessEnabled,
                                v.CoolTieringPolicy);
                            v.EstimatedIops = estIops;
                            v.EstimatedThroughputMiBps = estBw;
                            volumes.Add(v);
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
