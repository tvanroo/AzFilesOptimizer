using Azure;
using Azure.Data.Tables;
using System.Text.Json;

namespace AzFilesOptimizer.Backend.Models;

public enum PromptCategory
{
    Exclusion,
    WorkloadDetection,
    MigrationAssessment
}

public enum StopAction
{
    None,
    ExcludeVolume,
    SetWorkload,
    SkipRemaining
}

public class StopConditions
{
    public bool StopOnMatch { get; set; }
    public StopAction ActionOnMatch { get; set; }
    public string? TargetWorkloadId { get; set; } // Used when ActionOnMatch = SetWorkload
}

public class AnalysisPrompt : ITableEntity
{
    public string PartitionKey { get; set; } = "AnalysisPrompt";
    public string RowKey { get; set; } = string.Empty; // PromptId
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    
    // Prompt properties
    public string PromptId => RowKey;
    public string Name { get; set; } = string.Empty;
    public int Priority { get; set; } // Lower = earlier execution
    public string Category { get; set; } = PromptCategory.WorkloadDetection.ToString();
    public string PromptTemplate { get; set; } = string.Empty; // Rich text with {Variable} placeholders
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Serialized complex properties
    public string? StopConditionsJson { get; set; }
    
    // Property for working with deserialized object
    private StopConditions? _stopConditions;
    
    public StopConditions StopCondition
    {
        get
        {
            if (_stopConditions == null && !string.IsNullOrEmpty(StopConditionsJson))
            {
                try
                {
                    _stopConditions = JsonSerializer.Deserialize<StopConditions>(StopConditionsJson);
                }
                catch { /* Ignore deserialization errors */ }
            }
            return _stopConditions ?? new StopConditions();
        }
        set
        {
            _stopConditions = value;
            StopConditionsJson = JsonSerializer.Serialize(value);
        }
    }
}

// Request/Response models for API

public class CreatePromptRequest
{
    public string Name { get; set; } = string.Empty;
    public int Priority { get; set; }
    public PromptCategory Category { get; set; }
    public string PromptTemplate { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public StopConditions? StopConditions { get; set; }
}

public class UpdatePromptRequest
{
    public string Name { get; set; } = string.Empty;
    public int Priority { get; set; }
    public PromptCategory Category { get; set; }
    public string PromptTemplate { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public StopConditions? StopConditions { get; set; }
}

public class ReorderPromptsRequest
{
    public Dictionary<string, int> Priorities { get; set; } = new();
}

public class TestPromptRequest
{
    public string VolumeData { get; set; } = string.Empty; // JSON of DiscoveredAzureFileShare
}

public class TestPromptResponse
{
    public string Result { get; set; } = string.Empty;
    public bool WouldStopProcessing { get; set; }
    public string? Evidence { get; set; }
}
