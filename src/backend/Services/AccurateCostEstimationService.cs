using Microsoft.Extensions.Logging;
using AzFilesOptimizer.Backend.Models;
using AzFilesOptimizer.Backend.Models.Discovery;

namespace AzFilesOptimizer.Backend.Services;

/// <summary>
/// Main orchestration service for accurate 30-day cost estimation across all storage types
/// </summary>
public class AccurateCostEstimationService
{
    private readonly AzureFilesCostCalculator _filesCostCalculator;
    private readonly AnfCostCalculator _anfCostCalculator;
    private readonly ManagedDiskCostCalculator _diskCostCalculator;
    private readonly CostCollectionService _costCollectionService;
    private readonly ILogger<AccurateCostEstimationService> _logger;

    public AccurateCostEstimationService(
        AzureFilesCostCalculator filesCostCalculator,
        AnfCostCalculator anfCostCalculator,
        ManagedDiskCostCalculator diskCostCalculator,
        CostCollectionService costCollectionService,
        ILogger<AccurateCostEstimationService> logger)
    {
        _filesCostCalculator = filesCostCalculator;
        _anfCostCalculator = anfCostCalculator;
        _diskCostCalculator = diskCostCalculator;
        _costCollectionService = costCollectionService;
        _logger = logger;
    }

    /// <summary>
    /// Calculate 30-day cost estimate for a discovered resource
    /// </summary>
    public async Task<VolumeCostEstimate> Calculate30DayCostEstimateAsync(DiscoveredResource resource)
    {
        try
        {
            _logger.LogInformation("Calculating 30-day cost estimate for {ResourceName} ({ResourceType})", 
                resource.Name, resource.ResourceType);

            return resource.ResourceType switch
            {
                "AzureFile" => await CalculateAzureFilesCostAsync(resource),
                "ANF" => await CalculateAnfCostAsync(resource),
                "ManagedDisk" => await CalculateManagedDiskCostAsync(resource),
                _ => CreateUnknownResourceEstimate(resource)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate cost estimate for {ResourceName}", resource.Name);
            return new VolumeCostEstimate
            {
                VolumeId = resource.ResourceId,
                VolumeName = resource.Name,
                ResourceType = resource.ResourceType,
                EstimationMethod = "Error",
                ConfidenceLevel = 0,
                Warnings = new List<string> { $"Cost calculation failed: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Calculate costs for Azure Files share
    /// </summary>
    private async Task<VolumeCostEstimate> CalculateAzureFilesCostAsync(DiscoveredResource resource)
    {
        var volumeInfo = new AzureFilesVolumeInfo
        {
            VolumeId = resource.ResourceId,
            VolumeName = resource.Name,
            Region = resource.Location ?? "eastus",
            IsProvisioned = IsProvisionedShare(resource),
            Tier = GetShareTier(resource),
            Redundancy = GetRedundancy(resource),
            ProvisionedCapacityGb = resource.CapacityGb,
            UsedCapacityGb = resource.UsedGb,
            SnapshotSizeGb = resource.SnapshotSizeGb ?? 0,
            TransactionsPerMonth = await EstimateMonthlyTransactionsAsync(resource),
            EgressGbPerMonth = await EstimateMonthlyEgressAsync(resource)
        };

        return await _filesCostCalculator.CalculateAsync(volumeInfo);
    }

    /// <summary>
    /// Calculate costs for ANF volume
    /// </summary>
    private async Task<VolumeCostEstimate> CalculateAnfCostAsync(DiscoveredResource resource)
    {
        var volumeInfo = new AnfVolumeInfo
        {
            VolumeId = resource.ResourceId,
            VolumeName = resource.Name,
            Region = resource.Location ?? "eastus",
            ServiceLevel = GetAnfServiceLevel(resource),
            ProvisionedCapacityGb = resource.CapacityGb,
            UsedCapacityGb = resource.UsedGb,
            SnapshotSizeGb = resource.SnapshotSizeGb ?? 0,
            CoolAccessEnabled = IsCoolAccessEnabled(resource),
            HotDataGb = GetHotDataSize(resource),
            CoolDataGb = GetCoolDataSize(resource),
            DataTieredToCoolGb = await EstimateMonthlyTieringAsync(resource),
            DataRetrievedFromCoolGb = await EstimateMonthlyRetrievalAsync(resource)
        };

        return await _anfCostCalculator.CalculateAsync(volumeInfo);
    }

    /// <summary>
    /// Calculate costs for Managed Disk (uses actual billing data when available)
    /// </summary>
    private async Task<VolumeCostEstimate> CalculateManagedDiskCostAsync(DiscoveredResource resource)
    {
        var diskInfo = new ManagedDiskVolumeInfo
        {
            VolumeId = resource.ResourceId,
            VolumeName = resource.Name,
            Region = resource.Location ?? "eastus",
            DiskType = GetDiskType(resource),
            DiskSizeGb = (int)resource.CapacityGb,
            SubscriptionId = ExtractSubscriptionId(resource.ResourceId),
            ResourceGroupName = ExtractResourceGroup(resource.ResourceId)
        };

        return await _diskCostCalculator.CalculateAsync(diskInfo);
    }

    /// <summary>
    /// Determine if share is provisioned (Premium)
    /// </summary>
    private bool IsProvisionedShare(DiscoveredResource resource)
    {
        var tier = resource.Properties?.GetValueOrDefault("tier")?.ToString() ?? "";
        return tier.Contains("Premium", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get share tier (Hot, Cool, TransactionOptimized, Premium)
    /// </summary>
    private string GetShareTier(DiscoveredResource resource)
    {
        var tier = resource.Properties?.GetValueOrDefault("tier")?.ToString() ?? "Hot";
        
        if (tier.Contains("Premium", StringComparison.OrdinalIgnoreCase))
            return "Premium";
        if (tier.Contains("Cool", StringComparison.OrdinalIgnoreCase))
            return "Cool";
        if (tier.Contains("TransactionOptimized", StringComparison.OrdinalIgnoreCase))
            return "TransactionOptimized";
        
        return "Hot";
    }

    /// <summary>
    /// Get redundancy type (LRS, ZRS, GRS, GZRS)
    /// </summary>
    private string GetRedundancy(DiscoveredResource resource)
    {
        var sku = resource.Properties?.GetValueOrDefault("sku")?.ToString() ?? "Standard_LRS";
        
        if (sku.Contains("GZRS", StringComparison.OrdinalIgnoreCase))
            return "GZRS";
        if (sku.Contains("GRS", StringComparison.OrdinalIgnoreCase))
            return "GRS";
        if (sku.Contains("ZRS", StringComparison.OrdinalIgnoreCase))
            return "ZRS";
        
        return "LRS";
    }

    /// <summary>
    /// Get ANF service level
    /// </summary>
    private string GetAnfServiceLevel(DiscoveredResource resource)
    {
        var serviceLevel = resource.Properties?.GetValueOrDefault("serviceLevel")?.ToString() ?? "Standard";
        return serviceLevel;
    }

    /// <summary>
    /// Check if cool access is enabled for ANF
    /// </summary>
    private bool IsCoolAccessEnabled(DiscoveredResource resource)
    {
        var coolAccess = resource.Properties?.GetValueOrDefault("coolAccess")?.ToString() ?? "false";
        return bool.TryParse(coolAccess, out var enabled) && enabled;
    }

    /// <summary>
    /// Get hot data size for ANF with cool access
    /// </summary>
    private double GetHotDataSize(DiscoveredResource resource)
    {
        var hotData = resource.Properties?.GetValueOrDefault("hotDataSizeGb")?.ToString();
        return double.TryParse(hotData, out var size) ? size : 0;
    }

    /// <summary>
    /// Get cool data size for ANF with cool access
    /// </summary>
    private double GetCoolDataSize(DiscoveredResource resource)
    {
        var coolData = resource.Properties?.GetValueOrDefault("coolDataSizeGb")?.ToString();
        return double.TryParse(coolData, out var size) ? size : 0;
    }

    /// <summary>
    /// Get disk type (Premium SSD, Standard SSD, Standard HDD, etc.)
    /// </summary>
    private string GetDiskType(DiscoveredResource resource)
    {
        var sku = resource.Properties?.GetValueOrDefault("sku")?.ToString() ?? "Premium_LRS";
        
        if (sku.Contains("Premium", StringComparison.OrdinalIgnoreCase))
            return "Premium SSD";
        if (sku.Contains("StandardSSD", StringComparison.OrdinalIgnoreCase))
            return "Standard SSD";
        if (sku.Contains("Standard", StringComparison.OrdinalIgnoreCase))
            return "Standard HDD";
        if (sku.Contains("UltraSSD", StringComparison.OrdinalIgnoreCase))
            return "Ultra Disk";
        
        return "Premium SSD";
    }

    /// <summary>
    /// Estimate monthly transactions from Azure Monitor metrics
    /// </summary>
    private async Task<long> EstimateMonthlyTransactionsAsync(DiscoveredResource resource)
    {
        try
        {
            // Try to get actual transaction metrics from Azure Monitor
            var metrics = await GetMetricsFromMonitorAsync(resource.ResourceId, "Transactions", 30);
            if (metrics > 0)
            {
                return (long)metrics;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get transaction metrics for {ResourceName}", resource.Name);
        }

        // Fallback: estimate based on capacity (rough heuristic)
        // Assume ~1000 transactions per GB of used capacity per day
        var estimatedDaily = (long)(resource.UsedGb * 1000);
        return estimatedDaily * 30;
    }

    /// <summary>
    /// Estimate monthly egress from Azure Monitor metrics
    /// </summary>
    private async Task<double> EstimateMonthlyEgressAsync(DiscoveredResource resource)
    {
        try
        {
            // Try to get actual egress metrics from Azure Monitor
            var metrics = await GetMetricsFromMonitorAsync(resource.ResourceId, "Egress", 30);
            if (metrics > 0)
            {
                return metrics / (1024.0 * 1024.0 * 1024.0); // Convert bytes to GB
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get egress metrics for {ResourceName}", resource.Name);
        }

        // Assume minimal egress if we don't have data
        return 0;
    }

    /// <summary>
    /// Estimate monthly cool tiering activity
    /// </summary>
    private async Task<double> EstimateMonthlyTieringAsync(DiscoveredResource resource)
    {
        try
        {
            var metrics = await GetMetricsFromMonitorAsync(resource.ResourceId, "CoolTieringDataWrite", 30);
            if (metrics > 0)
            {
                return metrics / (1024.0 * 1024.0 * 1024.0); // Convert bytes to GB
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cool tiering metrics not available for {ResourceName}", resource.Name);
        }

        return 0;
    }

    /// <summary>
    /// Estimate monthly cool retrieval activity
    /// </summary>
    private async Task<double> EstimateMonthlyRetrievalAsync(DiscoveredResource resource)
    {
        try
        {
            var metrics = await GetMetricsFromMonitorAsync(resource.ResourceId, "CoolTieringDataRead", 30);
            if (metrics > 0)
            {
                return metrics / (1024.0 * 1024.0 * 1024.0); // Convert bytes to GB
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cool retrieval metrics not available for {ResourceName}", resource.Name);
        }

        return 0;
    }

    /// <summary>
    /// Get metrics from Azure Monitor (placeholder - integrate with actual metrics service)
    /// </summary>
    private async Task<double> GetMetricsFromMonitorAsync(string resourceId, string metricName, int days)
    {
        // TODO: Integrate with actual Azure Monitor metrics collection
        // For now, return 0 to indicate no metrics available
        await Task.CompletedTask;
        return 0;
    }

    /// <summary>
    /// Extract subscription ID from resource ID
    /// </summary>
    private string ExtractSubscriptionId(string resourceId)
    {
        var parts = resourceId.Split('/');
        var subIndex = Array.IndexOf(parts, "subscriptions");
        return subIndex >= 0 && subIndex + 1 < parts.Length ? parts[subIndex + 1] : "";
    }

    /// <summary>
    /// Extract resource group from resource ID
    /// </summary>
    private string ExtractResourceGroup(string resourceId)
    {
        var parts = resourceId.Split('/');
        var rgIndex = Array.IndexOf(parts, "resourceGroups");
        return rgIndex >= 0 && rgIndex + 1 < parts.Length ? parts[rgIndex + 1] : "";
    }

    /// <summary>
    /// Create estimate for unknown resource type
    /// </summary>
    private VolumeCostEstimate CreateUnknownResourceEstimate(DiscoveredResource resource)
    {
        _logger.LogWarning("Unknown resource type: {ResourceType} for {ResourceName}", 
            resource.ResourceType, resource.Name);

        return new VolumeCostEstimate
        {
            VolumeId = resource.ResourceId,
            VolumeName = resource.Name,
            ResourceType = resource.ResourceType,
            EstimationMethod = "Unknown",
            TotalEstimatedCost = 0,
            ConfidenceLevel = 0,
            Warnings = new List<string> { $"Resource type '{resource.ResourceType}' is not supported for cost estimation" }
        };
    }

    /// <summary>
    /// Calculate cost estimates for multiple resources
    /// </summary>
    public async Task<List<VolumeCostEstimate>> CalculateBulkEstimatesAsync(List<DiscoveredResource> resources)
    {
        var estimates = new List<VolumeCostEstimate>();

        foreach (var resource in resources)
        {
            try
            {
                var estimate = await Calculate30DayCostEstimateAsync(resource);
                estimates.Add(estimate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to calculate estimate for {ResourceName}", resource.Name);
                estimates.Add(CreateUnknownResourceEstimate(resource));
            }
        }

        _logger.LogInformation("Calculated {Count} cost estimates with average confidence {AvgConfidence:F1}%",
            estimates.Count, estimates.Average(e => e.ConfidenceLevel));

        return estimates;
    }
}
