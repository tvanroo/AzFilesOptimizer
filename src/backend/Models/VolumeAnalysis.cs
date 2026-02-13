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

public class CostSummary
{
    /// <summary>
    /// Total cost for the primary analysis period (typically 30 days).
    /// </summary>
    public double TotalCost30Days { get; set; }

    /// <summary>
    /// Average daily cost over the analysis period.
    /// </summary>
    public double DailyAverage { get; set; }

    /// <summary>
    /// Currency code for the cost values (e.g., USD).
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// True when cost reflects actual billed values from Cost Management (after scaling),
    /// false when cost is purely based on retail price estimates.
    /// </summary>
    public bool IsActual { get; set; }

    /// <summary>
    /// Start of the cost analysis period (UTC).
    /// </summary>
    public DateTime? PeriodStart { get; set; }

    /// <summary>
    /// End of the cost analysis period (UTC).
    /// </summary>
    public DateTime? PeriodEnd { get; set; }
}

public class VolumeWithAnalysis
{
    public string VolumeId { get; set; } = string.Empty; // Computed from ResourceId
    
    /// <summary>
    /// Type discriminator: "AzureFiles", "ANF", or "ManagedDisk"
    /// </summary>
    public string VolumeType { get; set; } = string.Empty;
    
    /// <summary>
    /// The volume data - can be DiscoveredAzureFileShare, DiscoveredAnfVolume, or DiscoveredManagedDisk.
    /// Use polymorphic serialization to handle all types.
    /// </summary>
    public object VolumeData { get; set; } = new();
    
    public AiAnalysisResult? AiAnalysis { get; set; }
    public UserAnnotations? UserAnnotations { get; set; }

    /// <summary>
    /// Optional compact cost summary for this volume, if cost analysis has been run.
    /// </summary>
    public CostSummary? CostSummary { get; set; }
    
    /// <summary>
    /// Full detailed cost analysis with all components, pricing data, and debugging information.
    /// Includes CostCalculationInputs, RetailPricingData, ActualCostsApplied status, etc.
    /// </summary>
    public VolumeCostAnalysis? DetailedCostAnalysis { get; set; }

    /// <summary>
    /// Universal cost calculation inputs, standardized across all storage platforms.
    /// Includes capacity, performance, transaction, and configuration metrics used
    /// for cost calculations. Fields may be null when not applicable to the platform.
    /// </summary>
    public UniversalCostInputs? CostCalculationInputs { get; set; }

    /// <summary>
    /// High-level cost status for this volume: Pending, Completed, Failed, etc.
    /// Primarily used by the UI when cost data is still being collected.
    /// </summary>
    public string? CostStatus { get; set; }

    /// <summary>
    /// Recommended capacity for this workload in GiB. When AI sizing is available and
    /// has sufficient metrics, this is derived from CapacitySizing.RecommendedCapacityGiB.
    /// Otherwise it falls back to the current provisioned capacity for the volume.
    /// </summary>
    public double? RequiredCapacityGiB { get; set; }

    /// <summary>
    /// Recommended throughput in MiB/s. When AI sizing is available and has sufficient
    /// metrics, this is derived from CapacitySizing.RecommendedThroughputMiBps
    /// (based on observed peaks plus buffer). Otherwise it falls back to the current
    /// configured or estimated throughput for the volume.
    /// </summary>
        public double? RequiredThroughputMiBps { get; set; }
        public string? ThroughputCalculationNote { get; set; }

    /// <summary>
    /// Current configured or estimated throughput for the volume in MiB/s, based on
    /// discovered properties (e.g., ProvisionedBandwidthMiBps, ThroughputMibps,
    /// EstimatedThroughputMiBps).
    /// </summary>
    public double? CurrentThroughputMiBps { get; set; }

    /// <summary>
    /// Current configured or estimated IOPS limit for the volume, based on discovered
    /// properties (e.g., ProvisionedIops, EstimatedIops).
    /// </summary>
    public double? CurrentIops { get; set; }

    /// <summary>
    /// Optional history of user annotation changes for this volume.
    /// /// Populated for detail views.
    /// </summary>
    public List<AnnotationHistoryEntry>? AnnotationHistory { get; set; }
    
    /// <summary>
    /// Hypothetical cost if this volume was migrated to ANF Flexible Tier.
    /// Used for "what-if" analysis across all volume types.
    /// </summary>
    public HypotheticalCostResult? HypotheticalAnfFlexibleCost { get; set; }
}

/// <summary>
/// Result of hypothetical ANF Flexible Tier cost calculation
/// </summary>
public class HypotheticalCostResult
{
    /// <summary>
    /// Total estimated monthly cost in USD
    /// </summary>
    public double TotalMonthlyCost { get; set; }
    
    /// <summary>
    /// Breakdown of cost components (capacity, throughput, cool storage, retrieval, etc.)
    /// </summary>
    public List<CostComponentEstimate> CostComponents { get; set; } = new();
    
    /// <summary>
    /// Whether cool access is enabled for this calculation
    /// </summary>
    public bool CoolAccessEnabled { get; set; }
    
    /// <summary>
    /// Cool data assumptions applied (if cool access enabled)
    /// </summary>
    public CoolDataAssumptions? AppliedAssumptions { get; set; }
    
    /// <summary>
    /// Additional notes about the calculation (minimums applied, assumptions used, etc.)
    /// </summary>
    public string CalculationNotes { get; set; } = string.Empty;
}
