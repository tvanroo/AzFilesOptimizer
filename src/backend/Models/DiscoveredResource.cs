namespace AzFilesOptimizer.Backend.Models;

public class DiscoveredAzureFileShare
{
    // Hierarchy identifiers
    public string TenantId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string StorageAccountName { get; set; } = string.Empty;
    public string ShareName { get; set; } = string.Empty;
    
    // Resource identification
    public string ResourceId { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    
    // Storage Account properties
    public string StorageAccountSku { get; set; } = string.Empty; // e.g., "Standard_LRS", "Premium_LRS"
    public string StorageAccountKind { get; set; } = string.Empty; // e.g., "StorageV2", "FileStorage"
    public bool? EnableHttpsTrafficOnly { get; set; }
    public string? MinimumTlsVersion { get; set; }
    public bool? AllowBlobPublicAccess { get; set; }
    public bool? AllowSharedKeyAccess { get; set; }
    
    // File Share properties
    public string AccessTier { get; set; } = string.Empty; // "Hot", "Cool", "TransactionOptimized", "Premium"
    public DateTime? AccessTierChangeTime { get; set; }
    public string? AccessTierStatus { get; set; }
    public long? ShareQuotaGiB { get; set; }
    public long? ShareUsageBytes { get; set; }
    public string[]? EnabledProtocols { get; set; } // "SMB", "NFS"
    public string? RootSquash { get; set; } // For NFS shares
    
    // Performance properties (Premium shares)
    public int? ProvisionedIops { get; set; }
    public int? ProvisionedBandwidthMiBps { get; set; }
    public int? EstimatedIops { get; set; }
    public double? EstimatedThroughputMiBps { get; set; }
    
    // Lease properties
    public string? LeaseStatus { get; set; } // "Locked", "Unlocked"
    public string? LeaseState { get; set; } // "Available", "Leased", "Expired", "Breaking", "Broken"
    public string? LeaseDuration { get; set; } // "Infinite", "Fixed"
    
    // Snapshot properties
    public DateTime? SnapshotTime { get; set; }
    public bool IsSnapshot { get; set; }
    public string? SnapshotId { get; set; }
    
    // Soft delete properties
    public bool? IsDeleted { get; set; }
    public DateTime? DeletedTime { get; set; }
    public int? RemainingRetentionDays { get; set; }
    public string? Version { get; set; }
    
    // Metadata and tags
    public Dictionary<string, string>? Metadata { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
    
    // Timestamps
    public DateTime? LastModifiedTime { get; set; }
    public DateTime? CreationTime { get; set; }
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    
    // Snapshot and Backup metadata
    public int? SnapshotCount { get; set; }
    public long? TotalSnapshotSizeBytes { get; set; }
    public double? ChurnRateBytesPerDay { get; set; }
    public bool? BackupPolicyConfigured { get; set; }
    
    // Monitoring availability
    public bool MonitoringEnabled { get; set; }
    public int? MonitoringDataAvailableDays { get; set; }
    public string? HistoricalMetricsSummary { get; set; }
}

public class DiscoveredAnfVolume
{
    public string ResourceId { get; set; } = string.Empty;
    public string VolumeName { get; set; } = string.Empty;
    public string NetAppAccountName { get; set; } = string.Empty;
    public string CapacityPoolName { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string ServiceLevel { get; set; } = string.Empty; // "Standard", "Premium", "Ultra"
    public string? PoolQosType { get; set; } // "Auto" or "Manual"
    public long ProvisionedSizeBytes { get; set; }
    public double? ThroughputMibps { get; set; }
    public double? ActualThroughputMibps { get; set; }
    public bool? CoolAccessEnabled { get; set; }
    public string? CoolTieringPolicy { get; set; } // e.g., "Auto", "SnapshotOnly"
    public int? EstimatedIops { get; set; }
    public double? EstimatedThroughputMiBps { get; set; }
    public string[]? ProtocolTypes { get; set; } // e.g., ["NFSv3", "NFSv4.1", "CIFS"]
    public Dictionary<string, string>? Tags { get; set; }
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    public string? MinimumTlsVersion { get; set; }

    // Snapshot and Backup metadata
    public int? SnapshotCount { get; set; }
    public long? TotalSnapshotSizeBytes { get; set; }
    public double? ChurnRateBytesPerDay { get; set; }
    public bool? BackupPolicyConfigured { get; set; }

    // Monitoring availability
    public bool MonitoringEnabled { get; set; }
    public int? MonitoringDataAvailableDays { get; set; }
    public string? HistoricalMetricsSummary { get; set; }
}

public class DiscoveredManagedDisk
{
    // Hierarchy identifiers
    public string TenantId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string DiskName { get; set; } = string.Empty;

    // Resource identification
    public string ResourceId { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;

    // Disk properties
    public string DiskSku { get; set; } = string.Empty;
    public string DiskTier { get; set; } = string.Empty;
    public long DiskSizeGB { get; set; }
    public string DiskState { get; set; } = string.Empty;
    public string ProvisioningState { get; set; } = string.Empty;
    public long? DiskSizeBytes { get; set; }
    public string? DiskType { get; set; }
    public bool? BurstingEnabled { get; set; }

    // Performance estimation
    public int? EstimatedIops { get; set; }
    public double? EstimatedThroughputMiBps { get; set; }

    // Attachment information
    public bool IsAttached { get; set; }
    public string? AttachedVmId { get; set; }
    public string? AttachedVmName { get; set; }
    public int? Lun { get; set; }
    public string? VmSize { get; set; }
    public int? VmCpuCount { get; set; }
    public double? VmMemoryGiB { get; set; }
    public string? VmOsType { get; set; }
    public bool? IsOsDisk { get; set; }

    // Tags and metadata
    public Dictionary<string, string>? Tags { get; set; }
    public Dictionary<string, string>? VmTags { get; set; }

    // Timestamps
    public DateTime? TimeCreated { get; set; }
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;

    // Monitoring availability
    public bool MonitoringEnabled { get; set; }
    public int? MonitoringDataAvailableDays { get; set; }
    public string? HistoricalMetricsSummary { get; set; }

    // Managed disk usage/metrics
    public long? UsedBytes { get; set; }
    public double? AverageReadIops { get; set; }
    public double? AverageWriteIops { get; set; }
    public double? AverageReadThroughputMiBps { get; set; }
    public double? AverageWriteThroughputMiBps { get; set; }

    // VM-side data disk metrics
    public string? VmMetricsSummary { get; set; }
    public int? VmMonitoringDataAvailableDays { get; set; }
}

public class DiscoveryResult
{
    public string JobId { get; set; } = string.Empty;
    public List<DiscoveredAzureFileShare> AzureFileShares { get; set; } = new();
    public List<DiscoveredAnfVolume> AnfVolumes { get; set; } = new();
    public List<DiscoveredManagedDisk> ManagedDisks { get; set; } = new();
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}
