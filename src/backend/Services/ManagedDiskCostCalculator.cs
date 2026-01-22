using Microsoft.Extensions.Logging;
using AzFilesOptimizer.Backend.Models;

namespace AzFilesOptimizer.Backend.Services;

/// <summary>
/// Calculates accurate 30-day cost estimates for Managed Disks
/// Prioritizes actual billing data, falls back to retail pricing
/// </summary>
public class ManagedDiskCostCalculator
{
    private readonly AzureRetailPricesClient _pricesClient;
    private readonly CostCollectionService _costCollectionService;
    private readonly ILogger<ManagedDiskCostCalculator> _logger;

    public ManagedDiskCostCalculator(
        AzureRetailPricesClient pricesClient,
        CostCollectionService costCollectionService,
        ILogger<ManagedDiskCostCalculator> logger)
    {
        _pricesClient = pricesClient;
        _costCollectionService = costCollectionService;
        _logger = logger;
    }

    /// <summary>
    /// Calculate 30-day cost estimate for a Managed Disk
    /// </summary>
    public async Task<VolumeCostEstimate> CalculateAsync(ManagedDiskVolumeInfo diskInfo)
    {
        var estimate = new VolumeCostEstimate
        {
            VolumeId = diskInfo.VolumeId,
            VolumeName = diskInfo.VolumeName,
            ResourceType = "ManagedDisk",
            Region = diskInfo.Region,
            PeriodDays = 30
        };

        try
        {
            // Try to get actual billing data first
            var actualCost = await TryGetActualBillingDataAsync(diskInfo);
            
            if (actualCost.HasValue)
            {
                await PopulateWithActualCostAsync(diskInfo, estimate, actualCost.Value);
            }
            else
            {
                await PopulateWithEstimatedCostAsync(diskInfo, estimate);
            }

            estimate.TotalEstimatedCost = estimate.CostComponents.Sum(c => c.EstimatedCost);
            estimate.ConfidenceLevel = DetermineConfidenceLevel(diskInfo, estimate);
            
            _logger.LogInformation("Calculated 30-day disk estimate for {DiskName}: ${TotalCost} ({Method}, {Confidence}% confidence)",
                diskInfo.VolumeName, estimate.TotalEstimatedCost, estimate.EstimationMethod, estimate.ConfidenceLevel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate cost for disk {DiskName}", diskInfo.VolumeName);
            estimate.Warnings.Add($"Cost calculation failed: {ex.Message}");
            estimate.ConfidenceLevel = 10;
        }

        return estimate;
    }

    /// <summary>
    /// Try to get actual billing data from Cost Management API
    /// </summary>
    private async Task<double?> TryGetActualBillingDataAsync(ManagedDiskVolumeInfo diskInfo)
    {
        try
        {
            if (string.IsNullOrEmpty(diskInfo.SubscriptionId) || string.IsNullOrEmpty(diskInfo.ResourceGroupName))
            {
                _logger.LogDebug("Missing subscription or resource group for {DiskName}, cannot fetch actual costs", 
                    diskInfo.VolumeName);
                return null;
            }

            // Try to get actual cost from the last 30 days using the existing GetManagedDiskCostAsync method
            // This requires creating a DiscoveredManagedDisk object from diskInfo
            var discoveredDisk = new DiscoveredManagedDisk
            {
                ResourceId = diskInfo.VolumeId,
                DiskName = diskInfo.VolumeName,
                Location = diskInfo.Region,
                DiskSizeGB = (long)diskInfo.DiskSizeGb,
                ManagedDiskType = diskInfo.DiskType,
                SubscriptionId = diskInfo.SubscriptionId,
                ResourceGroup = diskInfo.ResourceGroupName ?? string.Empty,
                TenantId = string.Empty // Not available in diskInfo
            };

            var endDate = DateTime.UtcNow.Date;
            var startDate = endDate.AddDays(-30);
            
            var costAnalysis = await _costCollectionService.GetManagedDiskCostAsync(
                discoveredDisk,
                startDate,
                endDate);

            if (costAnalysis != null && costAnalysis.TotalCostForPeriod > 0)
            {
                _logger.LogInformation("Found actual billing data for {DiskName}: ${ActualCost} for last 30 days",
                    diskInfo.VolumeName, costAnalysis.TotalCostForPeriod);
                return costAnalysis.TotalCostForPeriod;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve actual billing data for {DiskName}", diskInfo.VolumeName);
        }

        return null;
    }

    /// <summary>
    /// Populate estimate with actual billing data
    /// </summary>
    private async Task PopulateWithActualCostAsync(ManagedDiskVolumeInfo diskInfo, VolumeCostEstimate estimate, double actualCost)
    {
        estimate.EstimationMethod = "Actual billing data (Cost Management API)";
        
        estimate.CostComponents.Add(new CostComponentEstimate
        {
            ComponentType = "actual_cost",
            Description = $"{diskInfo.DiskType} disk ({diskInfo.DiskSizeGb} GB) - actual cost",
            Quantity = 1,
            Unit = "month",
            UnitPrice = actualCost,
            EstimatedCost = actualCost,
            DataSource = "Azure Cost Management API"
        });

        estimate.Notes.Add("Using actual billing data from the last 30 days");
        estimate.Notes.Add($"Disk: {diskInfo.DiskType}, Size: {diskInfo.DiskSizeGb} GB");

        await Task.CompletedTask;
    }

    /// <summary>
    /// Populate estimate with retail pricing (fallback)
    /// </summary>
    private async Task PopulateWithEstimatedCostAsync(ManagedDiskVolumeInfo diskInfo, VolumeCostEstimate estimate)
    {
        estimate.EstimationMethod = "Retail pricing estimate (no billing data available)";
        estimate.Warnings.Add("Actual billing data not available, using retail pricing");

        var prices = await _pricesClient.GetManagedDiskPricingAsync(diskInfo.Region, diskInfo.DiskType);
        
        if (!prices.Any())
        {
            estimate.Warnings.Add($"No pricing data available for {diskInfo.DiskType} in {diskInfo.Region}");
            return;
        }

        // Find the appropriate disk tier based on size
        var diskTier = GetDiskTier(diskInfo.DiskType, diskInfo.DiskSizeGb);
        var tierPrice = prices.FirstOrDefault(p => 
            p.MeterName.Contains(diskTier, StringComparison.OrdinalIgnoreCase) ||
            p.SkuName.Contains(diskTier, StringComparison.OrdinalIgnoreCase));

        if (tierPrice == null)
        {
            // Fallback: try to find any capacity-based pricing
            tierPrice = prices.FirstOrDefault(p => p.IsStorageCapacity);
        }

        if (tierPrice != null)
        {
            var monthlyCost = tierPrice.RetailPrice;
            
            // For v2 and Ultra disks, pricing is per GB + IOPS + throughput
            if (diskInfo.DiskType.Contains("v2", StringComparison.OrdinalIgnoreCase) ||
                diskInfo.DiskType.Contains("Ultra", StringComparison.OrdinalIgnoreCase))
            {
                monthlyCost = CalculateFlexibleDiskCost(diskInfo, prices);
            }

            estimate.CostComponents.Add(new CostComponentEstimate
            {
                ComponentType = "storage",
                Description = $"{diskInfo.DiskType} - {diskTier} ({diskInfo.DiskSizeGb} GB)",
                Quantity = diskInfo.DiskSizeGb,
                Unit = tierPrice.UnitOfMeasure,
                UnitPrice = tierPrice.RetailPrice,
                EstimatedCost = monthlyCost,
                DataSource = "Azure Retail Prices API"
            });

            estimate.Notes.Add($"Disk tier: {diskTier}");
        }
        else
        {
            estimate.Warnings.Add($"Could not find pricing for disk tier {diskTier}");
        }

        // Snapshots
        if (diskInfo.SnapshotSizeGb > 0)
        {
            var snapshotPrice = prices.FirstOrDefault(p => p.IsSnapshot);
            if (snapshotPrice != null)
            {
                var snapshotCost = diskInfo.SnapshotSizeGb * snapshotPrice.RetailPrice;
                
                estimate.CostComponents.Add(new CostComponentEstimate
                {
                    ComponentType = "snapshots",
                    Description = $"Disk snapshots ({diskInfo.SnapshotSizeGb} GB differential)",
                    Quantity = diskInfo.SnapshotSizeGb,
                    Unit = snapshotPrice.UnitOfMeasure,
                    UnitPrice = snapshotPrice.RetailPrice,
                    EstimatedCost = snapshotCost,
                    DataSource = "Azure Retail Prices API"
                });
            }
        }

        // Transactions (for Standard HDD/SSD)
        if (diskInfo.TransactionsPerMonth > 0 && 
            (diskInfo.DiskType.Contains("Standard", StringComparison.OrdinalIgnoreCase)))
        {
            var transactionPrice = prices.FirstOrDefault(p => p.IsTransaction);
            if (transactionPrice != null)
            {
                var transactionCost = (diskInfo.TransactionsPerMonth / 10000.0) * transactionPrice.RetailPrice;
                
                estimate.CostComponents.Add(new CostComponentEstimate
                {
                    ComponentType = "transactions",
                    Description = $"Disk transactions ({diskInfo.TransactionsPerMonth:N0})",
                    Quantity = diskInfo.TransactionsPerMonth / 10000.0,
                    Unit = "10K transactions",
                    UnitPrice = transactionPrice.RetailPrice,
                    EstimatedCost = transactionCost,
                    DataSource = "Azure Retail Prices API"
                });
            }
        }
    }

    /// <summary>
    /// Calculate cost for flexible pricing disks (Premium v2, Ultra)
    /// </summary>
    private double CalculateFlexibleDiskCost(ManagedDiskVolumeInfo diskInfo, List<PriceItem> prices)
    {
        double totalCost = 0;

        // Capacity cost
        var capacityPrice = prices.FirstOrDefault(p => 
            p.MeterName.Contains("Capacity", StringComparison.OrdinalIgnoreCase) ||
            p.MeterName.Contains("Provisioned", StringComparison.OrdinalIgnoreCase));
        
        if (capacityPrice != null)
        {
            totalCost += diskInfo.DiskSizeGb * capacityPrice.RetailPrice;
        }

        // IOPS cost (if specified)
        if (diskInfo.ProvisionedIops > 0)
        {
            var iopsPrice = prices.FirstOrDefault(p => 
                p.MeterName.Contains("IOPS", StringComparison.OrdinalIgnoreCase));
            
            if (iopsPrice != null)
            {
                totalCost += diskInfo.ProvisionedIops * iopsPrice.RetailPrice;
            }
        }

        // Throughput cost (if specified)
        if (diskInfo.ProvisionedThroughputMBps > 0)
        {
            var throughputPrice = prices.FirstOrDefault(p => 
                p.MeterName.Contains("Throughput", StringComparison.OrdinalIgnoreCase));
            
            if (throughputPrice != null)
            {
                totalCost += diskInfo.ProvisionedThroughputMBps * throughputPrice.RetailPrice;
            }
        }

        return totalCost;
    }

    /// <summary>
    /// Get disk tier based on type and size
    /// </summary>
    private string GetDiskTier(string diskType, int sizeGb)
    {
        if (diskType.Contains("Premium SSD", StringComparison.OrdinalIgnoreCase))
        {
            return sizeGb switch
            {
                <= 4 => "P1",
                <= 8 => "P2",
                <= 16 => "P3",
                <= 32 => "P4",
                <= 64 => "P6",
                <= 128 => "P10",
                <= 256 => "P15",
                <= 512 => "P20",
                <= 1024 => "P30",
                <= 2048 => "P40",
                <= 4096 => "P50",
                <= 8192 => "P60",
                <= 16384 => "P70",
                _ => "P80"
            };
        }
        else if (diskType.Contains("Standard SSD", StringComparison.OrdinalIgnoreCase))
        {
            return sizeGb switch
            {
                <= 4 => "E1",
                <= 8 => "E2",
                <= 16 => "E3",
                <= 32 => "E4",
                <= 64 => "E6",
                <= 128 => "E10",
                <= 256 => "E15",
                <= 512 => "E20",
                <= 1024 => "E30",
                <= 2048 => "E40",
                <= 4096 => "E50",
                <= 8192 => "E60",
                <= 16384 => "E70",
                _ => "E80"
            };
        }
        else if (diskType.Contains("Standard HDD", StringComparison.OrdinalIgnoreCase))
        {
            return sizeGb switch
            {
                <= 32 => "S4",
                <= 64 => "S6",
                <= 128 => "S10",
                <= 256 => "S15",
                <= 512 => "S20",
                <= 1024 => "S30",
                <= 2048 => "S40",
                <= 4096 => "S50",
                <= 8192 => "S60",
                <= 16384 => "S70",
                _ => "S80"
            };
        }

        return "Unknown";
    }

    /// <summary>
    /// Determine confidence level based on data source
    /// </summary>
    private double DetermineConfidenceLevel(ManagedDiskVolumeInfo diskInfo, VolumeCostEstimate estimate)
    {
        var confidence = 100.0;

        // Actual billing data = highest confidence
        if (estimate.EstimationMethod.Contains("Actual billing", StringComparison.OrdinalIgnoreCase))
        {
            confidence = 95;
            return confidence;
        }

        // Retail pricing = lower confidence
        confidence = 75;

        // Reduce confidence for flexible pricing without IOPS/throughput data
        if ((diskInfo.DiskType.Contains("v2", StringComparison.OrdinalIgnoreCase) ||
             diskInfo.DiskType.Contains("Ultra", StringComparison.OrdinalIgnoreCase)) &&
            (diskInfo.ProvisionedIops == 0 || diskInfo.ProvisionedThroughputMBps == 0))
        {
            confidence -= 20;
            estimate.Notes.Add("IOPS/throughput not specified for flexible pricing disk");
        }

        if (!estimate.CostComponents.Any())
        {
            confidence = 10;
        }

        if (estimate.Warnings.Any())
        {
            confidence -= 5 * estimate.Warnings.Count;
        }

        return Math.Max(10, Math.Min(100, confidence));
    }
}

/// <summary>
/// Input data for Managed Disk cost calculation
/// </summary>
public class ManagedDiskVolumeInfo
{
    public string VolumeId { get; set; } = string.Empty;
    public string VolumeName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string DiskType { get; set; } = "Premium SSD"; // Premium SSD, Standard SSD, Standard HDD, Premium SSD v2, Ultra Disk
    public int DiskSizeGb { get; set; }
    public double SnapshotSizeGb { get; set; }
    public long TransactionsPerMonth { get; set; }
    public int ProvisionedIops { get; set; } // For Premium v2 and Ultra
    public int ProvisionedThroughputMBps { get; set; } // For Premium v2 and Ultra
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceGroupName { get; set; } = string.Empty;
}
