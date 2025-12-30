namespace AzFilesOptimizer.Backend.Models;

public class DiscoveredAzureFileShare
{
    public string ResourceId { get; set; } = string.Empty;
    public string StorageAccountName { get; set; } = string.Empty;
    public string ShareName { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty; // e.g., "Hot", "Cool", "TransactionOptimized"
    public long? QuotaGiB { get; set; }
    public long? UsageBytes { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
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
