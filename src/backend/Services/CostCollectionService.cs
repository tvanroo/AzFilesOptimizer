using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.CostManagement;
using Azure.ResourceManager.CostManagement.Models;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AzFilesOptimizer.Backend.Models;

namespace AzFilesOptimizer.Backend.Services;

public class CostCollectionService
{
    private readonly ILogger _logger;
    private readonly TokenCredential _credential;
    private readonly RetailPricingService _pricingService;
    private readonly MetricsCollectionService _metricsService;
    private readonly MetricsNormalizationService _normalizationService;
    private readonly Dictionary<string, RegionalPricing> _pricingCache = new();
    
    private const string CostManagementApiVersion = "2021-10-01";
    private const string PricingApiVersion = "2021-10-01";

    public CostCollectionService(
        ILogger logger, 
        TokenCredential credential, 
        RetailPricingService pricingService,
        MetricsCollectionService metricsService,
        MetricsNormalizationService normalizationService)
    {
        _logger = logger;
        _credential = credential;
        _pricingService = pricingService;
        _metricsService = metricsService;
        _normalizationService = normalizationService;
    }

    /// <summary>
    /// Get weekday-weighted transaction average from metrics
    /// </summary>
    private async Task<(double weekdayAvg, double weekendAvg, int sampleDays, double confidence)?> GetTransactionMetricsAsync(
        string shareResourceId,
        string shareName)
    {
        try
        {
            // Collect last 7-14 days of file share-level metrics to capture weekday/weekend patterns
            var (hasData, daysAvailable, metricsSummary) = await _metricsService.CollectFileShareMetricsAsync(
                shareResourceId,
                shareName);
            
            if (!hasData || string.IsNullOrEmpty(metricsSummary))
            {
                _logger.LogDebug("No transaction metrics available for file share {Share}", shareName);
                return null;
            }
            
            // Parse metrics JSON to extract transaction data
            var metricsTimeSeries = _normalizationService.ParseMetricsToTimeSeries(metricsSummary, "Transactions", "total");
            
            if (metricsTimeSeries == null || metricsTimeSeries.Count == 0)
            {
                _logger.LogDebug("No transaction data found in metrics for file share {Share}", shareName);
                return null;
            }
            
            // Get weekday/weekend breakdown
            var (weekdayAvg, weekendAvg, weekdayCount, weekendCount) = 
                _normalizationService.GetWeekdayWeekendAverages(metricsTimeSeries);
            
            int sampleDays = metricsTimeSeries.Count;
            bool hasSufficientWeekendData = weekendCount >= 2;
            
            // Calculate confidence
            double confidence = _normalizationService.CalculateConfidenceScore(
                sampleDays,
                hasCapacityChange: false,
                hasSufficientWeekendData,
                metricsTimeSeries.Count);
            
            _logger.LogInformation(
                "Transaction metrics for file share {Share}: Weekday={Weekday:F0}/day, Weekend={Weekend:F0}/day, SampleDays={Days}, Confidence={Confidence:F0}%",
                shareName, weekdayAvg, weekendAvg, sampleDays, confidence);
            
            return (weekdayAvg, weekendAvg, sampleDays, confidence);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error collecting transaction metrics for file share {Share}", shareName);
            return null;
        }
    }
    
    /// <summary>
    /// Get average egress per day from metrics
    /// </summary>
    private async Task<(double avgEgressGbPerDay, int sampleDays)?> GetEgressMetricsAsync(
        string shareResourceId,
        string shareName)
    {
        try
        {
            var (hasData, daysAvailable, metricsSummary) = await _metricsService.CollectFileShareMetricsAsync(
                shareResourceId,
                shareName);
            
            if (!hasData || string.IsNullOrEmpty(metricsSummary))
                return null;
            
            // Parse metrics JSON to extract egress data
            var egressTimeSeries = _normalizationService.ParseMetricsToTimeSeries(metricsSummary, "Egress", "total");
            
            if (egressTimeSeries == null || egressTimeSeries.Count == 0)
                return null;
            
            // Convert from bytes to GB and get average
            var egressGbTimeSeries = egressTimeSeries.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value / (1024.0 * 1024.0 * 1024.0));
            
            double avgEgressGbPerDay = egressGbTimeSeries.Values.Average();
            int sampleDays = egressGbTimeSeries.Count;
            
            _logger.LogInformation(
                "Egress metrics for file share {Share}: {AvgEgress:F2} GB/day over {Days} days",
                shareName, avgEgressGbPerDay, sampleDays);
            
            return (avgEgressGbPerDay, sampleDays);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error collecting egress metrics for file share {Share}", shareName);
            return null;
        }
    }
    
    /// <summary>
    /// Collect Azure Files storage costs for a specific storage account
    /// </summary>
    public async Task<VolumeCostAnalysis> GetAzureFilesCostAsync(
        DiscoveredAzureFileShare share,
        DateTime periodStart,
        DateTime periodEnd)
    {
        try
        {
            var analysis = new VolumeCostAnalysis
            {
                ResourceId = share.ResourceId,
                VolumeName = share.ShareName,
                ResourceType = "AzureFile",
                Region = share.Location,
                JobId = "",
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                CapacityGigabytes = (share.ShareQuotaGiB ?? 0) * 1024.0,
                UsedGigabytes = (share.ShareUsageBytes ?? 0) / (1024.0 * 1024.0 * 1024.0),
                AverageTransactionsPerDay = 0, // Will be replaced by actuals
                AverageEgressPerDayGb = 0, // Will be replaced by actuals
                SnapshotCount = share.SnapshotCount,
                TotalSnapshotSizeGb = (share.TotalSnapshotSizeBytes ?? 0) / (1024.0 * 1024.0 * 1024.0),
                BackupConfigured = share.BackupPolicyConfigured ?? false,
                CostCalculationInputs = new Dictionary<string, object>
                {
                    { "AccessTier", share.AccessTier ?? "null" },
                    { "IsProvisioned", share.IsProvisioned },
                    { "ProvisionedTier", share.ProvisionedTier ?? "null" },
                    { "RedundancyType", share.RedundancyType ?? "null" },
                    { "ShareQuotaGiB", share.ShareQuotaGiB ?? 0 },
                    { "ShareUsageBytes", share.ShareUsageBytes ?? 0 },
                    { "ProvisionedIops", share.ProvisionedIops ?? 0 },
                    { "ProvisionedBandwidthMiBps", share.ProvisionedBandwidthMiBps ?? 0 },
                    { "SnapshotCount", share.SnapshotCount ?? 0 }
                }
            };

            // Parse enums with null checks and defaults
            AzureFilesAccessTier? accessTier = null;
            if (!share.IsProvisioned && !string.IsNullOrEmpty(share.AccessTier))
            {
                // Map Premium to TransactionOptimized since Premium tier doesn't exist for pay-as-you-go
                var tierValue = share.AccessTier.Equals("Premium", StringComparison.OrdinalIgnoreCase) 
                    ? "TransactionOptimized" 
                    : share.AccessTier;
                if (Enum.TryParse<AzureFilesAccessTier>(tierValue, true, out var parsedTier))
                {
                    accessTier = parsedTier;
                }
            }
            
            AzureFilesProvisionedTier? provisionedTier = null;
            if (share.IsProvisioned && !string.IsNullOrEmpty(share.ProvisionedTier))
            {
                if (Enum.TryParse<AzureFilesProvisionedTier>(share.ProvisionedTier, true, out var parsedProvTier))
                {
                    provisionedTier = parsedProvTier;
                }
            }
            
            var redundancy = StorageRedundancy.LRS; // Default
            if (!string.IsNullOrEmpty(share.RedundancyType))
            {
                Enum.TryParse<StorageRedundancy>(share.RedundancyType, true, out redundancy);
            }

            var pricing = await _pricingService.GetAzureFilesPricingAsync(
                share.Location,
                accessTier,
                provisionedTier,
                redundancy
            );

            if (pricing == null)
            {
                _logger.LogWarning("Could not retrieve pricing for Azure File Share {ShareName}", share.ShareName);
                analysis.Warnings.Add("Failed to retrieve retail pricing data");
                return analysis;
            }
            
            // Store pricing data for debugging
            analysis.RetailPricingData = new Dictionary<string, object>
            {
                { "AccessTier", accessTier?.ToString() ?? "null" },
                { "ProvisionedTier", provisionedTier?.ToString() ?? "null" },
                { "Redundancy", redundancy.ToString() },
                { "StoragePricePerGbMonth", pricing.StoragePricePerGbMonth },
                { "ProvisionedV1StoragePricePerGibMonth", pricing.ProvisionedV1StoragePricePerGibMonth },
                { "ProvisionedStoragePricePerGibHour", pricing.ProvisionedStoragePricePerGibHour },
                { "ProvisionedIOPSPricePerHour", pricing.ProvisionedIOPSPricePerHour },
                { "ProvisionedThroughputPricePerMiBPerSecPerHour", pricing.ProvisionedThroughputPricePerMiBPerSecPerHour },
                { "SnapshotPricePerGbMonth", pricing.SnapshotPricePerGbMonth },
                { "EgressPricePerGb", pricing.EgressPricePerGb },
                { "WriteOperationsPricePer10K", pricing.WriteOperationsPricePer10K },
                { "ReadOperationsPricePer10K", pricing.ReadOperationsPricePer10K }
            };
            
            var days = periodEnd.Subtract(periodStart).Days;
            if (days == 0) days = 1;

            if (share.IsProvisioned)
            {
                // Provisioned cost calculation
                var provisionedCost = new StorageCostComponent();
                if (share.ProvisionedTier == "ProvisionedV1")
                {
                    provisionedCost = StorageCostComponent.ForCapacity(
                        share.ResourceId,
                        share.Location,
                        analysis.CapacityGigabytes,
                        pricing.ProvisionedV1StoragePricePerGibMonth,
                        periodStart,
                        periodEnd,
                        share.ProvisionedTier
                    );
                } else {
                    provisionedCost = StorageCostComponent.ForCapacity(
                        share.ResourceId,
                        share.Location,
                        analysis.CapacityGigabytes,
                        pricing.ProvisionedStoragePricePerGibHour * 24 * days,
                        periodStart,
                        periodEnd,
                        share.ProvisionedTier
                    );
                }
                analysis.AddCostComponent(provisionedCost);

                if(share.ProvisionedIops > 0)
                {
                    var iopsCost = new StorageCostComponent
                    {
                        ComponentType = "IOPS",
                        ResourceId = share.ResourceId,
                        Region = share.Location,
                        Quantity = share.ProvisionedIops.Value,
                        UnitPrice = pricing.ProvisionedIOPSPricePerHour,
                        CostForPeriod = share.ProvisionedIops.Value * pricing.ProvisionedIOPSPricePerHour * 24 * days,
                        PeriodStart = periodStart,
                        PeriodEnd = periodEnd
                    };
                    analysis.AddCostComponent(iopsCost);
                }

                if(share.ProvisionedBandwidthMiBps > 0)
                {
                    var throughputCost = new StorageCostComponent
                    {
                        ComponentType = "Throughput",
                        ResourceId = share.ResourceId,
                        Region = share.Location,
                        Quantity = share.ProvisionedBandwidthMiBps.Value,
                        UnitPrice = pricing.ProvisionedThroughputPricePerMiBPerSecPerHour,
                        CostForPeriod = share.ProvisionedBandwidthMiBps.Value * pricing.ProvisionedThroughputPricePerMiBPerSecPerHour * 24 * days,
                        PeriodStart = periodStart,
                        PeriodEnd = periodEnd
                    };
                    analysis.AddCostComponent(throughputCost);
                }

            } else {
                // Pay-as-you-go cost calculation
                var storageCost = StorageCostComponent.ForCapacity(
                    share.ResourceId,
                    share.Location,
                    analysis.UsedGigabytes,
                    pricing.StoragePricePerGbMonth,
                    periodStart,
                    periodEnd,
                    share.AccessTier
                );
                analysis.AddCostComponent(storageCost);
            }

            if(share.SnapshotCount > 0)
            {
                var snapshotCost = StorageCostComponent.ForSnapshots(
                    share.ResourceId,
                    share.Location,
                    analysis.TotalSnapshotSizeGb.Value,
                    pricing.SnapshotPricePerGbMonth,
                    periodStart,
                    periodEnd
                );
                analysis.AddCostComponent(snapshotCost);
            }
            
            // Add transaction costs (for pay-as-you-go tiers only)
            if (!share.IsProvisioned && accessTier.HasValue)
            {
                // Use file share resource ID for share-level metrics
                var transactionMetrics = await GetTransactionMetricsAsync(
                    share.ResourceId,
                    share.ShareName);
                    
                    if (transactionMetrics.HasValue)
                    {
                        var (weekdayAvg, weekendAvg, sampleDays, confidence) = transactionMetrics.Value;
                        
                        // Store in analysis for debugging
                        analysis.WeekdayAvgTransactionsPerDay = weekdayAvg;
                        analysis.WeekendAvgTransactionsPerDay = weekendAvg;
                        analysis.MetricsSampleDays = sampleDays;
                        analysis.ProjectionConfidenceScore = confidence;
                        
                        // Project monthly transactions: 22 weekdays + 8 weekend days
                        double monthlyTransactions = (weekdayAvg * 22) + (weekendAvg * 8);
                        double transactionsPer10K = monthlyTransactions / 10000.0;
                        
                        // Use appropriate transaction pricing based on tier
                        double transactionPrice = accessTier.Value switch
                        {
                            AzureFilesAccessTier.TransactionOptimized => pricing.WriteOperationsPricePer10K,
                            AzureFilesAccessTier.Hot => pricing.WriteOperationsPricePer10K,
                            AzureFilesAccessTier.Cool => pricing.WriteOperationsPricePer10K,
                            _ => pricing.WriteOperationsPricePer10K
                        };
                        
                        if (transactionPrice > 0 && transactionsPer10K > 0)
                        {
                            var transactionCost = new StorageCostComponent
                            {
                                ComponentType = "transactions",
                                ResourceId = share.ResourceId,
                                Region = share.Location,
                                Quantity = transactionsPer10K,
                                UnitPrice = transactionPrice,
                                CostForPeriod = transactionsPer10K * transactionPrice,
                                Unit = "per 10k transactions",
                                PeriodStart = periodStart,
                                PeriodEnd = periodEnd,
                                IsEstimated = true,
                                Notes = $"Based on {sampleDays} days of metrics. Weekday: {weekdayAvg:F0}/day, Weekend: {weekendAvg:F0}/day"
                            };
                            analysis.AddCostComponent(transactionCost);
                            
                            _logger.LogInformation(
                                "Added transaction costs for {Share}: ${Cost:F2}/month (Confidence: {Confidence:F0}%)",
                                share.ShareName, transactionCost.CostForPeriod, confidence);
                        }
                        
                        if (sampleDays < 7)
                        {
                            analysis.Warnings.Add($"Transaction costs based on only {sampleDays} days of data. Confidence: {confidence:F0}%");
                        }
                    }
                else
                {
                    analysis.Warnings.Add("No transaction metrics available. Transaction costs not included.");
                    _logger.LogDebug("No transaction metrics for {Share}, skipping transaction costs", share.ShareName);
                }
                
                // Add egress costs
                var egressMetrics = await GetEgressMetricsAsync(share.ResourceId, share.ShareName);
                    if (egressMetrics.HasValue && pricing.EgressPricePerGb > 0)
                    {
                        var (avgEgressGbPerDay, sampleDays) = egressMetrics.Value;
                        double monthlyEgressGb = avgEgressGbPerDay * 30;
                        
                        analysis.AvgEgressGbPerDay = avgEgressGbPerDay;
                        
                        if (monthlyEgressGb > 0)
                        {
                            var egressCost = StorageCostComponent.ForEgress(
                                share.ResourceId,
                                share.Location,
                                monthlyEgressGb,
                                pricing.EgressPricePerGb,
                                periodStart,
                                periodEnd
                            );
                            egressCost.Notes = $"Based on {sampleDays} days of metrics: {avgEgressGbPerDay:F2} GB/day";
                            analysis.AddCostComponent(egressCost);
                            
                            _logger.LogInformation(
                                "Added egress costs for {Share}: ${Cost:F2}/month ({EgressGb:F2} GB/month)",
                                share.ShareName, egressCost.CostForPeriod, monthlyEgressGb);
                        }
                    }
                else if (pricing.EgressPricePerGb > 0)
                {
                    _logger.LogDebug("No egress metrics for {Share}, skipping egress costs", share.ShareName);
                }
            }

            // Try to replace retail estimate with actual billed cost if available
            await TryApplyActualCostAsync(analysis, periodStart, periodEnd);

            _logger.LogInformation(
                "Calculated Azure Files costs for {Share}: ${Cost} over {Days} days (Source: {Source})",
                share.ShareName,
                analysis.TotalCostForPeriod,
                days,
                analysis.CostComponents.All(c => !c.IsEstimated) ? "Actual" : "RetailEstimate");
            
            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating Azure Files costs for {Share}", share.ShareName);
            throw;
        }
    }

    /// <summary>
    /// Get cool tier breakdown for ANF volumes with cool access enabled
    /// </summary>
    private async Task<(double coolTierGb, double hotTierGb, double coolTierPercent)?> GetAnfCoolTierBreakdownAsync(
        string volumeResourceId,
        string volumeName,
        double provisionedSizeGb)
    {
        try
        {
            var (hasData, daysAvailable, metricsSummary) = await _metricsService.CollectAnfVolumeMetricsAsync(
                volumeResourceId,
                volumeName);
            
            if (!hasData || string.IsNullOrEmpty(metricsSummary))
                return null;
            
            // Parse metrics JSON to extract cool tier data
            var coolTierTimeSeries = _normalizationService.ParseMetricsToTimeSeries(metricsSummary, "CoolTierDataUsed", "average");
            
            if (coolTierTimeSeries == null || coolTierTimeSeries.Count == 0)
                return null;
            
            // Get average cool tier usage and convert from bytes to GB
            double avgCoolTierBytes = coolTierTimeSeries.Values.Average();
            double coolTierGb = avgCoolTierBytes / (1024.0 * 1024.0 * 1024.0);
            double hotTierGb = Math.Max(0, provisionedSizeGb - coolTierGb);
            double coolTierPercent = provisionedSizeGb > 0 ? (coolTierGb / provisionedSizeGb) * 100 : 0;
            
            _logger.LogInformation(
                "Cool tier breakdown for {Volume}: Hot={HotGb:F2} GB, Cool={CoolGb:F2} GB ({CoolPercent:F1}%)",
                volumeName, hotTierGb, coolTierGb, coolTierPercent);
            
            return (coolTierGb, hotTierGb, coolTierPercent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting cool tier breakdown for {Volume}", volumeName);
            return null;
        }
    }
    
    /// <summary>
    /// Get flexible throughput usage for ANF Flexible service level
    /// </summary>
    private async Task<(double avgThroughputMiBps, double additionalThroughput)?> GetAnfFlexibleThroughputAsync(
        string volumeResourceId,
        string volumeName)
    {
        try
        {
            var (hasData, daysAvailable, metricsSummary) = await _metricsService.CollectAnfVolumeMetricsAsync(
                volumeResourceId,
                volumeName);
            
            if (!hasData || string.IsNullOrEmpty(metricsSummary))
                return null;
            
            // Parse metrics JSON to extract throughput data
            var throughputTimeSeries = _normalizationService.ParseMetricsToTimeSeries(metricsSummary, "AverageThroughput", "average");
            
            if (throughputTimeSeries == null || throughputTimeSeries.Count == 0)
                return null;
            
            // Get average throughput in MiB/s
            double avgThroughputMiBps = throughputTimeSeries.Values.Average();
            
            // Flexible tier includes 128 MiB/s baseline for free
            const double baselineThroughput = 128.0;
            double additionalThroughput = Math.Max(0, avgThroughputMiBps - baselineThroughput);
            
            _logger.LogInformation(
                "Flexible throughput for {Volume}: Avg={AvgThroughput:F2} MiB/s, Additional={Additional:F2} MiB/s (above baseline)",
                volumeName, avgThroughputMiBps, additionalThroughput);
            
            return (avgThroughputMiBps, additionalThroughput);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting flexible throughput for {Volume}", volumeName);
            return null;
        }
    }
    
    /// <summary>
    /// Get steady-state capacity for ANF volume
    /// </summary>
    private async Task<(double steadyStateGb, bool hasChanged, DateTime? changeDate, int daysUsed)?> GetAnfSteadyStateCapacityAsync(
        string volumeResourceId,
        string volumeName,
        double currentProvisionedGb)
    {
        try
        {
            var (hasData, daysAvailable, metricsSummary) = await _metricsService.CollectAnfVolumeMetricsAsync(
                volumeResourceId,
                volumeName);
            
            if (!hasData || string.IsNullOrEmpty(metricsSummary))
                return null;
            
            // Parse metrics JSON to extract volume logical size (provisioned capacity over time)
            var capacityTimeSeries = _normalizationService.ParseMetricsToTimeSeries(metricsSummary, "VolumeLogicalSize", "average");
            
            if (capacityTimeSeries == null || capacityTimeSeries.Count == 0)
                return (currentProvisionedGb, false, null, 0);
            
            // Convert from bytes to GB
            var capacityGbTimeSeries = capacityTimeSeries.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value / (1024.0 * 1024.0 * 1024.0));
            
            // Detect steady state
            var (steadyStateGb, hasChanged, changeDate, daysUsed) = _normalizationService.GetSteadyStateValue(
                capacityGbTimeSeries,
                lookbackDays: 7,
                changeThreshold: 0.20);
            
            if (hasChanged)
            {
                _logger.LogInformation(
                    "Capacity change detected for {Volume}: Using last {Days} days for steady state ({SteadyState:F2} GB)",
                    volumeName, daysUsed, steadyStateGb);
            }
            
            return (steadyStateGb, hasChanged, changeDate, daysUsed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error detecting capacity changes for {Volume}", volumeName);
            return (currentProvisionedGb, false, null, 0);
        }
    }
    
    /// <summary>
    /// Collect Azure NetApp Files costs for a volume
    /// </summary>
    public async Task<VolumeCostAnalysis> GetAnfVolumeCostAsync(
        DiscoveredAnfVolume volume,
        DateTime periodStart,
        DateTime periodEnd)
    {
        try
        {
            var analysis = new VolumeCostAnalysis
            {
                ResourceId = volume.ResourceId,
                VolumeName = volume.VolumeName,
                ResourceType = "ANF",
                Region = volume.Location,
                StorageAccountOrPoolName = volume.CapacityPoolName,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                CapacityGigabytes = volume.ProvisionedSizeBytes / (1024.0 * 1024.0 * 1024.0),
                UsedGigabytes = 0, // ANF doesn't expose used bytes, billed on provisioned
                SnapshotCount = volume.SnapshotCount ?? 0,
                TotalSnapshotSizeGb = (volume.TotalSnapshotSizeBytes ?? 0) / (1024.0 * 1024.0 * 1024.0),
                BackupConfigured = volume.BackupPolicyConfigured ?? false,
                CostCalculationInputs = new Dictionary<string, object>
                {
                    { "CapacityPoolServiceLevel", volume.CapacityPoolServiceLevel ?? "null" },
                    { "ProvisionedSizeBytes", volume.ProvisionedSizeBytes },
                    { "SnapshotCount", volume.SnapshotCount ?? 0 },
                    { "BackupConfigured", volume.BackupPolicyConfigured ?? false }
                }
            };

            // Parse service level with null check
            var serviceLevel = AnfServiceLevel.Standard; // Default
            if (!string.IsNullOrEmpty(volume.CapacityPoolServiceLevel))
            {
                if (!Enum.TryParse<AnfServiceLevel>(volume.CapacityPoolServiceLevel, true, out serviceLevel))
                {
                    _logger.LogWarning("Unknown ANF service level '{ServiceLevel}' for volume {VolumeName}, using Standard", 
                        volume.CapacityPoolServiceLevel, volume.VolumeName);
                    serviceLevel = AnfServiceLevel.Standard;
                }
            }

            var pricing = await _pricingService.GetAnfPricingAsync(
                volume.Location,
                serviceLevel
            );

            if (pricing == null)
            {
                _logger.LogWarning("Could not retrieve pricing for ANF volume {VolumeName} in region {Region} with service level {ServiceLevel}", 
                    volume.VolumeName, volume.Location, serviceLevel);
                analysis.Warnings.Add($"Failed to retrieve retail pricing data for region {volume.Location} and service level {serviceLevel}");
                return analysis;
            }
            
            // Log pricing details for debugging
            _logger.LogInformation(
                "Retrieved ANF pricing for {Volume} | Region: {Region} | ServiceLevel: {ServiceLevel} | CapacityPrice/GiB/hr: ${CapacityPrice} | CapacityPrice/TiB/mo: ${CapacityPriceMonth}",
                volume.VolumeName, volume.Location, serviceLevel, pricing.CapacityPricePerGibHour, pricing.CapacityPricePerTibMonth);
            
            if (pricing.CapacityPricePerGibHour <= 0)
            {
                _logger.LogError(
                    "Zero or negative capacity pricing returned for ANF volume {VolumeName} in region {Region} with service level {ServiceLevel}. This will result in $0 cost calculation.",
                    volume.VolumeName, volume.Location, serviceLevel);
                analysis.Warnings.Add($"Invalid pricing data: CapacityPricePerGibHour is {pricing.CapacityPricePerGibHour}");
            }
            
            // Store pricing data for debugging
            analysis.RetailPricingData = new Dictionary<string, object>
            {
                { "ServiceLevel", serviceLevel.ToString() },
                { "CapacityPricePerGibHour", pricing.CapacityPricePerGibHour },
                { "SnapshotPricePerGibHour", pricing.SnapshotPricePerGibHour }
            };
            
            var days = periodEnd.Subtract(periodStart).Days;
            if (days == 0) days = 1;
            
            // Check for steady-state capacity
            var steadyStateResult = await GetAnfSteadyStateCapacityAsync(
                volume.ResourceId,
                volume.VolumeName,
                analysis.CapacityGigabytes);
            
            double capacityForCosting = analysis.CapacityGigabytes;
            if (steadyStateResult.HasValue)
            {
                var (steadyStateGb, hasChanged, changeDate, daysUsed) = steadyStateResult.Value;
                if (hasChanged && daysUsed > 0)
                {
                    capacityForCosting = steadyStateGb;
                    analysis.CapacityChangedDuringPeriod = true;
                    analysis.LastCapacityChangeDate = changeDate;
                    analysis.MetricsSampleDays = daysUsed;
                    analysis.Warnings.Add($"Capacity changed during period. Using steady-state value from last {daysUsed} days: {steadyStateGb:F2} GB");
                    
                    _logger.LogInformation(
                        "Using steady-state capacity for {Volume}: {SteadyState:F2} GB (changed from {Current:F2} GB)",
                        volume.VolumeName, steadyStateGb, analysis.CapacityGigabytes);
                }
            }

            // Check for cool tier breakdown (if cool access enabled)
            bool hasCoolTier = volume.CoolAccessEnabled ?? false;
            if (hasCoolTier)
            {
                var coolTierResult = await GetAnfCoolTierBreakdownAsync(
                    volume.ResourceId,
                    volume.VolumeName,
                    capacityForCosting);
                
                if (coolTierResult.HasValue && pricing.CoolTierStoragePricePerGibMonth > 0)
                {
                    var (coolTierGb, hotTierGb, coolTierPercent) = coolTierResult.Value;
                    
                    analysis.CoolTierUsagePercent = coolTierPercent;
                    
                    // Hot tier storage cost
                    if (hotTierGb > 0)
                    {
                        var hotStorageCost = new StorageCostComponent
                        {
                            ComponentType = "hot-storage",
                            ResourceId = volume.ResourceId,
                            Region = volume.Location,
                            Quantity = hotTierGb,
                            UnitPrice = pricing.CapacityPricePerGibHour * 730,
                            CostForPeriod = hotTierGb * pricing.CapacityPricePerGibHour * 730,
                            Unit = "GiB/month",
                            PeriodStart = periodStart,
                            PeriodEnd = periodEnd,
                            IsEstimated = true,
                            Notes = $"Hot tier capacity: {hotTierGb:F2} GB ({100 - coolTierPercent:F1}% of total)",
                            SkuName = $"ANF-{volume.CapacityPoolServiceLevel}-Hot"
                        };
                        analysis.AddCostComponent(hotStorageCost);
                    }
                    
                    // Cool tier storage cost
                    if (coolTierGb > 0)
                    {
                        var coolStorageCost = new StorageCostComponent
                        {
                            ComponentType = "cool-storage",
                            ResourceId = volume.ResourceId,
                            Region = volume.Location,
                            Quantity = coolTierGb,
                            UnitPrice = pricing.CoolTierStoragePricePerGibMonth,
                            CostForPeriod = coolTierGb * pricing.CoolTierStoragePricePerGibMonth,
                            Unit = "GiB/month",
                            PeriodStart = periodStart,
                            PeriodEnd = periodEnd,
                            IsEstimated = true,
                            Notes = $"Cool tier capacity: {coolTierGb:F2} GB ({coolTierPercent:F1}% of total)",
                            SkuName = $"ANF-{volume.CapacityPoolServiceLevel}-Cool"
                        };
                        analysis.AddCostComponent(coolStorageCost);
                    }
                    
                    // Cool tier data transfer costs (estimated)
                    if (pricing.CoolTierDataTransferPricePerGib > 0)
                    {
                        // Estimate monthly tier-in/tier-out as 10% of cool tier data
                        double estimatedTransferGb = coolTierGb * 0.10;
                        if (estimatedTransferGb > 0)
                        {
                            var coolTransferCost = new StorageCostComponent
                            {
                                ComponentType = "cool-tier-transfer",
                                ResourceId = volume.ResourceId,
                                Region = volume.Location,
                                Quantity = estimatedTransferGb,
                                UnitPrice = pricing.CoolTierDataTransferPricePerGib,
                                CostForPeriod = estimatedTransferGb * pricing.CoolTierDataTransferPricePerGib,
                                Unit = "GiB",
                                PeriodStart = periodStart,
                                PeriodEnd = periodEnd,
                                IsEstimated = true,
                                Notes = $"Estimated cool tier data transfer (tier-in + retrieval): {estimatedTransferGb:F2} GB"
                            };
                            analysis.AddCostComponent(coolTransferCost);
                        }
                    }
                    
                    _logger.LogInformation(
                        "Added cool tier costs for {Volume}: Hot={HotGb:F2} GB, Cool={CoolGb:F2} GB ({CoolPercent:F1}%)",
                        volume.VolumeName, hotTierGb, coolTierGb, coolTierPercent);
                }
                else
                {
                    // Cool access enabled but no metrics - use standard capacity cost
                    analysis.Warnings.Add("Cool access enabled but no cool tier metrics available. Using standard capacity pricing.");
                    var storageCost = StorageCostComponent.ForCapacity(
                        volume.ResourceId,
                        volume.Location,
                        capacityForCosting,
                        pricing.CapacityPricePerGibHour * 730,
                        periodStart,
                        periodEnd,
                        $"ANF-{volume.CapacityPoolServiceLevel}"
                    );
                    analysis.AddCostComponent(storageCost);
                }
            }
            else
            {
                // Standard capacity cost (no cool tier)
                _logger.LogInformation(
                    "Calculating standard capacity cost for {Volume} | Capacity: {CapacityGb:F2} GB | Price/GiB/month: ${PricePerGib} | Expected cost: ${ExpectedCost:F2}",
                    volume.VolumeName, 
                    capacityForCosting, 
                    pricing.CapacityPricePerGibHour * 730,
                    capacityForCosting * pricing.CapacityPricePerGibHour * 730);
                
                var storageCost = StorageCostComponent.ForCapacity(
                    volume.ResourceId,
                    volume.Location,
                    capacityForCosting,
                    pricing.CapacityPricePerGibHour * 730, // Convert hourly to monthly
                    periodStart,
                    periodEnd,
                    $"ANF-{volume.CapacityPoolServiceLevel}"
                );
                analysis.AddCostComponent(storageCost);
            }
            
            // Flexible throughput costs (Flexible service level only)
            if (serviceLevel == AnfServiceLevel.Flexible && pricing.FlexibleThroughputPricePerMiBSecHour > 0)
            {
                var flexibleThroughputResult = await GetAnfFlexibleThroughputAsync(
                    volume.ResourceId,
                    volume.VolumeName);
                
                if (flexibleThroughputResult.HasValue)
                {
                    var (avgThroughputMiBps, additionalThroughput) = flexibleThroughputResult.Value;
                    
                    if (additionalThroughput > 0)
                    {
                        var throughputCost = new StorageCostComponent
                        {
                            ComponentType = "flexible-throughput",
                            ResourceId = volume.ResourceId,
                            Region = volume.Location,
                            Quantity = additionalThroughput,
                            UnitPrice = pricing.FlexibleThroughputPricePerMiBSecHour * 730, // Convert to monthly
                            CostForPeriod = additionalThroughput * pricing.FlexibleThroughputPricePerMiBSecHour * 730,
                            Unit = "MiB/s/month",
                            PeriodStart = periodStart,
                            PeriodEnd = periodEnd,
                            IsEstimated = true,
                            Notes = $"Additional throughput above 128 MiB/s baseline: {additionalThroughput:F2} MiB/s (avg: {avgThroughputMiBps:F2} MiB/s)"
                        };
                        analysis.AddCostComponent(throughputCost);
                        
                        _logger.LogInformation(
                            "Added flexible throughput costs for {Volume}: ${Cost:F2}/month ({Additional:F2} MiB/s above baseline)",
                            volume.VolumeName, throughputCost.CostForPeriod, additionalThroughput);
                    }
                }
                else
                {
                    _logger.LogDebug("No throughput metrics for {Volume}, skipping flexible throughput costs", volume.VolumeName);
                }
            }

            // Snapshot costs (billed by used capacity)
            if (analysis.TotalSnapshotSizeGb > 0)
            {
                var snapshotCost = StorageCostComponent.ForSnapshots(
                    volume.ResourceId,
                    volume.Location,
                    analysis.TotalSnapshotSizeGb.Value,
                    pricing.SnapshotPricePerGibHour * 730, // Convert hourly to monthly
                    periodStart,
                    periodEnd
                );
                analysis.AddCostComponent(snapshotCost);
            }

            // Backup costs (if configured, typically included in volume cost for ANF)
            if (analysis.BackupConfigured && analysis.CapacityGigabytes > 0)
            {
                var backupCost = StorageCostComponent.ForBackup(
                    volume.ResourceId,
                    volume.Location,
                    analysis.CapacityGigabytes * 0.15,
                    0.05, // Default backup price
                    periodStart,
                    periodEnd
                );
                analysis.AddCostComponent(backupCost);
            }

            // Try to replace retail estimate with actual billed cost if available
            await TryApplyActualCostAsync(analysis, periodStart, periodEnd);

            _logger.LogInformation("Calculated ANF costs for {Volume}: ${Cost} over {Days} days (Source: {Source})",
                volume.VolumeName,
                analysis.TotalCostForPeriod,
                days,
                analysis.CostComponents.All(c => !c.IsEstimated) ? "Actual" : "RetailEstimate");
            
            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating ANF costs for {Volume}", volume.VolumeName);
            throw;
        }
    }

    /// <summary>
    /// Collect Managed Disk costs
    /// </summary>
    public async Task<VolumeCostAnalysis> GetManagedDiskCostAsync(
        DiscoveredManagedDisk disk,
        DateTime periodStart,
        DateTime periodEnd)
    {
        try
        {
            var analysis = new VolumeCostAnalysis
            {
                ResourceId = disk.ResourceId,
                VolumeName = disk.DiskName,
                ResourceType = "ManagedDisk",
                Region = disk.Location,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                CapacityGigabytes = disk.DiskSizeGB,
                UsedGigabytes = disk.DiskSizeGB,
                SnapshotCount = 0, // Snapshot count not tracked for managed disks in discovery
                TotalSnapshotSizeGb = 0
            };

            // Parse enums with null checks and defaults
            var diskType = ManagedDiskType.StandardHDD; // Default
            if (!string.IsNullOrEmpty(disk.ManagedDiskType))
            {
                if (!Enum.TryParse<ManagedDiskType>(disk.ManagedDiskType, true, out diskType))
                {
                    _logger.LogWarning("Unknown managed disk type '{DiskType}' for disk {DiskName}, using StandardHDD",
                        disk.ManagedDiskType, disk.DiskName);
                }
            }
            
            var redundancy = StorageRedundancy.LRS; // Default
            if (!string.IsNullOrEmpty(disk.RedundancyType))
            {
                Enum.TryParse<StorageRedundancy>(disk.RedundancyType, true, out redundancy);
            }

            var pricing = await _pricingService.GetManagedDiskPricingAsync(
                disk.Location,
                diskType,
                disk.PricingTier ?? "P10",
                redundancy
            );

            if (pricing == null)
            {
                _logger.LogWarning("Could not retrieve pricing for Managed Disk {DiskName}", disk.DiskName);
                analysis.Warnings.Add("Failed to retrieve retail pricing data");
                return analysis;
            }
            
            // Store pricing data for debugging
            analysis.RetailPricingData = new Dictionary<string, object>
            {
                { "DiskType", diskType.ToString() },
                { "SKU", pricing.SKU },
                { "Redundancy", redundancy.ToString() },
                { "PricePerMonth", pricing.PricePerMonth },
                { "SnapshotPricePerGibMonth", pricing.SnapshotPricePerGibMonth }
            };
            
            var days = (periodEnd - periodStart).Days;
            if (days == 0) days = 1;

            // Managed disk storage cost (monthly billing)
            var diskCost = StorageCostComponent.ForCapacity(
                disk.ResourceId,
                disk.Location,
                analysis.CapacityGigabytes,
                pricing.PricePerMonth,
                periodStart,
                periodEnd,
                $"{disk.ManagedDiskType}-{disk.PricingTier}"
            );
            analysis.AddCostComponent(diskCost);


            // Try to replace retail estimate with actual billed cost if available
            await TryApplyActualCostAsync(analysis, periodStart, periodEnd);

            _logger.LogInformation("Calculated Managed Disk costs for {Disk}: ${Cost} over {Days} days (Source: {Source})",
                disk.DiskName,
                analysis.TotalCostForPeriod,
                days,
                analysis.CostComponents.All(c => !c.IsEstimated) ? "Actual" : "RetailEstimate");
            
            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating Managed Disk costs for {Disk}", disk.DiskName);
            throw;
        }
    }

    /// <summary>
    /// Enrich cost analysis with detailed meter-level actual costs from Cost Management API
    /// </summary>
    public async Task EnrichWithDetailedActualCostsAsync(VolumeCostAnalysis analysis, DateTime periodStart, DateTime periodEnd)
    {
        try
        {
            // For Azure Files and ANF, we need to query at the parent resource level
            var costQueryResourceId = analysis.ResourceId;
            
            // Azure Files: query at storage account level, not share level
            if (analysis.ResourceType == "AzureFile" && costQueryResourceId.Contains("/fileServices/"))
            {
                // Extract storage account ResourceId from share ResourceId
                // From: /subscriptions/.../storageAccounts/marinocloudshell/fileServices/default/shares/aidocs
                // To:   /subscriptions/.../storageAccounts/marinocloudshell
                var storageAccountEndIndex = costQueryResourceId.IndexOf("/fileServices/", StringComparison.OrdinalIgnoreCase);
                if (storageAccountEndIndex > 0)
                {
                    costQueryResourceId = costQueryResourceId.Substring(0, storageAccountEndIndex);
                    _logger.LogInformation(
                        "Azure Files share detected - querying at storage account level | Storage Account ResourceId: {StorageAccountResourceId}",
                        costQueryResourceId);
                }
            }
            // ANF: query at capacity pool level, not volume level
            else if (analysis.ResourceType == "ANF" && costQueryResourceId.Contains("/volumes/"))
            {
                // Extract capacity pool ResourceId from volume ResourceId
                // From: /subscriptions/.../capacityPools/beekertestpool/volumes/testvol2migration
                // To:   /subscriptions/.../capacityPools/beekertestpool
                var volumeEndIndex = costQueryResourceId.IndexOf("/volumes/", StringComparison.OrdinalIgnoreCase);
                if (volumeEndIndex > 0)
                {
                    costQueryResourceId = costQueryResourceId.Substring(0, volumeEndIndex);
                    _logger.LogInformation(
                        "ANF volume detected - querying at capacity pool level | Capacity Pool ResourceId: {CapacityPoolResourceId}",
                        costQueryResourceId);
                }
            }
            
            _logger.LogInformation(
                "Attempting to retrieve actual costs for {ResourceType} '{VolumeName}' | ResourceId: {ResourceId} | Period: {Start} to {End}",
                analysis.ResourceType,
                analysis.VolumeName,
                costQueryResourceId,
                periodStart.ToString("yyyy-MM-dd"),
                periodEnd.ToString("yyyy-MM-dd"));
            
            var detailedCosts = await GetDetailedActualCostsAsync(costQueryResourceId, periodStart, periodEnd);
            
            if (detailedCosts == null || detailedCosts.Count == 0)
            {
                _logger.LogWarning(
                    "No actual cost data available for {ResourceType} '{VolumeName}' (ResourceId: {ResourceId}). " +
                    "Keeping retail estimates. This may indicate: 1) Cost data has 24-48hr lag, 2) Resource was recently created, " +
                    "or 3) Resource ID format issue.",
                    analysis.ResourceType,
                    analysis.VolumeName,
                    analysis.ResourceId);
                analysis.ActualCostsApplied = false;
                analysis.ActualCostsNotAppliedReason = "No meter data returned from Cost Management API";
                analysis.ActualCostMeterCount = 0;
                return;
            }

            // Store detailed meter costs
            analysis.DetailedMeterCosts = detailedCosts;
            analysis.LastActualCostUpdate = DateTime.UtcNow;

            // Extract metadata from meters
            analysis.BillingMetadata = ExtractMetadataFromMeters(detailedCosts, analysis.ResourceType, periodStart, periodEnd);

            // Replace cost components with actual meter-based breakdown
            // Group by meter name and sum costs across all days in the period
            analysis.CostComponents.Clear();
            
            var aggregatedMeters = detailedCosts
                .GroupBy(m => new { m.Meter, m.MeterSubcategory, m.ComponentType })
                .Select(g => new
                {
                    g.Key.Meter,
                    g.Key.MeterSubcategory,
                    g.Key.ComponentType,
                    TotalCost = g.Sum(m => m.CostUSD),
                    Currency = g.First().Currency,
                    DayCount = g.Count()
                })
                .ToList();
            
            foreach (var meter in aggregatedMeters)
            {
                var component = new StorageCostComponent
                {
                    ComponentType = meter.ComponentType,
                    Region = analysis.Region,
                    UnitPrice = meter.TotalCost / meter.DayCount, // Average daily cost
                    Unit = "day",
                    Quantity = meter.DayCount,
                    CostForPeriod = meter.TotalCost,
                    Currency = meter.Currency,
                    ResourceId = analysis.ResourceId,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    IsEstimated = false,
                    Notes = $"Meter: {meter.Meter}, Subcategory: {meter.MeterSubcategory} (aggregated over {meter.DayCount} days)"
                };
                analysis.CostComponents.Add(component);
            }

            analysis.RecalculateTotals();
            analysis.Notes = (analysis.Notes ?? string.Empty) + " | Enriched with actual meter-level costs from Cost Management API.";
            analysis.ActualCostsApplied = true;
            analysis.ActualCostMeterCount = detailedCosts.Count;
            
            _logger.LogInformation(
                "Enriched cost analysis for {ResourceId} with {MeterCount} meters, total: ${Total}",
                analysis.ResourceId,
                detailedCosts.Count,
                analysis.TotalCostForPeriod);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich with detailed costs for {ResourceId}, keeping retail estimates", analysis.ResourceId);
            analysis.ActualCostsApplied = false;
            analysis.ActualCostsNotAppliedReason = $"Exception: {ex.Message}";
        }
    }

    private async Task TryApplyActualCostAsync(VolumeCostAnalysis analysis, DateTime periodStart, DateTime periodEnd)
    {
        try
        {
            var actual = await TryGetActualCostAsync(analysis.ResourceId, periodStart, periodEnd);
            if (!actual.HasValue || actual.Value <= 0)
            {
                analysis.ActualCostsApplied = false;
                analysis.ActualCostsNotAppliedReason = "No actual cost data found in Cost Management API";
                analysis.ActualCostMeterCount = 0;
                return; // keep retail estimate
            }

            var estimatedTotal = analysis.TotalCostForPeriod;
            if (estimatedTotal <= 0)
            {
                // No breakdown to scale; create a single actual cost component
                analysis.CostComponents.Clear();
                analysis.CostComponents.Add(new StorageCostComponent
                {
                    ComponentType = "actual-billed",
                    Region = analysis.Region,
                    UnitPrice = actual.Value,
                    Unit = "total",
                    Quantity = 1,
                    CostForPeriod = actual.Value,
                    Currency = "USD",
                    ResourceId = analysis.ResourceId,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    IsEstimated = false,
                    Notes = "Actual billed cost from Cost Management API"
                });
                analysis.RecalculateTotals();
                return;
            }

            // Scale each component so the sum matches actual billed cost
            var factor = actual.Value / estimatedTotal;
            foreach (var component in analysis.CostComponents)
            {
                component.CostForPeriod *= factor;
                component.IsEstimated = false;
            }

            analysis.RecalculateTotals();
            analysis.Notes = (analysis.Notes ?? string.Empty) +
                " | Actual billed total from Cost Management applied; retail components scaled to match.";
            analysis.ActualCostsApplied = true;
            analysis.ActualCostMeterCount = 1; // Simple total
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply actual billed cost for {ResourceId}", analysis.ResourceId);
            analysis.ActualCostsApplied = false;
            analysis.ActualCostsNotAppliedReason = $"Exception: {ex.Message}";
        }
    }

    /// <summary>
    /// Get detailed meter-level costs from Cost Management API
    /// This is the core method that implements the PowerShell script logic
    /// </summary>
    private async Task<List<MeterCostEntry>?> GetDetailedActualCostsAsync(string resourceId, DateTime periodStart, DateTime periodEnd)
    {
        try
        {
            var subscriptionId = ExtractSubscriptionId(resourceId);
            if (string.IsNullOrEmpty(subscriptionId))
            {
                _logger.LogWarning("Could not extract subscription ID from ResourceId: {ResourceId}", resourceId);
                return null;
            }

            var scope = new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}");
            var dataset = new QueryDataset
            {
                Granularity = GranularityType.Daily
            };

            // Add aggregations for both Cost and CostUSD
            dataset.Aggregation.Add("totalCost", new QueryAggregation("Cost", FunctionType.Sum));
            dataset.Aggregation.Add("totalCostUSD", new QueryAggregation("CostUSD", FunctionType.Sum));

            // Add grouping by ResourceId, MeterSubcategory, and Meter (matching PowerShell script)
            dataset.Grouping.Add(new QueryGrouping("ResourceId", "Dimension"));
            dataset.Grouping.Add(new QueryGrouping("MeterSubcategory", "Dimension"));
            dataset.Grouping.Add(new QueryGrouping("Meter", "Dimension"));

            // Filter to the specific resource ID
            var comparison = new QueryComparisonExpression(
                "ResourceId",
                QueryOperatorType.In,
                new[] { resourceId });

            dataset.Filter = new QueryFilter { Dimensions = comparison };

            var timePeriod = new QueryTimePeriod(periodStart, periodEnd);
            var queryDefinition = new QueryDefinition(ExportType.ActualCost, TimeframeType.Custom, dataset)
            {
                TimePeriod = timePeriod
            };
            
            _logger.LogInformation(
                "Cost Management API Query | Scope: /subscriptions/{SubscriptionId} | ResourceId Filter: {ResourceId} | Period: {Start} to {End} | Granularity: Daily | Grouping: ResourceId, MeterSubcategory, Meter",
                subscriptionId,
                resourceId,
                periodStart.ToString("yyyy-MM-dd"),
                periodEnd.ToString("yyyy-MM-dd"));

            var armClient = new ArmClient(_credential);
            var response = await armClient.UsageQueryAsync(scope, queryDefinition);
            var result = response.Value;

            if (result?.Rows == null || result.Rows.Count == 0)
            {
                _logger.LogWarning(
                    "Cost Management API returned no data | ResourceId: {ResourceId} | Query returned {RowCount} rows | This could mean: no usage in period, resource created after period, or incorrect resource ID format",
                    resourceId,
                    result?.Rows?.Count ?? 0);
                return null;
            }
            
            _logger.LogInformation(
                "Cost Management API returned {RowCount} rows for ResourceId: {ResourceId}",
                result.Rows.Count,
                resourceId);

            // Parse the result rows into MeterCostEntry objects
            var meterCosts = new List<MeterCostEntry>();
            var columns = result.Columns?.ToList();
            
            if (columns == null || columns.Count == 0)
            {
                return null;
            }

            // Build column index map
            var columnMap = new Dictionary<string, int>();
            for (int i = 0; i < columns.Count; i++)
            {
                columnMap[columns[i].Name] = i;
            }

            foreach (var row in result.Rows)
            {
                if (row == null || row.Count == 0) continue;

                var entry = new MeterCostEntry();

                // Extract values based on column positions
                if (columnMap.TryGetValue("Cost", out var costIdx) && costIdx < row.Count)
                {
                    if (double.TryParse(row[costIdx]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var cost))
                        entry.Cost = cost;
                }

                if (columnMap.TryGetValue("CostUSD", out var costUsdIdx) && costUsdIdx < row.Count)
                {
                    if (double.TryParse(row[costUsdIdx]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var costUsd))
                        entry.CostUSD = costUsd;
                }

                if (columnMap.TryGetValue("UsageDate", out var dateIdx) && dateIdx < row.Count)
                {
                    if (DateTime.TryParse(row[dateIdx]?.ToString(), out var usageDate))
                        entry.UsageDate = usageDate;
                }

                if (columnMap.TryGetValue("ResourceId", out var ridIdx) && ridIdx < row.Count)
                {
                    entry.ResourceId = row[ridIdx]?.ToString() ?? resourceId;
                }
                else
                {
                    entry.ResourceId = resourceId;
                }

                if (columnMap.TryGetValue("MeterSubcategory", out var subIdx) && subIdx < row.Count)
                {
                    entry.MeterSubcategory = row[subIdx]?.ToString() ?? string.Empty;
                }

                if (columnMap.TryGetValue("Meter", out var meterIdx) && meterIdx < row.Count)
                {
                    entry.Meter = row[meterIdx]?.ToString() ?? string.Empty;
                }

                if (columnMap.TryGetValue("Currency", out var currIdx) && currIdx < row.Count)
                {
                    entry.Currency = row[currIdx]?.ToString() ?? "USD";
                }

                // Map meter to component type
                entry.ComponentType = MapMeterToCostComponent(entry.Meter, entry.MeterSubcategory);

                meterCosts.Add(entry);
            }

            _logger.LogInformation(
                "Retrieved {Count} meter cost entries for {ResourceId} from {Start} to {End}",
                meterCosts.Count,
                resourceId,
                periodStart,
                periodEnd);

            return meterCosts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying detailed costs from Cost Management API for {ResourceId}", resourceId);
            return null;
        }
    }

    private async Task<double?> TryGetActualCostAsync(string resourceId, DateTime periodStart, DateTime periodEnd)
    {
        try
        {
            var subscriptionId = ExtractSubscriptionId(resourceId);
            if (string.IsNullOrEmpty(subscriptionId))
            {
                return null;
            }

            // Build Cost Management query using official Azure.ResourceManager.CostManagement SDK
            var scope = new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}");

            var dataset = new QueryDataset();

            // Aggregate total cost over the period
            dataset.Aggregation.Add("totalCost", new QueryAggregation("Cost", FunctionType.Sum));

            // Filter to the specific resource ID
            var comparison = new QueryComparisonExpression(
                "ResourceId",
                QueryOperatorType.In,
                new[] { resourceId });

            var filter = new QueryFilter
            {
                Dimensions = comparison
            };
            dataset.Filter = filter;

            var timePeriod = new QueryTimePeriod(periodStart, periodEnd);

            var queryDefinition = new QueryDefinition(ExportType.ActualCost, TimeframeType.Custom, dataset)
            {
                TimePeriod = timePeriod
            };

            var armClient = new ArmClient(_credential);
            var response = await armClient.UsageQueryAsync(scope, queryDefinition);
            var result = response.Value;

            if (result?.Rows == null || result.Rows.Count == 0)
            {
                return null;
            }

            var firstRow = result.Rows[0];
            if (firstRow == null || firstRow.Count == 0)
            {
                return null;
            }

            var costData = firstRow[0];
            if (costData == null)
            {
                return null;
            }

            var costString = costData.ToString();
            if (string.IsNullOrWhiteSpace(costString))
            {
                return null;
            }

            if (double.TryParse(costString, NumberStyles.Any, CultureInfo.InvariantCulture, out var cost))
            {
                return cost;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error querying Cost Management API for {ResourceId}", resourceId);
            return null;
        }
    }

    private string? ExtractSubscriptionId(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId)) return null;

        const string subPrefix = "/subscriptions/";
        var subIndex = resourceId.IndexOf(subPrefix, StringComparison.OrdinalIgnoreCase);
        if (subIndex < 0) return null;

        subIndex += subPrefix.Length;
        var slashIndex = resourceId.IndexOf('/', subIndex);
        if (slashIndex < 0) return null;

        return resourceId.Substring(subIndex, slashIndex - subIndex);
    }
    
    /// <summary>
    /// Extract storage account resource ID from share resource ID
    /// </summary>
    private string? ExtractStorageAccountResourceId(string shareResourceId)
    {
        if (string.IsNullOrWhiteSpace(shareResourceId)) return null;
        
        // Share resource ID format:
        // /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Storage/storageAccounts/{sa}/fileServices/default/shares/{share}
        // We need:
        // /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Storage/storageAccounts/{sa}
        
        var fileServicesIndex = shareResourceId.IndexOf("/fileServices/", StringComparison.OrdinalIgnoreCase);
        if (fileServicesIndex > 0)
        {
            return shareResourceId.Substring(0, fileServicesIndex);
        }
        
        return null;
    }

    /// <summary>
    /// Get regional pricing, with caching
    /// </summary>
    public async Task<RegionalPricing> GetRegionalPricingAsync(string region)
    {
        try
        {
            // Check cache first
            if (_pricingCache.TryGetValue(region, out var cached) && !cached.IsExpired)
            {
                return cached;
            }

            // Fetch fresh pricing from Azure Pricing API
            var pricing = await FetchPricingFromAzureAsync(region);
            
            if (pricing == null)
            {
                _logger.LogWarning("Failed to fetch pricing for region {Region}, using defaults", region);
                pricing = RegionalPricing.DefaultForUs();
                pricing.Region = region;
            }

            // Cache it
            _pricingCache[region] = pricing;
            return pricing;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching regional pricing for {Region}, using defaults", region);
            return RegionalPricing.DefaultForUs();
        }
    }

    /// <summary>
    /// Fetch pricing from Azure Pricing API
    /// </summary>
    private async Task<RegionalPricing?> FetchPricingFromAzureAsync(string region)
    {
        try
        {
            using var httpClient = new HttpClient();
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://management.azure.com/.default" }), default);
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

            // Query for storage pricing
            var url = $"https://prices.azure.com/api/retail/prices?$filter=serviceName eq 'Storage'" +
                     $" and location eq '{NormalizeRegionName(region)}'&$top=100";

            _logger.LogDebug("Fetching pricing from: {Url}", url);
            var response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Pricing API returned {Status}", (int)response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<PricingApiResponse>(content, options);

            if (result?.Items == null || result.Items.Count == 0)
            {
                _logger.LogWarning("No pricing items returned for region {Region}", region);
                return null;
            }

            // Parse pricing from results
            return ParsePricingFromResponse(region, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching pricing from Azure API for region {Region}", region);
            return null;
        }
    }

    /// <summary>
    /// Parse pricing from Azure Pricing API response
    /// </summary>
    private RegionalPricing? ParsePricingFromResponse(string region, PricingApiResponse response)
    {
        var pricing = new RegionalPricing { Region = region };

        // Extract pricing for different storage operations
        foreach (var item in response.Items)
        {
            if (item.UnitPrice == 0) continue;

            // Standard storage
            if (item.MeterName.Contains("LRS Data Stored", StringComparison.OrdinalIgnoreCase))
            {
                pricing.StandardStoragePricePerGb = item.UnitPrice;
            }
            // Premium storage
            else if (item.MeterName.Contains("Premium Data", StringComparison.OrdinalIgnoreCase))
            {
                pricing.PremiumStoragePricePerGb = item.UnitPrice;
            }
            // Transactions
            else if (item.MeterName.Contains("Class 2", StringComparison.OrdinalIgnoreCase) ||
                    item.MeterName.Contains("Transactions", StringComparison.OrdinalIgnoreCase))
            {
                if (!pricing.TransactionPricePer10kStandard.ToString().Contains("E-"))
                {
                    pricing.TransactionPricePer10kStandard = item.UnitPrice * 10000;
                }
            }
            // Snapshots
            else if (item.MeterName.Contains("Snapshot", StringComparison.OrdinalIgnoreCase))
            {
                pricing.SnapshotPricePerGb = item.UnitPrice;
            }
            // Data egress
            else if (item.MeterName.Contains("Egress", StringComparison.OrdinalIgnoreCase))
            {
                pricing.InternetEgressPricePerGb = item.UnitPrice;
            }
        }

        return pricing.StandardStoragePricePerGb > 0 ? pricing : null;
    }

    /// <summary>
    /// Normalize region name for Azure Pricing API
    /// </summary>
    private string NormalizeRegionName(string region)
    {
        // Convert "eastus" to "East US", etc.
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo
            .ToTitleCase(region.Replace("-", " "));
    }

    /// <summary>
    /// Internal class for pricing API response
    /// </summary>
    private class PricingApiResponse
    {
        public List<PricingItem> Items { get; set; } = new();
    }

    private class PricingItem
    {
        public string MeterName { get; set; } = string.Empty;
        public double UnitPrice { get; set; }
    }

    /// <summary>
    /// Clear pricing cache
    /// </summary>
    public void ClearPricingCache()
    {
        _pricingCache.Clear();
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public Dictionary<string, object> GetCacheStatistics()
    {
        return new Dictionary<string, object>
        {
            ["CachedRegions"] = _pricingCache.Keys.ToList(),
            ["CacheSize"] = _pricingCache.Count,
            ["LastUpdated"] = _pricingCache.Values.FirstOrDefault()?.LastUpdated ?? DateTime.MinValue
        };
    }

    /// <summary>
    /// Map meter name and subcategory to cost component type
    /// Resource-type-specific logic to intelligently categorize different meters
    /// </summary>
    private string MapMeterToCostComponent(string meterName, string meterSubcategory)
    {
        if (string.IsNullOrWhiteSpace(meterName))
            return "unknown";

        var meter = meterName.ToLowerInvariant();
        var subcategory = meterSubcategory?.ToLowerInvariant() ?? string.Empty;

        // Storage/capacity meters
        if (MeterPatterns.StorageAccount.StorageMeters.Any(m => meter.Contains(m.ToLowerInvariant())) ||
            MeterPatterns.AnfVolume.CapacityMeters.Any(m => meter.Contains(m.ToLowerInvariant())) ||
            MeterPatterns.ManagedDisk.CapacityMeters.Any(m => meter.Contains(m.ToLowerInvariant())) ||
            meter.Contains("data stored") ||
            meter.Contains("capacity") ||
            meter.Contains("provisioned"))
        {
            return "storage";
        }

        // Transaction meters
        if (MeterPatterns.StorageAccount.TransactionMeters.Any(m => meter.Contains(m.ToLowerInvariant())) ||
            meter.Contains("operations") ||
            meter.Contains("transactions") ||
            meter.Contains("read") ||
            meter.Contains("write") ||
            meter.Contains("list") ||
            meter.Contains("class 1") ||
            meter.Contains("class 2"))
        {
            return "transactions";
        }

        // Egress/Data Transfer Out
        if (MeterPatterns.StorageAccount.EgressMeters.Any(m => meter.Contains(m.ToLowerInvariant())) ||
            meter.Contains("egress") ||
            meter.Contains("data transfer out"))
        {
            return "egress";
        }

        // Ingress/Data Transfer In
        if (MeterPatterns.StorageAccount.IngressMeters.Any(m => meter.Contains(m.ToLowerInvariant())) ||
            meter.Contains("data transfer in"))
        {
            return "ingress";
        }

        // Snapshots
        if (MeterPatterns.AnfVolume.SnapshotMeters.Any(m => meter.Contains(m.ToLowerInvariant())) ||
            MeterPatterns.ManagedDisk.SnapshotMeters.Any(m => meter.Contains(m.ToLowerInvariant())) ||
            meter.Contains("snapshot"))
        {
            return "snapshots";
        }

        // Backup
        if (MeterPatterns.AnfVolume.BackupMeters.Any(m => meter.Contains(m.ToLowerInvariant())) ||
            meter.Contains("backup"))
        {
            return "backup";
        }

        // Replication (GRS, etc.)
        if (MeterPatterns.StorageAccount.ReplicationMeters.Any(m => meter.Contains(m.ToLowerInvariant())) ||
            meter.Contains("replication"))
        {
            return "replication";
        }

        // IOPS or disk operations
        if (MeterPatterns.ManagedDisk.OperationMeters.Any(m => meter.Contains(m.ToLowerInvariant())))
        {
            return "operations";
        }

        return "other";
    }

    /// <summary>
    /// Extract volume metadata from billing meter data
    /// Uses resource-type-specific logic to infer properties
    /// </summary>
    private VolumeMetadataFromBilling ExtractMetadataFromMeters(
        List<MeterCostEntry> meters,
        string resourceType,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var metadata = new VolumeMetadataFromBilling
        {
            MetadataFromDate = periodStart,
            MetadataToDate = periodEnd,
            TotalMeterCount = meters.Count,
            AdditionalMetadata = new Dictionary<string, string>()
        };

        var days = (periodEnd - periodStart).Days;
        if (days == 0) days = 1;

        // Extract redundancy type from meter names
        foreach (var meter in meters)
        {
            var meterLower = meter.Meter.ToLowerInvariant();
            
            if (meterLower.Contains("gzrs"))
            {
                metadata.RedundancyType = meterLower.Contains("ra-gzrs") ? "RA-GZRS" : "GZRS";
                metadata.HasGeoReplication = true;
            }
            else if (meterLower.Contains("grs"))
            {
                metadata.RedundancyType = meterLower.Contains("ra-grs") ? "RA-GRS" : "GRS";
                metadata.HasGeoReplication = true;
            }
            else if (meterLower.Contains("zrs") && string.IsNullOrEmpty(metadata.RedundancyType))
            {
                metadata.RedundancyType = "ZRS";
            }
            else if (meterLower.Contains("lrs") && string.IsNullOrEmpty(metadata.RedundancyType))
            {
                metadata.RedundancyType = "LRS";
            }
        }

        // Resource-type-specific metadata extraction
        switch (resourceType.ToLowerInvariant())
        {
            case "azurefile":
            case "storageaccount":
                ExtractStorageAccountMetadata(meters, metadata, days);
                break;

            case "anf":
            case "anfvolume":
                ExtractAnfMetadata(meters, metadata, days);
                break;

            case "manageddisk":
            case "disk":
                ExtractManagedDiskMetadata(meters, metadata, days);
                break;
        }

        // Calculate confidence score based on meter diversity and data points
        metadata.ConfidenceScore = CalculateMetadataConfidence(meters, days);

        return metadata;
    }

    /// <summary>
    /// Extract metadata specific to Storage Accounts / Azure Files
    /// </summary>
    private void ExtractStorageAccountMetadata(List<MeterCostEntry> meters, VolumeMetadataFromBilling metadata, int days)
    {
        double totalReadOps = 0;
        double totalWriteOps = 0;
        double totalListOps = 0;
        double totalEgress = 0;
        double totalIngress = 0;

        foreach (var meter in meters)
        {
            var meterLower = meter.Meter.ToLowerInvariant();
            var quantity = meter.Quantity ?? 0;

            if (meterLower.Contains("read operations"))
                totalReadOps += quantity;
            else if (meterLower.Contains("write operations"))
                totalWriteOps += quantity;
            else if (meterLower.Contains("list operations"))
                totalListOps += quantity;
            else if (meterLower.Contains("data transfer out") || meterLower.Contains("egress"))
                totalEgress += quantity;
            else if (meterLower.Contains("data transfer in"))
                totalIngress += quantity;

            // Storage tier detection
            if (meterLower.Contains("premium") && string.IsNullOrEmpty(metadata.StorageTier))
                metadata.StorageTier = "Premium";
            else if (meterLower.Contains("hot") && string.IsNullOrEmpty(metadata.StorageTier))
                metadata.StorageTier = "Hot";
            else if (meterLower.Contains("cool"))
                metadata.StorageTier = "Cool";
            else if (meterLower.Contains("archive"))
                metadata.StorageTier = "Archive";
            else if (meterLower.Contains("standard") && string.IsNullOrEmpty(metadata.StorageTier))
                metadata.StorageTier = "Standard";
        }

        metadata.AverageReadOperationsPerDay = days > 0 ? totalReadOps / days : null;
        metadata.AverageWriteOperationsPerDay = days > 0 ? totalWriteOps / days : null;
        metadata.AverageListOperationsPerDay = days > 0 ? totalListOps / days : null;
        metadata.AverageDataTransferGbPerDay = days > 0 ? totalEgress / days : null;
        metadata.AverageDataIngressGbPerDay = days > 0 ? totalIngress / days : null;

        if (metadata.AverageReadOperationsPerDay > 0 || metadata.AverageWriteOperationsPerDay > 0)
            metadata.AdditionalMetadata!["HasSignificantIOActivity"] = "true";
    }

    /// <summary>
    /// Extract metadata specific to Azure NetApp Files volumes
    /// </summary>
    private void ExtractAnfMetadata(List<MeterCostEntry> meters, VolumeMetadataFromBilling metadata, int days)
    {
        var detectedProtocols = new List<string>();

        foreach (var meter in meters)
        {
            var meterLower = meter.Meter.ToLowerInvariant();
            var subcategoryLower = meter.MeterSubcategory.ToLowerInvariant();

            // Service level detection from meters
            if (meterLower.Contains("ultra capacity") || subcategoryLower.Contains("ultra"))
                metadata.ServiceLevel = "Ultra";
            else if (meterLower.Contains("premium capacity") || subcategoryLower.Contains("premium"))
                metadata.ServiceLevel ??= "Premium";
            else if (meterLower.Contains("standard capacity") || subcategoryLower.Contains("standard"))
                metadata.ServiceLevel ??= "Standard";

            // Protocol detection
            if (meterLower.Contains("nfsv3") && !detectedProtocols.Contains("NFSv3"))
                detectedProtocols.Add("NFSv3");
            if (meterLower.Contains("nfsv4") && !detectedProtocols.Contains("NFSv4.1"))
                detectedProtocols.Add("NFSv4.1");
            if (meterLower.Contains("smb") || meterLower.Contains("cifs"))
                if (!detectedProtocols.Contains("SMB"))
                    detectedProtocols.Add("SMB");
            if (meterLower.Contains("dual-protocol"))
                detectedProtocols.Add("Dual-Protocol");
        }

        if (detectedProtocols.Count > 0)
            metadata.DetectedProtocols = detectedProtocols;

        metadata.AdditionalMetadata!["ANFServiceLevel"] = metadata.ServiceLevel ?? "Unknown";
    }

    /// <summary>
    /// Extract metadata specific to Managed Disks
    /// </summary>
    private void ExtractManagedDiskMetadata(List<MeterCostEntry> meters, VolumeMetadataFromBilling metadata, int days)
    {
        double totalDiskOps = 0;

        foreach (var meter in meters)
        {
            var meterLower = meter.Meter.ToLowerInvariant();
            var subcategoryLower = meter.MeterSubcategory.ToLowerInvariant();
            var quantity = meter.Quantity ?? 0;

            // Disk type detection
            if (subcategoryLower.Contains("premium ssd") || meterLower.Contains("premium ssd"))
                metadata.DiskType = "Premium SSD";
            else if (subcategoryLower.Contains("standard ssd") || meterLower.Contains("standard ssd"))
                metadata.DiskType ??= "Standard SSD";
            else if (subcategoryLower.Contains("standard hdd") || meterLower.Contains("standard hdd"))
                metadata.DiskType ??= "Standard HDD";
            else if (subcategoryLower.Contains("ultra disk") || meterLower.Contains("ultra"))
                metadata.DiskType ??= "Ultra Disk";

            // IOPS/operations tracking
            if (meterLower.Contains("disk operations") || meterLower.Contains("iops"))
                totalDiskOps += quantity;
        }

        if (totalDiskOps > 0)
        {
            metadata.AdditionalMetadata!["AverageDiskOpsPerDay"] = (days > 0 ? totalDiskOps / days : 0).ToString("F2");
        }

        metadata.AdditionalMetadata!["DiskType"] = metadata.DiskType ?? "Unknown";
    }

    /// <summary>
    /// Calculate confidence score for metadata based on data quality
    /// </summary>
    private double CalculateMetadataConfidence(List<MeterCostEntry> meters, int days)
    {
        if (meters.Count == 0) return 0;

        double confidence = 50; // Base confidence

        // More meters = higher confidence (up to +30)
        confidence += Math.Min(meters.Count * 3, 30);

        // More days of data = higher confidence (up to +20)
        confidence += Math.Min(days * 2, 20);

        // Diversity of meter types adds confidence
        var uniqueComponentTypes = meters.Select(m => m.ComponentType).Distinct().Count();
        if (uniqueComponentTypes >= 3) confidence += 10;

        return Math.Min(confidence, 100);
    }
}
