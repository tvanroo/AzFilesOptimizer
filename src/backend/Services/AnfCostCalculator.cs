using Microsoft.Extensions.Logging;
using AzFilesOptimizer.Backend.Models;

namespace AzFilesOptimizer.Backend.Services;

/// <summary>
/// Calculates accurate 30-day cost estimates for Azure NetApp Files volumes
/// Handles tier-based pricing, cool access, and throughput requirements
/// </summary>
public class AnfCostCalculator
{
    private readonly AzureRetailPricesClient _pricesClient;
    private readonly ILogger<AnfCostCalculator> _logger;

    public AnfCostCalculator(AzureRetailPricesClient pricesClient, ILogger<AnfCostCalculator> logger)
    {
        _pricesClient = pricesClient;
        _logger = logger;
    }

    /// <summary>
    /// Calculate 30-day cost estimate for an ANF volume
    /// </summary>
    public async Task<VolumeCostEstimate> CalculateAsync(AnfVolumeInfo volumeInfo)
    {
        var estimate = new VolumeCostEstimate
        {
            VolumeId = volumeInfo.VolumeId,
            VolumeName = volumeInfo.VolumeName,
            ResourceType = "ANF",
            Region = volumeInfo.Region,
            EstimationMethod = $"{volumeInfo.ServiceLevel} tier pricing" + (volumeInfo.CoolAccessEnabled ? " with cool access" : ""),
            PeriodDays = 30
        };

        try
        {
            var prices = await _pricesClient.GetAnfPricingAsync(volumeInfo.Region, volumeInfo.ServiceLevel);
            
            if (!prices.Any())
            {
                estimate.Warnings.Add($"No pricing data available for ANF {volumeInfo.ServiceLevel} in {volumeInfo.Region}");
                estimate.ConfidenceLevel = 10;
                return estimate;
            }

            // Calculate capacity cost
            await CalculateCapacityCostAsync(volumeInfo, estimate, prices);

            // Calculate cool access costs if enabled
            if (volumeInfo.CoolAccessEnabled)
            {
                await CalculateCoolAccessCostsAsync(volumeInfo, estimate, prices);
            }

            estimate.TotalEstimatedCost = estimate.CostComponents.Sum(c => c.EstimatedCost);
            estimate.ConfidenceLevel = DetermineConfidenceLevel(volumeInfo, estimate);
            
            _logger.LogInformation("Calculated 30-day ANF estimate for {VolumeName}: ${TotalCost} ({Method}, {Confidence}% confidence)",
                volumeInfo.VolumeName, estimate.TotalEstimatedCost, estimate.EstimationMethod, estimate.ConfidenceLevel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate ANF cost for {VolumeName}", volumeInfo.VolumeName);
            estimate.Warnings.Add($"Cost calculation failed: {ex.Message}");
            estimate.ConfidenceLevel = 10;
        }

        return estimate;
    }

    /// <summary>
    /// Calculate capacity costs for ANF
    /// </summary>
    private async Task CalculateCapacityCostAsync(AnfVolumeInfo volumeInfo, VolumeCostEstimate estimate, List<PriceItem> prices)
    {
        // ANF charges for provisioned capacity, not used capacity
        var capacityPrice = prices.FirstOrDefault(p => 
            p.MeterName.Contains("Provisioned", StringComparison.OrdinalIgnoreCase) ||
            p.MeterName.Contains("Capacity", StringComparison.OrdinalIgnoreCase));

        if (capacityPrice == null)
        {
            estimate.Warnings.Add("Capacity pricing not found for ANF");
            return;
        }

        var provisionedGb = volumeInfo.ProvisionedCapacityGb;
        
        // Apply minimum capacity based on volume type
        if (volumeInfo.CoolAccessEnabled && provisionedGb < 2400)
        {
            estimate.Notes.Add($"Minimum 2,400 GB required for cool access (provisioned: {provisionedGb:N0} GB)");
            provisionedGb = Math.Max(provisionedGb, 2400);
        }
        else if (provisionedGb < 50)
        {
            estimate.Notes.Add($"Minimum 50 GB required for regular volumes (provisioned: {provisionedGb:N0} GB)");
            provisionedGb = Math.Max(provisionedGb, 50);
        }

        var monthlyCost = provisionedGb * capacityPrice.RetailPrice;
        
        estimate.CostComponents.Add(new CostComponentEstimate
        {
            ComponentType = "storage",
            Description = $"Provisioned capacity ({provisionedGb:N0} GB) - {volumeInfo.ServiceLevel} tier",
            Quantity = provisionedGb,
            Unit = capacityPrice.UnitOfMeasure,
            UnitPrice = capacityPrice.RetailPrice,
            EstimatedCost = monthlyCost,
            DataSource = "Azure Retail Prices API"
        });

        // Add throughput information
        var throughputMibPerTib = GetThroughputPerTib(volumeInfo.ServiceLevel, volumeInfo.CoolAccessEnabled);
        var provisionedTib = provisionedGb / 1024.0;
        var includedThroughputMibS = provisionedTib * throughputMibPerTib;
        
        estimate.Notes.Add($"Included throughput: {includedThroughputMibS:N0} MiB/s ({throughputMibPerTib} MiB/s per TiB)");

        // Snapshots consume volume capacity (no separate charge)
        if (volumeInfo.SnapshotSizeGb > 0)
        {
            estimate.Notes.Add($"Snapshots ({volumeInfo.SnapshotSizeGb:N0} GB) consume volume capacity - no separate charge");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Calculate cool access costs (hot storage, cool storage, tiering, retrieval)
    /// </summary>
    private async Task CalculateCoolAccessCostsAsync(AnfVolumeInfo volumeInfo, VolumeCostEstimate estimate, List<PriceItem> prices)
    {
        if (volumeInfo.HotDataGb == 0 && volumeInfo.CoolDataGb == 0)
        {
            estimate.Warnings.Add("Cool access enabled but hot/cool data breakdown not available");
            estimate.Notes.Add("Assuming all data is hot tier");
            return;
        }

        // Cool data storage price (significantly lower than hot)
        var coolStoragePrice = prices.FirstOrDefault(p => 
            p.MeterName.Contains("Cool", StringComparison.OrdinalIgnoreCase) &&
            p.MeterName.Contains("Storage", StringComparison.OrdinalIgnoreCase));

        if (coolStoragePrice != null && volumeInfo.CoolDataGb > 0)
        {
            var coolCost = volumeInfo.CoolDataGb * coolStoragePrice.RetailPrice;
            
            estimate.CostComponents.Add(new CostComponentEstimate
            {
                ComponentType = "storage_cool",
                Description = $"Cool tier storage ({volumeInfo.CoolDataGb:N0} GB)",
                Quantity = volumeInfo.CoolDataGb,
                Unit = coolStoragePrice.UnitOfMeasure,
                UnitPrice = coolStoragePrice.RetailPrice,
                EstimatedCost = coolCost,
                DataSource = "Azure Retail Prices API"
            });
        }

        // Cool data tiering (hot → cool)
        var tieringPrice = prices.FirstOrDefault(p => 
            p.MeterName.Contains("Tiering", StringComparison.OrdinalIgnoreCase) ||
            (p.MeterName.Contains("Cool", StringComparison.OrdinalIgnoreCase) && 
             p.MeterName.Contains("Write", StringComparison.OrdinalIgnoreCase)));

        if (tieringPrice != null && volumeInfo.DataTieredToCoolGb > 0)
        {
            var tieringCost = volumeInfo.DataTieredToCoolGb * tieringPrice.RetailPrice;
            
            estimate.CostComponents.Add(new CostComponentEstimate
            {
                ComponentType = "cool_tiering",
                Description = $"Data tiering to cool ({volumeInfo.DataTieredToCoolGb:N0} GB)",
                Quantity = volumeInfo.DataTieredToCoolGb,
                Unit = tieringPrice.UnitOfMeasure,
                UnitPrice = tieringPrice.RetailPrice,
                EstimatedCost = tieringCost,
                DataSource = "Azure Retail Prices API"
            });
        }

        // Cool data retrieval (cool → hot)
        var retrievalPrice = prices.FirstOrDefault(p => 
            p.MeterName.Contains("Retrieval", StringComparison.OrdinalIgnoreCase) ||
            (p.MeterName.Contains("Cool", StringComparison.OrdinalIgnoreCase) && 
             p.MeterName.Contains("Read", StringComparison.OrdinalIgnoreCase)));

        if (retrievalPrice != null && volumeInfo.DataRetrievedFromCoolGb > 0)
        {
            var retrievalCost = volumeInfo.DataRetrievedFromCoolGb * retrievalPrice.RetailPrice;
            
            estimate.CostComponents.Add(new CostComponentEstimate
            {
                ComponentType = "cool_retrieval",
                Description = $"Data retrieval from cool ({volumeInfo.DataRetrievedFromCoolGb:N0} GB)",
                Quantity = volumeInfo.DataRetrievedFromCoolGb,
                Unit = retrievalPrice.UnitOfMeasure,
                UnitPrice = retrievalPrice.RetailPrice,
                EstimatedCost = retrievalCost,
                DataSource = "Azure Retail Prices API"
            });
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Get throughput per TiB based on service level and cool access
    /// </summary>
    private double GetThroughputPerTib(string serviceLevel, bool coolAccessEnabled)
    {
        return serviceLevel.ToUpperInvariant() switch
        {
            "STANDARD" => 16.0,
            "PREMIUM" => coolAccessEnabled ? 36.0 : 64.0, // Reduced when cool access enabled
            "ULTRA" => coolAccessEnabled ? 68.0 : 128.0,   // Reduced when cool access enabled
            "FLEXIBLE" => 128.0, // Base, can purchase additional
            _ => 16.0
        };
    }

    /// <summary>
    /// Determine confidence level based on available data
    /// </summary>
    private double DetermineConfidenceLevel(AnfVolumeInfo volumeInfo, VolumeCostEstimate estimate)
    {
        var confidence = 100.0;

        // ANF pricing is straightforward (capacity-based), so high confidence by default
        
        if (volumeInfo.CoolAccessEnabled && volumeInfo.HotDataGb == 0 && volumeInfo.CoolDataGb == 0)
        {
            confidence -= 30;
            estimate.Notes.Add("Cool access enabled but data distribution unknown");
        }

        if (volumeInfo.CoolAccessEnabled && (volumeInfo.DataTieredToCoolGb == 0 && volumeInfo.DataRetrievedFromCoolGb == 0))
        {
            confidence -= 15;
            estimate.Notes.Add("Cool access tiering/retrieval metrics unavailable");
        }

        if (!estimate.CostComponents.Any())
        {
            confidence = 10;
            estimate.Notes.Add("No pricing data available");
        }

        if (estimate.Warnings.Any())
        {
            confidence -= 10 * estimate.Warnings.Count;
        }

        return Math.Max(10, Math.Min(100, confidence));
    }
}

/// <summary>
/// Input data for ANF cost calculation
/// </summary>
public class AnfVolumeInfo
{
    public string VolumeId { get; set; } = string.Empty;
    public string VolumeName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string ServiceLevel { get; set; } = "Standard"; // Standard, Premium, Ultra, Flexible
    public double ProvisionedCapacityGb { get; set; }
    public double UsedCapacityGb { get; set; }
    public double SnapshotSizeGb { get; set; }
    public bool CoolAccessEnabled { get; set; } = false;
    public double HotDataGb { get; set; }
    public double CoolDataGb { get; set; }
    public double DataTieredToCoolGb { get; set; } // Data moved from hot to cool during the month
    public double DataRetrievedFromCoolGb { get; set; } // Data retrieved from cool during the month
    public bool IsLargeVolume { get; set; } = false;
    public double RequiredThroughputMibS { get; set; } // For Flexible tier
}
