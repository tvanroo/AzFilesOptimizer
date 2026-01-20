using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using AzFilesOptimizer.Backend.Models;

namespace AzFilesOptimizer.Backend.Services;

public class CostForecastingService
{
    private readonly ILogger _logger;

    public CostForecastingService(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Forecast costs for the next 30 days based on historical data and recent changes
    /// </summary>
    public CostForecastResult ForecastCosts(
        VolumeCostAnalysis historicalAnalysis,
        List<CostMetrics> historicalMetrics,
        List<string>? recentChanges = null)
    {
        try
        {
            recentChanges ??= new List<string>();

            var forecast = new CostForecastResult
            {
                ResourceId = historicalAnalysis.ResourceId,
                VolumeName = historicalAnalysis.VolumeName,
                HistoricalPeriodStart = historicalAnalysis.PeriodStart,
                HistoricalPeriodEnd = historicalAnalysis.PeriodEnd,
                ForecastPeriodStart = DateTime.UtcNow,
                ForecastPeriodEnd = DateTime.UtcNow.AddDays(30)
            };

            // Get daily costs from historical data
            var dailyCosts = CalculateDailyCosts(historicalMetrics, historicalAnalysis);

            if (!dailyCosts.Any())
            {
                _logger.LogWarning("No historical cost data available for forecasting {Volume}", historicalAnalysis.VolumeName);
                forecast.Trend = "Unknown";
                forecast.ConfidencePercentage = 10;
                forecast.ForecastedCostFor30Days = historicalAnalysis.TotalCostForPeriod;
                forecast.MidEstimate30Days = historicalAnalysis.TotalCostForPeriod;
                return forecast;
            }

            // Perform trend analysis
            var trendAnalysis = AnalyzeTrend(dailyCosts);
            forecast.BaselineCost = trendAnalysis.BaselineCost;
            forecast.TrendComponent = trendAnalysis.TrendComponent;
            forecast.VarianceComponent = trendAnalysis.VarianceComponent;
            forecast.StandardDeviation = trendAnalysis.StandardDeviation;
            forecast.CoefficientOfVariation = trendAnalysis.CoefficientOfVariation;
            forecast.DailyGrowthRatePercentage = trendAnalysis.DailyGrowthRate;

            // Determine trend direction
            DetermineTrend(forecast, trendAnalysis);

            // Calculate forecasted costs
            var forecastedDailyCost = CalculateForecastedDailyCost(trendAnalysis, dailyCosts.Count, recentChanges);
            forecast.ForecastedCostPerDay = forecastedDailyCost;
            forecast.ForecastedCostFor30Days = forecastedDailyCost * 30;

            // Calculate confidence based on data quality and variance
            forecast.ConfidencePercentage = CalculateConfidence(trendAnalysis, recentChanges);

            // Calculate percentile estimates
            CalculatePercentileEstimates(forecast, dailyCosts, trendAnalysis);

            // Calculate change from historical
            var historicalDaily = historicalAnalysis.TotalCostForPeriod / historicalAnalysis.PeriodDays;
            forecast.PercentageChangeFromHistorical = historicalDaily > 0
                ? ((forecastedDailyCost - historicalDaily) / historicalDaily) * 100
                : 0;

            // Detect recent changes
            DetectRecentChanges(forecast, historicalAnalysis, recentChanges);

            // Identify risk factors
            IdentifyRiskFactors(forecast, historicalAnalysis, trendAnalysis);

            // Generate recommendations
            GenerateRecommendations(forecast, historicalAnalysis);

            _logger.LogInformation("Forecast generated for {Volume}: ${ForecastedCost} ({Trend}, {Confidence}% confidence)",
                forecast.VolumeName, forecast.ForecastedCostFor30Days, forecast.Trend, forecast.ConfidencePercentage);

            return forecast;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forecasting costs for {Volume}", historicalAnalysis.VolumeName);
            throw;
        }
    }

    /// <summary>
    /// Calculate daily costs from time-series metrics
    /// </summary>
    private List<double> CalculateDailyCosts(List<CostMetrics> metrics, VolumeCostAnalysis analysis)
    {
        if (!metrics.Any()) return new List<double>();

        var dailyCosts = metrics
            .OrderBy(m => m.Timestamp)
            .GroupBy(m => m.Timestamp.Date)
            .Select(g => g.Sum(m => m.TotalCost))
            .ToList();

        return dailyCosts;
    }

    /// <summary>
    /// Analyze trend in daily costs using linear regression
    /// </summary>
    private TrendAnalysisResult AnalyzeTrend(List<double> dailyCosts)
    {
        var result = new TrendAnalysisResult();

        if (dailyCosts.Count < 2) return result;

        // Calculate statistics
        var avgCost = dailyCosts.Average();
        var variance = dailyCosts.Sum(x => Math.Pow(x - avgCost, 2)) / dailyCosts.Count;
        var stdDev = Math.Sqrt(variance);
        
        result.BaselineCost = avgCost;
        result.StandardDeviation = stdDev;
        result.CoefficientOfVariation = avgCost > 0 ? stdDev / avgCost : 0;

        // Linear regression
        var n = dailyCosts.Count;
        var xSum = Enumerable.Range(0, n).Sum(i => (double)i);
        var ySum = dailyCosts.Sum();
        var xySum = Enumerable.Range(0, n).Sum(i => i * dailyCosts[i]);
        var x2Sum = Enumerable.Range(0, n).Sum(i => i * i);

        var slope = (n * xySum - xSum * ySum) / (n * x2Sum - xSum * xSum);
        result.TrendComponent = slope;
        result.DailyGrowthRate = (slope / avgCost) * 100;

        // Variance component
        result.VarianceComponent = stdDev;

        return result;
    }

    /// <summary>
    /// Determine trend direction (Increasing, Decreasing, Stable)
    /// </summary>
    private void DetermineTrend(CostForecastResult forecast, TrendAnalysisResult analysis)
    {
        var growthThreshold = 0.5; // 0.5% daily growth
        var declineThreshold = -0.5;

        if (analysis.DailyGrowthRate > growthThreshold)
        {
            forecast.Trend = "Increasing";
            forecast.TrendDescription = $"Cost growing at {analysis.DailyGrowthRate:F2}% per day";
        }
        else if (analysis.DailyGrowthRate < declineThreshold)
        {
            forecast.Trend = "Decreasing";
            forecast.TrendDescription = $"Cost declining at {Math.Abs(analysis.DailyGrowthRate):F2}% per day";
        }
        else
        {
            forecast.Trend = "Stable";
            forecast.TrendDescription = $"Cost is stable with minimal variance";
        }
    }

    /// <summary>
    /// Calculate forecasted daily cost accounting for trend and recent changes
    /// </summary>
    private double CalculateForecastedDailyCost(TrendAnalysisResult analysis, int historicalDays, List<string> recentChanges)
    {
        var baseCost = analysis.BaselineCost;

        // Project trend forward 15 days (midpoint of forecast)
        var projectionDays = 15;
        var trendedCost = baseCost + (analysis.TrendComponent * projectionDays);

        // Factor in recent changes
        if (recentChanges != null)
        {
            var changeMultiplier = CalculateChangeMultiplier(recentChanges);
            trendedCost *= changeMultiplier;
        }

        return Math.Max(trendedCost, 0);
    }

    /// <summary>
    /// Calculate multiplier based on recent changes
    /// </summary>
    private double CalculateChangeMultiplier(List<string> recentChanges)
    {
        var multiplier = 1.0;

        foreach (var change in recentChanges)
        {
            var lower = change.ToLowerInvariant();
            
            // Capacity changes
            if (lower.Contains("capacity") && lower.Contains("increase"))
                multiplier *= 1.15; // 15% cost increase
            else if (lower.Contains("capacity") && lower.Contains("decrease"))
                multiplier *= 0.85; // 15% cost decrease
            
            // Tier changes
            else if (lower.Contains("premium"))
                multiplier *= 1.30; // Premium is ~30% more expensive
            else if (lower.Contains("standard"))
                multiplier *= 0.75; // Standard is less expensive
            
            // Backup changes
            else if (lower.Contains("backup") && lower.Contains("enabled"))
                multiplier *= 1.10; // 10% cost increase for backup
            else if (lower.Contains("backup") && lower.Contains("disabled"))
                multiplier *= 0.90;
            
            // Snapshot policy
            else if (lower.Contains("snapshot") && lower.Contains("enabled"))
                multiplier *= 1.05;
            else if (lower.Contains("snapshot") && lower.Contains("disabled"))
                multiplier *= 0.95;
        }

        return multiplier;
    }

    /// <summary>
    /// Calculate confidence percentage for the forecast
    /// </summary>
    private double CalculateConfidence(TrendAnalysisResult analysis, List<string> recentChanges)
    {
        var confidence = 75.0; // Base confidence

        // High variance reduces confidence
        if (analysis.CoefficientOfVariation > 0.5)
            confidence -= 25;
        else if (analysis.CoefficientOfVariation > 0.3)
            confidence -= 15;

        // Recent changes reduce confidence
        if (recentChanges.Any())
            confidence -= (5 * Math.Min(recentChanges.Count, 3));

        // Stable trends increase confidence
        if (Math.Abs(analysis.DailyGrowthRate) < 0.5)
            confidence += 10;

        return Math.Max(10, Math.Min(95, confidence));
    }

    /// <summary>
    /// Calculate low, mid, and high percentile estimates
    /// </summary>
    private void CalculatePercentileEstimates(CostForecastResult forecast, List<double> dailyCosts, TrendAnalysisResult analysis)
    {
        var dailyForecast = forecast.ForecastedCostPerDay;
        var stdDev = analysis.StandardDeviation;

        // Use percentiles based on standard deviation
        var lowDaily = Math.Max(dailyForecast - stdDev, 0);
        var midDaily = dailyForecast;
        var highDaily = dailyForecast + stdDev;

        forecast.LowEstimate30Days = lowDaily * 30;
        forecast.MidEstimate30Days = midDaily * 30;
        forecast.HighEstimate30Days = highDaily * 30;
    }

    /// <summary>
    /// Detect recent changes in the volume
    /// </summary>
    private void DetectRecentChanges(CostForecastResult forecast, VolumeCostAnalysis analysis, List<string> providedChanges)
    {
        // Add provided changes
        foreach (var change in providedChanges)
        {
            forecast.AddRecentChange(change);
        }

        // Detect cost-based changes
        if (forecast.PercentageChangeFromHistorical > 20)
            forecast.AddRecentChange("Significant cost increase detected");
        else if (forecast.PercentageChangeFromHistorical < -20)
            forecast.AddRecentChange("Significant cost decrease detected");

        // Detect capacity changes
        if (analysis.SnapshotCount > 100)
            forecast.AddRecentChange("High snapshot count may indicate increased backup activity");

        // Detect backup configuration
        if (analysis.BackupConfigured)
            forecast.AddRecentChange("Backup is configured and consuming storage");
    }

    /// <summary>
    /// Identify risk factors for the volume
    /// </summary>
    private void IdentifyRiskFactors(CostForecastResult forecast, VolumeCostAnalysis analysis, TrendAnalysisResult trend)
    {
        // Rapid growth risk
        if (forecast.DailyGrowthRatePercentage > 5.0)
            forecast.AddRiskFactor($"Rapid cost growth at {forecast.DailyGrowthRatePercentage:F1}% per day");

        // High variance
        if (trend.CoefficientOfVariation > 0.5)
            forecast.AddRiskFactor("High variability in daily costs may affect forecast accuracy");

        // High snapshot usage
        if (analysis.SnapshotCount > 50)
            forecast.AddRiskFactor($"High snapshot count ({analysis.SnapshotCount}) consuming {analysis.TotalSnapshotSizeGb:F1} GB");

        // Capacity approaching limits
        if (analysis.UsedGigabytes > 0 && analysis.CapacityGigabytes > 0)
        {
            var utilization = (analysis.UsedGigabytes / analysis.CapacityGigabytes) * 100;
            if (utilization > 80)
                forecast.AddRiskFactor($"Volume is {utilization:F0}% full, may require expansion");
        }

        // Low confidence
        if (forecast.ConfidencePercentage < 50)
            forecast.AddRiskFactor("Low forecast confidence due to volatile historical data");
    }

    /// <summary>
    /// Generate recommendations based on forecast
    /// </summary>
    private void GenerateRecommendations(CostForecastResult forecast, VolumeCostAnalysis analysis)
    {
        // Growth recommendation
        if (forecast.HasRapidGrowth)
        {
            forecast.AddRecommendation($"Investigate cause of {forecast.DailyGrowthRatePercentage:F1}% daily cost growth");
            forecast.AddRecommendation("Consider implementing retention policies to reduce data growth");
        }

        // Snapshot recommendation
        if (analysis.SnapshotCount > 50 && analysis.TotalSnapshotSizeGb > 0)
        {
            forecast.AddRecommendation($"Review snapshot retention policy (currently {analysis.SnapshotCount} snapshots consuming {analysis.TotalSnapshotSizeGb:F1} GB)");
        }

        // Backup recommendation
        if (analysis.BackupConfigured && forecast.BackupCostPercentage > 20)
        {
            forecast.AddRecommendation("Backup represents >20% of cost; verify if retention period is necessary");
        }

        // Forecast confidence
        if (forecast.HasLowConfidence)
        {
            forecast.AddRecommendation("Forecast confidence is low; check again after 7 days when more stable data is available");
        }

        // Cost optimization
        if (forecast.EgressCostPercentage > 25)
        {
            forecast.AddRecommendation("Data egress costs are high; consider using intra-regional transfer options");
        }
    }

    /// <summary>
    /// Internal class for trend analysis results
    /// </summary>
    private class TrendAnalysisResult
    {
        public double BaselineCost { get; set; }
        public double TrendComponent { get; set; }
        public double VarianceComponent { get; set; }
        public double StandardDeviation { get; set; }
        public double CoefficientOfVariation { get; set; }
        public double DailyGrowthRate { get; set; }
    }
}