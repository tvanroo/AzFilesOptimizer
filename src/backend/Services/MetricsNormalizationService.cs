using Microsoft.Extensions.Logging;
using System.Text.Json;
using Azure.Core;
using System.Globalization;

namespace AzFilesOptimizer.Backend.Services;

/// <summary>
/// Service for normalizing metrics to steady-state and handling weekday/weekend patterns
/// </summary>
public class MetricsNormalizationService
{
    private readonly ILogger _logger;
    private readonly TokenCredential _credential;
    
    public MetricsNormalizationService(ILogger logger, TokenCredential credential)
    {
        _logger = logger;
        _credential = credential;
    }
    
    /// <summary>
    /// Calculate weekday-weighted average for transaction patterns
    /// Assumes 22 weekdays and 8 weekend days per 30-day month
    /// </summary>
    public double GetWeekdayWeightedAverage(Dictionary<DateTime, double> dataPoints)
    {
        if (dataPoints == null || dataPoints.Count == 0)
            return 0;
        
        var weekdayPoints = new List<double>();
        var weekendPoints = new List<double>();
        
        foreach (var kvp in dataPoints)
        {
            if (kvp.Key.DayOfWeek == DayOfWeek.Saturday || kvp.Key.DayOfWeek == DayOfWeek.Sunday)
                weekendPoints.Add(kvp.Value);
            else
                weekdayPoints.Add(kvp.Value);
        }
        
        if (weekdayPoints.Count == 0 && weekendPoints.Count == 0)
            return 0;
        
        double weekdayAvg = weekdayPoints.Count > 0 ? weekdayPoints.Average() : 0;
        double weekendAvg = weekendPoints.Count > 0 ? weekendPoints.Average() : weekdayAvg; // Use weekday if no weekend data
        
        // Weighted average: (5 weekdays + 2 weekend days) / 7
        return (weekdayAvg * 5 + weekendAvg * 2) / 7;
    }
    
    /// <summary>
    /// Get weekday and weekend averages separately
    /// </summary>
    public (double weekdayAvg, double weekendAvg, int weekdayCount, int weekendCount) GetWeekdayWeekendAverages(Dictionary<DateTime, double> dataPoints)
    {
        if (dataPoints == null || dataPoints.Count == 0)
            return (0, 0, 0, 0);
        
        var weekdayPoints = new List<double>();
        var weekendPoints = new List<double>();
        
        foreach (var kvp in dataPoints)
        {
            if (kvp.Key.DayOfWeek == DayOfWeek.Saturday || kvp.Key.DayOfWeek == DayOfWeek.Sunday)
                weekendPoints.Add(kvp.Value);
            else
                weekdayPoints.Add(kvp.Value);
        }
        
        double weekdayAvg = weekdayPoints.Count > 0 ? weekdayPoints.Average() : 0;
        double weekendAvg = weekendPoints.Count > 0 ? weekendPoints.Average() : 0;
        
        return (weekdayAvg, weekendAvg, weekdayPoints.Count, weekendPoints.Count);
    }
    
    /// <summary>
    /// Detect if value has changed significantly and return steady-state value
    /// If change > 20% in last lookbackDays, use only last 3 days
    /// </summary>
    public (double steadyStateValue, bool hasChanged, DateTime? changeDate, int daysUsed) GetSteadyStateValue(
        Dictionary<DateTime, double> timeSeriesData, 
        int lookbackDays = 7,
        double changeThreshold = 0.20)
    {
        if (timeSeriesData == null || timeSeriesData.Count == 0)
            return (0, false, null, 0);
        
        var sortedData = timeSeriesData.OrderBy(kvp => kvp.Key).ToList();
        
        if (sortedData.Count < 2)
            return (sortedData.First().Value, false, null, 1);
        
        // Look at last N days
        var cutoffDate = DateTime.UtcNow.AddDays(-lookbackDays);
        var recentData = sortedData.Where(kvp => kvp.Key >= cutoffDate).ToList();
        
        if (recentData.Count < 2)
            return (sortedData.Average(kvp => kvp.Value), false, null, sortedData.Count);
        
        // Check for significant changes by comparing first half vs second half of recent period
        int midPoint = recentData.Count / 2;
        var firstHalf = recentData.Take(midPoint).ToList();
        var secondHalf = recentData.Skip(midPoint).ToList();
        
        if (firstHalf.Count == 0 || secondHalf.Count == 0)
            return (recentData.Average(kvp => kvp.Value), false, null, recentData.Count);
        
        double firstAvg = firstHalf.Average(kvp => kvp.Value);
        double secondAvg = secondHalf.Average(kvp => kvp.Value);
        
        // Detect change
        bool hasChanged = false;
        DateTime? changeDate = null;
        
        if (firstAvg > 0)
        {
            double changePercent = Math.Abs(secondAvg - firstAvg) / firstAvg;
            if (changePercent > changeThreshold)
            {
                hasChanged = true;
                changeDate = secondHalf.First().Key;
                _logger.LogInformation(
                    "Significant capacity change detected: {FirstAvg:F2} -> {SecondAvg:F2} ({ChangePercent:P0} change)",
                    firstAvg, secondAvg, changePercent);
            }
        }
        
        // If changed, use only last 3 days for steady state
        if (hasChanged)
        {
            var last3Days = sortedData.Where(kvp => kvp.Key >= DateTime.UtcNow.AddDays(-3)).ToList();
            if (last3Days.Count > 0)
            {
                double steadyState = last3Days.Average(kvp => kvp.Value);
                return (steadyState, true, changeDate, last3Days.Count);
            }
        }
        
        // Otherwise use all recent data
        return (recentData.Average(kvp => kvp.Value), false, null, recentData.Count);
    }
    
    /// <summary>
    /// Project monthly value from daily sample
    /// Applies confidence adjustment based on sample size
    /// </summary>
    public (double monthlyProjection, double confidenceScore) ProjectMonthlyFromDailySample(
        double dailyAverage, 
        int sampleDays,
        int daysInMonth = 30)
    {
        if (sampleDays <= 0)
            return (0, 0);
        
        double monthlyProjection = dailyAverage * daysInMonth;
        
        // Calculate confidence score based on sample size
        double confidenceScore = 100.0;
        
        if (sampleDays < 3)
            confidenceScore = 30.0; // Very low confidence
        else if (sampleDays < 7)
            confidenceScore = 50.0 + (sampleDays - 3) * 5.0; // 50-70% confidence
        else if (sampleDays < 14)
            confidenceScore = 70.0 + (sampleDays - 7) * 2.0; // 70-84% confidence
        else if (sampleDays < 30)
            confidenceScore = 84.0 + (sampleDays - 14) * 1.0; // 84-100% confidence
        else
            confidenceScore = 100.0; // Full month of data
        
        return (monthlyProjection, Math.Min(confidenceScore, 100.0));
    }
    
    /// <summary>
    /// Detect capacity changes by analyzing metrics time series
    /// This is a lightweight alternative to Resource Graph queries
    /// </summary>
    public async Task<(bool hasChanged, DateTime? changeDate, double newCapacity)> DetectCapacityChangeFromMetrics(
        string resourceId,
        string metricName,
        DateTime startTime,
        DateTime endTime,
        MetricsCollectionService metricsService)
    {
        try
        {
            // This method would query capacity metrics and detect changes
            // For now, return false to indicate no change detected
            // This will be enhanced when integrated with MetricsCollectionService
            
            _logger.LogDebug(
                "Capacity change detection not yet implemented for resource {ResourceId}",
                resourceId);
            
            return (false, null, 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, 
                "Error detecting capacity change for resource {ResourceId}",
                resourceId);
            return (false, null, 0);
        }
    }
    
    /// <summary>
    /// Parse metrics JSON from MetricsCollectionService into time series dictionary
    /// </summary>
    public Dictionary<DateTime, double>? ParseMetricsToTimeSeries(
        string? metricsJson, 
        string metricName,
        string aggregationType = "total")
    {
        if (string.IsNullOrEmpty(metricsJson))
            return null;
        
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var metricsData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(metricsJson, options);
            
            if (metricsData == null || !metricsData.ContainsKey(metricName))
                return null;
            
            var metricData = metricsData[metricName];
            
            // The metrics are already aggregated by MetricsCollectionService
            // For time series, we would need the raw data points
            // This is a simplified implementation that returns the aggregated value
            // with the current timestamp
            
            var result = new Dictionary<DateTime, double>();
            
            if (metricData.TryGetProperty(aggregationType, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number)
                {
                    result[DateTime.UtcNow] = value.GetDouble();
                }
            }
            
            return result.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing metrics JSON for {MetricName}", metricName);
            return null;
        }
    }
    
    /// <summary>
    /// Calculate confidence score based on data quality
    /// </summary>
    public double CalculateConfidenceScore(
        int sampleDays,
        bool hasCapacityChange,
        bool hasSufficientWeekendData,
        int dataPointCount)
    {
        double confidence = 50.0; // Base confidence
        
        // Sample size scoring (up to 30 points)
        if (sampleDays >= 30)
            confidence += 30;
        else if (sampleDays >= 14)
            confidence += 20;
        else if (sampleDays >= 7)
            confidence += 15;
        else if (sampleDays >= 3)
            confidence += 10;
        
        // Capacity stability scoring (up to 10 points)
        if (!hasCapacityChange)
            confidence += 10;
        
        // Weekend data scoring (up to 10 points)
        if (hasSufficientWeekendData)
            confidence += 10;
        
        // Data point count scoring (up to 10 points)
        if (dataPointCount >= 100)
            confidence += 10;
        else if (dataPointCount >= 50)
            confidence += 5;
        
        return Math.Min(confidence, 100.0);
    }
}
