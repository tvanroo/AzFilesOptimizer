using Azure;
using Azure.Data.Tables;
using AzFilesOptimizer.Backend.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AzFilesOptimizer.Backend.Services;

/// <summary>
/// Service for tracking cost history over time in Table Storage
/// </summary>
public class CostHistoryService
{
    private readonly TableClient _costHistoryTable;
    private readonly ILogger _logger;

    public CostHistoryService(string connectionString, ILogger logger)
    {
        _logger = logger;
        var tableServiceClient = new TableServiceClient(connectionString);
        _costHistoryTable = tableServiceClient.GetTableClient("CostHistory");
        _costHistoryTable.CreateIfNotExists();
    }

    /// <summary>
    /// Save a cost snapshot for a volume
    /// PartitionKey = JobId, RowKey = VolumeId_Timestamp
    /// </summary>
    public async Task SaveCostSnapshotAsync(string jobId, string volumeId, VolumeCostAnalysis costAnalysis)
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var rowKey = $"{SanitizeForTableStorage(volumeId)}_{timestamp}";

            var entity = new TableEntity(jobId, rowKey)
            {
                { "VolumeId", volumeId },
                { "VolumeName", costAnalysis.VolumeName },
                { "ResourceType", costAnalysis.ResourceType },
                { "ResourceId", costAnalysis.ResourceId },
                { "Region", costAnalysis.Region },
                { "TotalCostForPeriod", costAnalysis.TotalCostForPeriod },
                { "TotalCostPerDay", costAnalysis.TotalCostPerDay },
                { "CapacityGigabytes", costAnalysis.CapacityGigabytes },
                { "UsedGigabytes", costAnalysis.UsedGigabytes },
                { "PeriodStart", costAnalysis.PeriodStart },
                { "PeriodEnd", costAnalysis.PeriodEnd },
                { "AnalysisTimestamp", costAnalysis.AnalysisTimestamp },
                { "LastActualCostUpdate", costAnalysis.LastActualCostUpdate ?? DateTime.MinValue },
                
                // Store cost breakdown as JSON
                { "CostBreakdownJson", JsonSerializer.Serialize(costAnalysis.CostBreakdown) },
                
                // Store billing metadata if available
                { "HasBillingMetadata", costAnalysis.BillingMetadata != null },
                { "BillingMetadataJson", costAnalysis.BillingMetadata != null 
                    ? JsonSerializer.Serialize(costAnalysis.BillingMetadata) 
                    : null },
                
                // Number of detailed meters
                { "MeterCount", costAnalysis.DetailedMeterCosts?.Count ?? 0 }
            };

            await _costHistoryTable.AddEntityAsync(entity);
            
            _logger.LogInformation(
                "Saved cost snapshot for volume {VolumeId} in job {JobId}, cost: ${Cost}",
                volumeId,
                jobId,
                costAnalysis.TotalCostForPeriod);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save cost snapshot for volume {VolumeId} in job {JobId}", volumeId, jobId);
            throw;
        }
    }

    /// <summary>
    /// Get cost history for a specific volume
    /// </summary>
    public async Task<List<CostSnapshot>> GetCostHistoryAsync(string jobId, string volumeId, int monthsBack = 3)
    {
        try
        {
            var sanitizedVolumeId = SanitizeForTableStorage(volumeId);
            var cutoffDate = DateTime.UtcNow.AddMonths(-monthsBack);
            
            var query = _costHistoryTable.QueryAsync<TableEntity>(
                filter: $"PartitionKey eq '{jobId}' and VolumeId eq '{volumeId}'");

            var snapshots = new List<CostSnapshot>();
            
            await foreach (var entity in query)
            {
                var analysisTimestamp = entity.GetDateTime("AnalysisTimestamp") ?? DateTime.MinValue;
                
                if (analysisTimestamp < cutoffDate)
                    continue;

                var snapshot = new CostSnapshot
                {
                    JobId = jobId,
                    VolumeId = entity.GetString("VolumeId") ?? string.Empty,
                    VolumeName = entity.GetString("VolumeName") ?? string.Empty,
                    ResourceType = entity.GetString("ResourceType") ?? string.Empty,
                    TotalCost = entity.GetDouble("TotalCostForPeriod") ?? 0,
                    DailyCost = entity.GetDouble("TotalCostPerDay") ?? 0,
                    AnalysisTimestamp = analysisTimestamp,
                    PeriodStart = entity.GetDateTime("PeriodStart") ?? DateTime.MinValue,
                    PeriodEnd = entity.GetDateTime("PeriodEnd") ?? DateTime.MinValue
                };

                // Deserialize cost breakdown if available
                var costBreakdownJson = entity.GetString("CostBreakdownJson");
                if (!string.IsNullOrEmpty(costBreakdownJson))
                {
                    snapshot.CostBreakdown = JsonSerializer.Deserialize<Dictionary<string, double>>(costBreakdownJson);
                }

                snapshots.Add(snapshot);
            }

            snapshots = snapshots.OrderByDescending(s => s.AnalysisTimestamp).ToList();
            
            _logger.LogInformation(
                "Retrieved {Count} cost snapshots for volume {VolumeId} in job {JobId}",
                snapshots.Count,
                volumeId,
                jobId);

            return snapshots;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cost history for volume {VolumeId} in job {JobId}", volumeId, jobId);
            return new List<CostSnapshot>();
        }
    }

    /// <summary>
    /// Get cost trends across all volumes in a job
    /// </summary>
    public async Task<CostTrendSummary> GetCostTrendsAsync(string jobId)
    {
        try
        {
            var query = _costHistoryTable.QueryAsync<TableEntity>(
                filter: $"PartitionKey eq '{jobId}'");

            var allSnapshots = new List<CostSnapshot>();
            
            await foreach (var entity in query)
            {
                var snapshot = new CostSnapshot
                {
                    JobId = jobId,
                    VolumeId = entity.GetString("VolumeId") ?? string.Empty,
                    VolumeName = entity.GetString("VolumeName") ?? string.Empty,
                    ResourceType = entity.GetString("ResourceType") ?? string.Empty,
                    TotalCost = entity.GetDouble("TotalCostForPeriod") ?? 0,
                    DailyCost = entity.GetDouble("TotalCostPerDay") ?? 0,
                    AnalysisTimestamp = entity.GetDateTime("AnalysisTimestamp") ?? DateTime.MinValue
                };

                allSnapshots.Add(snapshot);
            }

            // Calculate trends
            var summary = new CostTrendSummary
            {
                JobId = jobId,
                TotalSnapshots = allSnapshots.Count,
                UniqueVolumes = allSnapshots.Select(s => s.VolumeId).Distinct().Count(),
                LatestTotalCost = 0,
                OldestTotalCost = 0,
                AverageDailyCost = 0,
                TrendDirection = "Unknown"
            };

            if (allSnapshots.Count > 0)
            {
                var latestSnapshots = allSnapshots
                    .GroupBy(s => s.VolumeId)
                    .Select(g => g.OrderByDescending(s => s.AnalysisTimestamp).First())
                    .ToList();

                var oldestSnapshots = allSnapshots
                    .GroupBy(s => s.VolumeId)
                    .Select(g => g.OrderBy(s => s.AnalysisTimestamp).First())
                    .ToList();

                summary.LatestTotalCost = latestSnapshots.Sum(s => s.TotalCost);
                summary.OldestTotalCost = oldestSnapshots.Sum(s => s.TotalCost);
                summary.AverageDailyCost = latestSnapshots.Average(s => s.DailyCost);

                // Determine trend direction
                if (summary.LatestTotalCost > summary.OldestTotalCost * 1.1)
                    summary.TrendDirection = "Increasing";
                else if (summary.LatestTotalCost < summary.OldestTotalCost * 0.9)
                    summary.TrendDirection = "Decreasing";
                else
                    summary.TrendDirection = "Stable";

                summary.CostByResourceType = latestSnapshots
                    .GroupBy(s => s.ResourceType)
                    .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalCost));
            }

            _logger.LogInformation(
                "Generated cost trend summary for job {JobId}: {Direction}, latest: ${Latest}, oldest: ${Oldest}",
                jobId,
                summary.TrendDirection,
                summary.LatestTotalCost,
                summary.OldestTotalCost);

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cost trends for job {JobId}", jobId);
            throw;
        }
    }

    /// <summary>
    /// Sanitize string for use in Azure Table Storage keys
    /// </summary>
    private string SanitizeForTableStorage(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "unknown";

        // Replace invalid characters with underscores
        var invalid = new[] { '/', '\\', '#', '?', '\t', '\n', '\r' };
        var sanitized = input;
        
        foreach (var c in invalid)
        {
            sanitized = sanitized.Replace(c, '_');
        }

        // Limit length (max 1024 for row key, but keep it reasonable)
        if (sanitized.Length > 500)
            sanitized = sanitized.Substring(0, 500);

        return sanitized;
    }
}

/// <summary>
/// Represents a cost snapshot at a point in time
/// </summary>
public class CostSnapshot
{
    public string JobId { get; set; } = string.Empty;
    public string VolumeId { get; set; } = string.Empty;
    public string VolumeName { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public double TotalCost { get; set; }
    public double DailyCost { get; set; }
    public DateTime AnalysisTimestamp { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public Dictionary<string, double>? CostBreakdown { get; set; }
}

/// <summary>
/// Summary of cost trends for a job
/// </summary>
public class CostTrendSummary
{
    public string JobId { get; set; } = string.Empty;
    public int TotalSnapshots { get; set; }
    public int UniqueVolumes { get; set; }
    public double LatestTotalCost { get; set; }
    public double OldestTotalCost { get; set; }
    public double AverageDailyCost { get; set; }
    public string TrendDirection { get; set; } = "Unknown";
    public Dictionary<string, double>? CostByResourceType { get; set; }
}
