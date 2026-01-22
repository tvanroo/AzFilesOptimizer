namespace AzFilesOptimizer.Backend.Models;

/// <summary>
/// Unified wrapper for different discovered resource types
/// to enable consistent cost estimation across Azure Files, ANF, and Managed Disks
/// </summary>
public class UnifiedResource
{
    public string ResourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty; // "AzureFile", "ANF", "ManagedDisk"
    public string Location { get; set; } = string.Empty;
    public double CapacityGb { get; set; }
    public double UsedGb { get; set; }
    public double? SnapshotSizeGb { get; set; }
    public Dictionary<string, object>? Properties { get; set; }

    /// <summary>
    /// Create UnifiedResource from DiscoveredAzureFileShare
    /// </summary>
    public static UnifiedResource FromFileShare(DiscoveredAzureFileShare share)
    {
        return new UnifiedResource
        {
            ResourceId = share.ResourceId,
            Name = share.ShareName,
            ResourceType = "AzureFile",
            Location = share.Location,
            CapacityGb = share.ShareQuotaGiB ?? 0,
            UsedGb = share.ShareUsageBytes.HasValue ? share.ShareUsageBytes.Value / (1024.0 * 1024.0 * 1024.0) : 0,
            SnapshotSizeGb = share.TotalSnapshotSizeBytes.HasValue ? share.TotalSnapshotSizeBytes.Value / (1024.0 * 1024.0 * 1024.0) : null,
            Properties = new Dictionary<string, object>
            {
                ["tier"] = share.AccessTier,
                ["sku"] = share.StorageAccountSku,
                ["redundancy"] = share.RedundancyType ?? "LRS"
            }
        };
    }

    /// <summary>
    /// Create UnifiedResource from DiscoveredAnfVolume
    /// </summary>
    public static UnifiedResource FromAnfVolume(DiscoveredAnfVolume volume)
    {
        return new UnifiedResource
        {
            ResourceId = volume.ResourceId,
            Name = volume.VolumeName,
            ResourceType = "ANF",
            Location = volume.Location,
            CapacityGb = volume.ProvisionedSizeBytes / (1024.0 * 1024.0 * 1024.0),
            UsedGb = 0, // ANF doesn't provide used size directly
            SnapshotSizeGb = volume.TotalSnapshotSizeBytes.HasValue ? volume.TotalSnapshotSizeBytes.Value / (1024.0 * 1024.0 * 1024.0) : null,
            Properties = new Dictionary<string, object>
            {
                ["serviceLevel"] = volume.ServiceLevel,
                ["coolAccess"] = (volume.CoolAccessEnabled ?? false).ToString(),
                ["poolQosType"] = volume.PoolQosType ?? "Auto"
            }
        };
    }

    /// <summary>
    /// Create UnifiedResource from DiscoveredManagedDisk
    /// </summary>
    public static UnifiedResource FromManagedDisk(DiscoveredManagedDisk disk)
    {
        return new UnifiedResource
        {
            ResourceId = disk.ResourceId,
            Name = disk.DiskName,
            ResourceType = "ManagedDisk",
            Location = disk.Location,
            CapacityGb = disk.DiskSizeGB,
            UsedGb = disk.DiskSizeGB, // For disks, used = capacity
            SnapshotSizeGb = null, // Disk snapshots stored separately
            Properties = new Dictionary<string, object>
            {
                ["sku"] = disk.DiskSku,
                ["diskType"] = disk.ManagedDiskType ?? "PremiumSSD"
            }
        };
    }
}
