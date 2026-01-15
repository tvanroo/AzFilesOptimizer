namespace AzFilesOptimizer.Backend.Models;

/// <summary>
/// Container for discovered volumes with AI analysis and user annotations.
/// This is stored in Blob Storage as JSON: jobs/{jobId}/discovered-volumes.json
/// </summary>
public class DiscoveryData
{
    public string JobId { get; set; } = string.Empty;
    public DateTime DiscoveredAt { get; set; }
    public DateTime? LastAnalyzed { get; set; }
    public List<DiscoveredVolumeWithAnalysis> Volumes { get; set; } = new();
}

/// <summary>
/// History entry for user annotations on a volume.
/// Stored inside DiscoveryData so we have a per-volume audit trail.
/// </summary>
public class AnnotationHistoryEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string UserId { get; set; } = string.Empty;
    public string? ConfirmedWorkloadId { get; set; }
    public string? ConfirmedWorkloadName { get; set; }
    public MigrationStatus? MigrationStatus { get; set; }
    public string[]? CustomTags { get; set; }
    public string? Notes { get; set; }
    /// <summary>
    /// Source of the change, e.g. "Update" or "BulkUpdate".
    /// </summary>
    public string? Source { get; set; }
}

/// <summary>
/// Wraps any discovered volume type with its analysis and annotation data.
/// VolumeData contains the actual volume object (could be Azure Files, ANF, or Managed Disk).
/// </summary>
public class DiscoveredVolumeWithAnalysis
{
    /// <summary>
    /// Type discriminator: "AzureFiles", "ANF", or "ManagedDisk"
    /// </summary>
    public string VolumeType { get; set; } = string.Empty;
    
    /// <summary>
    /// The volume data - can be DiscoveredAzureFileShare, DiscoveredAnfVolume, or DiscoveredManagedDisk
    /// </summary>
    public object VolumeData { get; set; } = new();
    
    /// <summary>
    /// Convenience property for Azure Files shares (backwards compatibility)
    /// </summary>
    public DiscoveredAzureFileShare? Volume 
    { 
        get => VolumeData as DiscoveredAzureFileShare;
        set { if (value != null) { VolumeData = value; VolumeType = "AzureFiles"; } }
    }
    
    public AiAnalysisResult? AiAnalysis { get; set; }
    public UserAnnotations? UserAnnotations { get; set; }

    /// <summary>
    /// History of user annotation changes for this volume.
    /// </summary>
    public List<AnnotationHistoryEntry> AnnotationHistory { get; set; } = new();
    
    /// <summary>
    /// Computed unique identifier for this volume (hash of ResourceId)
    /// </summary>
    public string VolumeId => ComputeVolumeId(GetResourceId());
    
    private string GetResourceId()
    {
        return VolumeType switch
        {
            "AzureFiles" => (VolumeData as DiscoveredAzureFileShare)?.ResourceId ?? "",
            "ANF" => (VolumeData as DiscoveredAnfVolume)?.ResourceId ?? "",
            "ManagedDisk" => (VolumeData as DiscoveredManagedDisk)?.ResourceId ?? "",
            _ => ""
        };
    }
    
    private static string ComputeVolumeId(string resourceId)
    {
        if (string.IsNullOrEmpty(resourceId))
            return Guid.NewGuid().ToString();
            
        // Create a deterministic ID from the resource ID
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(resourceId));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
