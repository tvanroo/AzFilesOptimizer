using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.NetApp;
using AzFilesOptimizer.Backend.Models;
using Microsoft.Extensions.Logging;

namespace AzFilesOptimizer.Backend.Services;

public partial class DiscoveryService
{
    private readonly ILogger _logger;
    private readonly JobLogService? _jobLogService;
    private readonly string? _jobId;
    private MetricsCollectionService? _metricsService;

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
        string[]? resourceGroupNames,
        TokenCredential credential,
        string? tenantId = null)
    {
        await LogProgressAsync($"Starting discovery for subscription: {subscriptionId}");

        var result = new DiscoveryResult();
        var armClient = new ArmClient(credential);
        
        // Initialize metrics collection service
        _metricsService = new MetricsCollectionService(_logger, credential);

        try
        {
            await LogProgressAsync("Connecting to Azure Resource Manager...");
            var subscription = await armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}")).GetAsync();

            // Discover Azure Files shares
            await LogProgressAsync("Step 1/2: Discovering Azure Files shares...");
            result.AzureFileShares = await DiscoverAzureFilesSharesAsync(
                subscription.Value, resourceGroupNames, tenantId);
            await LogProgressAsync($"✓ Found {result.AzureFileShares.Count} Azure Files shares");

            // Discover ANF volumes
            await LogProgressAsync("Step 2/3: Discovering Azure NetApp Files volumes...");
            result.AnfVolumes = await DiscoverAnfVolumesAsync(
                subscription.Value, resourceGroupNames);
            await LogProgressAsync($"✓ Found {result.AnfVolumes.Count} ANF volumes");

            // Discover managed disks
            // TODO: Re-implement managed disk discovery with new pricing model
            // await LogProgressAsync("Step 3/3: Discovering managed disks (excluding OS disks)...");
            // result.ManagedDisks = await DiscoverManagedDisksAsync(
            //     subscription.Value, resourceGroupNames, tenantId);
            // await LogProgressAsync($"✓ Found {result.ManagedDisks.Count} managed disks");

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
        string[]? resourceGroupFilters,
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
        // Cache storage-account-level metrics so we don't query repeatedly per share
        var accountMetricsCache = new Dictionary<string, (bool hasData, int? daysAvailable, string? metricsSummary)>();

        try
        {
            await LogProgressAsync("  • Enumerating storage accounts...");
            
            // Get all storage accounts in subscription
            await foreach (var storageAccount in subscription.GetStorageAccountsAsync())
            {
                storageAccountCount++;
                
                // Apply resource group filter if specified
                if (resourceGroupFilters != null && resourceGroupFilters.Length > 0)
                {
                    bool matchesAnyRg = false;
                    foreach (var rgFilter in resourceGroupFilters)
                    {
                        if (storageAccount.Id.ResourceGroupName.Equals(rgFilter, StringComparison.OrdinalIgnoreCase))
                        {
                            matchesAnyRg = true;
                            break;
                        }
                    }
                    if (!matchesAnyRg)
                    {
                        continue;
                    }
                }

                await LogProgressAsync($"  • Checking storage account: {storageAccount.Data.Name} (RG: {storageAccount.Id.ResourceGroupName})");

                try
                {
                    // Get file shares for this storage account
                    var fileServices = storageAccount.GetFileService();
                    var fileServiceResource = await fileServices.GetAsync();
                    
                    int shareCountForAccount = 0;
                    // Enumerate base shares (exclude snapshots) and include soft-deleted shares
                    await foreach (var share in fileServiceResource.Value.GetFileShares().GetAllAsync(expand: "deleted"))
                    {
                        // Defensive: if SDK still returns snapshot entries, skip them
                        if (share.Data.SnapshotOn.HasValue)
                        {
                            continue;
                        }

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
                            await LogProgressAsync($"      ⚠ Could not retrieve stats for share {share.Data.Name}");
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
                            
                            // Pricing metadata
                            RedundancyType = ExtractRedundancyFromSku(storageAccount.Data.Sku?.Name.ToString()),
                            IsProvisioned = (storageAccount.Data.Kind?.ToString() ?? "").Equals("FileStorage", StringComparison.OrdinalIgnoreCase),
                            ProvisionedTier = GetProvisionedTier(storageAccount.Data.Sku?.Name.ToString()),
                            
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
                                await LogProgressAsync($"      → Collecting snapshots for {share.Data.Name}...");
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
                                
                                if (snapshotList.Count > 0)
                                {
                                    await LogProgressAsync($"      ✓ Found {snapshotList.Count} snapshot(s)");
                                }
                                
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
                                
                                // Snapshot-specific work ends here; metrics are handled below for all shares
                            }
                            catch (Exception snapshotEx)
                            {
                                _logger.LogWarning(snapshotEx, "Failed to collect snapshot metadata for share {ShareName}", share.Data.Name);
                                await LogProgressAsync($"      ⚠ Failed to collect snapshots for {share.Data.Name}: {snapshotEx.Message}");
                            }
                        }
                        
                        // Populate monitoring info for ALL shares using file share-level metrics
                        if (_metricsService != null)
                        {
                            await LogProgressAsync($"      → Collecting file share metrics for {share.Data.Name}...");
                            var shareMetrics = await _metricsService.CollectFileShareMetricsAsync(
                                discoveredShare.ResourceId, share.Data.Name);
                            
                            if (shareMetrics.hasData)
                            {
                                await LogProgressAsync($"      ✓ File share metrics collected: {shareMetrics.daysAvailable} days available");
                                discoveredShare.MonitoringEnabled = true;
                                discoveredShare.MonitoringDataAvailableDays = shareMetrics.daysAvailable;
                                discoveredShare.HistoricalMetricsSummary = shareMetrics.metricsSummary;
                            }
                            else
                            {
                                await LogProgressAsync($"      ⚠ No file share metrics data available for {share.Data.Name}");
                                discoveredShare.MonitoringEnabled = false;
                                discoveredShare.MonitoringDataAvailableDays = null;
                                discoveredShare.HistoricalMetricsSummary = null;
                            }
                        }
                        else
                        {
                            await LogProgressAsync($"      ⚠ Metrics service not initialized");
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
                    await LogProgressAsync($"    ⚠ Failed to access storage account {storageAccount.Data.Name}: {ex.Message}");
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
        string[]? resourceGroupFilters)
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
                if (resourceGroupFilters != null && resourceGroupFilters.Length > 0)
                {
                    bool matchesAnyRg = false;
                    foreach (var rgFilter in resourceGroupFilters)
                    {
                        if (netAppAccount.Id.ResourceGroupName.Equals(rgFilter, StringComparison.OrdinalIgnoreCase))
                        {
                            matchesAnyRg = true;
                            break;
                        }
                    }
                    if (!matchesAnyRg)
                    {
                        continue;
                    }
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
                            int? coolnessPeriodDays = null;
                            long? maxFiles = null;
                            string? subnetId = null;
                            string? vnetName = null;
                            string? subnetName = null;
                            string? networkFeatures = null;
                            string? securityStyle = null;
                            bool? isKerberosEnabled = null;
                            string? encryptionKeySource = null;
                            bool? isLdapEnabled = null;
                            string? unixPermissions = null;
                            string? availabilityZone = null;
                            bool? isLargeVolume = null;
                            string? avsDataStore = null;
                            string? volumeType = null;
                            string? mountPath = null;
                            try
                            {
                                // Use reflection to avoid compile errors if properties are absent in current SDK
                                var data = volume.Data;
                                var t = data.GetType();

                                bool? GetBool(string name)
                                {
                                    var p = t.GetProperty(name);
                                    if (p == null) return null;
                                    var vObj = p.GetValue(data);
                                    return vObj as bool? ?? (vObj != null ? (bool)vObj : (bool?)null);
                                }

                                long? GetLong(string name)
                                {
                                    var p = t.GetProperty(name);
                                    if (p == null) return null;
                                    var vObj = p.GetValue(data);
                                    if (vObj is long l) return l;
                                    if (vObj is int i) return i;
                                    return null;
                                }

                                int? GetInt(string name)
                                {
                                    var p = t.GetProperty(name);
                                    if (p == null) return null;
                                    var vObj = p.GetValue(data);
                                    if (vObj is int i) return i;
                                    if (vObj is long l) return checked((int)l);
                                    return null;
                                }

                                string? GetString(string name)
                                {
                                    var p = t.GetProperty(name);
                                    if (p == null) return null;
                                    return p.GetValue(data)?.ToString();
                                }

                                // Cool access and tiering
                                var coolProp = t.GetProperty("IsCoolAccessEnabled")
                                              ?? t.GetProperty("CoolAccessEnabled")
                                              ?? t.GetProperty("CoolAccess");
                                if (coolProp != null)
                                {
                                    var val = coolProp.GetValue(data);
                                    coolAccessEnabled = val as bool? ?? (val != null ? (bool)val : (bool?)null);
                                }
                                var tieringProp = t.GetProperty("CoolAccessTieringPolicy")
                                                 ?? t.GetProperty("TieringPolicyName")
                                                 ?? t.GetProperty("TieringPolicy");
                                if (tieringProp != null)
                                {
                                    coolTieringPolicy = tieringProp.GetValue(data)?.ToString();
                                }
                                coolnessPeriodDays = GetInt("CoolnessPeriod");

                                // Capacity / files
                                maxFiles = GetLong("MaximumNumberOfFiles");

                                // Network / security
                                subnetId = GetString("SubnetId");
                                networkFeatures = GetString("NetworkFeatures");
                                securityStyle = GetString("SecurityStyle");
                                isKerberosEnabled = GetBool("IsKerberosEnabled");
                                encryptionKeySource = GetString("EncryptionKeySource");
                                isLdapEnabled = GetBool("IsLdapEnabled");
                                unixPermissions = GetString("UnixPermissions");
                                availabilityZone = GetString("ProvisionedAvailabilityZone") ?? GetString("AvailabilityZone");
                                isLargeVolume = GetBool("IsLargeVolume");
                                avsDataStore = GetString("AvsDataStore");
                                volumeType = GetString("VolumeType");

                                // Derive vnet/subnet names from subnetId
                                if (!string.IsNullOrWhiteSpace(subnetId))
                                {
                                    var parts = subnetId.Split('/', StringSplitOptions.RemoveEmptyEntries);
                                    for (int i = 0; i < parts.Length - 1; i++)
                                    {
                                        if (parts[i].Equals("virtualNetworks", StringComparison.OrdinalIgnoreCase))
                                        {
                                            vnetName = parts[i + 1];
                                        }
                                        if (parts[i].Equals("subnets", StringComparison.OrdinalIgnoreCase))
                                        {
                                            subnetName = parts[i + 1];
                                        }
                                    }
                                }

                                // Mount path (first mount target IP + creation token)
                                try
                                {
                                    var creationToken = GetString("CreationToken");
                                    var mtProp = t.GetProperty("MountTargets");
                                    string? ip = null;
                                    if (mtProp != null)
                                    {
                                        var mtVal = mtProp.GetValue(data) as System.Collections.IEnumerable;
                                        if (mtVal != null)
                                        {
                                            foreach (var mt in mtVal)
                                            {
                                                if (mt == null) continue;
                                                var mtType = mt.GetType();
                                                var ipProp = mtType.GetProperty("IpAddress") ?? mtType.GetProperty("IPAddress");
                                                var ipVal = ipProp?.GetValue(mt)?.ToString();
                                                if (!string.IsNullOrWhiteSpace(ipVal))
                                                {
                                                    ip = ipVal;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    if (!string.IsNullOrWhiteSpace(ip) && !string.IsNullOrWhiteSpace(creationToken))
                                    {
                                        mountPath = $"{ip}:/{creationToken}";
                                    }
                                }
                                catch { /* ignore mount path failures */ }
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
                                
                                // Pricing metadata
                                CapacityPoolServiceLevel = capacityPool.Data.ServiceLevel.ToString(),
                                IsFlexibleServiceLevel = (capacityPool.Data.ServiceLevel.ToString() ?? "").Equals("Flexible", StringComparison.OrdinalIgnoreCase),
                                ProvisionedSizeBytes = volume.Data.UsageThreshold,
                                ThroughputMibps = volume.Data.ThroughputMibps,
                                ActualThroughputMibps = volume.Data.ActualThroughputMibps,
                                CoolAccessEnabled = coolAccessEnabled,
                                CoolTieringPolicy = coolTieringPolicy,
                                CoolnessPeriodDays = coolnessPeriodDays,
                                MaximumNumberOfFiles = maxFiles,
                                SubnetId = subnetId,
                                VirtualNetworkName = vnetName,
                                SubnetName = subnetName,
                                NetworkFeatures = networkFeatures,
                                SecurityStyle = securityStyle,
                                IsKerberosEnabled = isKerberosEnabled,
                                EncryptionKeySource = encryptionKeySource,
                                IsLdapEnabled = isLdapEnabled,
                                UnixPermissions = unixPermissions,
                                AvailabilityZone = availabilityZone,
                                IsLargeVolume = isLargeVolume,
                                AvsDataStore = avsDataStore,
                                VolumeType = volumeType,
                                MountPath = mountPath,
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
                            
                            // Collect ANF snapshot metadata and metrics
                            try
                            {
                                await LogProgressAsync($"        → Collecting snapshots for volume {volume.Data.Name}...");
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
                                
                                if (snapshotList.Count > 0)
                                {
                                    await LogProgressAsync($"        ✓ Found {snapshotList.Count} snapshot(s)");
                                }
                                
                                // Collect actual Azure Monitor metrics for ANF volume
                                if (_metricsService != null)
                                {
                                    await LogProgressAsync($"        → Collecting Azure Monitor metrics for volume {volume.Data.Name}...");
                                    var (hasData, daysAvailable, metricsSummary) = await _metricsService
                                        .CollectAnfVolumeMetricsAsync(volume.Id.ToString(), volume.Data.Name);
                                    
                                    v.MonitoringEnabled = hasData;
                                    v.MonitoringDataAvailableDays = daysAvailable;
                                    v.HistoricalMetricsSummary = metricsSummary;
                                    
                                    if (hasData)
                                    {
                                        await LogProgressAsync($"        ✓ Metrics collected: {daysAvailable} days available");
                                    }
                                    else
                                    {
                                        await LogProgressAsync($"        ⚠ No metrics data available for volume {volume.Data.Name}");
                                    }
                                }
                            }
                            catch (Exception snapshotEx)
                            {
                                _logger.LogWarning(snapshotEx, "Failed to collect snapshot/metrics metadata for ANF volume {VolumeName}", volume.Data.Name);
                                await LogProgressAsync($"        ⚠ Failed to collect snapshots for volume {volume.Data.Name}: {snapshotEx.Message}");
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
                    await LogProgressAsync($"    ⚠ Failed to access NetApp account {netAppAccount.Data.Name}: {ex.Message}");
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
