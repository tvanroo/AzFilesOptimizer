using System;
using Azure.Data.Tables;
using AzFilesOptimizer.Backend.Models;
using System.Text.Json;

namespace AzFilesOptimizer.Backend.Services;

/// <summary>
/// Extension methods for cost analysis and forecasting storage
/// </summary>
public partial class DiscoveredResourceStorageService
{
    private TableClient? _costAnalysesTableClient;
    private TableClient? _forecastsTableClient;

    private TableClient CostAnalysesTableClient
    {
        get
        {
            if (_costAnalysesTableClient == null)
            {
                var serviceClient = new TableServiceClient(_storageConnectionString);
                _costAnalysesTableClient = serviceClient.GetTableClient("costanalyses");
                _costAnalysesTableClient.CreateIfNotExists();
            }
            return _costAnalysesTableClient;
        }
    }

    private TableClient ForecastsTableClient
    {
        get
        {
            if (_forecastsTableClient == null)
            {
                var serviceClient = new TableServiceClient(_storageConnectionString);
                _forecastsTableClient = serviceClient.GetTableClient("costforecasts");
                _forecastsTableClient.CreateIfNotExists();
            }
            return _forecastsTableClient;
        }
    }

    /// <summary>
    /// Save volume cost analyses to storage
    /// </summary>
    public async Task SaveVolumeCostAnalysesAsync(string jobId, List<VolumeCostAnalysis> costAnalyses)
    {
        if (costAnalyses == null || costAnalyses.Count == 0) return;

        foreach (var cost in costAnalyses)
        {
            try
            {
                // Ensure we have a stable volume identifier separate from the row key
                var volumeId = string.IsNullOrEmpty(cost.VolumeId)
                    ? (string.IsNullOrEmpty(cost.ResourceId) ? Guid.NewGuid().ToString() : cost.ResourceId)
                    : cost.VolumeId;

                // Use AnalysisId as the row key so multiple volumes per job don't overwrite each other
                var entity = new TableEntity(jobId, cost.AnalysisId)
                {
                    { "AnalysisId", cost.AnalysisId },
                    { "VolumeId", volumeId },
                    { "VolumeName", cost.VolumeName },
                    { "ResourceType", cost.ResourceType },
                    { "ResourceId", cost.ResourceId },
                    { "Region", cost.Region },
                    { "StorageAccountOrPoolName", cost.StorageAccountOrPoolName },
                    { "TotalCostPerDay", cost.TotalCostPerDay },
                    { "TotalCostForPeriod", cost.TotalCostForPeriod },
                    { "PeriodStart", cost.PeriodStart },
                    { "PeriodEnd", cost.PeriodEnd },
                    { "PeriodDays", cost.PeriodDays },
                    { "CapacityGigabytes", cost.CapacityGigabytes },
                    { "UsedGigabytes", cost.UsedGigabytes },
                    { "AverageTransactionsPerDay", cost.AverageTransactionsPerDay ?? 0 },
                    { "AverageEgressPerDayGb", cost.AverageEgressPerDayGb ?? 0 },
                    { "SnapshotCount", cost.SnapshotCount ?? 0 },
                    { "TotalSnapshotSizeGb", cost.TotalSnapshotSizeGb ?? 0 },
                    { "BackupConfigured", cost.BackupConfigured },
                    { "CostComponents", JsonSerializer.Serialize(cost.CostComponents) },
                    { "CostBreakdown", JsonSerializer.Serialize(cost.CostBreakdown) },
                    { "AnalysisTimestamp", cost.AnalysisTimestamp },
                    { "Notes", cost.Notes ?? "" },
                    { "Warnings", JsonSerializer.Serialize(cost.Warnings) }
                };

                await CostAnalysesTableClient.UpsertEntityAsync(entity);
            }
            catch (Exception ex)
            {
                // Log error but continue with other costs
                System.Diagnostics.Debug.WriteLine($"Failed to save cost analysis for {cost.VolumeName}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Retrieve volume cost analyses by job ID
    /// </summary>
    public async Task<List<VolumeCostAnalysis>> GetVolumeCostsByJobIdAsync(string jobId)
    {
        var costs = new List<VolumeCostAnalysis>();

        try
        {
            await foreach (var entity in CostAnalysesTableClient.QueryAsync<TableEntity>(filter: $"PartitionKey eq '{jobId}'"))
            {
                var storedResourceId = entity.GetString("ResourceId") ?? string.Empty;
                var normalizedVolumeId = !string.IsNullOrEmpty(storedResourceId)
                    ? ComputeVolumeIdFromResourceId(storedResourceId)
                    : (entity.GetString("VolumeId") ?? entity.RowKey);

                var cost = new VolumeCostAnalysis
                {
                    AnalysisId = entity.GetString("AnalysisId") ?? entity.RowKey,
                    VolumeId = normalizedVolumeId,
                    VolumeName = entity.GetString("VolumeName") ?? "",
                    ResourceType = entity.GetString("ResourceType") ?? "",
                    ResourceId = storedResourceId,
                    Region = entity.GetString("Region") ?? "",
                    StorageAccountOrPoolName = entity.GetString("StorageAccountOrPoolName"),
                    TotalCostPerDay = entity.GetDouble("TotalCostPerDay") ?? 0,
                    TotalCostForPeriod = entity.GetDouble("TotalCostForPeriod") ?? 0,
                    PeriodStart = entity.GetDateTime("PeriodStart") ?? DateTime.UtcNow.AddDays(-30),
                    PeriodEnd = entity.GetDateTime("PeriodEnd") ?? DateTime.UtcNow,
                    CapacityGigabytes = entity.GetDouble("CapacityGigabytes") ?? 0,
                    UsedGigabytes = entity.GetDouble("UsedGigabytes") ?? 0,
                    AverageTransactionsPerDay = entity.GetDouble("AverageTransactionsPerDay"),
                    AverageEgressPerDayGb = entity.GetDouble("AverageEgressPerDayGb"),
                    SnapshotCount = entity.GetInt32("SnapshotCount"),
                    TotalSnapshotSizeGb = entity.GetDouble("TotalSnapshotSizeGb"),
                    BackupConfigured = entity.GetBoolean("BackupConfigured") ?? false,
                    AnalysisTimestamp = entity.GetDateTime("AnalysisTimestamp") ?? DateTime.UtcNow,
                    Notes = entity.GetString("Notes")
                };

                // Deserialize complex fields
                var componentsJson = entity.GetString("CostComponents");
                if (!string.IsNullOrEmpty(componentsJson))
                {
                    try
                    {
                        cost.CostComponents = JsonSerializer.Deserialize<List<StorageCostComponent>>(componentsJson) ?? new();
                    }
                    catch { }
                }

                var warningsJson = entity.GetString("Warnings");
                if (!string.IsNullOrEmpty(warningsJson))
                {
                    try
                    {
                        cost.Warnings = JsonSerializer.Deserialize<List<string>>(warningsJson) ?? new();
                    }
                    catch { }
                }

                costs.Add(cost);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to retrieve costs for job {jobId}: {ex.Message}");
        }

        // If multiple analyses exist for the same volume within a job (e.g., after reruns),
        // return only the latest analysis per VolumeId to avoid duplicate cards in the UI.
        var deduped = costs
            .GroupBy(c => c.VolumeId)
            .Select(g => g.OrderByDescending(c => c.AnalysisTimestamp).First())
            .ToList();

        return deduped;
    }

    private static string ComputeVolumeIdFromResourceId(string resourceId)
    {
        if (string.IsNullOrEmpty(resourceId))
            return Guid.NewGuid().ToString();

        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(resourceId));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Save cost forecasts to storage
    /// </summary>
    public async Task SaveCostForecastsAsync(string jobId, List<CostForecastResult> forecasts)
    {
        if (forecasts == null || forecasts.Count == 0) return;

        foreach (var forecast in forecasts)
        {
            try
            {
                var entity = new TableEntity(jobId, forecast.ForecastId)
                {
                    { "ResourceId", forecast.ResourceId },
                    { "VolumeName", forecast.VolumeName },
                    { "ForecastedCostPerDay", forecast.ForecastedCostPerDay },
                    { "ForecastedCostFor30Days", forecast.ForecastedCostFor30Days },
                    { "LowEstimate30Days", forecast.LowEstimate30Days },
                    { "MidEstimate30Days", forecast.MidEstimate30Days },
                    { "HighEstimate30Days", forecast.HighEstimate30Days },
                    { "ConfidencePercentage", forecast.ConfidencePercentage },
                    { "Trend", forecast.Trend },
                    { "TrendDescription", forecast.TrendDescription },
                    { "PercentageChangeFromHistorical", forecast.PercentageChangeFromHistorical },
                    { "DailyGrowthRatePercentage", forecast.DailyGrowthRatePercentage },
                    { "StandardDeviation", forecast.StandardDeviation },
                    { "CoefficientOfVariation", forecast.CoefficientOfVariation },
                    { "BaselineCost", forecast.BaselineCost },
                    { "TrendComponent", forecast.TrendComponent },
                    { "VarianceComponent", forecast.VarianceComponent },
                    { "RecentChanges", JsonSerializer.Serialize(forecast.RecentChanges) },
                    { "RiskFactors", JsonSerializer.Serialize(forecast.RiskFactors) },
                    { "ForecastByComponent", JsonSerializer.Serialize(forecast.ForecastByComponent) },
                    { "ForecastGeneratedAt", forecast.ForecastGeneratedAt },
                    { "Notes", forecast.Notes ?? "" },
                    { "Recommendations", JsonSerializer.Serialize(forecast.Recommendations) }
                };

                await ForecastsTableClient.UpsertEntityAsync(entity);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save forecast for {forecast.VolumeName}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Retrieve cost forecasts by job ID
    /// </summary>
    public async Task<List<CostForecastResult>> GetCostForecastsByJobIdAsync(string jobId)
    {
        var forecasts = new List<CostForecastResult>();

        try
        {
            await foreach (var entity in ForecastsTableClient.QueryAsync<TableEntity>(filter: $"PartitionKey eq '{jobId}'"))
            {
                var forecast = new CostForecastResult
                {
                    ForecastId = entity.RowKey,
                    ResourceId = entity.GetString("ResourceId") ?? "",
                    VolumeName = entity.GetString("VolumeName") ?? "",
                    ForecastedCostPerDay = entity.GetDouble("ForecastedCostPerDay") ?? 0,
                    ForecastedCostFor30Days = entity.GetDouble("ForecastedCostFor30Days") ?? 0,
                    LowEstimate30Days = entity.GetDouble("LowEstimate30Days") ?? 0,
                    MidEstimate30Days = entity.GetDouble("MidEstimate30Days") ?? 0,
                    HighEstimate30Days = entity.GetDouble("HighEstimate30Days") ?? 0,
                    ConfidencePercentage = entity.GetDouble("ConfidencePercentage") ?? 0,
                    Trend = entity.GetString("Trend") ?? "Unknown",
                    TrendDescription = entity.GetString("TrendDescription") ?? "",
                    PercentageChangeFromHistorical = entity.GetDouble("PercentageChangeFromHistorical") ?? 0,
                    DailyGrowthRatePercentage = entity.GetDouble("DailyGrowthRatePercentage") ?? 0,
                    StandardDeviation = entity.GetDouble("StandardDeviation") ?? 0,
                    CoefficientOfVariation = entity.GetDouble("CoefficientOfVariation") ?? 0,
                    BaselineCost = entity.GetDouble("BaselineCost") ?? 0,
                    TrendComponent = entity.GetDouble("TrendComponent") ?? 0,
                    VarianceComponent = entity.GetDouble("VarianceComponent") ?? 0,
                    ForecastGeneratedAt = entity.GetDateTime("ForecastGeneratedAt") ?? DateTime.UtcNow,
                    Notes = entity.GetString("Notes")
                };

                // Deserialize complex fields
                var changesJson = entity.GetString("RecentChanges");
                if (!string.IsNullOrEmpty(changesJson))
                {
                    try
                    {
                        forecast.RecentChanges = JsonSerializer.Deserialize<List<string>>(changesJson) ?? new();
                    }
                    catch { }
                }

                var risksJson = entity.GetString("RiskFactors");
                if (!string.IsNullOrEmpty(risksJson))
                {
                    try
                    {
                        forecast.RiskFactors = JsonSerializer.Deserialize<List<string>>(risksJson) ?? new();
                    }
                    catch { }
                }

                var recsJson = entity.GetString("Recommendations");
                if (!string.IsNullOrEmpty(recsJson))
                {
                    try
                    {
                        forecast.Recommendations = JsonSerializer.Deserialize<List<string>>(recsJson) ?? new();
                    }
                    catch { }
                }

                forecasts.Add(forecast);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to retrieve forecasts for job {jobId}: {ex.Message}");
        }

        return forecasts;
    }

    /// <summary>
    /// Update cost assumptions for a volume
    /// </summary>
    public async Task UpdateCostAssumptionsAsync(string jobId, string volumeId, Dictionary<string, object> assumptions)
    {
        try
        {
            var costs = await GetVolumeCostsByJobIdAsync(jobId);
            var cost = costs.FirstOrDefault(c => c.VolumeId == volumeId);
            
            if (cost != null)
            {
                cost.Notes = $"Custom assumptions applied: {JsonSerializer.Serialize(assumptions)}";
                await SaveVolumeCostAnalysesAsync(jobId, new List<VolumeCostAnalysis> { cost });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update cost assumptions: {ex.Message}");
        }
    }
}