namespace AzFilesOptimizer.Backend.Models;

/// <summary>
/// Configuration for cool data assumptions when metrics are not available
/// Supports 3-tier hierarchy: Global → Job → Volume
/// </summary>
public class CoolDataAssumptions
{
    /// <summary>
    /// Percentage of data assumed to be in cool tier (0-100)
    /// Default: 80%
    /// </summary>
    public double CoolDataPercentage { get; set; } = 80.0;
    
    /// <summary>
    /// Percentage of cool data assumed to be retrieved per billing period (0-100)
    /// Default: 15%
    /// </summary>
    public double CoolDataRetrievalPercentage { get; set; } = 15.0;
    
    /// <summary>
    /// Source of these assumptions in the configuration hierarchy
    /// </summary>
    public AssumptionSource Source { get; set; } = AssumptionSource.Global;
    
    /// <summary>
    /// When these assumptions were last modified
    /// </summary>
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Optional identifier of who modified these assumptions
    /// </summary>
    public string? LastModifiedBy { get; set; }
    
    /// <summary>
    /// Create global default assumptions
    /// </summary>
    public static CoolDataAssumptions CreateGlobalDefaults()
    {
        return new CoolDataAssumptions
        {
            CoolDataPercentage = 80.0,
            CoolDataRetrievalPercentage = 15.0,
            Source = AssumptionSource.Global,
            LastModifiedAt = DateTime.UtcNow,
            LastModifiedBy = "System"
        };
    }
    
    /// <summary>
    /// Create assumptions with custom values
    /// </summary>
    public static CoolDataAssumptions Create(
        double coolDataPercentage, 
        double coolDataRetrievalPercentage,
        AssumptionSource source,
        string? modifiedBy = null)
    {
        if (coolDataPercentage < 0 || coolDataPercentage > 100)
            throw new ArgumentException("Cool data percentage must be between 0 and 100", nameof(coolDataPercentage));
        
        if (coolDataRetrievalPercentage < 0 || coolDataRetrievalPercentage > 100)
            throw new ArgumentException("Retrieval percentage must be between 0 and 100", nameof(coolDataRetrievalPercentage));
        
        return new CoolDataAssumptions
        {
            CoolDataPercentage = coolDataPercentage,
            CoolDataRetrievalPercentage = coolDataRetrievalPercentage,
            Source = source,
            LastModifiedAt = DateTime.UtcNow,
            LastModifiedBy = modifiedBy
        };
    }
    
    /// <summary>
    /// Validate the assumptions values
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        
        if (CoolDataPercentage < 0 || CoolDataPercentage > 100)
            errors.Add("Cool data percentage must be between 0 and 100");
        
        if (CoolDataRetrievalPercentage < 0 || CoolDataRetrievalPercentage > 100)
            errors.Add("Retrieval percentage must be between 0 and 100");
        
        return errors;
    }
    
    /// <summary>
    /// Clone the assumptions with a new source
    /// </summary>
    public CoolDataAssumptions WithSource(AssumptionSource newSource, string? modifiedBy = null)
    {
        return new CoolDataAssumptions
        {
            CoolDataPercentage = this.CoolDataPercentage,
            CoolDataRetrievalPercentage = this.CoolDataRetrievalPercentage,
            Source = newSource,
            LastModifiedAt = DateTime.UtcNow,
            LastModifiedBy = modifiedBy ?? this.LastModifiedBy
        };
    }
}

/// <summary>
/// Source of cool data assumptions in the configuration hierarchy
/// </summary>
public enum AssumptionSource
{
    /// <summary>
    /// Global application-level defaults
    /// </summary>
    Global = 0,
    
    /// <summary>
    /// Job-level override (applies to all volumes in job without volume-level overrides)
    /// </summary>
    Job = 1,
    
    /// <summary>
    /// Volume-level override (highest priority)
    /// </summary>
    Volume = 2
}
