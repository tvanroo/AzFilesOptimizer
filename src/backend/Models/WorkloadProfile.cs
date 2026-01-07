using Azure;
using Azure.Data.Tables;
using System.Text.Json;

namespace AzFilesOptimizer.Backend.Models;

public class PerformanceRequirements
{
    public long? MinSizeGB { get; set; }
    public long? MaxSizeGB { get; set; }
    public int? MinIops { get; set; }
    public int? MaxIops { get; set; }
    public string LatencySensitivity { get; set; } = "Medium"; // Low, Medium, High, VeryHigh, Ultra
    public int? MinThroughputMBps { get; set; }
    public int? MaxThroughputMBps { get; set; }
    public string? IoPattern { get; set; } // Sequential, Random, Mixed
}

public class AnfSuitability
{
    public bool Compatible { get; set; }
    public string? RecommendedServiceLevel { get; set; } // Standard, Premium, Ultra
    public string? Notes { get; set; }
    public string[]? Caveats { get; set; }
}

public class DetectionHints
{
    public string[]? NamingPatterns { get; set; }
    public string[]? CommonTags { get; set; }
    public string[]? FileTypeIndicators { get; set; }
    public string[]? PathPatterns { get; set; }
}

public class WorkloadProfile : ITableEntity
{
    public string PartitionKey { get; set; } = "WorkloadProfile";
    public string RowKey { get; set; } = string.Empty; // ProfileId
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    
    // Profile properties
    public string ProfileId => RowKey;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty; // Rich text for AI context
    public bool IsSystemProfile { get; set; } // Pre-built vs custom
    public bool IsExclusionProfile { get; set; } // Auto-exclude from migration
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Serialized complex properties (stored as JSON strings in Table Storage)
    public string? PerformanceRequirementsJson { get; set; }
    public string? AnfSuitabilityJson { get; set; }
    public string? DetectionHintsJson { get; set; }
    
    // Properties for working with deserialized objects (not stored in table)
    private PerformanceRequirements? _performanceRequirements;
    private AnfSuitability? _anfSuitability;
    private DetectionHints? _detectionHints;
    
    public PerformanceRequirements PerformanceRequirements
    {
        get
        {
            if (_performanceRequirements == null && !string.IsNullOrEmpty(PerformanceRequirementsJson))
            {
                try
                {
                    _performanceRequirements = JsonSerializer.Deserialize<PerformanceRequirements>(PerformanceRequirementsJson);
                }
                catch { /* Ignore deserialization errors */ }
            }
            return _performanceRequirements ?? new PerformanceRequirements();
        }
        set
        {
            _performanceRequirements = value;
            PerformanceRequirementsJson = JsonSerializer.Serialize(value);
        }
    }
    
    public AnfSuitability AnfSuitabilityInfo
    {
        get
        {
            if (_anfSuitability == null && !string.IsNullOrEmpty(AnfSuitabilityJson))
            {
                try
                {
                    _anfSuitability = JsonSerializer.Deserialize<AnfSuitability>(AnfSuitabilityJson);
                }
                catch { /* Ignore deserialization errors */ }
            }
            return _anfSuitability ?? new AnfSuitability();
        }
        set
        {
            _anfSuitability = value;
            AnfSuitabilityJson = JsonSerializer.Serialize(value);
        }
    }
    
    public DetectionHints Hints
    {
        get
        {
            if (_detectionHints == null && !string.IsNullOrEmpty(DetectionHintsJson))
            {
                try
                {
                    _detectionHints = JsonSerializer.Deserialize<DetectionHints>(DetectionHintsJson);
                }
                catch { /* Ignore deserialization errors */ }
            }
            return _detectionHints ?? new DetectionHints();
        }
        set
        {
            _detectionHints = value;
            DetectionHintsJson = JsonSerializer.Serialize(value);
        }
    }
}
