using Azure;
using Azure.Data.Tables;
using AzFilesOptimizer.Backend.Models;
using System.Text;
using System.Web;

namespace AzFilesOptimizer.Backend.Services;

public class DiscoveredResourceStorageService
{
    private readonly TableClient _sharesTableClient;
    private readonly TableClient _volumesTableClient;

    public DiscoveredResourceStorageService(string storageConnectionString)
    {
        var tableServiceClient = new TableServiceClient(storageConnectionString);
        
        _sharesTableClient = tableServiceClient.GetTableClient("discoveredshares");
        _sharesTableClient.CreateIfNotExists();
        
        _volumesTableClient = tableServiceClient.GetTableClient("discoveredanfvolumes");
        _volumesTableClient.CreateIfNotExists();
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
            { "ProvisionedMaxIops", share.ProvisionedMaxIops },
            { "ProvisionedMaxBandwidthMiBps", share.ProvisionedMaxBandwidthMiBps },
            
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
            { "DiscoveredAt", share.DiscoveredAt }
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
            { "ProvisionedSizeBytes", volume.ProvisionedSizeBytes },
            { "ProtocolTypes", volume.ProtocolTypes != null ? string.Join(",", volume.ProtocolTypes) : null },
            { "Tags", volume.Tags != null ? System.Text.Json.JsonSerializer.Serialize(volume.Tags) : null },
            { "DiscoveredAt", volume.DiscoveredAt }
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
            ProvisionedMaxIops = entity.GetInt32("ProvisionedMaxIops"),
            ProvisionedMaxBandwidthMiBps = entity.GetInt32("ProvisionedMaxBandwidthMiBps"),
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
            DiscoveredAt = entity.GetDateTime("DiscoveredAt") ?? DateTime.UtcNow
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
            ProvisionedSizeBytes = entity.GetInt64("ProvisionedSizeBytes") ?? 0,
            ProtocolTypes = entity.GetString("ProtocolTypes")?.Split(',', StringSplitOptions.RemoveEmptyEntries),
            Tags = DeserializeDictionary(entity.GetString("Tags")),
            DiscoveredAt = entity.GetDateTime("DiscoveredAt") ?? DateTime.UtcNow
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
}
