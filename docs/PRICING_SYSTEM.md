# Azure Pricing System Documentation

This document describes the Azure Retail Pricing API integration, caching architecture, and troubleshooting guide for the AzFilesOptimizer pricing system.

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Azure Retail Pricing API](#azure-retail-pricing-api)
4. [Caching Strategy](#caching-strategy)
5. [Adding New Pricing Support](#adding-new-pricing-support)
6. [Troubleshooting](#troubleshooting)
7. [Common Issues and Solutions](#common-issues-and-solutions)

---

## Overview

The pricing system collects real-time Azure pricing data from the Azure Retail Pricing API and caches it locally to provide accurate cost estimates for:

- **Azure Files** (pay-as-you-go and provisioned tiers)
- **Azure NetApp Files** (Standard, Premium, Ultra, and Flexible service levels)
- **Managed Disks** (Premium SSD, Standard SSD, Standard HDD, Premium SSD v2, Ultra Disk)

All pricing is retrieved in **native units** (per hour, per month, per GiB, etc.) as returned by the Azure API and converted to monthly costs during calculation.

---

## Architecture

### Components

```
┌─────────────────────────────────────────────────────────────┐
│                    Cost Collection Service                   │
│  (Orchestrates pricing retrieval and cost calculation)       │
└─────────────────────┬───────────────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────────────────┐
│                  Retail Pricing Service                      │
│  • Queries Azure Retail Pricing API                          │
│  • Manages two-tier caching (memory + Table Storage)        │
│  • Provides pricing data to cost calculators                 │
└─────────────────────┬───────────────────────────────────────┘
                      │
         ┌────────────┴────────────┐
         ▼                         ▼
┌──────────────────┐      ┌──────────────────┐
│  Memory Cache    │      │  Table Storage   │
│  (60 sec TTL)    │      │  RetailPriceCache│
│  In-process      │      │  (60 sec TTL)    │
└──────────────────┘      └──────────────────┘
         │                         │
         └────────────┬────────────┘
                      ▼
         ┌────────────────────────┐
         │ Azure Retail Prices API│
         │ prices.azure.com       │
         └────────────────────────┘
```

### Key Classes

- **`RetailPricingService`** (`Services/RetailPricingService.cs`)
  - Queries Azure Retail Pricing API
  - Manages caching (memory + Table Storage)
  - Provides pricing data via `GetAnfPricingAsync()`, `GetAzureFilesPricingAsync()`, etc.

- **`CostCollectionService`** (`Services/CostCollectionService.cs`)
  - Orchestrates cost calculation for discovered resources
  - Calls `RetailPricingService` to get pricing
  - Applies pricing to resource metrics and configuration

- **`RetailPriceCache`** (`Models/RetailPriceCache.cs`)
  - Table Storage entity for persisting pricing data
  - Schema includes meter details, unit price, expiration, region, etc.

---

## Azure Retail Pricing API

### Base URL
```
https://prices.azure.com/api/retail/prices?api-version=2023-01-01-preview
```

### Query Structure

The API uses OData filters. Example query for ANF Flexible pricing:

```
$filter=
  serviceFamily eq 'Storage' and
  serviceName eq 'Azure NetApp Files' and
  productName eq 'Azure NetApp Files' and
  armRegionName eq 'southcentralus' and
  skuName eq 'Flexible Service Level'
```

### Response Format

```json
{
  "Items": [
    {
      "currencyCode": "USD",
      "retailPrice": 0.000181,
      "armRegionName": "southcentralus",
      "location": "US South Central",
      "meterId": "...",
      "meterName": "Flexible Service Level Capacity",
      "productName": "Azure NetApp Files",
      "skuName": "Flexible Service Level",
      "serviceName": "Azure NetApp Files",
      "unitOfMeasure": "1 Hour",
      "effectiveStartDate": "2024-01-01T00:00:00Z"
    }
  ],
  "NextPageLink": "..."
}
```

### Important Fields

- **`retailPrice`**: The unit price (use this directly, no conversion needed)
- **`unitOfMeasure`**: Defines the billing unit (e.g., "1 Hour", "1/Month", "10K")
- **`meterName`**: Human-readable meter description
- **`armRegionName`**: Lowercase region name (e.g., "southcentralus")
- **`skuName`**: SKU/tier identifier

### Unit of Measure Examples

| Service | Meter | Unit | Meaning |
|---------|-------|------|---------|
| ANF | Capacity | `1 Hour` | Price per GiB per hour |
| ANF | Throughput | `1 Hour` | Price per MiB/s per hour |
| Azure Files | Storage | `1 GB/Month` | Price per GB per month |
| Azure Files | Transactions | `10K` | Price per 10,000 operations |
| Managed Disk | P30 Disk | `1/Month` | Fixed monthly price |

---

## Caching Strategy

### Two-Tier Cache

1. **Memory Cache** (in-process, per-instance)
   - TTL: 60 seconds (configurable via `MemoryCacheExpiry`)
   - Fastest retrieval
   - Lost on app restart

2. **Table Storage Cache** (`RetailPriceCache` table)
   - TTL: 60 seconds (configurable via `TableStorageExpiry`)
   - Persistent across restarts
   - Shared across function instances

### Cache Keys

Cache keys are constructed from resource type, region, service level, and meter type:

#### ANF Cache Keys
```csharp
// Format: "anf-{serviceLevel}-{meterType}"
"anf-flexible-capacity"      // ANF Flexible capacity pricing
"anf-flexible-throughput"    // ANF Flexible throughput pricing
"anf-premium-capacity"       // ANF Premium capacity pricing
```

#### Azure Files Cache Keys
```csharp
// Format: "azurefiles-{tier}-{redundancy}-{meterType}"
"azurefiles-hot-lrs-storage"           // Hot tier LRS storage
"azurefiles-hot-lrs-writeoperations"   // Hot tier write operations
```

#### Managed Disk Cache Keys
```csharp
// Format: "manageddisk-{sku}-{redundancy}"
"manageddisk-p30-lrs"           // P30 Premium SSD LRS
"manageddisk-p30-lrs-snapshot"  // P30 snapshot pricing
```

### Cache Lookup Flow

```
1. Check memory cache (by region:meterKey)
   ├─ Hit + Not Expired → Return cached price
   └─ Miss or Expired → Continue to step 2

2. Check Table Storage (by PartitionKey=region, RowKey=meterKey)
   ├─ Hit + Not Expired → Cache in memory → Return price
   └─ Miss or Expired → Continue to step 3

3. Query Azure Retail Pricing API
   ├─ Success → Cache in both memory and Table Storage → Return price
   └─ Failure → Return null (log error)
```

### Cache Expiration

Current settings (for debugging):
- **Memory Cache TTL**: 60 seconds
- **Table Storage TTL**: 60 seconds

Production settings (recommended):
- **Memory Cache TTL**: 1-4 hours
- **Table Storage TTL**: 24 hours

Update in `RetailPricingService.cs`:
```csharp
private static readonly TimeSpan MemoryCacheExpiry = TimeSpan.FromHours(1);
private static readonly TimeSpan TableStorageExpiry = TimeSpan.FromHours(24);
```

---

## Adding New Pricing Support

### Step 1: Define the Pricing Model

Create a model class in `Models/` to represent the pricing structure:

```csharp
public class NewServicePricing
{
    public string Region { get; set; } = string.Empty;
    public string ServiceLevel { get; set; } = string.Empty;
    
    // Pricing fields
    public double CapacityPricePerGibHour { get; set; }
    public double TransactionPricePer10K { get; set; }
    
    // Add other meters as needed
}
```

### Step 2: Build the API Query

In `RetailPricingService.cs`, create a method to build the OData filter:

```csharp
private string BuildNewServiceQuery(string region, string serviceLevel)
{
    var armRegionName = NormalizeRegionForApi(region);
    
    var filters = new List<string>
    {
        "serviceFamily eq 'YourServiceFamily'",
        "serviceName eq 'Your Service Name'",
        $"armRegionName eq '{armRegionName}'",
        $"skuName eq '{serviceLevel}'"
    };
    
    return string.Join(" and ", filters);
}
```

### Step 3: Create the Refresh Method

Query the API and cache the results:

```csharp
private async Task RefreshNewServicePricingAsync(string region, string serviceLevel, string? jobId = null)
{
    try
    {
        var query = BuildNewServiceQuery(region, serviceLevel);
        _logger.LogInformation("Querying pricing API for {Service}: {Query}", "NewService", query);
        
        var meters = await QueryRetailApiAsync(query);
        
        _logger.LogInformation("Pricing API returned {Count} meters for {Service}", meters.Count, "NewService");
        
        await CacheMetersAsync(region, "NewService", meters, (meter) =>
        {
            var meterNameLower = meter.MeterName.ToLowerInvariant();
            string meterType = "";
            
            // Parse meter name to determine type
            if (meterNameLower.Contains("capacity"))
                meterType = "capacity";
            else if (meterNameLower.Contains("transaction"))
                meterType = "transaction";
            
            if (string.IsNullOrEmpty(meterType))
                return ""; // Skip unrecognized meters
            
            // Return cache key
            return $"newservice-{serviceLevel.ToLowerInvariant()}-{meterType}";
        }, jobId);
        
        _logger.LogInformation("Refreshed pricing for {Service} in {Region}", "NewService", region);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error refreshing pricing for {Service}", "NewService");
    }
}
```

### Step 4: Create the Public Get Method

Retrieve cached pricing or force refresh:

```csharp
public async Task<NewServicePricing?> GetNewServicePricingAsync(
    string region, 
    string serviceLevel, 
    string? jobId = null)
{
    try
    {
        var pricing = new NewServicePricing
        {
            Region = region,
            ServiceLevel = serviceLevel
        };
        
        // Capacity meter
        var capacityKey = $"newservice-{serviceLevel.ToLowerInvariant()}-capacity";
        var capacityMeter = await GetCachedPriceAsync(region, capacityKey, "NewService");
        
        // Force refresh if missing or zero price
        if (capacityMeter == null || capacityMeter.UnitPrice == 0)
        {
            if (_jobLogService != null && jobId != null)
            {
                await _jobLogService.AddLogAsync(jobId, 
                    $"🔄 FORCING REFRESH: NewService pricing for {region}/{serviceLevel}");
            }
            
            await RefreshNewServicePricingAsync(region, serviceLevel, jobId);
            capacityMeter = await GetCachedPriceAsync(region, capacityKey, "NewService");
            
            if (_jobLogService != null && jobId != null)
            {
                await _jobLogService.AddLogAsync(jobId, 
                    $"🔄 AFTER REFRESH: Cached price is now ${capacityMeter?.UnitPrice ?? 0}");
            }
        }
        
        if (capacityMeter != null)
        {
            pricing.CapacityPricePerGibHour = capacityMeter.UnitPrice;
        }
        
        // Add other meters as needed
        
        return pricing;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting pricing for NewService");
        return null;
    }
}
```

### Step 5: Integrate into Cost Calculation

In `CostCollectionService.cs`, call your new pricing method:

```csharp
var pricing = await _pricingService.GetNewServicePricingAsync(
    resource.Location,
    resource.ServiceLevel,
    jobId);

if (pricing == null)
{
    _logger.LogWarning("Could not retrieve pricing for {Resource}", resource.Name);
    return null;
}

// Calculate costs using pricing data
var monthlyCost = resource.CapacityGiB * pricing.CapacityPricePerGibHour * 730;
```

---

## Troubleshooting

### Debugging Tools

#### 1. Enable Detailed Logging

All pricing operations are logged with prefixes for easy filtering:

- `🔄 FORCING REFRESH` - API query triggered
- `📝 CACHING` - Pricing data being cached
- `🔍 DEBUG` - Cache retrieval details
- `✅ ASSIGNED` - Pricing assigned to model
- `⚠️ WARNING` - Issues or missing data
- `[CACHE LOOKUP]` - Cache key being searched
- `[TABLE STORAGE]` - Table Storage operations

#### 2. Check Cache Contents

View cached pricing in Azure Portal:

1. Navigate to: **Storage Account** → **Tables** → `RetailPriceCache`
2. Look for entries with:
   - **PartitionKey** = region (e.g., "southcentralus")
   - **RowKey** = meter key (e.g., "anf-flexible-capacity")
3. Check the `UnitPrice` field

#### 3. Test API Queries Manually

Use curl or browser to test queries:

```bash
curl "https://prices.azure.com/api/retail/prices?api-version=2023-01-01-preview&\$filter=serviceFamily%20eq%20'Storage'%20and%20serviceName%20eq%20'Azure%20NetApp%20Files'%20and%20armRegionName%20eq%20'southcentralus'%20and%20skuName%20eq%20'Flexible%20Service%20Level'"
```

---

## Common Issues and Solutions

### Issue 1: Cost Calculation Shows $0

**Symptoms:**
```
💰 Data Capacity Cost Calculation: 50.00 GiB * ($0.000000/Hour * 730 Hours) = $0.000
```

**Root Causes:**
1. Cache key mismatch (case sensitivity)
2. Pricing not retrieved from API
3. Table not created before cache write

**Solution:**
1. Check logs for `🔄 FORCING REFRESH` and `📝 CACHING` messages
2. Verify cache key format matches between storage and retrieval
3. Ensure `CreateIfNotExistsAsync()` is called before caching
4. Check that region names are consistently lowercased with `.ToLowerInvariant()`

**Fix Applied (2026-01-28):**
- Added `.ToLowerInvariant()` to all region-based cache key construction
- Added table initialization in `CacheMetersAsync()`

### Issue 2: RetailPriceCache Table Not Created

**Symptoms:**
- Table doesn't appear in Azure Portal
- 404 errors when querying Table Storage

**Solution:**

Ensure table creation happens before first use:

```csharp
private async Task CacheMetersAsync(...)
{
    // Ensure table exists before attempting to cache
    try
    {
        await _tableClient.CreateIfNotExistsAsync();
        _logger.LogInformation("RetailPriceCache table ensured before caching {Count} meters", meters.Count);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to ensure RetailPriceCache table exists.");
    }
    
    // ... continue with caching
}
```

### Issue 3: API Returns No Meters

**Symptoms:**
```
ANF pricing API returned 0 meters for Flexible
```

**Root Causes:**
1. Incorrect `skuName` filter
2. Region not available for service
3. Service level name mismatch

**Solution:**
1. Check Azure Retail Pricing API documentation for correct SKU names
2. Test query in browser with `$filter` parameter
3. Verify region supports the service level
4. Check for typos in service/product names

**Example Fix:**
```csharp
// Correct: ANF uses "Flexible Service Level" not "Flexible"
var skuName = serviceLevel == AnfServiceLevel.Flexible 
    ? "Flexible Service Level" 
    : serviceLevelStr;
```

### Issue 4: Cache Key Collision

**Symptoms:**
- Wrong pricing returned for different service levels
- Pricing changes unexpectedly

**Root Cause:**
Insufficient specificity in cache key construction.

**Solution:**

Ensure cache keys include all distinguishing factors:

```csharp
// ❌ BAD: Too generic
var key = $"anf-capacity";  // Collision between Standard/Premium/Ultra/Flexible

// ✅ GOOD: Specific to service level
var key = $"anf-{serviceLevel.ToLowerInvariant()}-capacity";
```

### Issue 5: Pricing Unit Mismatch

**Symptoms:**
- Cost is off by a factor of 730 (hourly vs monthly)
- Cost is off by a factor of 1024 (GiB vs GB)

**Root Cause:**
Misunderstanding of API unit of measure.

**Solution:**

1. Always check `unitOfMeasure` field in API response
2. Store price exactly as returned by API
3. Apply conversion during calculation, not during caching

**Example:**
```csharp
// API returns: unitOfMeasure = "1 Hour", retailPrice = 0.000181
// Store as-is: pricing.CapacityPricePerGibHour = 0.000181

// Convert to monthly during calculation:
var monthlyCost = capacityGiB * pricing.CapacityPricePerGibHour * 730;
```

### Issue 6: Logs Not Appearing in UI

**Symptoms:**
- Detailed logs visible in Azure Function logs but not in Job UI

**Root Cause:**
Logs not being routed through `JobLogService`.

**Solution:**

Pass `jobId` to pricing methods and use `JobLogService`:

```csharp
public async Task<AnfMeterPricing?> GetAnfPricingAsync(
    string region, 
    AnfServiceLevel serviceLevel, 
    string? jobId = null)  // ← Accept jobId
{
    // ...
    
    if (_jobLogService != null && jobId != null)
    {
        await _jobLogService.AddLogAsync(jobId, 
            $"🔄 FORCING REFRESH: ANF pricing for {region}/{serviceLevel}");
    }
}
```

---

## Testing Checklist

When adding new pricing support or troubleshooting:

- [ ] Query API manually and verify response
- [ ] Check meter names match filter logic
- [ ] Verify cache keys are unique and consistent
- [ ] Confirm region names are lowercase
- [ ] Test with cache miss (first run)
- [ ] Test with cache hit (subsequent runs)
- [ ] Verify Table Storage entry created
- [ ] Check logs appear in Job UI
- [ ] Validate cost calculation formula
- [ ] Compare calculated cost with Azure Portal
- [ ] Test across multiple regions
- [ ] Test cache expiration behavior

---

## Performance Considerations

### API Rate Limits

The Azure Retail Pricing API has rate limits. Best practices:

- Cache aggressively (24-hour TTL recommended)
- Batch queries when possible (one query returns multiple meters)
- Use memory cache to avoid Table Storage round-trips
- Implement exponential backoff for retry logic

### Cache TTL Tuning

**Short TTL (60 seconds)** - Use during development:
- Pros: Fresh pricing, easy debugging
- Cons: More API calls, slower performance

**Long TTL (24 hours)** - Use in production:
- Pros: Fast, fewer API calls, cost-effective
- Cons: Pricing may be stale for new meters

Pricing rarely changes mid-month, so 24-hour caching is safe for production.

---

## Related Documentation

- [Azure Retail Pricing API Documentation](https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices)
- [Azure NetApp Files Pricing](https://azure.microsoft.com/en-us/pricing/details/netapp/)
- [Azure Files Pricing](https://azure.microsoft.com/en-us/pricing/details/storage/files/)
- [Azure Managed Disks Pricing](https://azure.microsoft.com/en-us/pricing/details/managed-disks/)

---

## Version History

| Date | Change | Author |
|------|--------|--------|
| 2026-01-28 | Initial documentation created | Warp AI |
| 2026-01-28 | Fixed cache key case sensitivity bug | Warp AI |
| 2026-01-28 | Added table initialization to CacheMetersAsync | Warp AI |
