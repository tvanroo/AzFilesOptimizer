namespace AzFilesOptimizer.Backend.Models;

/// <summary>
/// Represents AI-driven capacity and throughput sizing recommendations for an ANF volume
/// based on historical metrics analysis with configurable buffer.
/// </summary>
public class CapacitySizingResult
{
    // Capacity Analysis
    /// <summary>
    /// Peak observed capacity in GiB from historical metrics (30-day max)
    /// </summary>
    public double PeakCapacityGiB { get; set; }
    
    /// <summary>
    /// Recommended capacity in GiB = PeakCapacityGiB * (1 + BufferPercent/100)
    /// This is the minimum ANF volume size needed to handle peak workload
    /// </summary>
    public double RecommendedCapacityGiB { get; set; }
    
    /// <summary>
    /// Buffer percentage applied above peak (e.g., 30 = 30% buffer above peak)
    /// Can be negative for aggressive sizing (e.g., -10 = 10% below peak)
    /// </summary>
    public double BufferPercent { get; set; }
    
    /// <summary>
    /// Human-readable capacity unit for display (GiB, TiB, PiB)
    /// </summary>
    public string CapacityUnit { get; set; } = "GiB";
    
    /// <summary>
    /// Recommended capacity formatted in the appropriate unit
    /// </summary>
    public double RecommendedCapacityInUnit { get; set; }
    
    // Throughput Analysis
    /// <summary>
    /// Peak observed total throughput in MiB/s (read + write combined)
    /// </summary>
    public double PeakThroughputMiBps { get; set; }
    
    /// <summary>
    /// Recommended throughput in MiB/s = PeakThroughputMiBps * (1 + BufferPercent/100)
    /// This is the minimum ANF throughput tier needed
    /// </summary>
    public double RecommendedThroughputMiBps { get; set; }
    
    /// <summary>
    /// Peak observed read throughput in MiB/s
    /// </summary>
    public double PeakReadThroughputMiBps { get; set; }
    
    /// <summary>
    /// Peak observed write throughput in MiB/s
    /// </summary>
    public double PeakWriteThroughputMiBps { get; set; }
    
    // IOPS Analysis
    /// <summary>
    /// Peak observed total IOPS (read + write combined)
    /// </summary>
    public double PeakTotalIOPS { get; set; }
    
    /// <summary>
    /// Peak observed read IOPS
    /// </summary>
    public double PeakReadIOPS { get; set; }
    
    /// <summary>
    /// Peak observed write IOPS
    /// </summary>
    public double PeakWriteIOPS { get; set; }
    
    // Metadata
    /// <summary>
    /// When this analysis was performed
    /// </summary>
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Total number of metric data points analyzed (across all metrics)
    /// </summary>
    public int MetricDataPoints { get; set; }
    
    /// <summary>
    /// Number of days of historical data analyzed
    /// </summary>
    public int DaysAnalyzed { get; set; }
    
    /// <summary>
    /// Data quality score (0.0-1.0) based on completeness and consistency
    /// </summary>
    public double DataQualityScore { get; set; }
    
    // AI-Enhanced Analysis
    /// <summary>
    /// AI-generated reasoning explaining the sizing recommendations,
    /// considering workload type, patterns, and growth projections
    /// </summary>
    public string? AiReasoning { get; set; }
    
    /// <summary>
    /// AI-generated warnings about edge cases, insufficient data,
    /// high variability, or other concerns
    /// </summary>
    public string? Warnings { get; set; }
    
    /// <summary>
    /// Suggested ANF service level based on performance requirements
    /// (Standard, Premium, or Ultra)
    /// </summary>
    public string? SuggestedServiceLevel { get; set; }
    
    /// <summary>
    /// Whether there was sufficient data to make a confident recommendation
    /// </summary>
    public bool HasSufficientData { get; set; }
}
