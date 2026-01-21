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

    /// <summary>
    /// Enrich cost analysis with detailed meter-level actual costs from Cost Management API
    /// </summary>
    public async Task EnrichWithDetailedActualCostsAsync(VolumeCostAnalysis analysis, DateTime periodStart, DateTime periodEnd)
    {
        try
        {
            var detailedCosts = await GetDetailedActualCostsAsync(analysis.ResourceId, periodStart, periodEnd);
            
            if (detailedCosts == null || detailedCosts.Count == 0)
            {
                _logger.LogInformation("No detailed cost data available for {ResourceId}, keeping retail estimates", analysis.ResourceId);
                return;
            }

            // Store detailed meter costs
            analysis.DetailedMeterCosts = detailedCosts;
            analysis.LastActualCostUpdate = DateTime.UtcNow;

            // Extract metadata from meters
            analysis.BillingMetadata = ExtractMetadataFromMeters(detailedCosts, analysis.ResourceType, periodStart, periodEnd);

            // Replace cost components with actual meter-based breakdown
            analysis.CostComponents.Clear();
            
            foreach (var meterEntry in detailedCosts)
            {
                var component = new StorageCostComponent
                {
                    ComponentType = meterEntry.ComponentType,
                    Region = analysis.Region,
                    UnitPrice = meterEntry.CostUSD,
                    Unit = meterEntry.Unit ?? "unknown",
                    Quantity = meterEntry.Quantity ?? 1,
                    CostForPeriod = meterEntry.CostUSD,
                    Currency = meterEntry.Currency,
                    ResourceId = analysis.ResourceId,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    IsEstimated = false,
                    Notes = $"Meter: {meterEntry.Meter}, Subcategory: {meterEntry.MeterSubcategory}"
                };
                analysis.CostComponents.Add(component);
            }

            analysis.RecalculateTotals();
            analysis.Notes = (analysis.Notes ?? string.Empty) + " | Enriched with actual meter-level costs from Cost Management API.";
            
            _logger.LogInformation(
                "Enriched cost analysis for {ResourceId} with {MeterCount} meters, total: ${Total}",
                analysis.ResourceId,
                detailedCosts.Count,
                analysis.TotalCostForPeriod);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich with detailed costs for {ResourceId}, keeping retail estimates", analysis.ResourceId);
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
            dataset.Grouping.Add(new QueryGrouping("ResourceId", QueryColumnType.Dimension));
            dataset.Grouping.Add(new QueryGrouping("MeterSubcategory", QueryColumnType.Dimension));
            dataset.Grouping.Add(new QueryGrouping("Meter", QueryColumnType.Dimension));

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

            var armClient = new ArmClient(_credential);
            var response = await armClient.UsageQueryAsync(scope, queryDefinition);
            var result = response.Value;

            if (result?.Rows == null || result.Rows.Count == 0)
            {
                return null;
            }

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
        double totalProtocolOps = 0;
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
