using Microsoft.Extensions.Logging;
using AzFilesOptimizer.Backend.Models;

namespace AzFilesOptimizer.Backend.Services;

/// <summary>
/// Calculates accurate 30-day cost estimates for Azure Files shares
/// Handles both pay-as-you-go and provisioned (Premium) pricing models
/// </summary>
public class AzureFilesCostCalculator
{
    private readonly AzureRetailPricesClient _pricesClient;
    private readonly ILogger<AzureFilesCostCalculator> _logger;

    public AzureFilesCostCalculator(AzureRetailPricesClient pricesClient, ILogger<AzureFilesCostCalculator> logger)
    {
        _pricesClient = pricesClient;
        _logger = logger;
    }

    /// <summary>
    /// Calculate 30-day cost estimate for an Azure Files share
    /// </summary>
    public async Task<VolumeCostEstimate> CalculateAsync(AzureFilesVolumeInfo volumeInfo)
    {
        var estimate = new VolumeCostEstimate
        {
            VolumeId = volumeInfo.VolumeId,
            VolumeName = volumeInfo.VolumeName,
            ResourceType = "AzureFile",
            Region = volumeInfo.Region,
            EstimationMethod = volumeInfo.IsProvisioned ? "Provisioned Pricing" : "Pay-as-you-go Pricing",
            PeriodDays = 30
        };

        try
        {
            if (volumeInfo.IsProvisioned)
            {
                await CalculateProvisionedCostAsync(volumeInfo, estimate);
            }
            else
            {
                await CalculatePayAsYouGoCostAsync(volumeInfo, estimate);
            }

            estimate.TotalEstimatedCost = estimate.CostComponents.Sum(c => c.EstimatedCost);
            estimate.ConfidenceLevel = DetermineConfidenceLevel(volumeInfo, estimate);
            
            _logger.LogInformation("Calculated 30-day estimate for {VolumeName}: ${TotalCost} ({Method}, {Confidence}% confidence)",
                volumeInfo.VolumeName, estimate.TotalEstimatedCost, estimate.EstimationMethod, estimate.ConfidenceLevel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate cost for {VolumeName}", volumeInfo.VolumeName);
            estimate.Warnings.Add($"Cost calculation failed: {ex.Message}");
            estimate.ConfidenceLevel = 10;
        }

        return estimate;
    }

    /// <summary>
    /// Calculate cost for provisioned/Premium Files shares
    /// </summary>
    private async Task CalculateProvisionedCostAsync(AzureFilesVolumeInfo volumeInfo, VolumeCostEstimate estimate)
    {
        var prices = await _pricesClient.GetPremiumFilesPricingAsync(volumeInfo.Region, volumeInfo.Redundancy);
        
        if (!prices.Any())
        {
            estimate.Warnings.Add($"No pricing data available for Premium Files in {volumeInfo.Region}");
            return;
        }

        // Find capacity pricing
        var capacityPrice = prices.FirstOrDefault(p => p.IsStorageCapacity);
        if (capacityPrice != null)
        {
            var provisionedGb = Math.Max(volumeInfo.ProvisionedCapacityGb, 100); // Minimum 100 GB
            var monthlyCost = provisionedGb * capacityPrice.RetailPrice;
            
            estimate.CostComponents.Add(new CostComponentEstimate
            {
                ComponentType = "storage",
                Description = $"Provisioned capacity ({provisionedGb:N0} GB)",
                Quantity = provisionedGb,
                Unit = capacityPrice.UnitOfMeasure,
                UnitPrice = capacityPrice.RetailPrice,
                EstimatedCost = monthlyCost,
                DataSource = "Azure Retail Prices API"
            });
            
            estimate.Notes.Add($"Provisioned {provisionedGb:N0} GB includes transactions and performance");
        }

        // Snapshots (billed separately)
        if (volumeInfo.SnapshotSizeGb > 0)
        {
            var snapshotPrice = prices.FirstOrDefault(p => p.IsSnapshot);
            if (snapshotPrice != null)
            {
                var snapshotCost = volumeInfo.SnapshotSizeGb * snapshotPrice.RetailPrice;
                
                estimate.CostComponents.Add(new CostComponentEstimate
                {
                    ComponentType = "snapshots",
                    Description = $"Snapshots ({volumeInfo.SnapshotSizeGb:N2} GB differential)",
                    Quantity = volumeInfo.SnapshotSizeGb,
                    Unit = snapshotPrice.UnitOfMeasure,
                    UnitPrice = snapshotPrice.RetailPrice,
                    EstimatedCost = snapshotCost,
                    DataSource = "Azure Retail Prices API"
                });
            }
        }

        // Egress (if applicable)
        if (volumeInfo.EgressGbPerMonth > 0)
        {
            await AddEgressCostAsync(volumeInfo, estimate, prices);
        }
    }

    /// <summary>
    /// Calculate cost for pay-as-you-go (Hot/Cool/Transaction Optimized) shares
    /// </summary>
    private async Task CalculatePayAsYouGoCostAsync(AzureFilesVolumeInfo volumeInfo, VolumeCostEstimate estimate)
    {
        var prices = await _pricesClient.GetAzureFilesPricingAsync(volumeInfo.Region, volumeInfo.Tier, volumeInfo.Redundancy);
        
        if (!prices.Any())
        {
            estimate.Warnings.Add($"No pricing data available for {volumeInfo.Tier} tier in {volumeInfo.Region}");
            return;
        }

        // Storage capacity
        var capacityPrice = prices.FirstOrDefault(p => p.IsStorageCapacity);
        if (capacityPrice != null)
        {
            var usedGb = volumeInfo.UsedCapacityGb > 0 ? volumeInfo.UsedCapacityGb : volumeInfo.ProvisionedCapacityGb;
            var monthlyCost = usedGb * capacityPrice.RetailPrice;
            
            estimate.CostComponents.Add(new CostComponentEstimate
            {
                ComponentType = "storage",
                Description = $"Storage capacity ({usedGb:N2} GB)",
                Quantity = usedGb,
                Unit = capacityPrice.UnitOfMeasure,
                UnitPrice = capacityPrice.RetailPrice,
                EstimatedCost = monthlyCost,
                DataSource = "Azure Retail Prices API"
            });
        }

        // Transactions
        if (volumeInfo.TransactionsPerMonth > 0)
        {
            var transactionPrices = prices.Where(p => p.IsTransaction).ToList();
            if (transactionPrices.Any())
            {
                // Azure Files has different transaction types (write, read, list, etc.)
                // For estimation, use an average or the most common type
                var avgTransactionPrice = transactionPrices.Average(p => p.RetailPrice);
                var transactionCost = (volumeInfo.TransactionsPerMonth / 10000.0) * avgTransactionPrice;
                
                estimate.CostComponents.Add(new CostComponentEstimate
                {
                    ComponentType = "transactions",
                    Description = $"Transactions ({volumeInfo.TransactionsPerMonth:N0} operations)",
                    Quantity = volumeInfo.TransactionsPerMonth / 10000.0,
                    Unit = "10K transactions",
                    UnitPrice = avgTransactionPrice,
                    EstimatedCost = transactionCost,
                    DataSource = "Azure Retail Prices API (averaged)"
                });

                if (volumeInfo.TransactionsPerMonth == 0)
                {
                    estimate.Warnings.Add("Transaction count not available, estimate excludes transaction costs");
                }
            }
        }

        // Snapshots
        if (volumeInfo.SnapshotSizeGb > 0)
        {
            var snapshotPrice = prices.FirstOrDefault(p => p.IsSnapshot);
            if (snapshotPrice != null)
            {
                var snapshotCost = volumeInfo.SnapshotSizeGb * snapshotPrice.RetailPrice;
                
                estimate.CostComponents.Add(new CostComponentEstimate
                {
                    ComponentType = "snapshots",
                    Description = $"Snapshots ({volumeInfo.SnapshotSizeGb:N2} GB differential)",
                    Quantity = volumeInfo.SnapshotSizeGb,
                    Unit = snapshotPrice.UnitOfMeasure,
                    UnitPrice = snapshotPrice.RetailPrice,
                    EstimatedCost = snapshotCost,
                    DataSource = "Azure Retail Prices API"
                });
            }
        }

        // Egress
        if (volumeInfo.EgressGbPerMonth > 0)
        {
            await AddEgressCostAsync(volumeInfo, estimate, prices);
        }
    }

    /// <summary>
    /// Add egress/data transfer costs
    /// </summary>
    private Task AddEgressCostAsync(AzureFilesVolumeInfo volumeInfo, VolumeCostEstimate estimate, List<PriceItem> prices)
    {
        var egressPrice = prices.FirstOrDefault(p => p.IsDataTransfer);
        if (egressPrice != null && egressPrice.RetailPrice > 0)
        {
            var egressCost = volumeInfo.EgressGbPerMonth * egressPrice.RetailPrice;
            
            estimate.CostComponents.Add(new CostComponentEstimate
            {
                ComponentType = "egress",
                Description = $"Data egress ({volumeInfo.EgressGbPerMonth:N2} GB)",
                Quantity = volumeInfo.EgressGbPerMonth,
                Unit = egressPrice.UnitOfMeasure,
                UnitPrice = egressPrice.RetailPrice,
                EstimatedCost = egressCost,
                DataSource = "Azure Retail Prices API"
            });
        }
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Determine confidence level based on available data
    /// </summary>
    private double DetermineConfidenceLevel(AzureFilesVolumeInfo volumeInfo, VolumeCostEstimate estimate)
    {
        var confidence = 100.0;

        // Reduce confidence if we're missing key metrics
        if (volumeInfo.UsedCapacityGb == 0 && !volumeInfo.IsProvisioned)
        {
            confidence -= 20;
            estimate.Notes.Add("Using provisioned capacity instead of actual usage");
        }

        if (volumeInfo.TransactionsPerMonth == 0 && !volumeInfo.IsProvisioned)
        {
            confidence -= 25;
            estimate.Notes.Add("Transaction metrics unavailable, cost may be underestimated");
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
/// Input data for Azure Files cost calculation
/// </summary>
public class AzureFilesVolumeInfo
{
    public string VolumeId { get; set; } = string.Empty;
    public string VolumeName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Tier { get; set; } = "Hot"; // Hot, Cool, TransactionOptimized
    public string Redundancy { get; set; } = "LRS"; // LRS, ZRS, GRS, GZRS
    public bool IsProvisioned { get; set; } = false;
    public double ProvisionedCapacityGb { get; set; }
    public double UsedCapacityGb { get; set; }
    public double SnapshotSizeGb { get; set; }
    public long TransactionsPerMonth { get; set; }
    public double EgressGbPerMonth { get; set; }
}

/// <summary>
/// 30-day cost estimate for a volume
/// </summary>
public class VolumeCostEstimate
{
    public string VolumeId { get; set; } = string.Empty;
    public string VolumeName { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string EstimationMethod { get; set; } = string.Empty;
    public double TotalEstimatedCost { get; set; }
    public double ConfidenceLevel { get; set; }
    public int PeriodDays { get; set; } = 30;
    public List<CostComponentEstimate> CostComponents { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public DateTime EstimatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Individual cost component within an estimate
/// </summary>
public class CostComponentEstimate
{
    public string ComponentType { get; set; } = string.Empty; // storage, transactions, egress, snapshots
    public string Description { get; set; } = string.Empty;
    public double Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public double UnitPrice { get; set; }
    public double EstimatedCost { get; set; }
    public string DataSource { get; set; } = string.Empty; // e.g., "Azure Retail Prices API", "Actual billing data"
}
