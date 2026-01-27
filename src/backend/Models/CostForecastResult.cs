namespace AzFilesOptimizer.Backend.Models;

/// <summary>
/// Represents a cost forecast for the next 30 days with confidence intervals and trend analysis.
/// </summary>
public class CostForecastResult
{
    /// <summary>
    /// Unique identifier for this forecast
    /// </summary>
    public string ForecastId { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Resource ID being forecasted
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;
    
    /// <summary>
    /// Volume name for reference
    /// </summary>
    public string VolumeName { get; set; } = string.Empty;
    
    /// <summary>
    /// Forecasted cost per day (mid-range estimate)
    /// </summary>
    public double ForecastedCostPerDay { get; set; }
    
    /// <summary>
    /// Forecasted total cost for next 30 days (mid-range estimate)
    /// </summary>
    public double ForecastedCostFor30Days { get; set; }
    
    /// <summary>
    /// Low-range estimate (25th percentile)
    /// </summary>
    public double LowEstimate30Days { get; set; }
    
    /// <summary>
    /// Mid-range estimate (50th percentile)
    /// </summary>
    public double MidEstimate30Days { get; set; }
    
    /// <summary>
    /// High-range estimate (75th percentile)
    /// </summary>
    public double HighEstimate30Days { get; set; }
    
    /// <summary>
    /// Confidence level (0-100 percentage)
    /// </summary>
    public double ConfidencePercentage { get; set; }
    
    /// <summary>
    /// Trend analysis (Increasing, Decreasing, Stable)
    /// </summary>
    public string Trend { get; set; } = "Stable";
    
    /// <summary>
    /// Trend description with reasoning
    /// </summary>
    public string TrendDescription { get; set; } = string.Empty;
    
    /// <summary>
    /// Percentage change from historical 30-day average
    /// </summary>
    public double PercentageChangeFromHistorical { get; set; }
    
    /// <summary>
    /// Daily growth rate as a percentage
    /// </summary>
    public double DailyGrowthRatePercentage { get; set; }
    
    /// <summary>
    /// Standard deviation of cost over historical period
    /// </summary>
    public double StandardDeviation { get; set; }
    
    /// <summary>
    /// Coefficient of variation (ratio of std dev to mean)
    /// </summary>
    public double CoefficientOfVariation { get; set; }
    
    /// <summary>
    /// Base cost (baseline before trend)
    /// </summary>
    public double BaselineCost { get; set; }
    
    /// <summary>
    /// Trend component (incremental change)
    /// </summary>
    public double TrendComponent { get; set; }
    
    /// <summary>
    /// Seasonal/variance component
    /// </summary>
    public double VarianceComponent { get; set; }
    
    /// <summary>
    /// List of recent changes detected in the volume
    /// </summary>
    public List<string> RecentChanges { get; set; } = new();
    
    /// <summary>
    /// List of identified risk factors
    /// </summary>
    public List<string> RiskFactors { get; set; } = new();
    
    /// <summary>
    /// Cost breakdown forecast by component type
    /// </summary>
    public Dictionary<string, double> ForecastByComponent { get; set; } = new();

    /// <summary>
    /// Percentage of forecasted cost that is backup
    /// </summary>
    public double BackupCostPercentage { get; set; }

    /// <summary>
    /// Percentage of forecasted cost that is egress
    /// </summary>
    public double EgressCostPercentage { get; set; }
    
    /// <summary>
    /// Timestamp when forecast was generated
    /// </summary>
    public DateTime ForecastGeneratedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Start of historical period used for forecast
    /// </summary>
    public DateTime HistoricalPeriodStart { get; set; }
    
    /// <summary>
    /// End of historical period used for forecast
    /// </summary>
    public DateTime HistoricalPeriodEnd { get; set; }
    
    /// <summary>
    /// Start of forecast period
    /// </summary>
    public DateTime ForecastPeriodStart { get; set; }
    
    /// <summary>
    /// End of forecast period
    /// </summary>
    public DateTime ForecastPeriodEnd { get; set; }
    
    /// <summary>
    /// Any assumptions or notes about this forecast
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Recommendations based on forecast
    /// </summary>
    public List<string> Recommendations { get; set; } = new();
    
    /// <summary>
    /// Check if this forecast has high confidence
    /// </summary>
    public bool HasHighConfidence => ConfidencePercentage >= 80;
    
    /// <summary>
    /// Check if this forecast has medium confidence
    /// </summary>
    public bool HasMediumConfidence => ConfidencePercentage >= 50 && ConfidencePercentage < 80;
    
    /// <summary>
    /// Check if this forecast has low confidence
    /// </summary>
    public bool HasLowConfidence => ConfidencePercentage < 50;
    
    /// <summary>
    /// Get confidence level as text
    /// </summary>
    public string ConfidenceLevel => HasHighConfidence ? "High" : 
                                      HasMediumConfidence ? "Medium" : "Low";
    
    /// <summary>
    /// Check if rapid growth detected
    /// </summary>
    public bool HasRapidGrowth => DailyGrowthRatePercentage > 5.0;
    
    /// <summary>
    /// Get uncertainty range (high - low)
    /// </summary>
    public double UncertaintyRange => HighEstimate30Days - LowEstimate30Days;
    
    /// <summary>
    /// Get uncertainty as percentage of mid estimate
    /// </summary>
    public double UncertaintyPercentage => MidEstimate30Days > 0 
        ? (UncertaintyRange / MidEstimate30Days) * 100 
        : 0;
    
    /// <summary>
    /// Add a recent change
    /// </summary>
    public void AddRecentChange(string change)
    {
        RecentChanges.Add(change);
    }
    
    /// <summary>
    /// Add a risk factor
    /// </summary>
    public void AddRiskFactor(string risk)
    {
        RiskFactors.Add(risk);
    }
    
    /// <summary>
    /// Add a recommendation
    /// </summary>
    public void AddRecommendation(string recommendation)
    {
        Recommendations.Add(recommendation);
    }
    
    /// <summary>
    /// Get forecast summary
    /// </summary>
    public string GetSummary()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Forecast for {VolumeName}");
        sb.AppendLine($"Forecasted 30-day cost: ${ForecastedCostFor30Days:F2}");
        sb.AppendLine($"Range: ${LowEstimate30Days:F2} - ${HighEstimate30Days:F2}");
        sb.AppendLine($"Confidence: {ConfidenceLevel} ({ConfidencePercentage:F0}%)");
        sb.AppendLine($"Trend: {Trend} ({PercentageChangeFromHistorical:+0.0;-0.0;0.0}% from historical average)");
        
        if (RecentChanges.Any())
        {
            sb.AppendLine("Recent changes:");
            foreach (var change in RecentChanges)
            {
                sb.AppendLine($"  - {change}");
            }
        }
        
        if (RiskFactors.Any())
        {
            sb.AppendLine("Risk factors:");
            foreach (var risk in RiskFactors)
            {
                sb.AppendLine($"  - {risk}");
            }
        }
        
        return sb.ToString();
    }
}