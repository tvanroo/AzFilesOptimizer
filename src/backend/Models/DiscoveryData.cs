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
/// Wraps a DiscoveredAzureFileShare with its analysis and annotation data
/// </summary>
public class DiscoveredVolumeWithAnalysis
{
    public DiscoveredAzureFileShare Volume { get; set; } = new();
    public AiAnalysisResult? AiAnalysis { get; set; }
    public UserAnnotations? UserAnnotations { get; set; }
    
    /// <summary>
    /// Computed unique identifier for this volume (hash of ResourceId)
    /// </summary>
    public string VolumeId => ComputeVolumeId(Volume.ResourceId);
    
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
