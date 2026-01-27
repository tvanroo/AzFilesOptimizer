using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AzFilesOptimizer.Backend.Services;

/// <summary>
/// Client for Azure Retail Prices API with caching and error handling
/// </summary>
public class AzureRetailPricesClient
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AzureRetailPricesClient> _logger;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromHours(24);
    private const string BaseUrl = "https://prices.azure.com/api/retail/prices";

    public AzureRetailPricesClient(HttpClient httpClient, IMemoryCache cache, ILogger<AzureRetailPricesClient> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Get pricing for Azure Files storage
    /// </summary>
    public async Task<List<PriceItem>> GetAzureFilesPricingAsync(string region, string tier = "Hot", string redundancy = "LRS")
    {
        var cacheKey = $"files_pricing_{region}_{tier}_{redundancy}";
        if (_cache.TryGetValue<List<PriceItem>>(cacheKey, out var cachedResult))
        {
            return cachedResult ?? new List<PriceItem>();
        }

        try
        {
            // Map tier names to what Azure Retail Prices API expects
            // TransactionOptimized doesn't exist as a tier name - it's just standard Files pricing
            var apiTier = tier switch
            {
                "TransactionOptimized" => "", // Standard tier has no tier suffix in SKU name
                "Hot" => "Hot",
                "Cool" => "Cool",
                _ => "Hot" // Default to Hot
            };

            // Build filter based on whether we're filtering by tier or not
            string filter;
            if (string.IsNullOrEmpty(apiTier))
            {
                // For TransactionOptimized (standard), don't filter by tier, but exclude Hot and Cool
                filter = $"serviceName eq 'Storage' and productName eq 'Files' and armRegionName eq '{region}' and skuName contains '{redundancy}' and not (skuName contains 'Hot' or skuName contains 'Cool')";
            }
            else
            {
                filter = $"serviceName eq 'Storage' and productName eq 'Files' and armRegionName eq '{region}' and skuName contains '{apiTier}' and skuName contains '{redundancy}'";
            }
            
            var prices = await QueryPricesAsync(filter);
            
            _cache.Set(cacheKey, prices, _cacheExpiry);
            return prices;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Azure Files pricing for region {Region}, tier {Tier}, redundancy {Redundancy}", region, tier, redundancy);
            return new List<PriceItem>();
        }
    }

    /// <summary>
    /// Get pricing for Premium Files (provisioned)
    /// </summary>
    public async Task<List<PriceItem>> GetPremiumFilesPricingAsync(string region, string redundancy = "LRS")
    {
        var cacheKey = $"premium_files_pricing_{region}_{redundancy}";
        if (_cache.TryGetValue<List<PriceItem>>(cacheKey, out var cachedResult))
        {
            return cachedResult ?? new List<PriceItem>();
        }

        try
        {
            var filter = $"serviceName eq 'Storage' and productName eq 'Premium Files' and armRegionName eq '{region}' and skuName contains '{redundancy}'";
            var prices = await QueryPricesAsync(filter);
            
            _cache.Set(cacheKey, prices, _cacheExpiry);
            return prices;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Premium Files pricing for region {Region}, redundancy {Redundancy}", region, redundancy);
            return new List<PriceItem>();
        }
    }

    /// <summary>
    /// Get pricing for Azure NetApp Files
    /// </summary>
    public async Task<List<PriceItem>> GetAnfPricingAsync(string region, string tier = "Standard")
    {
        var cacheKey = $"anf_pricing_{region}_{tier}";
        if (_cache.TryGetValue<List<PriceItem>>(cacheKey, out var cachedResult))
        {
            return cachedResult ?? new List<PriceItem>();
        }

        try
        {
            var filter = $"serviceName eq 'Azure NetApp Files' and productName contains '{tier}' and armRegionName eq '{region}'";
            var prices = await QueryPricesAsync(filter);
            
            _cache.Set(cacheKey, prices, _cacheExpiry);
            return prices;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get ANF pricing for region {Region}, tier {Tier}", region, tier);
            return new List<PriceItem>();
        }
    }

    /// <summary>
    /// Get pricing for Managed Disks
    /// </summary>
    public async Task<List<PriceItem>> GetManagedDiskPricingAsync(string region, string diskType = "Premium SSD")
    {
        var cacheKey = $"disk_pricing_{region}_{diskType.Replace(" ", "_")}";
        if (_cache.TryGetValue<List<PriceItem>>(cacheKey, out var cachedResult))
        {
            return cachedResult ?? new List<PriceItem>();
        }

        try
        {
            var filter = $"serviceName eq 'Storage' and productName contains '{diskType}' and productName contains 'Managed Disks' and armRegionName eq '{region}'";
            var prices = await QueryPricesAsync(filter);
            
            _cache.Set(cacheKey, prices, _cacheExpiry);
            return prices;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Managed Disk pricing for region {Region}, type {DiskType}", region, diskType);
            return new List<PriceItem>();
        }
    }

    /// <summary>
    /// Query Azure Retail Prices API with custom filter
    /// </summary>
    private async Task<List<PriceItem>> QueryPricesAsync(string filter)
    {
        var url = $"{BaseUrl}?$filter={Uri.EscapeDataString(filter)}";
        _logger.LogDebug("Querying Azure Retail Prices API: {Url}", url);

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var priceResponse = JsonSerializer.Deserialize<PriceResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return priceResponse?.Items ?? new List<PriceItem>();
    }

    /// <summary>
    /// Clear all pricing cache
    /// </summary>
    public void ClearCache()
    {
        // Note: IMemoryCache doesn't provide a direct way to clear all entries
        // In a real implementation, you might use a distributed cache or track cache keys
        _logger.LogInformation("Cache clear requested (implementation depends on cache provider)");
    }
}

/// <summary>
/// Response from Azure Retail Prices API
/// </summary>
public class PriceResponse
{
    public List<PriceItem> Items { get; set; } = new();
    public string? NextPageLink { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Individual price item from Azure Retail Prices API
/// </summary>
public class PriceItem
{
    public string CurrencyCode { get; set; } = string.Empty;
    public double UnitPrice { get; set; }
    public double RetailPrice { get; set; }
    public string UnitOfMeasure { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string ArmRegionName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string SkuName { get; set; } = string.Empty;
    public string MeterName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsPrimaryMeterRegion { get; set; }
    public string ArmSkuName { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string SkuId { get; set; } = string.Empty;
    public DateTime EffectiveStartDate { get; set; }
    public string ServiceId { get; set; } = string.Empty;
    public string ServiceFamily { get; set; } = string.Empty;
    public string TermLength { get; set; } = string.Empty;
    
    /// <summary>
    /// Check if this price item is for storage capacity
    /// </summary>
    public bool IsStorageCapacity => 
        MeterName.Contains("Data Stored", StringComparison.OrdinalIgnoreCase) ||
        MeterName.Contains("Provisioned", StringComparison.OrdinalIgnoreCase) ||
        MeterName.Contains("Capacity", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Check if this price item is for transactions
    /// </summary>
    public bool IsTransaction => 
        MeterName.Contains("Transaction", StringComparison.OrdinalIgnoreCase) ||
        MeterName.Contains("Operation", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Check if this price item is for data transfer/egress
    /// </summary>
    public bool IsDataTransfer => 
        MeterName.Contains("Data Transfer", StringComparison.OrdinalIgnoreCase) ||
        MeterName.Contains("Bandwidth", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Check if this price item is for snapshots
    /// </summary>
    public bool IsSnapshot => 
        MeterName.Contains("Snapshot", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Get normalized meter type for cost component classification
    /// </summary>
    public string GetCostComponentType()
    {
        if (IsStorageCapacity) return "storage";
        if (IsTransaction) return "transactions";
        if (IsDataTransfer) return "egress";
        if (IsSnapshot) return "snapshots";
        return "other";
    }
}