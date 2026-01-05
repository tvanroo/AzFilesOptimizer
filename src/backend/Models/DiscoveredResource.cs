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
    public int? ProvisionedMaxIops { get; set; }
    public int? ProvisionedMaxBandwidthMiBps { get; set; }
    
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
    public long ProvisionedSizeBytes { get; set; }
    public string[]? ProtocolTypes { get; set; } // e.g., ["NFSv3", "NFSv4.1", "CIFS"]
    public Dictionary<string, string>? Tags { get; set; }
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
}

public class DiscoveryResult
{
    public string JobId { get; set; } = string.Empty;
    public List<DiscoveredAzureFileShare> AzureFileShares { get; set; } = new();
    public List<DiscoveredAnfVolume> AnfVolumes { get; set; } = new();
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}
