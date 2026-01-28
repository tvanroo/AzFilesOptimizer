using Azure;
using Azure.Data.Tables;
using AzFilesOptimizer.Backend.Models;
using System.Text;
using System.Web;

namespace AzFilesOptimizer.Backend.Services;

public partial class DiscoveredResourceStorageService
{
    private readonly TableClient _sharesTableClient;
    private readonly TableClient _volumesTableClient;
    private readonly TableClient _disksTableClient;
    private string _storageConnectionString;

public DiscoveredResourceStorageService(string storageConnectionString)
    {
        _storageConnectionString = storageConnectionString;
        var tableServiceClient = new TableServiceClient(storageConnectionString);

        _sharesTableClient = tableServiceClient.GetTableClient("discoveredshares");
        _sharesTableClient.CreateIfNotExists();

        _volumesTableClient = tableServiceClient.GetTableClient("discoveredanfvolumes");
        _volumesTableClient.CreateIfNotExists();

        _disksTableClient = tableServiceClient.GetTableClient("discovereddisks");
        _disksTableClient.CreateIfNotExists();
    }

    public async Task SaveSharesAsync(string jobId, List<DiscoveredAzureFileShare> shares)
    {
        if (shares == null || shares.Count == 0) return;

        // Batch operations in groups of 100 (Table Storage limit)
        var batches = shares.Select((share, index) => new { share, index })
                           .GroupBy(x => x.index / 100)
                           .Select(g => g.Select(x => x.share).ToList());

        foreach (var batch in batches)
        {
            var tasks = batch.Select(share => SaveShareAsync(jobId, share));
            await Task.WhenAll(tasks);
        }
    }

    private async Task SaveShareAsync(string jobId, DiscoveredAzureFileShare share)
    {
        var entity = new TableEntity(jobId, EncodeResourceId(share.ResourceId))
        {
            // Hierarchy
            { "TenantId", share.TenantId },
            { "SubscriptionId", share.SubscriptionId },
            { "ResourceGroup", share.ResourceGroup },
            { "StorageAccountName", share.StorageAccountName },
            { "ShareName", share.ShareName },
            
            // Resource identification
            { "ResourceId", share.ResourceId },
            { "Location", share.Location },
            
            // Storage Account properties
            { "StorageAccountSku", share.StorageAccountSku },
            { "StorageAccountKind", share.StorageAccountKind },
            { "EnableHttpsTrafficOnly", share.EnableHttpsTrafficOnly },
            { "MinimumTlsVersion", share.MinimumTlsVersion },
            { "AllowBlobPublicAccess", share.AllowBlobPublicAccess },
            { "AllowSharedKeyAccess", share.AllowSharedKeyAccess },
            
            // File Share properties
            { "AccessTier", share.AccessTier },
            { "AccessTierChangeTime", share.AccessTierChangeTime },
            { "AccessTierStatus", share.AccessTierStatus },
            { "ShareQuotaGiB", share.ShareQuotaGiB },
            { "ShareUsageBytes", share.ShareUsageBytes },
            { "EnabledProtocols", share.EnabledProtocols != null ? string.Join(",", share.EnabledProtocols) : null },
            { "RootSquash", share.RootSquash },
            
            // Performance properties
            { "ProvisionedIops", share.ProvisionedIops },
            { "ProvisionedBandwidthMiBps", share.ProvisionedBandwidthMiBps },
            { "EstimatedIops", share.EstimatedIops },
            { "EstimatedThroughputMiBps", share.EstimatedThroughputMiBps },
            
            // Lease properties
            { "LeaseStatus", share.LeaseStatus },
            { "LeaseState", share.LeaseState },
            { "LeaseDuration", share.LeaseDuration },
            
            // Snapshot properties
            { "SnapshotTime", share.SnapshotTime },
            { "IsSnapshot", share.IsSnapshot },
            { "SnapshotId", share.SnapshotId },
            
            // Soft delete properties
            { "IsDeleted", share.IsDeleted },
            { "DeletedTime", share.DeletedTime },
            { "RemainingRetentionDays", share.RemainingRetentionDays },
            { "Version", share.Version },
            
            // Timestamps
            { "LastModifiedTime", share.LastModifiedTime },
            { "CreationTime", share.CreationTime },
            { "DiscoveredAt", share.DiscoveredAt },

            // Monitoring / Metrics
            { "MonitoringEnabled", share.MonitoringEnabled },
            { "MonitoringDataAvailableDays", share.MonitoringDataAvailableDays },
            { "HistoricalMetricsSummary", share.HistoricalMetricsSummary }
        };

        // Store metadata and tags as JSON strings (Table Storage doesn't support complex types)
        if (share.Metadata != null && share.Metadata.Count > 0)
        {
            entity["Metadata"] = System.Text.Json.JsonSerializer.Serialize(share.Metadata);
        }
        
        if (share.Tags != null && share.Tags.Count > 0)
        {
            entity["Tags"] = System.Text.Json.JsonSerializer.Serialize(share.Tags);
        }

        await _sharesTableClient.UpsertEntityAsync(entity);
    }

    public async Task<List<DiscoveredAzureFileShare>> GetSharesByJobIdAsync(string jobId)
    {
        var shares = new List<DiscoveredAzureFileShare>();

        await foreach (var entity in _sharesTableClient.QueryAsync<TableEntity>(filter: $"PartitionKey eq '{jobId}'"))
        {
            shares.Add(EntityToShare(entity));
        }

        return shares;
    }

    public async Task DeleteSharesByJobIdAsync(string jobId)
    {
        var entities = new List<TableEntity>();
        
        await foreach (var entity in _sharesTableClient.QueryAsync<TableEntity>(filter: $"PartitionKey eq '{jobId}'"))
        {
            entities.Add(entity);
        }

        // Delete in batches
        var tasks = entities.Select(entity => _sharesTableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey));
        await Task.WhenAll(tasks);
    }

    public async Task SaveVolumesAsync(string jobId, List<DiscoveredAnfVolume> volumes)
    {
        if (volumes == null || volumes.Count == 0) return;

        var tasks = volumes.Select(volume => SaveVolumeAsync(jobId, volume));
        await Task.WhenAll(tasks);
    }

    private async Task SaveVolumeAsync(string jobId, DiscoveredAnfVolume volume)
    {
        var entity = new TableEntity(jobId, EncodeResourceId(volume.ResourceId))
        {
            { "ResourceId", volume.ResourceId },
            { "VolumeName", volume.VolumeName },
            { "NetAppAccountName", volume.NetAppAccountName },
            { "CapacityPoolName", volume.CapacityPoolName },
            { "ResourceGroup", volume.ResourceGroup },
            { "SubscriptionId", volume.SubscriptionId },
            { "Location", volume.Location },
            { "ServiceLevel", volume.ServiceLevel },
            { "PoolQosType", volume.PoolQosType },
            { "PoolEncryptionType", volume.PoolEncryptionType },
            { "CapacityPoolServiceLevel", volume.CapacityPoolServiceLevel },
            { "IsFlexibleServiceLevel", volume.IsFlexibleServiceLevel },
            { "ProvisionedSizeBytes", volume.ProvisionedSizeBytes },
            { "ThroughputMibps", volume.ThroughputMibps },
            { "ActualThroughputMibps", volume.ActualThroughputMibps },
            { "PoolTotalThroughputMibps", volume.PoolTotalThroughputMibps },
            { "PoolTotalCapacityBytes", volume.PoolTotalCapacityBytes },
            { "CoolAccessEnabled", volume.CoolAccessEnabled },
            { "CoolTieringPolicy", volume.CoolTieringPolicy },
            { "CoolnessPeriodDays", volume.CoolnessPeriodDays },
            { "MaximumNumberOfFiles", volume.MaximumNumberOfFiles },
            { "SubnetId", volume.SubnetId },
            { "VirtualNetworkName", volume.VirtualNetworkName },
            { "SubnetName", volume.SubnetName },
            { "NetworkFeatures", volume.NetworkFeatures },
            { "SecurityStyle", volume.SecurityStyle },
            { "IsKerberosEnabled", volume.IsKerberosEnabled },
            { "EncryptionKeySource", volume.EncryptionKeySource },
            { "IsLdapEnabled", volume.IsLdapEnabled },
            { "UnixPermissions", volume.UnixPermissions },
            { "AvailabilityZone", volume.AvailabilityZone },
            { "IsLargeVolume", volume.IsLargeVolume },
            { "AvsDataStore", volume.AvsDataStore },
            { "VolumeType", volume.VolumeType },
            { "MountPath", volume.MountPath },
            { "EstimatedIops", volume.EstimatedIops },
            { "EstimatedThroughputMiBps", volume.EstimatedThroughputMiBps },
            { "ProtocolTypes", volume.ProtocolTypes != null ? string.Join(",", volume.ProtocolTypes) : null },
            { "Tags", volume.Tags != null ? System.Text.Json.JsonSerializer.Serialize(volume.Tags) : null },
            { "DiscoveredAt", volume.DiscoveredAt },

            // Snapshot / backup metadata
            { "SnapshotCount", volume.SnapshotCount },
            { "TotalSnapshotSizeBytes", volume.TotalSnapshotSizeBytes },
            { "ChurnRateBytesPerDay", volume.ChurnRateBytesPerDay },
            { "BackupPolicyConfigured", volume.BackupPolicyConfigured },

            // Monitoring / metrics
            { "MonitoringEnabled", volume.MonitoringEnabled },
            { "MonitoringDataAvailableDays", volume.MonitoringDataAvailableDays },
            { "HistoricalMetricsSummary", volume.HistoricalMetricsSummary },

            // Security / TLS
            { "MinimumTlsVersion", volume.MinimumTlsVersion },
            
            // Cool data assumptions (volume-level overrides)
            { "CoolDataPercentageOverride", volume.CoolDataPercentageOverride },
            { "CoolDataRetrievalPercentageOverride", volume.CoolDataRetrievalPercentageOverride },
            { "CoolAssumptionsModifiedAt", volume.CoolAssumptionsModifiedAt }
        };

        await _volumesTableClient.UpsertEntityAsync(entity);
    }

    public async Task<List<DiscoveredAnfVolume>> GetVolumesByJobIdAsync(string jobId)
    {
        var volumes = new List<DiscoveredAnfVolume>();

        await foreach (var entity in _volumesTableClient.QueryAsync<TableEntity>(filter: $"PartitionKey eq '{jobId}'"))
        {
            volumes.Add(EntityToVolume(entity));
        }

        return volumes;
    }

    public async Task DeleteVolumesByJobIdAsync(string jobId)
    {
        var entities = new List<TableEntity>();
        
        await foreach (var entity in _volumesTableClient.QueryAsync<TableEntity>(filter: $"PartitionKey eq '{jobId}'"))
        {
            entities.Add(entity);
        }

        var tasks = entities.Select(entity => _volumesTableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey));
        await Task.WhenAll(tasks);
    }

    public async Task DeleteDisksByJobIdAsync(string jobId)
    {
        var entities = new List<TableEntity>();

        await foreach (var entity in _disksTableClient.QueryAsync<TableEntity>(filter: $"PartitionKey eq '{jobId}'"))
        {
            entities.Add(entity);
        }

        var tasks = entities.Select(entity => _disksTableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey));
        await Task.WhenAll(tasks);
    }

    private static string EncodeResourceId(string resourceId)
    {
        // Azure Table Storage RowKey doesn't allow /, \, #, ?
        // Encode resource ID to Base64 to make it safe
        var bytes = Encoding.UTF8.GetBytes(resourceId);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static DiscoveredAzureFileShare EntityToShare(TableEntity entity)
    {
        return new DiscoveredAzureFileShare
        {
            TenantId = entity.GetString("TenantId") ?? string.Empty,
            SubscriptionId = entity.GetString("SubscriptionId") ?? string.Empty,
            ResourceGroup = entity.GetString("ResourceGroup") ?? string.Empty,
            StorageAccountName = entity.GetString("StorageAccountName") ?? string.Empty,
            ShareName = entity.GetString("ShareName") ?? string.Empty,
            ResourceId = entity.GetString("ResourceId") ?? string.Empty,
            Location = entity.GetString("Location") ?? string.Empty,
            StorageAccountSku = entity.GetString("StorageAccountSku") ?? string.Empty,
            StorageAccountKind = entity.GetString("StorageAccountKind") ?? string.Empty,
            EnableHttpsTrafficOnly = entity.GetBoolean("EnableHttpsTrafficOnly"),
            MinimumTlsVersion = entity.GetString("MinimumTlsVersion"),
            AllowBlobPublicAccess = entity.GetBoolean("AllowBlobPublicAccess"),
            AllowSharedKeyAccess = entity.GetBoolean("AllowSharedKeyAccess"),
            AccessTier = entity.GetString("AccessTier") ?? string.Empty,
            AccessTierChangeTime = entity.GetDateTime("AccessTierChangeTime"),
            AccessTierStatus = entity.GetString("AccessTierStatus"),
            ShareQuotaGiB = entity.GetInt64("ShareQuotaGiB"),
            ShareUsageBytes = entity.GetInt64("ShareUsageBytes"),
            EnabledProtocols = entity.GetString("EnabledProtocols")?.Split(',', StringSplitOptions.RemoveEmptyEntries),
            RootSquash = entity.GetString("RootSquash"),
            ProvisionedIops = entity.GetInt32("ProvisionedIops"),
            ProvisionedBandwidthMiBps = entity.GetInt32("ProvisionedBandwidthMiBps"),
            EstimatedIops = entity.GetInt32("EstimatedIops"),
            EstimatedThroughputMiBps = entity.GetDouble("EstimatedThroughputMiBps"),
            LeaseStatus = entity.GetString("LeaseStatus"),
            LeaseState = entity.GetString("LeaseState"),
            LeaseDuration = entity.GetString("LeaseDuration"),
            SnapshotTime = entity.GetDateTime("SnapshotTime"),
            IsSnapshot = entity.GetBoolean("IsSnapshot") ?? false,
            SnapshotId = entity.GetString("SnapshotId"),
            IsDeleted = entity.GetBoolean("IsDeleted"),
            DeletedTime = entity.GetDateTime("DeletedTime"),
            RemainingRetentionDays = entity.GetInt32("RemainingRetentionDays"),
            Version = entity.GetString("Version"),
            Metadata = DeserializeDictionary(entity.GetString("Metadata")),
            Tags = DeserializeDictionary(entity.GetString("Tags")),
            LastModifiedTime = entity.GetDateTime("LastModifiedTime"),
            CreationTime = entity.GetDateTime("CreationTime"),
            DiscoveredAt = entity.GetDateTime("DiscoveredAt") ?? DateTime.UtcNow,

            // Monitoring / Metrics
            MonitoringEnabled = entity.GetBoolean("MonitoringEnabled") ?? false,
            MonitoringDataAvailableDays = entity.GetInt32("MonitoringDataAvailableDays"),
            HistoricalMetricsSummary = entity.GetString("HistoricalMetricsSummary")
        };
    }

    private static DiscoveredAnfVolume EntityToVolume(TableEntity entity)
    {
        return new DiscoveredAnfVolume
        {
            ResourceId = entity.GetString("ResourceId") ?? string.Empty,
            VolumeName = entity.GetString("VolumeName") ?? string.Empty,
            NetAppAccountName = entity.GetString("NetAppAccountName") ?? string.Empty,
            CapacityPoolName = entity.GetString("CapacityPoolName") ?? string.Empty,
            ResourceGroup = entity.GetString("ResourceGroup") ?? string.Empty,
            SubscriptionId = entity.GetString("SubscriptionId") ?? string.Empty,
            Location = entity.GetString("Location") ?? string.Empty,
            ServiceLevel = entity.GetString("ServiceLevel") ?? string.Empty,
            PoolQosType = entity.GetString("PoolQosType"),
            PoolEncryptionType = entity.GetString("PoolEncryptionType"),
            CapacityPoolServiceLevel = entity.GetString("CapacityPoolServiceLevel"),
            IsFlexibleServiceLevel = entity.GetBoolean("IsFlexibleServiceLevel") ?? false,
            ProvisionedSizeBytes = entity.GetInt64("ProvisionedSizeBytes") ?? 0,
            ThroughputMibps = entity.GetDouble("ThroughputMibps"),
            ActualThroughputMibps = entity.GetDouble("ActualThroughputMibps"),
            PoolTotalThroughputMibps = entity.GetDouble("PoolTotalThroughputMibps"),
            PoolTotalCapacityBytes = entity.GetInt64("PoolTotalCapacityBytes"),
            CoolAccessEnabled = entity.GetBoolean("CoolAccessEnabled"),
            CoolTieringPolicy = entity.GetString("CoolTieringPolicy"),
            CoolnessPeriodDays = entity.GetInt32("CoolnessPeriodDays"),
            MaximumNumberOfFiles = entity.GetInt64("MaximumNumberOfFiles"),
            SubnetId = entity.GetString("SubnetId"),
            VirtualNetworkName = entity.GetString("VirtualNetworkName"),
            SubnetName = entity.GetString("SubnetName"),
            NetworkFeatures = entity.GetString("NetworkFeatures"),
            SecurityStyle = entity.GetString("SecurityStyle"),
            IsKerberosEnabled = entity.GetBoolean("IsKerberosEnabled"),
            EncryptionKeySource = entity.GetString("EncryptionKeySource"),
            IsLdapEnabled = entity.GetBoolean("IsLdapEnabled"),
            UnixPermissions = entity.GetString("UnixPermissions"),
            AvailabilityZone = entity.GetString("AvailabilityZone"),
            IsLargeVolume = entity.GetBoolean("IsLargeVolume"),
            AvsDataStore = entity.GetString("AvsDataStore"),
            VolumeType = entity.GetString("VolumeType"),
            MountPath = entity.GetString("MountPath"),
            EstimatedIops = entity.GetInt32("EstimatedIops"),
            EstimatedThroughputMiBps = entity.GetDouble("EstimatedThroughputMiBps"),
            ProtocolTypes = entity.GetString("ProtocolTypes")?.Split(',', StringSplitOptions.RemoveEmptyEntries),
            Tags = DeserializeDictionary(entity.GetString("Tags")),
            DiscoveredAt = entity.GetDateTime("DiscoveredAt") ?? DateTime.UtcNow,

            // Snapshot / backup metadata
            SnapshotCount = entity.GetInt32("SnapshotCount"),
            TotalSnapshotSizeBytes = entity.GetInt64("TotalSnapshotSizeBytes"),
            ChurnRateBytesPerDay = entity.GetDouble("ChurnRateBytesPerDay"),
            BackupPolicyConfigured = entity.GetBoolean("BackupPolicyConfigured"),

            // Monitoring / metrics
            MonitoringEnabled = entity.GetBoolean("MonitoringEnabled") ?? false,
            MonitoringDataAvailableDays = entity.GetInt32("MonitoringDataAvailableDays"),
            HistoricalMetricsSummary = entity.GetString("HistoricalMetricsSummary"),

            // Security / TLS
            MinimumTlsVersion = entity.GetString("MinimumTlsVersion"),
            
            // Cool data assumptions (volume-level overrides)
            CoolDataPercentageOverride = entity.GetDouble("CoolDataPercentageOverride"),
            CoolDataRetrievalPercentageOverride = entity.GetDouble("CoolDataRetrievalPercentageOverride"),
            CoolAssumptionsModifiedAt = entity.GetDateTime("CoolAssumptionsModifiedAt")
        };
    }

    private static Dictionary<string, string>? DeserializeDictionary(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveDisksAsync(string jobId, List<DiscoveredManagedDisk> disks)
    {
        if (disks == null || disks.Count == 0) return;

        var tasks = disks.Select(disk => SaveDiskAsync(jobId, disk));
        await Task.WhenAll(tasks);
    }

    private async Task SaveDiskAsync(string jobId, DiscoveredManagedDisk disk)
    {
        var entity = new TableEntity(jobId, EncodeResourceId(disk.ResourceId))
        {
            { "TenantId", disk.TenantId },
            { "SubscriptionId", disk.SubscriptionId },
            { "ResourceGroup", disk.ResourceGroup },
            { "DiskName", disk.DiskName },
            { "ResourceId", disk.ResourceId },
            { "Location", disk.Location },
            { "DiskSku", disk.DiskSku },
            { "DiskTier", disk.DiskTier },
            { "DiskSizeGB", disk.DiskSizeGB },
            { "DiskState", disk.DiskState },
            { "ProvisioningState", disk.ProvisioningState },
            { "DiskSizeBytes", disk.DiskSizeBytes },
            { "DiskType", disk.DiskType },
            { "BurstingEnabled", disk.BurstingEnabled },
            { "EstimatedIops", disk.EstimatedIops },
            { "EstimatedThroughputMiBps", disk.EstimatedThroughputMiBps },
            { "IsAttached", disk.IsAttached },
            { "AttachedVmId", disk.AttachedVmId },
            { "AttachedVmName", disk.AttachedVmName },
            { "Lun", disk.Lun },
            { "VmSize", disk.VmSize },
            { "VmCpuCount", disk.VmCpuCount },
            { "VmMemoryGiB", disk.VmMemoryGiB },
            { "VmOsType", disk.VmOsType },
            { "IsOsDisk", disk.IsOsDisk },
            { "TimeCreated", disk.TimeCreated },
            { "DiscoveredAt", disk.DiscoveredAt },
            { "MonitoringEnabled", disk.MonitoringEnabled },
            { "MonitoringDataAvailableDays", disk.MonitoringDataAvailableDays },
            { "HistoricalMetricsSummary", disk.HistoricalMetricsSummary },
            { "UsedBytes", disk.UsedBytes },
            { "AverageReadIops", disk.AverageReadIops },
            { "AverageWriteIops", disk.AverageWriteIops },
            { "AverageReadThroughputMiBps", disk.AverageReadThroughputMiBps },
            { "AverageWriteThroughputMiBps", disk.AverageWriteThroughputMiBps },
            { "VmMetricsSummary", disk.VmMetricsSummary },
            { "VmMonitoringDataAvailableDays", disk.VmMonitoringDataAvailableDays },
            { "VmOverallMetricsSummary", disk.VmOverallMetricsSummary },
            { "VmOverallMonitoringDataAvailableDays", disk.VmOverallMonitoringDataAvailableDays }
        };

        if (disk.Tags != null && disk.Tags.Count > 0)
        {
            entity["Tags"] = System.Text.Json.JsonSerializer.Serialize(disk.Tags);
        }

        if (disk.VmTags != null && disk.VmTags.Count > 0)
        {
            entity["VmTags"] = System.Text.Json.JsonSerializer.Serialize(disk.VmTags);
        }

        await _disksTableClient.UpsertEntityAsync(entity);
    }

    public async Task<List<DiscoveredManagedDisk>> GetDisksByJobIdAsync(string jobId)
    {
        var disks = new List<DiscoveredManagedDisk>();

        await foreach (var entity in _disksTableClient.QueryAsync<TableEntity>(filter: $"PartitionKey eq '{jobId}'"))
        {
            disks.Add(EntityToDisk(entity));
        }

        return disks;
    }

    private static DiscoveredManagedDisk EntityToDisk(TableEntity entity)
    {
        return new DiscoveredManagedDisk
        {
            TenantId = entity.GetString("TenantId") ?? string.Empty,
            SubscriptionId = entity.GetString("SubscriptionId") ?? string.Empty,
            ResourceGroup = entity.GetString("ResourceGroup") ?? string.Empty,
            DiskName = entity.GetString("DiskName") ?? string.Empty,
            ResourceId = entity.GetString("ResourceId") ?? string.Empty,
            Location = entity.GetString("Location") ?? string.Empty,
            DiskSku = entity.GetString("DiskSku") ?? string.Empty,
            DiskTier = entity.GetString("DiskTier") ?? string.Empty,
            DiskSizeGB = entity.GetInt64("DiskSizeGB") ?? 0,
            DiskState = entity.GetString("DiskState") ?? string.Empty,
            ProvisioningState = entity.GetString("ProvisioningState") ?? string.Empty,
            DiskSizeBytes = entity.GetInt64("DiskSizeBytes"),
            DiskType = entity.GetString("DiskType"),
            BurstingEnabled = entity.GetBoolean("BurstingEnabled"),
            EstimatedIops = entity.GetInt32("EstimatedIops"),
            EstimatedThroughputMiBps = entity.GetDouble("EstimatedThroughputMiBps"),
            IsAttached = entity.GetBoolean("IsAttached") ?? false,
            AttachedVmId = entity.GetString("AttachedVmId"),
            AttachedVmName = entity.GetString("AttachedVmName"),
            Lun = entity.GetInt32("Lun"),
            VmSize = entity.GetString("VmSize"),
            VmCpuCount = entity.GetInt32("VmCpuCount"),
            VmMemoryGiB = entity.GetDouble("VmMemoryGiB"),
            VmOsType = entity.GetString("VmOsType"),
            IsOsDisk = entity.GetBoolean("IsOsDisk"),
            Tags = DeserializeDictionary(entity.GetString("Tags")),
            VmTags = DeserializeDictionary(entity.GetString("VmTags")),
            TimeCreated = entity.GetDateTime("TimeCreated"),
            DiscoveredAt = entity.GetDateTime("DiscoveredAt") ?? DateTime.UtcNow,
            MonitoringEnabled = entity.GetBoolean("MonitoringEnabled") ?? false,
            MonitoringDataAvailableDays = entity.GetInt32("MonitoringDataAvailableDays"),
            HistoricalMetricsSummary = entity.GetString("HistoricalMetricsSummary"),
            UsedBytes = entity.GetInt64("UsedBytes"),
            AverageReadIops = entity.GetDouble("AverageReadIops"),
            AverageWriteIops = entity.GetDouble("AverageWriteIops"),
            AverageReadThroughputMiBps = entity.GetDouble("AverageReadThroughputMiBps"),
            AverageWriteThroughputMiBps = entity.GetDouble("AverageWriteThroughputMiBps"),
            VmMetricsSummary = entity.GetString("VmMetricsSummary"),
            VmMonitoringDataAvailableDays = entity.GetInt32("VmMonitoringDataAvailableDays"),
            VmOverallMetricsSummary = entity.GetString("VmOverallMetricsSummary"),
            VmOverallMonitoringDataAvailableDays = entity.GetInt32("VmOverallMonitoringDataAvailableDays")
        };
    }
}
