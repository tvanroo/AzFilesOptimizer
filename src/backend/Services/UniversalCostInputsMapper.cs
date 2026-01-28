using AzFilesOptimizer.Backend.Models;

namespace AzFilesOptimizer.Backend.Services;

/// <summary>
/// Maps between UniversalCostInputs and platform-specific volume info models,
/// and constructs UniversalCostInputs from discovered resource data
/// </summary>
public static class UniversalCostInputsMapper
{
    /// <summary>
    /// Create UniversalCostInputs from ANF volume data
    /// </summary>
    public static UniversalCostInputs FromAnfVolume(AnfVolumeInfo volume)
    {
        return new UniversalCostInputs
        {
            ResourceId = volume.VolumeId,
            ResourceName = volume.VolumeName,
            ResourceType = "ANF",
            Region = volume.Region,
            StorageTier = volume.ServiceLevel,
            
            // Capacity
            ProvisionedCapacityGiB = volume.ProvisionedCapacityGb,
            ConsumedCapacityGiB = volume.UsedCapacityGb,
            SnapshotSizeGiB = volume.SnapshotSizeGb,
            
            // Cool tier
            CoolAccessEnabled = volume.CoolAccessEnabled,
            CoolDataSizeGiB = volume.CoolDataGb,
            CoolDataReadGiB = volume.DataRetrievedFromCoolGb,
            CoolDataWriteGiB = volume.DataTieredToCoolGb,
            
            // Performance
            ThroughputMiBps = volume.RequiredThroughputMibS,
            
            // Flags
            IsProvisioned = true, // ANF is always provisioned
            IsLargeVolume = volume.IsLargeVolume,
            
            CollectedAt = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Create UniversalCostInputs from Azure Files share data
    /// </summary>
    public static UniversalCostInputs FromAzureFilesVolume(AzureFilesVolumeInfo volume)
    {
        return new UniversalCostInputs
        {
            ResourceId = volume.VolumeId,
            ResourceName = volume.VolumeName,
            ResourceType = "AzureFiles",
            Region = volume.Region,
            StorageTier = volume.Tier,
            Redundancy = volume.Redundancy,
            
            // Capacity
            ProvisionedCapacityGiB = volume.IsProvisioned ? volume.ProvisionedCapacityGb : null,
            ConsumedCapacityGiB = !volume.IsProvisioned ? volume.UsedCapacityGb : null,
            SnapshotSizeGiB = volume.SnapshotSizeGb,
            
            // Transactions
            TransactionsTotal = volume.TransactionsPerMonth,
            
            // Network
            EgressGiB = volume.EgressGbPerMonth,
            
            // Flags
            IsProvisioned = volume.IsProvisioned,
            
            CollectedAt = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Create UniversalCostInputs from Managed Disk data
    /// </summary>
    public static UniversalCostInputs FromManagedDisk(ManagedDiskVolumeInfo disk)
    {
        return new UniversalCostInputs
        {
            ResourceId = disk.VolumeId,
            ResourceName = disk.VolumeName,
            ResourceType = "ManagedDisk",
            Region = disk.Region,
            StorageTier = disk.DiskType,
            
            // Capacity
            ProvisionedCapacityGiB = disk.DiskSizeGb,
            SnapshotSizeGiB = disk.SnapshotSizeGb,
            
            // Performance
            IopsValue = disk.ProvisionedIops > 0 ? disk.ProvisionedIops : null,
            ThroughputMiBps = disk.ProvisionedThroughputMBps > 0 ? disk.ProvisionedThroughputMBps : null,
            
            // Transactions
            TransactionsTotal = disk.TransactionsPerMonth,
            
            // Flags
            IsProvisioned = true, // Managed Disks are always provisioned
            
            CollectedAt = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Create UniversalCostInputs from discovered ANF volume (DiscoveredAnfVolume)
    /// </summary>
    public static UniversalCostInputs FromDiscoveredAnfVolume(DiscoveredAnfVolume volume)
    {
        // Parse metrics if available
        double? coolTierSize = null;
        double? coolDataRead = null;
        double? coolDataWrite = null;
        
        if (!string.IsNullOrEmpty(volume.HistoricalMetricsSummary))
        {
            try
            {
                var metrics = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(
                    volume.HistoricalMetricsSummary,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                if (metrics != null)
                {
                    // Extract cool tier metrics if available
                    if (metrics.TryGetValue("VolumeCoolTierSize", out var coolSizeObj))
                    {
                        var coolSizeData = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(
                            coolSizeObj.ToString() ?? "{}");
                        if (coolSizeData != null && coolSizeData.TryGetValue("average", out var avgObj))
                        {
                            coolTierSize = Convert.ToDouble(avgObj) / (1024.0 * 1024.0 * 1024.0); // Convert to GiB
                        }
                    }
                    
                    if (metrics.TryGetValue("VolumeCoolTierDataReadSize", out var readObj))
                    {
                        var readData = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(
                            readObj.ToString() ?? "{}");
                        if (readData != null && readData.TryGetValue("total", out var totalObj))
                        {
                            coolDataRead = Convert.ToDouble(totalObj) / (1024.0 * 1024.0 * 1024.0); // Convert to GiB
                        }
                    }
                    
                    if (metrics.TryGetValue("VolumeCoolTierDataWriteSize", out var writeObj))
                    {
                        var writeData = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(
                            writeObj.ToString() ?? "{}");
                        if (writeData != null && writeData.TryGetValue("total", out var totalObj))
                        {
                            coolDataWrite = Convert.ToDouble(totalObj) / (1024.0 * 1024.0 * 1024.0); // Convert to GiB
                        }
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }
        }
        
        return new UniversalCostInputs
        {
            ResourceId = volume.ResourceId,
            ResourceName = volume.VolumeName ?? "Unknown",
            ResourceType = "ANF",
            Region = volume.Location ?? "unknown",
            StorageTier = volume.CapacityPoolServiceLevel,
            
            // Capacity
            ProvisionedCapacityGiB = volume.ProvisionedSizeBytes / (1024.0 * 1024.0 * 1024.0),
            SnapshotSizeGiB = (volume.TotalSnapshotSizeBytes ?? 0) / (1024.0 * 1024.0 * 1024.0),
            BackupSizeGiB = null, // Not currently tracked
            
            // Cool tier
            CoolAccessEnabled = volume.CoolAccessEnabled,
            CoolDataSizeGiB = coolTierSize,
            CoolDataReadGiB = coolDataRead,
            CoolDataWriteGiB = coolDataWrite,
            
            // Performance
            ThroughputMiBps = volume.ThroughputMibps,
            
            // Flags
            IsProvisioned = true,
            IsLargeVolume = volume.IsLargeVolume,
            
            // Metadata
            CollectedAt = volume.DiscoveredAt,
            MetricsPeriodDays = volume.MonitoringDataAvailableDays
        };
    }
    
    /// <summary>
    /// Convert UniversalCostInputs to ANF-specific volume info
    /// </summary>
    public static AnfVolumeInfo ToAnfVolumeInfo(UniversalCostInputs inputs)
    {
        return new AnfVolumeInfo
        {
            VolumeId = inputs.ResourceId,
            VolumeName = inputs.ResourceName,
            Region = inputs.Region,
            ServiceLevel = inputs.StorageTier ?? "Standard",
            ProvisionedCapacityGb = inputs.ProvisionedCapacityGiB ?? 0,
            UsedCapacityGb = inputs.ConsumedCapacityGiB ?? 0,
            SnapshotSizeGb = inputs.SnapshotSizeGiB ?? 0,
            CoolAccessEnabled = inputs.CoolAccessEnabled ?? false,
            HotDataGb = inputs.GetHotDataSizeGiB(),
            CoolDataGb = inputs.CoolDataSizeGiB ?? 0,
            DataTieredToCoolGb = inputs.CoolDataWriteGiB ?? 0,
            DataRetrievedFromCoolGb = inputs.CoolDataReadGiB ?? 0,
            IsLargeVolume = inputs.IsLargeVolume ?? false,
            RequiredThroughputMibS = inputs.ThroughputMiBps ?? 0
        };
    }
    
    /// <summary>
    /// Convert UniversalCostInputs to Azure Files volume info
    /// </summary>
    public static AzureFilesVolumeInfo ToAzureFilesVolumeInfo(UniversalCostInputs inputs)
    {
        return new AzureFilesVolumeInfo
        {
            VolumeId = inputs.ResourceId,
            VolumeName = inputs.ResourceName,
            Region = inputs.Region,
            Tier = inputs.StorageTier ?? "Hot",
            Redundancy = inputs.Redundancy ?? "LRS",
            IsProvisioned = inputs.IsProvisioned ?? false,
            ProvisionedCapacityGb = inputs.ProvisionedCapacityGiB ?? 0,
            UsedCapacityGb = inputs.ConsumedCapacityGiB ?? 0,
            SnapshotSizeGb = inputs.SnapshotSizeGiB ?? 0,
            TransactionsPerMonth = inputs.TransactionsTotal ?? 0,
            EgressGbPerMonth = inputs.EgressGiB ?? 0
        };
    }
    
    /// <summary>
    /// Convert UniversalCostInputs to Managed Disk volume info
    /// </summary>
    public static ManagedDiskVolumeInfo ToManagedDiskVolumeInfo(UniversalCostInputs inputs, string subscriptionId, string resourceGroup)
    {
        return new ManagedDiskVolumeInfo
        {
            VolumeId = inputs.ResourceId,
            VolumeName = inputs.ResourceName,
            Region = inputs.Region,
            DiskType = inputs.StorageTier ?? "Premium SSD",
            DiskSizeGb = (int)(inputs.ProvisionedCapacityGiB ?? 0),
            SnapshotSizeGb = inputs.SnapshotSizeGiB ?? 0,
            TransactionsPerMonth = inputs.TransactionsTotal ?? 0,
            ProvisionedIops = (int)(inputs.IopsValue ?? 0),
            ProvisionedThroughputMBps = (int)(inputs.ThroughputMiBps ?? 0),
            SubscriptionId = subscriptionId,
            ResourceGroupName = resourceGroup
        };
    }
}
