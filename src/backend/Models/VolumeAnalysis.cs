using Azure;
using Azure.Data.Tables;

namespace AzFilesOptimizer.Backend.Models;

// Nested objects for volume analysis results
public class PromptExecutionResult
{
    public string PromptId { get; set; } = string.Empty;
    public string PromptName { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string[]? Evidence { get; set; }
    public bool StoppedProcessing { get; set; }
}

public class AiAnalysisResult
{
    public DateTime? LastAnalyzed { get; set; }
    public string? SuggestedWorkloadId { get; set; }
    public string? SuggestedWorkloadName { get; set; }
    public double ConfidenceScore { get; set; } // 0.0 - 1.0
    public PromptExecutionResult[]? AppliedPrompts { get; set; }
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// AI-driven capacity and throughput sizing recommendations
    /// </summary>
    public CapacitySizingResult? CapacitySizing { get; set; }
}

public enum MigrationStatus
{
    Candidate,
    Excluded,
    UnderReview,
    Approved
}

public class UserAnnotations
{
    public string? ConfirmedWorkloadId { get; set; }
    public string? ConfirmedWorkloadName { get; set; }
    public string[]? CustomTags { get; set; }
    public MigrationStatus? MigrationStatus { get; set; }
    public string? Notes { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    
    /// <summary>
    /// User override for target ANF capacity in GiB (optional)
    /// </summary>
    public double? TargetCapacityGiB { get; set; }
    
    /// <summary>
    /// User override for target ANF throughput in MiB/s (optional)
    /// </summary>
    public double? TargetThroughputMiBps { get; set; }
}

// Analysis Job entity
public enum AnalysisJobStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

public class AnalysisJob : ITableEntity
{
    public string PartitionKey { get; set; } = "AnalysisJob";
    public string RowKey { get; set; } = string.Empty; // JobId
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    
    // Job properties
    public string JobId => RowKey;
    public string DiscoveryJobId { get; set; } = string.Empty;
    public string Status { get; set; } = AnalysisJobStatus.Pending.ToString();
    public int TotalVolumes { get; set; }
    public int ProcessedVolumes { get; set; }
    public int FailedVolumes { get; set; }
    
    // Capacity sizing configuration
    public double BufferPercent { get; set; } = 30.0;
    
    // Enabled prompt IDs as comma-separated string
    public string? EnabledPromptIdsString { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    
    // Property for working with prompt IDs as array
    private string[]? _cachedPromptIds;
    
    public string[]? EnabledPromptIds
    {
        get
        {
            if (_cachedPromptIds == null && !string.IsNullOrEmpty(EnabledPromptIdsString))
            {
                _cachedPromptIds = EnabledPromptIdsString.Split(',', StringSplitOptions.RemoveEmptyEntries);
            }
            return _cachedPromptIds;
        }
        set
        {
            _cachedPromptIds = value;
            EnabledPromptIdsString = value == null || value.Length == 0
                ? null
                : string.Join(",", value);
        }
    }
}

// Request/Response models for Analysis API

public class StartAnalysisRequest
{
    public string DiscoveryJobId { get; set; } = string.Empty;
    
    /// <summary>
    /// Buffer percentage to apply above peak capacity/throughput (default: 30%)
    /// Can be negative for aggressive sizing (e.g., -10 = 10% below peak)
    /// </summary>
    public double? BufferPercent { get; set; } = 30.0;
}

public class StartAnalysisResponse
{
    public string AnalysisJobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class AnalysisJobStatusResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TotalVolumes { get; set; }
    public int ProcessedVolumes { get; set; }
    public int FailedVolumes { get; set; }
    public int ProgressPercentage => TotalVolumes > 0 ? (ProcessedVolumes * 100 / TotalVolumes) : 0;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class UpdateAnnotationsRequest
{
    public string? ConfirmedWorkloadId { get; set; }
    public string[]? CustomTags { get; set; }
    public MigrationStatus? MigrationStatus { get; set; }
    public string? Notes { get; set; }
    
    /// <summary>
    /// User override for target ANF capacity in GiB (optional)
    /// </summary>
    public double? TargetCapacityGiB { get; set; }
    
    /// <summary>
    /// User override for target ANF throughput in MiB/s (optional)
    /// </summary>
    public double? TargetThroughputMiBps { get; set; }
}

public class BulkUpdateAnnotationsRequest
{
    public string[] VolumeIds { get; set; } = Array.Empty<string>();
    public UpdateAnnotationsRequest Annotations { get; set; } = new();
}

public class VolumeListResponse
{
    public List<VolumeWithAnalysis> Volumes { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class VolumeWithAnalysis
{
    public string VolumeId { get; set; } = string.Empty; // Computed from ResourceId
    public DiscoveredAzureFileShare VolumeData { get; set; } = new();
    public AiAnalysisResult? AiAnalysis { get; set; }
    public UserAnnotations? UserAnnotations { get; set; }

    /// <summary>
    /// Optional history of user annotation changes for this volume.
    /// Populated for detail views.
    /// </summary>
    public List<AnnotationHistoryEntry>? AnnotationHistory { get; set; }
}
