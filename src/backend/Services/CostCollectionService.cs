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
    private readonly Dictionary<string, RegionalPricing> _pricingCache = new();
    
    private const string CostManagementApiVersion = "2021-10-01";
    private const string PricingApiVersion = "2021-10-01";

    public CostCollectionService(ILogger logger, TokenCredential credential)
    {
        _logger = logger;
        _credential = credential;
    }

    /// <summary>
    /// Collect Azure Files storage costs for a specific storage account
    /// </summary>
    public async Task<VolumeCostAnalysis> GetAzureFilesCostAsync(
        string resourceId,
        string shareName,
        string region,
        long capacityBytes,
        long usedBytes,
        double avgTransactionsPerDay,
        double avgEgressPerDayBytes,
        int snapshotCount,
        long totalSnapshotBytes,
        bool backupConfigured,
        DateTime periodStart,
        DateTime periodEnd)
    {
        try
        {
            var analysis = new VolumeCostAnalysis
            {
                ResourceId = resourceId,
                VolumeName = shareName,
                ResourceType = "AzureFile",
                Region = region,
                JobId = "",
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                CapacityGigabytes = capacityBytes / (1024.0 * 1024.0 * 1024.0),
                UsedGigabytes = usedBytes / (1024.0 * 1024.0 * 1024.0),
                AverageTransactionsPerDay = avgTransactionsPerDay,
                AverageEgressPerDayGb = avgEgressPerDayBytes / (1024.0 * 1024.0 * 1024.0),
                SnapshotCount = snapshotCount,
                TotalSnapshotSizeGb = totalSnapshotBytes / (1024.0 * 1024.0 * 1024.0),
                BackupConfigured = backupConfigured
            };

            var pricing = await GetRegionalPricingAsync(region);
            var days = periodEnd.Subtract(periodStart).Days;
            if (days == 0) days = 1;

            // Determine tier based on capacity (use 100 GiB threshold, be careful to avoid compile-time overflow)
            var premiumThresholdBytes = 100L * 1024L * 1024L * 1024L;
            var isPremium = capacityBytes > premiumThresholdBytes; // > 100 GB = premium
            var tierName = isPremium ? "Premium" : "Standard";
            var storagePrice = isPremium ? pricing.PremiumStoragePricePerGb : pricing.StandardStoragePricePerGb;

            // Storage cost (based on used capacity for billing)
            var storageCost = StorageCostComponent.ForCapacity(
                resourceId,
                region,
                analysis.UsedGigabytes,
                storagePrice,
                periodStart,
                periodEnd,
                tierName);
            analysis.AddCostComponent(storageCost);

            // Transaction costs
            var totalTransactions = avgTransactionsPerDay * days;
            var transactionsIn10k = totalTransactions / 10000;
            if (transactionsIn10k > 0)
            {
                var transactionPrice = isPremium ? pricing.TransactionPricePer10kPremium : pricing.TransactionPricePer10kStandard;
                var transactionCost = StorageCostComponent.ForTransactions(
                    resourceId,
                    region,
                    transactionsIn10k,
                    transactionPrice,
                    periodStart,
                    periodEnd);
                analysis.AddCostComponent(transactionCost);
            }

            // Egress costs
            var totalEgressGb = (avgEgressPerDayBytes / (1024.0 * 1024.0 * 1024.0)) * days;
            if (totalEgressGb > 0)
            {
                var egressCost = StorageCostComponent.ForEgress(
                    resourceId,
                    region,
                    totalEgressGb,
                    pricing.InternetEgressPricePerGb,
                    periodStart,
                    periodEnd);
                analysis.AddCostComponent(egressCost);
            }

            // Snapshot costs
            if (analysis.TotalSnapshotSizeGb > 0)
            {
                var snapshotCost = StorageCostComponent.ForSnapshots(
                    resourceId,
                    region,
                    analysis.TotalSnapshotSizeGb.Value,
                    pricing.SnapshotPricePerGb,
                    periodStart,
                    periodEnd);
                analysis.AddCostComponent(snapshotCost);
            }

            // Backup costs (if configured)
            if (backupConfigured && analysis.UsedGigabytes > 0)
            {
                var backupCost = StorageCostComponent.ForBackup(
                    resourceId,
                    region,
                    analysis.UsedGigabytes * 0.1, // Assume 10% overhead for backup
                    pricing.BackupPricePerGb,
                    periodStart,
                    periodEnd);
                analysis.AddCostComponent(backupCost);
            }

            // Try to replace retail estimate with actual billed cost if available
            await TryApplyActualCostAsync(analysis, periodStart, periodEnd);

            _logger.LogInformation(
                "Calculated Azure Files costs for {Share}: ${Cost} over {Days} days (Source: {Source})",
                shareName,
                analysis.TotalCostForPeriod,
                days,
                analysis.CostComponents.All(c => !c.IsEstimated) ? "Actual" : "RetailEstimate");
            
            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating Azure Files costs for {Share}", shareName);
            throw;
        }
    }

    /// <summary>
    /// Collect Azure NetApp Files costs for a volume
    /// </summary>
    public async Task<VolumeCostAnalysis> GetAnfVolumeCostAsync(
        string resourceId,
        string volumeName,
        string poolName,
        string region,
        long provisionedBytes,
        long usedBytes,
        int snapshotCount,
        long totalSnapshotBytes,
        bool backupConfigured,
        DateTime periodStart,
        DateTime periodEnd)
    {
        try
        {
            var analysis = new VolumeCostAnalysis
            {
                ResourceId = resourceId,
                VolumeName = volumeName,
                ResourceType = "ANF",
                Region = region,
                StorageAccountOrPoolName = poolName,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                CapacityGigabytes = provisionedBytes / (1024.0 * 1024.0 * 1024.0),
                UsedGigabytes = usedBytes / (1024.0 * 1024.0 * 1024.0),
                SnapshotCount = snapshotCount,
                TotalSnapshotSizeGb = totalSnapshotBytes / (1024.0 * 1024.0 * 1024.0),
                BackupConfigured = backupConfigured
            };

            var pricing = await GetRegionalPricingAsync(region);
            
            // ANF is billed by provisioned capacity (minimum per pool)
            var provisionedTib = analysis.CapacityGigabytes / 1024.0;
            var storageCost = StorageCostComponent.ForCapacity(
                resourceId,
                region,
                analysis.CapacityGigabytes,
                pricing.UltraStoragePricePerGb,
                periodStart,
                periodEnd,
                "ANF-Provisioned");
            analysis.AddCostComponent(storageCost);

            // Snapshot costs (billed by used capacity)
            if (analysis.TotalSnapshotSizeGb > 0)
            {
                var snapshotCost = StorageCostComponent.ForSnapshots(
                    resourceId,
                    region,
                    analysis.TotalSnapshotSizeGb.Value,
                    pricing.SnapshotPricePerGb,
                    periodStart,
                    periodEnd);
                analysis.AddCostComponent(snapshotCost);
            }

            // Backup costs (if configured)
            if (backupConfigured && analysis.UsedGigabytes > 0)
            {
                var backupCost = StorageCostComponent.ForBackup(
                    resourceId,
                    region,
                    analysis.UsedGigabytes * 0.15, // Assume 15% overhead for ANF backup
                    pricing.BackupPricePerGb,
                    periodStart,
                    periodEnd);
                analysis.AddCostComponent(backupCost);
            }

            // Try to replace retail estimate with actual billed cost if available
            await TryApplyActualCostAsync(analysis, periodStart, periodEnd);

            _logger.LogInformation("Calculated ANF costs for {Volume}: ${Cost} over {Days} days (Source: {Source})",
                volumeName,
                analysis.TotalCostForPeriod,
                (periodEnd - periodStart).Days,
                analysis.CostComponents.All(c => !c.IsEstimated) ? "Actual" : "RetailEstimate");
            
            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating ANF costs for {Volume}", volumeName);
            throw;
        }
    }

    /// <summary>
    /// Collect Managed Disk costs
    /// </summary>
    public async Task<VolumeCostAnalysis> GetManagedDiskCostAsync(
        string resourceId,
        string diskName,
        string region,
        long diskSizeBytes,
        int snapshotCount,
        long totalSnapshotBytes,
        DateTime periodStart,
        DateTime periodEnd)
    {
        try
        {
            var analysis = new VolumeCostAnalysis
            {
                ResourceId = resourceId,
                VolumeName = diskName,
                ResourceType = "ManagedDisk",
                Region = region,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                CapacityGigabytes = diskSizeBytes / (1024.0 * 1024.0 * 1024.0),
                UsedGigabytes = diskSizeBytes / (1024.0 * 1024.0 * 1024.0),
                SnapshotCount = snapshotCount,
                TotalSnapshotSizeGb = totalSnapshotBytes / (1024.0 * 1024.0 * 1024.0)
            };

            var pricing = await GetRegionalPricingAsync(region);
            var days = (periodEnd - periodStart).Days;
            if (days == 0) days = 1;

            // Managed disk storage cost (monthly billing)
            var diskCost = StorageCostComponent.ForCapacity(
                resourceId,
                region,
                analysis.CapacityGigabytes,
                pricing.PremiumStoragePricePerGb,
                periodStart,
                periodEnd,
                "Managed-Disk");
            analysis.AddCostComponent(diskCost);

            // Snapshot costs
            if (analysis.TotalSnapshotSizeGb > 0)
            {
                var snapshotCost = StorageCostComponent.ForSnapshots(
                    resourceId,
                    region,
                    analysis.TotalSnapshotSizeGb.Value,
                    pricing.SnapshotPricePerGb,
                    periodStart,
                    periodEnd);
                analysis.AddCostComponent(snapshotCost);
            }

            // Try to replace retail estimate with actual billed cost if available
            await TryApplyActualCostAsync(analysis, periodStart, periodEnd);

            _logger.LogInformation("Calculated Managed Disk costs for {Disk}: ${Cost} over {Days} days (Source: {Source})",
                diskName,
                analysis.TotalCostForPeriod,
                days,
                analysis.CostComponents.All(c => !c.IsEstimated) ? "Actual" : "RetailEstimate");
            
            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating Managed Disk costs for {Disk}", diskName);
            throw;
        }
    }

    private async Task TryApplyActualCostAsync(VolumeCostAnalysis analysis, DateTime periodStart, DateTime periodEnd)
    {
        try
        {
            var actual = await TryGetActualCostAsync(analysis.ResourceId, periodStart, periodEnd);
            if (!actual.HasValue || actual.Value <= 0)
            {
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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply actual billed cost for {ResourceId}", analysis.ResourceId);
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
}