using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AzFilesOptimizer.Backend.Models;

namespace AzFilesOptimizer.Backend.Services;

/// <summary>
/// Service for fetching and caching Azure Retail Prices API data
/// </summary>
public class RetailPricingService
{
    private readonly ILogger _logger;
    private readonly TableClient _tableClient;
    private readonly HttpClient _httpClient;
    private readonly JobLogService? _jobLogService;
    
    // In-memory cache with 60-second expiry for debugging (TODO: change back to 24 hours)
    private readonly ConcurrentDictionary<string, CachedPrice> _memoryCache = new();
    private static readonly TimeSpan MemoryCacheExpiry = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan TableStorageExpiry = TimeSpan.FromSeconds(60);
    
    private const string RetailApiBaseUrl = "https://prices.azure.com/api/retail/prices";
    private const string ApiVersion = "2023-01-01-preview";
    
    public RetailPricingService(ILogger logger, TableServiceClient tableServiceClient, HttpClient httpClient, JobLogService? jobLogService = null)
    {
        _logger = logger;
        _httpClient = httpClient;
        _tableClient = tableServiceClient.GetTableClient("RetailPriceCache");
        _jobLogService = jobLogService;
    }
    
    /// <summary>
    /// Get Azure Files pricing for a specific tier and redundancy
    /// </summary>
    public async Task<AzureFilesMeterPricing?> GetAzureFilesPricingAsync(
        string region,
        AzureFilesAccessTier? accessTier = null,
        AzureFilesProvisionedTier? provisionedTier = null,
        StorageRedundancy redundancy = StorageRedundancy.LRS)
    {
        try
        {
            var pricing = new AzureFilesMeterPricing
            {
                AccessTier = accessTier,
                ProvisionedTier = provisionedTier,
                Redundancy = redundancy,
                Region = region
            };
            
            var redundancyStr = redundancy.ToString();
            
            // Build meter keys based on tier type
            if (accessTier.HasValue)
            {
                // Pay-as-you-go pricing
                var tierStr = accessTier.Value.ToString();
                
                // Storage meter
                var storageKey = RetailPriceCache.CreateAzureFilesMeterKey(tierStr, redundancyStr, "storage");
                var storageMeter = await GetCachedPriceAsync(region, storageKey, "AzureFiles");
                if (storageMeter == null)
                {
                    await RefreshAzureFilesPricingAsync(region, accessTier, redundancy);
                    storageMeter = await GetCachedPriceAsync(region, storageKey, "AzureFiles");
                }
                if (storageMeter != null)
                {
                    pricing.StoragePricePerGbMonth = storageMeter.UnitPrice;
                }
                
                // Transaction meters
                var writeOpsKey = RetailPriceCache.CreateAzureFilesMeterKey(tierStr, redundancyStr, "writeoperations");
                var writeOps = await GetCachedPriceAsync(region, writeOpsKey, "AzureFiles");
                if (writeOps != null)
                {
                    pricing.WriteOperationsPricePer10K = writeOps.UnitPrice;
                }
                
                var readOpsKey = RetailPriceCache.CreateAzureFilesMeterKey(tierStr, redundancyStr, "readoperations");
                var readOps = await GetCachedPriceAsync(region, readOpsKey, "AzureFiles");
                if (readOps != null)
                {
                    pricing.ReadOperationsPricePer10K = readOps.UnitPrice;
                }
                
                var listOpsKey = RetailPriceCache.CreateAzureFilesMeterKey(tierStr, redundancyStr, "listoperations");
                var listOps = await GetCachedPriceAsync(region, listOpsKey, "AzureFiles");
                if (listOps != null)
                {
                    pricing.ListOperationsPricePer10K = listOps.UnitPrice;
                }
            }
            else if (provisionedTier.HasValue)
            {
                // Provisioned pricing
                var tierStr = provisionedTier.Value.ToString();
                
                if (provisionedTier.Value == AzureFilesProvisionedTier.ProvisionedV1)
                {
                    var storageKey = RetailPriceCache.CreateAzureFilesMeterKey(tierStr, redundancyStr, "provisioned");
                    var storageMeter = await GetCachedPriceAsync(region, storageKey, "AzureFiles");
                    if (storageMeter == null)
                    {
                        await RefreshAzureFilesPricingAsync(region, null, redundancy, provisionedTier);
                        storageMeter = await GetCachedPriceAsync(region, storageKey, "AzureFiles");
                    }
                    if (storageMeter != null)
                    {
                        pricing.ProvisionedV1StoragePricePerGibMonth = storageMeter.UnitPrice;
                    }
                }
                else
                {
                    // Provisioned v2
                    var storageKey = RetailPriceCache.CreateAzureFilesMeterKey(tierStr, redundancyStr, "storage");
                    var iopsKey = RetailPriceCache.CreateAzureFilesMeterKey(tierStr, redundancyStr, "iops");
                    var throughputKey = RetailPriceCache.CreateAzureFilesMeterKey(tierStr, redundancyStr, "throughput");
                    
                    var storageMeter = await GetCachedPriceAsync(region, storageKey, "AzureFiles");
                    if (storageMeter == null)
                    {
                        await RefreshAzureFilesPricingAsync(region, null, redundancy, provisionedTier);
                        storageMeter = await GetCachedPriceAsync(region, storageKey, "AzureFiles");
                    }
                    
                    if (storageMeter != null)
                    {
                        pricing.ProvisionedStoragePricePerGibHour = storageMeter.UnitPrice;
                    }
                    
                    var iopsMeter = await GetCachedPriceAsync(region, iopsKey, "AzureFiles");
                    if (iopsMeter != null)
                    {
                        pricing.ProvisionedIOPSPricePerHour = iopsMeter.UnitPrice;
                    }
                    
                    var throughputMeter = await GetCachedPriceAsync(region, throughputKey, "AzureFiles");
                    if (throughputMeter != null)
                    {
                        pricing.ProvisionedThroughputPricePerMiBPerSecPerHour = throughputMeter.UnitPrice;
                    }
                }
            }
            
            // Snapshot pricing (common to all tiers)
            var snapshotKey = RetailPriceCache.CreateAzureFilesMeterKey("snapshot", redundancyStr, "storage");
            var snapshotMeter = await GetCachedPriceAsync(region, snapshotKey, "AzureFiles");
            if (snapshotMeter != null)
            {
                pricing.SnapshotPricePerGbMonth = snapshotMeter.UnitPrice;
            }
            
            // Egress pricing (common to all tiers)
            // Note: Egress pricing is typically not tier-specific, using "common" as tier
            var egressKey = RetailPriceCache.CreateAzureFilesMeterKey("common", redundancyStr, "egress");
            var egressMeter = await GetCachedPriceAsync(region, egressKey, "AzureFiles");
            if (egressMeter == null)
            {
                // Try to refresh if not in cache
                await RefreshAzureFilesPricingAsync(region, accessTier, redundancy);
                egressMeter = await GetCachedPriceAsync(region, egressKey, "AzureFiles");
            }
            if (egressMeter != null)
            {
                pricing.EgressPricePerGb = egressMeter.UnitPrice;
            }
            
            return pricing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Azure Files pricing for region {Region}", region);
            return null;
        }
    }
    
    /// <summary>
    /// Get Azure NetApp Files pricing for a specific service level
    /// </summary>
    public async Task<AnfMeterPricing?> GetAnfPricingAsync(string region, AnfServiceLevel serviceLevel, string? jobId = null)
    {
        try
        {
            var pricing = new AnfMeterPricing
            {
                ServiceLevel = serviceLevel,
                Region = region
            };
            
            var serviceLevelStr = serviceLevel.ToString();
            
            // Capacity meter
            var capacityKey = RetailPriceCache.CreateAnfMeterKey(serviceLevelStr, "capacity");
            var capacityMeter = await GetCachedPriceAsync(region, capacityKey, "ANF");
            
            // Force refresh if cache is missing OR has suspicious $0 price
            if (capacityMeter == null || capacityMeter.UnitPrice == 0)
            {
                if (_jobLogService != null && jobId != null)
                {
                    await _jobLogService.AddLogAsync(jobId, $"🔄 FORCING REFRESH: ANF pricing for {region}/{serviceLevel} (cache missing={capacityMeter == null} or price=${capacityMeter?.UnitPrice ?? 0})");
                }
                await RefreshAnfPricingAsync(region, serviceLevel, jobId);
                capacityMeter = await GetCachedPriceAsync(region, capacityKey, "ANF");
                if (_jobLogService != null && jobId != null)
                {
                    await _jobLogService.AddLogAsync(jobId, $"🔄 AFTER REFRESH: ANF pricing cached price is now ${capacityMeter?.UnitPrice ?? 0}");
                    await _jobLogService.AddLogAsync(jobId, $"🔍 DEBUG: CapacityMeter details - IsNull={capacityMeter == null}, UnitPrice={capacityMeter?.UnitPrice}, MeterName='{capacityMeter?.MeterName}', RowKey='{capacityMeter?.RowKey}'");
                }
            }
            else
            {
                if (_jobLogService != null && jobId != null)
                {
                    await _jobLogService.AddLogAsync(jobId, $"✅ CACHE HIT: ANF pricing for {region}/{serviceLevel} found in cache with price ${capacityMeter.UnitPrice}");
                }
            }
            
            if (capacityMeter != null)
            {
                pricing.CapacityPricePerGibHour = capacityMeter.UnitPrice;
                if (_jobLogService != null && jobId != null)
                {
                    await _jobLogService.AddLogAsync(jobId, $"✅ ASSIGNED: pricing.CapacityPricePerGibHour = ${pricing.CapacityPricePerGibHour} from capacityMeter.UnitPrice = ${capacityMeter.UnitPrice}");
                }
            }
            else
            {
                if (_jobLogService != null && jobId != null)
                {
                    await _jobLogService.AddLogAsync(jobId, $"⚠️ WARNING: capacityMeter is NULL after cache retrieval! CapacityPricePerGibHour will remain at default value ${pricing.CapacityPricePerGibHour}");
                }
            }
            
            // Flexible throughput (only for Flexible service level)
            if (serviceLevel == AnfServiceLevel.Flexible)
            {
                var throughputKey = RetailPriceCache.CreateAnfMeterKey(serviceLevelStr, "throughput");
                var throughputMeter = await GetCachedPriceAsync(region, throughputKey, "ANF");
                if (throughputMeter != null)
                {
                    pricing.FlexibleThroughputPricePerMiBSecHour = throughputMeter.UnitPrice;
                }
            }
            
            // Cool tier pricing (available for all service levels, uses shared SKU 'Standard Storage with Cool Access')
            // Note: Cool pricing is NOT service-level specific
            var coolStorageKey = RetailPriceCache.CreateAnfMeterKey("CoolAccess", "coolstorage");
            var coolStorageMeter = await GetCachedPriceAsync(region, coolStorageKey, "ANF");
            if (coolStorageMeter == null)
            {
                // Need to refresh cool pricing separately
                await RefreshAnfCoolPricingAsync(region);
                coolStorageMeter = await GetCachedPriceAsync(region, coolStorageKey, "ANF");
            }
            if (coolStorageMeter != null)
            {
                pricing.CoolTierStoragePricePerGibMonth = coolStorageMeter.UnitPrice;
            }
            
            var coolTransferKey = RetailPriceCache.CreateAnfMeterKey("CoolAccess", "cooltransfer");
            var coolTransferMeter = await GetCachedPriceAsync(region, coolTransferKey, "ANF");
            if (coolTransferMeter != null)
            {
                pricing.CoolTierDataTransferPricePerGib = coolTransferMeter.UnitPrice;
            }
            
            return pricing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting ANF pricing for region {Region}, service level {ServiceLevel}", 
                region, serviceLevel);
            return null;
        }
    }
    
    /// <summary>
    /// Get Managed Disk pricing for a specific disk type and SKU
    /// </summary>
    public async Task<ManagedDiskMeterPricing?> GetManagedDiskPricingAsync(
        string region,
        ManagedDiskType diskType,
        string sku,
        StorageRedundancy redundancy = StorageRedundancy.LRS)
    {
        try
        {
            var pricing = new ManagedDiskMeterPricing
            {
                DiskType = diskType,
                SKU = sku,
                Region = region,
                Redundancy = redundancy
            };
            
            var redundancyStr = redundancy.ToString();
            var meterKey = RetailPriceCache.CreateManagedDiskMeterKey(sku, redundancyStr);
            
            var meter = await GetCachedPriceAsync(region, meterKey, "ManagedDisk");
            if (meter == null)
            {
                await RefreshManagedDiskPricingAsync(region, diskType, sku, redundancy);
                meter = await GetCachedPriceAsync(region, meterKey, "ManagedDisk");
            }
            
            if (meter != null)
            {
                // For fixed-tier disks (Premium SSD, Standard SSD, Standard HDD)
                if (diskType != ManagedDiskType.PremiumSSDv2 && diskType != ManagedDiskType.UltraDisk)
                {
                    pricing.PricePerMonth = meter.UnitPrice;
                }
                else if (diskType == ManagedDiskType.PremiumSSDv2)
                {
                    // Premium SSD v2 has separate meters for capacity, IOPS, and throughput
                    // This is the capacity meter
                    pricing.CapacityPricePerGibMonth = meter.UnitPrice;
                    
                    // Get IOPS and throughput meters
                    var iopsKey = RetailPriceCache.CreateManagedDiskMeterKey($"{sku}-iops", redundancyStr);
                    var iopsMeter = await GetCachedPriceAsync(region, iopsKey, "ManagedDisk");
                    if (iopsMeter != null)
                    {
                        pricing.IOPSPricePerMonth = iopsMeter.UnitPrice;
                    }
                    
                    var throughputKey = RetailPriceCache.CreateManagedDiskMeterKey($"{sku}-throughput", redundancyStr);
                    var throughputMeter = await GetCachedPriceAsync(region, throughputKey, "ManagedDisk");
                    if (throughputMeter != null)
                    {
                        pricing.ThroughputPricePerMiBSecMonth = throughputMeter.UnitPrice;
                    }
                }
                else if (diskType == ManagedDiskType.UltraDisk)
                {
                    // Ultra Disk has separate meters
                    pricing.UltraCapacityPricePerGibMonth = meter.UnitPrice;
                }
            }
            
            // Bursting pricing (P30+)
            if (pricing.SupportsBursting)
            {
                var burstEnableKey = RetailPriceCache.CreateManagedDiskMeterKey($"{sku}-burst-enable", redundancyStr);
                var burstEnableMeter = await GetCachedPriceAsync(region, burstEnableKey, "ManagedDisk");
                if (burstEnableMeter != null)
                {
                    pricing.BurstingEnablementFeePerMonth = burstEnableMeter.UnitPrice;
                }
                
                var burstTxKey = RetailPriceCache.CreateManagedDiskMeterKey($"{sku}-burst-tx", redundancyStr);
                var burstTxMeter = await GetCachedPriceAsync(region, burstTxKey, "ManagedDisk");
                if (burstTxMeter != null)
                {
                    pricing.BurstingTransactionPricePer10K = burstTxMeter.UnitPrice;
                }
            }
            
            // Snapshot pricing
            var snapshotKey = RetailPriceCache.CreateManagedDiskMeterKey("snapshot", redundancyStr);
            var snapshotMeter = await GetCachedPriceAsync(region, snapshotKey, "ManagedDisk");
            if (snapshotMeter != null)
            {
                pricing.SnapshotPricePerGibMonth = snapshotMeter.UnitPrice;
            }
            
            return pricing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Managed Disk pricing for region {Region}, type {DiskType}, SKU {SKU}", 
                region, diskType, sku);
            return null;
        }
    }
    
    /// <summary>
    /// Refresh Azure Files pricing from Retail API
    /// </summary>
    private async Task RefreshAzureFilesPricingAsync(
        string region,
        AzureFilesAccessTier? accessTier = null,
        StorageRedundancy redundancy = StorageRedundancy.LRS,
        AzureFilesProvisionedTier? provisionedTier = null)
    {
        try
        {
            var query = BuildAzureFilesQuery(region, accessTier, redundancy, provisionedTier);
            var meters = await QueryRetailApiAsync(query);
            
            await CacheMetersAsync(region, "AzureFiles", meters, (meter) =>
            {
                // Determine meter type from meter name
                var meterNameLower = meter.MeterName.ToLowerInvariant();
                var skuNameLower = meter.SkuName?.ToLowerInvariant() ?? string.Empty;
                
                string tierStr = "";
                string meterType = "";
                string redundancyStr = "";
                
                // Extract tier
                if (skuNameLower.Contains("hot"))
                    tierStr = "Hot";
                else if (skuNameLower.Contains("cool"))
                    tierStr = "Cool";
                else if (skuNameLower.Contains("transaction optimized"))
                    tierStr = "TransactionOptimized";
                else if (skuNameLower.Contains("provisioned v2"))
                    tierStr = "ProvisionedV2SSD";
                else if (skuNameLower.Contains("provisioned"))
                    tierStr = "ProvisionedV1";
                else if (meterNameLower.Contains("snapshot"))
                    tierStr = "snapshot";
                
                // Extract redundancy
                if (skuNameLower.Contains("lrs"))
                    redundancyStr = "LRS";
                else if (skuNameLower.Contains("zrs"))
                    redundancyStr = "ZRS";
                else if (skuNameLower.Contains("grs"))
                    redundancyStr = "GRS";
                else if (skuNameLower.Contains("gzrs"))
                    redundancyStr = "GZRS";
                
                // Determine meter type
                if (meterNameLower.Contains("data stored"))
                    meterType = "storage";
                else if (meterNameLower.Contains("write operations"))
                    meterType = "writeoperations";
                else if (meterNameLower.Contains("read operations"))
                    meterType = "readoperations";
                else if (meterNameLower.Contains("list") || meterNameLower.Contains("create container"))
                    meterType = "listoperations";
                else if (meterNameLower.Contains("provisioned storage"))
                    meterType = "storage";
                else if (meterNameLower.Contains("provisioned iops"))
                    meterType = "iops";
                else if (meterNameLower.Contains("provisioned throughput"))
                    meterType = "throughput";
                else if (meterNameLower.Contains("data transfer out") || meterNameLower.Contains("egress"))
                {
                    meterType = "egress";
                    tierStr = "common"; // Egress is common across tiers
                }
                
                return RetailPriceCache.CreateAzureFilesMeterKey(tierStr, redundancyStr, meterType);
            });
            
            _logger.LogInformation("Refreshed Azure Files pricing for region {Region}, cached {Count} meters", 
                region, meters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing Azure Files pricing for region {Region}", region);
        }
    }
    
    /// <summary>
    /// Refresh ANF pricing from Retail API
    /// </summary>
    private async Task RefreshAnfPricingAsync(string region, AnfServiceLevel serviceLevel, string? jobId = null)
    {
        try
        {
            var query = BuildAnfQuery(region, serviceLevel);
            _logger.LogInformation("Querying ANF pricing API for region {Region}, service level {ServiceLevel}: {Query}", 
                region, serviceLevel, query);
            var meters = await QueryRetailApiAsync(query);
            _logger.LogInformation("ANF pricing API returned {Count} meters for {ServiceLevel}", 
                meters.Count, serviceLevel);
            foreach (var meter in meters)
            {
                _logger.LogInformation("  - Meter: {MeterName}, Price: ${Price}, SKU: {SKU}", 
                    meter.MeterName, meter.RetailPrice, meter.SkuName);
            }
            
            await CacheMetersAsync(region, "ANF", meters, (meter) =>
            {
                var meterNameLower = meter.MeterName.ToLowerInvariant();
                
                string serviceLevelStr = serviceLevel.ToString();
                string meterType = "";
                
                if (meterNameLower.Contains("capacity"))
                    meterType = "capacity";
                else if (meterNameLower.Contains("throughput"))
                    meterType = "throughput";
                
                if (string.IsNullOrEmpty(meterType))
                {
                    return ""; // Return empty to skip caching
                }
                
                return RetailPriceCache.CreateAnfMeterKey(serviceLevelStr, meterType);
            }, jobId);
            
            _logger.LogInformation("Refreshed ANF pricing for region {Region}, service level {ServiceLevel}, cached {Count} meters", 
                region, serviceLevel, meters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing ANF pricing for region {Region}, service level {ServiceLevel}", 
                region, serviceLevel);
        }
    }
    
    /// <summary>
    /// Refresh ANF Cool Access pricing from Retail API
    /// Cool storage uses a separate SKU: 'Standard Storage with Cool Access'
    /// </summary>
    private async Task RefreshAnfCoolPricingAsync(string region)
    {
        try
        {
            var query = BuildAnfCoolQuery(region);
            var meters = await QueryRetailApiAsync(query);
            
            await CacheMetersAsync(region, "ANF", meters, (meter) =>
            {
                var meterNameLower = meter.MeterName.ToLowerInvariant();
                string meterType = "";
                
                if (meterNameLower.Contains("capacity"))
                    meterType = "coolstorage";
                else if (meterNameLower.Contains("transfer"))
                    meterType = "cooltransfer";
                
                return RetailPriceCache.CreateAnfMeterKey("CoolAccess", meterType);
            });
            
            _logger.LogInformation("Refreshed ANF Cool Access pricing for region {Region}, cached {Count} meters", 
                region, meters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing ANF Cool Access pricing for region {Region}", region);
        }
    }
    
    /// <summary>
    /// Refresh Managed Disk pricing from Retail API
    /// </summary>
    private async Task RefreshManagedDiskPricingAsync(
        string region,
        ManagedDiskType diskType,
        string sku,
        StorageRedundancy redundancy)
    {
        try
        {
            var query = BuildManagedDiskQuery(region, diskType, sku, redundancy);
            var meters = await QueryRetailApiAsync(query);
            
            await CacheMetersAsync(region, "ManagedDisk", meters, (meter) =>
            {
                var meterNameLower = meter.MeterName.ToLowerInvariant();
                var redundancyStr = redundancy.ToString();
                
                // Extract SKU from meter name (e.g., "P30 LRS Disk" -> "P30")
                var skuMatch = System.Text.RegularExpressions.Regex.Match(meter.MeterName, @"([PESA]\d+)");
                var extractedSku = skuMatch.Success ? skuMatch.Groups[1].Value : sku;
                
                if (meterNameLower.Contains("burst") && meterNameLower.Contains("enablement"))
                    return RetailPriceCache.CreateManagedDiskMeterKey($"{extractedSku}-burst-enable", redundancyStr);
                else if (meterNameLower.Contains("burst") && meterNameLower.Contains("transaction"))
                    return RetailPriceCache.CreateManagedDiskMeterKey($"{extractedSku}-burst-tx", redundancyStr);
                else if (meterNameLower.Contains("snapshot"))
                    return RetailPriceCache.CreateManagedDiskMeterKey("snapshot", redundancyStr);
                else if (meterNameLower.Contains("iops"))
                    return RetailPriceCache.CreateManagedDiskMeterKey($"{extractedSku}-iops", redundancyStr);
                else if (meterNameLower.Contains("throughput"))
                    return RetailPriceCache.CreateManagedDiskMeterKey($"{extractedSku}-throughput", redundancyStr);
                else
                    return RetailPriceCache.CreateManagedDiskMeterKey(extractedSku, redundancyStr);
            });
            
            _logger.LogInformation("Refreshed Managed Disk pricing for region {Region}, type {DiskType}, SKU {SKU}, cached {Count} meters", 
                region, diskType, sku, meters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing Managed Disk pricing for region {Region}, type {DiskType}, SKU {SKU}", 
                region, diskType, sku);
        }
    }
    
    /// <summary>
    /// Build OData query for Azure Files
    /// </summary>
    private string BuildAzureFilesQuery(
        string region,
        AzureFilesAccessTier? accessTier,
        StorageRedundancy redundancy,
        AzureFilesProvisionedTier? provisionedTier)
    {
        var armRegionName = NormalizeRegionForApi(region);
        var filters = new List<string>
        {
            "serviceName eq 'Storage'",
            "productName eq 'Files'",
            $"armRegionName eq '{armRegionName}'",
            "priceType eq 'Consumption'"
        };
        
        // Add tier-specific filters
        if (accessTier.HasValue)
        {
            var tierName = accessTier.Value switch
            {
                AzureFilesAccessTier.Hot => "Hot",
                AzureFilesAccessTier.Cool => "Cool",
                AzureFilesAccessTier.TransactionOptimized => "Transaction Optimized",
                _ => ""
            };
            filters.Add($"skuName contains '{tierName}'");
        }
        else if (provisionedTier.HasValue)
        {
            filters.Add("skuName contains 'Provisioned'");
        }
        
        // Add redundancy filter
        filters.Add($"skuName contains '{redundancy}'");
        
        return string.Join(" and ", filters);
    }
    
    /// <summary>
    /// Build OData query for ANF
    /// </summary>
    private string BuildAnfQuery(string region, AnfServiceLevel serviceLevel)
    {
        var armRegionName = NormalizeRegionForApi(region);
        var serviceLevelStr = serviceLevel.ToString();
        
        // SKU name pattern: API uses "Flexible Service Level" but enum is just "Flexible"
        var skuName = serviceLevel == AnfServiceLevel.Flexible ? "Flexible Service Level" : serviceLevelStr;
        
        // Don't filter by specific meterName - we want ALL meters for this SKU (capacity, throughput, cool storage, etc.)
        var filters = new List<string>
        {
            "serviceFamily eq 'Storage'",
            "serviceName eq 'Azure NetApp Files'",
            "productName eq 'Azure NetApp Files'",
            $"armRegionName eq '{armRegionName}'",
            $"skuName eq '{skuName}'"
        };
        
        return string.Join(" and ", filters);
    }
    
    /// <summary>
    /// Build OData query for ANF Cool Access pricing
    /// Cool storage uses SKU 'Standard Storage with Cool Access' for all service levels
    /// </summary>
    private string BuildAnfCoolQuery(string region)
    {
        var armRegionName = NormalizeRegionForApi(region);
        
        var filters = new List<string>
        {
            "serviceFamily eq 'Storage'",
            "serviceName eq 'Azure NetApp Files'",
            "productName eq 'Azure NetApp Files'",
            $"armRegionName eq '{armRegionName}'",
            "skuName eq 'Standard Storage with Cool Access'"
        };
        
        return string.Join(" and ", filters);
    }
    
    /// <summary>
    /// Build OData query for Managed Disks
    /// </summary>
    private string BuildManagedDiskQuery(
        string region,
        ManagedDiskType diskType,
        string sku,
        StorageRedundancy redundancy)
    {
        var armRegionName = NormalizeRegionForApi(region);
        var productName = diskType switch
        {
            ManagedDiskType.PremiumSSD => "Premium SSD Managed Disks",
            ManagedDiskType.PremiumSSDv2 => "Premium SSD v2 Managed Disks",
            ManagedDiskType.StandardSSD => "Standard SSD Managed Disks",
            ManagedDiskType.StandardHDD => "Standard HDD Managed Disks",
            ManagedDiskType.UltraDisk => "Ultra Disks",
            _ => "Managed Disks"
        };
        
        var filters = new List<string>
        {
            "serviceName eq 'Storage'",
            $"productName eq '{productName}'",
            $"armRegionName eq '{armRegionName}'",
            $"meterName contains '{sku}'",
            $"meterName contains '{redundancy}'"
        };
        
        return string.Join(" and ", filters);
    }
    
    /// <summary>
    /// Query Azure Retail Prices API with pagination
    /// </summary>
    private async Task<List<RetailPriceMeter>> QueryRetailApiAsync(string filter)
    {
        var allMeters = new List<RetailPriceMeter>();
        var url = $"{RetailApiBaseUrl}?api-version={ApiVersion}&$filter={Uri.EscapeDataString(filter)}";
        
        while (!string.IsNullOrEmpty(url))
        {
            try
            {
                _logger.LogInformation("Querying Retail API: {Url}", url);
                
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                
                var result = JsonSerializer.Deserialize<RetailPriceResponse>(content, options);
                
                if (result?.Items != null)
                {
                    allMeters.AddRange(result.Items);
                }
                
                // Handle pagination
                url = result?.NextPageLink ?? string.Empty;
                
                // Safety: Limit to 10 pages to prevent infinite loops
                if (allMeters.Count > 1000)
                {
                    _logger.LogWarning("Retrieved more than 1000 meters, stopping pagination");
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying Retail API: {Url}", url);
                break;
            }
        }
        
        _logger.LogInformation("Retrieved {Count} meters from Retail API", allMeters.Count);
        return allMeters;
    }
    
    /// <summary>
    /// Cache meters in Table Storage
    /// </summary>
    private async Task CacheMetersAsync(
        string region,
        string resourceType,
        List<RetailPriceMeter> meters,
        Func<RetailPriceMeter, string> meterKeyFunc,
        string? jobId = null)
    {
        // Ensure table exists before attempting to cache
        try
        {
            await _tableClient.CreateIfNotExistsAsync();
            _logger.LogInformation("RetailPriceCache table ensured before caching {Count} meters", meters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure RetailPriceCache table exists. Caching will likely fail.");
        }
        
        foreach (var meter in meters)
        {
            try
            {
                var meterKey = meterKeyFunc(meter);
                if (string.IsNullOrWhiteSpace(meterKey))
                {
                    if (_jobLogService != null && jobId != null)
                    {
                        await _jobLogService.AddLogAsync(jobId, $"⚠️ SKIPPING meter with unrecognized name: {meter.MeterName} (SKU: {meter.SkuName})");
                    }
                    continue;
                }

                if (_jobLogService != null && jobId != null)
                {
                    await _jobLogService.AddLogAsync(jobId, $"📝 CACHING: '{meter.MeterName}' → key '{meterKey}' with price ${meter.RetailPrice}");
                }
                
                var cacheEntity = new RetailPriceCache
                {
                    PartitionKey = region.ToLowerInvariant(),
                    RowKey = meterKey,
                    ResourceType = resourceType,
                    MeterName = meter.MeterName,
                    ProductName = meter.ProductName,
                    SkuName = meter.SkuName,
                    ServiceName = meter.ServiceName,
                    MeterId = meter.MeterId,
                    UnitPrice = meter.RetailPrice,
                    UnitOfMeasure = meter.UnitOfMeasure,
                    Currency = meter.CurrencyCode,
                    EffectiveDate = meter.EffectiveStartDate,
                    ArmRegionName = meter.ArmRegionName,
                    Location = meter.Location,
                    ArmSkuName = meter.ArmSkuName ?? string.Empty,
                    LastUpdated = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.Add(TableStorageExpiry)
                };
                
                await _tableClient.UpsertEntityAsync(cacheEntity);
                
                // Also cache in memory
                var memoryKey = $"{region.ToLowerInvariant()}:{meterKey}";
                _memoryCache[memoryKey] = new CachedPrice
                {
                    Price = cacheEntity,
                    CachedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error caching meter {MeterName}", meter.MeterName);
            }
        }
    }
    
    /// <summary>
    /// Get cached price from memory or Table Storage
    /// </summary>
    private async Task<RetailPriceCache?> GetCachedPriceAsync(string region, string meterKey, string resourceType)
    {
        var memoryKey = $"{region.ToLowerInvariant()}:{meterKey}";
        
        _logger.LogInformation("[CACHE LOOKUP] Region={Region}, MeterKey={MeterKey}, MemoryKey={MemoryKey}", region, meterKey, memoryKey);
        
        // Check memory cache first
        if (_memoryCache.TryGetValue(memoryKey, out var cached))
        {
            var age = DateTime.UtcNow - cached.CachedAt;
            _logger.LogInformation("[CACHE HIT - MEMORY] Found in memory cache. Age={Age}s, UnitPrice=${Price}, Expiry={Expiry}s", 
                age.TotalSeconds, cached.Price.UnitPrice, MemoryCacheExpiry.TotalSeconds);
            
            if (DateTime.UtcNow - cached.CachedAt < MemoryCacheExpiry)
            {
                return cached.Price;
            }
            // Remove expired entry
            _logger.LogWarning("[CACHE EXPIRED - MEMORY] Memory cache entry expired. Removing.");
            _memoryCache.TryRemove(memoryKey, out _);
        }
        else
        {
            _logger.LogInformation("[CACHE MISS - MEMORY] Not found in memory cache. Checking Table Storage...");
        }
        
        // Check Table Storage
        try
        {
            _logger.LogInformation("[TABLE STORAGE LOOKUP] PartitionKey={PartitionKey}, RowKey={RowKey}", 
                region.ToLowerInvariant(), meterKey);
            
            var response = await _tableClient.GetEntityAsync<RetailPriceCache>(
                region.ToLowerInvariant(),
                meterKey);
            
            var price = response.Value;
            
            _logger.LogInformation("[TABLE STORAGE RESULT] Found entity. UnitPrice=${Price}, IsExpired={IsExpired}, MeterName='{MeterName}'", 
                price?.UnitPrice ?? 0, price?.IsExpired ?? true, price?.MeterName ?? "null");
            
            if (price != null && !price.IsExpired)
            {
                // Cache in memory
                _memoryCache[memoryKey] = new CachedPrice
                {
                    Price = price,
                    CachedAt = DateTime.UtcNow
                };
                
                _logger.LogInformation("[CACHE RESTORED] Restored to memory cache from Table Storage with UnitPrice=${Price}", price.UnitPrice);
                
                return price;
            }
            else
            {
                _logger.LogWarning("[TABLE STORAGE EXPIRED] Table Storage entry expired or null");
            }
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Not found, will need to query API
            _logger.LogInformation("[TABLE STORAGE 404] Not found in Table Storage");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TABLE STORAGE ERROR] Error retrieving cached price for {Region}:{MeterKey}", region, meterKey);
        }
        
        _logger.LogWarning("[CACHE MISS - TOTAL] Returning null - no valid cache entry found");
        return null;
    }
    
    /// <summary>
    /// Normalize region name for Retail API (e.g., "eastus" -> "eastus")
    /// </summary>
    private string NormalizeRegionForApi(string region)
    {
        // The API uses ARM region names (lowercase, no spaces)
        return region.ToLowerInvariant().Replace(" ", "");
    }
    
    /// <summary>
    /// Ensure Table Storage table exists
    /// </summary>
    public async Task EnsureTableExistsAsync()
    {
        try
        {
            await _tableClient.CreateIfNotExistsAsync();
            _logger.LogInformation("RetailPriceCache table ensured");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring RetailPriceCache table exists");
            throw;
        }
    }
    
    // Helper classes
    private class CachedPrice
    {
        public RetailPriceCache Price { get; set; } = new();
        public DateTime CachedAt { get; set; }
    }
    
    private class RetailPriceResponse
    {
        public List<RetailPriceMeter> Items { get; set; } = new();
        public string? NextPageLink { get; set; }
    }
    
    private class RetailPriceMeter
    {
        public string CurrencyCode { get; set; } = string.Empty;
        public double RetailPrice { get; set; }
        public string ArmRegionName { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public DateTime EffectiveStartDate { get; set; }
        public string MeterId { get; set; } = string.Empty;
        public string MeterName { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string SkuName { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public string UnitOfMeasure { get; set; } = string.Empty;
        public string? ArmSkuName { get; set; }
    }
}
