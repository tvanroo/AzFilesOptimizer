# Azure Retail Prices API URLs Reference

This document provides the complete Azure Retail Prices API URLs for all storage tier cost components. All URLs use `eastus2` as the example region; the application will substitute the actual resource region at runtime.

**Base API URL:** `https://prices.azure.com/api/retail/prices`

---

## Azure NetApp Files (ANF)

### ANF Standard (Regular)
```bash
# Capacity
https://prices.azure.com/api/retail/prices?$filter=serviceName eq 'Azure NetApp Files' and skuName eq 'Standard' and meterName eq 'Standard Capacity' and armRegionName eq 'eastus2'
```

### ANF Standard with Double Encryption
```bash
# Capacity (includes encryption premium)
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and skuName eq 'Standard Double Encrypted' and meterName eq 'Standard Double Encrypted Capacity' and armRegionName eq 'eastus2'
```

### ANF Standard with Cool Access
```bash
# Hot capacity
https://prices.azure.com/api/retail/prices?$filter=serviceName eq 'Azure NetApp Files' and skuName eq 'Standard' and meterName eq 'Standard Capacity' and armRegionName eq 'eastus2'

# Cool capacity
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and skuName eq 'Standard Storage with Cool Access' and meterName eq 'Standard Storage with Cool Access Capacity' and armRegionName eq 'eastus2'

# Data transfer (tiering and retrieval)
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and skuName eq 'Standard Storage with Cool Access' and meterName eq 'Standard Storage with Cool Access Data Transfer' and armRegionName eq 'eastus2'
```

### ANF Premium (Regular)
```bash
# Capacity
https://prices.azure.com/api/retail/prices?$filter=serviceName eq 'Azure NetApp Files' and skuName eq 'Premium' and meterName eq 'Premium Capacity' and armRegionName eq 'eastus2'
```

### ANF Premium with Double Encryption
```bash
# Capacity (includes encryption premium)
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and skuName eq 'Premium Double Encrypted' and meterName eq 'Premium Double Encrypted Capacity' and armRegionName eq 'eastus2'
```

### ANF Premium with Cool Access
```bash
# Hot capacity
https://prices.azure.com/api/retail/prices?$filter=serviceName eq 'Azure NetApp Files' and skuName eq 'Premium' and meterName eq 'Premium Capacity' and armRegionName eq 'eastus2'

# Cool capacity
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and skuName eq 'Standard Storage with Cool Access' and meterName eq 'Standard Storage with Cool Access Capacity' and armRegionName eq 'eastus2'

# Data transfer (tiering and retrieval)
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and skuName eq 'Standard Storage with Cool Access' and meterName eq 'Standard Storage with Cool Access Data Transfer' and armRegionName eq 'eastus2'
```

### ANF Ultra (Regular)
```bash
# Capacity
https://prices.azure.com/api/retail/prices?$filter=serviceName eq 'Azure NetApp Files' and skuName eq 'Ultra' and meterName eq 'Ultra Capacity' and armRegionName eq 'eastus2'
```

### ANF Ultra with Double Encryption
```bash
# Capacity (includes encryption premium)
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and skuName eq 'Ultra Double Encrypted' and meterName eq 'Ultra Double Encrypted Capacity' and armRegionName eq 'eastus2'
```

### ANF Ultra with Cool Access
```bash
# Hot capacity
https://prices.azure.com/api/retail/prices?$filter=serviceName eq 'Azure NetApp Files' and skuName eq 'Ultra' and meterName eq 'Ultra Capacity' and armRegionName eq 'eastus2'

# Cool capacity
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and skuName eq 'Standard Storage with Cool Access' and meterName eq 'Standard Storage with Cool Access Capacity' and armRegionName eq 'eastus2'

# Data transfer (tiering and retrieval)
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and skuName eq 'Standard Storage with Cool Access' and meterName eq 'Standard Storage with Cool Access Data Transfer' and armRegionName eq 'eastus2'
```

### ANF Flexible (Regular)
```bash
# Capacity
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and skuName eq 'Flexible Service Level' and meterName eq 'Flexible Service Level Capacity' and armRegionName eq 'eastus2'

# Throughput (per MiB/s)
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and skuName eq 'Flexible Service Level' and meterName eq 'Flexible Service Level Throughput MiBps' and armRegionName eq 'eastus2'
```

### ANF Flexible with Cool Access
```bash
# Hot capacity
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and skuName eq 'Flexible Service Level' and meterName eq 'Flexible Service Level Capacity' and armRegionName eq 'eastus2'

# Throughput (per MiB/s)
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and skuName eq 'Flexible Service Level' and meterName eq 'Flexible Service Level Throughput MiBps' and armRegionName eq 'eastus2'

# Cool capacity
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and skuName eq 'Standard Storage with Cool Access' and meterName eq 'Standard Storage with Cool Access Capacity' and armRegionName eq 'eastus2'

# Data transfer (tiering and retrieval)
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and skuName eq 'Standard Storage with Cool Access' and meterName eq 'Standard Storage with Cool Access Data Transfer' and armRegionName eq 'eastus2'
```

---

## Azure Files

### Azure Files Hot Tier (Pay-as-you-go)

**LRS:**
```bash
# Storage capacity
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Files' and skuName eq 'Hot LRS' and meterName eq 'Hot LRS Data Stored' and armRegionName eq 'eastus2'

# Write operations
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Files' and skuName eq 'Hot LRS' and meterName eq 'Hot LRS Write Operations' and armRegionName eq 'eastus2'

# Read operations
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Files' and skuName eq 'Hot LRS' and meterName eq 'Hot LRS Read Operations' and armRegionName eq 'eastus2'

# List operations
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Files' and skuName eq 'Hot LRS' and meterName eq 'Hot LRS List and Create Container Operations' and armRegionName eq 'eastus2'

# Other operations
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Files' and skuName eq 'Hot LRS' and meterName eq 'Hot LRS All Other Operations' and armRegionName eq 'eastus2'

# Snapshots
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Files' and skuName eq 'Hot LRS' and meterName eq 'Hot LRS Snapshots' and armRegionName eq 'eastus2'
```

**ZRS, GRS, GZRS:** Replace `Hot LRS` with `Hot ZRS`, `Hot GRS`, or `Hot GZRS` in the URLs above.

### Azure Files Cool Tier (Pay-as-you-go)

```bash
# Storage capacity
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Files' and skuName eq 'Cool LRS' and meterName eq 'Cool LRS Data Stored' and armRegionName eq 'eastus2'

# Write operations
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Files' and skuName eq 'Cool LRS' and meterName eq 'Cool LRS Write Operations' and armRegionName eq 'eastus2'

# Read operations
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Files' and skuName eq 'Cool LRS' and meterName eq 'Cool LRS Read Operations' and armRegionName eq 'eastus2'

# Data retrieval
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Files' and skuName eq 'Cool LRS' and meterName eq 'Cool LRS Data Retrieval' and armRegionName eq 'eastus2'

# Snapshots
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Files' and skuName eq 'Cool LRS' and meterName eq 'Cool LRS Snapshots' and armRegionName eq 'eastus2'
```

**ZRS, GRS, GZRS:** Replace `Cool LRS` with `Cool ZRS`, `Cool GRS`, or `Cool GZRS`.

### Azure Files Transaction Optimized Tier (Pay-as-you-go)

```bash
# Storage capacity
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Files' and skuName eq 'Transaction Optimized LRS' and meterName eq 'Transaction Optimized LRS Data Stored' and armRegionName eq 'eastus2'

# Write operations
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Files' and skuName eq 'Transaction Optimized LRS' and meterName eq 'Transaction Optimized LRS Write Operations' and armRegionName eq 'eastus2'

# Snapshots
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Files' and skuName eq 'Transaction Optimized LRS' and meterName eq 'Transaction Optimized LRS Snapshots' and armRegionName eq 'eastus2'
```

**ZRS, GRS, GZRS:** Replace `Transaction Optimized LRS` with corresponding redundancy variant.

### Azure Files Premium (Provisioned)

```bash
# LRS provisioned capacity
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Premium Files' and skuName eq 'Premium LRS' and meterName eq 'Premium LRS Provisioned' and armRegionName eq 'eastus2'

# LRS snapshots
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Premium Files' and skuName eq 'Premium LRS' and meterName eq 'Premium LRS Snapshots' and armRegionName eq 'eastus2'
```

**ZRS, GRS, GZRS:** Replace `Premium LRS` with `Premium ZRS`, `Premium GRS`, or `Premium GZRS`.

---

## Managed Disks

### Standard HDD

```bash
# Example: S30 (1 TiB)
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Standard HDD Managed Disks' and skuName eq 'S30 LRS' and meterName eq 'S30 Disks' and armRegionName eq 'eastus2'

# Transactions
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Standard HDD Managed Disks' and meterName eq 'Disk Operations' and armRegionName eq 'eastus2'

# Snapshots
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Standard HDD Managed Disks' and skuName eq 'Standard LRS' and meterName eq 'Standard Snapshots' and armRegionName eq 'eastus2'
```

**Note:** Replace `S30` with other tiers: S4, S6, S10, S15, S20, S30, S40, S50, S60, S70, S80

### Standard SSD

```bash
# Example: E30 (1 TiB)
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Standard SSD Managed Disks' and skuName eq 'E30 LRS' and meterName eq 'E30 Disks' and armRegionName eq 'eastus2'

# Transactions
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Standard SSD Managed Disks' and meterName eq 'Disk Operations' and armRegionName eq 'eastus2'

# Snapshots
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Standard SSD Managed Disks' and skuName eq 'Standard LRS' and meterName eq 'Standard Snapshots' and armRegionName eq 'eastus2'
```

**Note:** Replace `E30` with other tiers: E1-E80

### Premium SSD

```bash
# Example: P30 (1 TiB)
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Premium SSD Managed Disks' and skuName eq 'P30 LRS' and meterName eq 'P30 Disks' and armRegionName eq 'eastus2'

# Snapshots
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Premium SSD Managed Disks' and skuName eq 'Premium LRS' and meterName eq 'Premium Snapshots' and armRegionName eq 'eastus2'
```

**Note:** Replace `P30` with other tiers: P1-P80. No transaction charges for Premium SSD.

### Premium SSD v2

```bash
# Capacity
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Premium SSD v2 Managed Disk' and meterName eq 'vDisk Capacity' and armRegionName eq 'eastus2'

# Provisioned IOPS
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Premium SSD v2 Managed Disk' and meterName eq 'vDisk Provisioned IOPS' and armRegionName eq 'eastus2'

# Provisioned throughput
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Premium SSD v2 Managed Disk' and meterName eq 'vDisk Provisioned Throughput (MBps)' and armRegionName eq 'eastus2'

# Snapshots
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Premium SSD v2 Managed Disk' and meterName eq 'Snapshots' and armRegionName eq 'eastus2'
```

### Ultra Disk

```bash
# Capacity
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Ultra Disks' and meterName eq 'Provisioned Capacity' and armRegionName eq 'eastus2'

# Provisioned IOPS
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Ultra Disks' and meterName eq 'Provisioned IOPS' and armRegionName eq 'eastus2'

# Provisioned throughput
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Ultra Disks' and meterName eq 'Provisioned Throughput (MBps)' and armRegionName eq 'eastus2'

# Snapshots
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Storage' and productName eq 'Ultra Disks' and meterName eq 'Snapshots' and armRegionName eq 'eastus2'
```

---

## ANF Replication

### Cross-Region Replication (CRR)

```bash
# 10 minute replication frequency
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and meterName eq 'Cross Region Replication Data Transfer - 10 Minute' and armRegionName eq 'eastus2'

# Hourly replication frequency
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and meterName eq 'Cross Region Replication Data Transfer - Hourly' and armRegionName eq 'eastus2'

# Daily replication frequency
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Azure NetApp Files' and productName eq 'Azure NetApp Files' and meterName eq 'Cross Region Replication Data Transfer - Daily' and armRegionName eq 'eastus2'
```

### Cross-Zone Replication (CZR)

```bash
# No data transfer charges for CZR
# Destination volume charged at normal capacity rates
```

---

## Azure Backup

### Azure Files Backup

```bash
# Protected instance
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Backup' and productName eq 'Azure Backup' and meterName contains 'Azure Files' and meterName contains 'Protected Instance' and armRegionName eq 'eastus2'

# Backup storage
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Backup' and productName eq 'Azure Backup' and meterName contains 'Azure Files' and meterName contains 'LRS Backup Storage' and armRegionName eq 'eastus2'
```

### Managed Disk Backup

```bash
# Protected instance
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Backup' and productName eq 'Azure Backup' and meterName contains 'Disk' and meterName contains 'Protected Instance' and armRegionName eq 'eastus2'

# Backup storage (snapshot-based)
https://prices.azure.com/api/retail/prices?$filter=serviceFamily eq 'Storage' and serviceName eq 'Backup' and productName eq 'Azure Backup' and meterName contains 'Disk' and meterName contains 'Snapshot Storage' and armRegionName eq 'eastus2'
```

---

## Notes

1. **Region Parameter:** All URLs use `armRegionName eq 'eastus2'`. Replace with the actual resource region at runtime.

2. **URL Encoding:** When using these URLs in code, ensure proper URL encoding of special characters (spaces, quotes, etc.).

3. **API Version:** The base API URL uses the default version. For savings plans support, append `?api-version=2023-01-01-preview`.

4. **Meter Name Variations:** Some meter names may vary slightly by region or over time. Use `contains` instead of `eq` for more flexible matching if exact matches fail.

5. **Double Encryption Pricing:** Double encryption tiers have all-in-one pricing (not base + premium). The SKU names are:
   - `Standard Double Encrypted`
   - `Premium Double Encrypted`
   - `Ultra Double Encrypted`
   - **Note:** Flexible service level does NOT support double encryption (only single encryption)

6. **Cool Access Pricing:** All ANF tiers with cool access use the same cool storage pricing SKU (`Standard Storage with Cool Access`).

7. **Pagination:** The API returns paginated results. Check for `NextPageLink` in responses and follow it to retrieve all meters.

8. **Currency:** All prices are in USD by default. The API supports other currencies via the `currencyCode` parameter.

---

## API Response Format

The Azure Retail Prices API returns a JSON response with the following structure:

```json
{
  "BillingCurrency": "USD",
  "CustomerEntityId": "Default",
  "CustomerEntityType": "Retail",
  "Items": [
    {
      "currencyCode": "USD",
      "tierMinimumUnits": 0.0,
      "retailPrice": 0.000181,
      "unitPrice": 0.000181,
      "armRegionName": "eastus2",
      "location": "US East 2",
      "effectiveStartDate": "2024-01-01T00:00:00Z",
      "meterId": "abc123...",
      "meterName": "Standard Capacity",
      "productId": "DZH318Z0BQ5P",
      "skuId": "DZH318Z0BQ5P/003P",
      "productName": "Azure NetApp Files",
      "skuName": "Standard",
      "serviceName": "Azure NetApp Files",
      "serviceId": "DZH3147RZ57L",
      "serviceFamily": "Storage",
      "unitOfMeasure": "1 GiB/Hour",
      "type": "Consumption",
      "isPrimaryMeterRegion": true,
      "armSkuName": ""
    }
  ],
  "NextPageLink": null,
  "Count": 1
}
```

### Key Response Fields

| Field | Description | Usage in Cost Calculation |
|-------|-------------|---------------------------|
| **`retailPrice`** | The unit price in USD | **Use this value directly for cost calculations** |
| `unitPrice` | Same as retailPrice for retail customers | Typically same as retailPrice |
| `unitOfMeasure` | Billing unit (e.g., "1 GiB/Hour", "1/Month") | Critical for understanding the price unit |
| `meterName` | Human-readable meter description | Use to verify correct meter |
| `armRegionName` | Azure region code (lowercase) | Match against resource region |
| `skuName` | SKU/tier identifier | Use to distinguish between tiers |
| `currencyCode` | Currency (default: USD) | Convert if needed |
| `effectiveStartDate` | When this price became effective | Check for price changes |

### Extracting the Price

**Step 1: Parse the JSON response**
```javascript
const response = await fetch(apiUrl);
const data = await response.json();
```

**Step 2: Get the first item (should be only one with specific filters)**
```javascript
if (data.Items && data.Items.length > 0) {
  const priceItem = data.Items[0];
  const price = priceItem.retailPrice;  // This is the value you need
  const unit = priceItem.unitOfMeasure;
  
  console.log(`Price: $${price} per ${unit}`);
}
```

**Step 3: Handle the unit of measure**
```javascript
// Example: ANF Standard Capacity
// retailPrice: 0.000181
// unitOfMeasure: "1 GiB/Hour"

// To calculate monthly cost:
const capacityGiB = 1024;  // 1 TiB
const hoursPerMonth = 730;
const monthlyCost = capacityGiB * 0.000181 * hoursPerMonth;
// Result: $135.46 per month for 1 TiB
```

### Common Unit of Measure Patterns

| Service | Meter Type | Unit of Measure | Conversion to Monthly |
|---------|------------|-----------------|----------------------|
| ANF | Capacity | `1 GiB/Hour` | `price × GiB × 730` |
| ANF | Throughput | `1 Hour` | `price × MiB/s × 730` |
| ANF | Data Transfer | `1 GB` | `price × GB` (one-time) |
| Azure Files | Storage | `1 GB/Month` | `price × GB` (already monthly) |
| Azure Files | Transactions | `10K` | `price × (operations / 10000)` |
| Premium Files | Provisioned | `1 GB/Month` | `price × GB` (already monthly) |
| Managed Disk | Fixed Tier | `1/Month` | `price` (fixed monthly) |
| Managed Disk | Transactions | `10K` | `price × (operations / 10000)` |
| Premium v2/Ultra | Capacity | `1 GiB/Month` | `price × GiB` (already monthly) |
| Premium v2/Ultra | IOPS | `1/Hour` | `price × IOPS × 730` |
| Premium v2/Ultra | Throughput | `1/Hour` | `price × MiB/s × 730` |

### Handling Pagination

If the response includes multiple pages:

```javascript
let allItems = [];
let nextLink = apiUrl;

while (nextLink) {
  const response = await fetch(nextLink);
  const data = await response.json();
  
  allItems = allItems.concat(data.Items);
  nextLink = data.NextPageLink;  // null when no more pages
}

// Process all items
for (const item of allItems) {
  console.log(`${item.meterName}: $${item.retailPrice} per ${item.unitOfMeasure}`);
}
```

### Example: Complete Price Extraction

```csharp
// C# example for ANF Standard capacity pricing
var apiUrl = "https://prices.azure.com/api/retail/prices?" +
    "$filter=serviceName eq 'Azure NetApp Files' and " +
    "skuName eq 'Standard' and " +
    "meterName eq 'Standard Capacity' and " +
    "armRegionName eq 'eastus2'";

var response = await httpClient.GetStringAsync(apiUrl);
var data = JsonSerializer.Deserialize<PricingResponse>(response);

if (data.Items?.Count > 0)
{
    var item = data.Items[0];
    var pricePerGibHour = item.RetailPrice;  // e.g., 0.000181
    var unitOfMeasure = item.UnitOfMeasure;  // "1 GiB/Hour"
    
    // Calculate monthly cost for 1 TiB
    var capacityGiB = 1024;
    var hoursPerMonth = 730;
    var monthlyCost = capacityGiB * pricePerGibHour * hoursPerMonth;
    
    Console.WriteLine($"Price: ${pricePerGibHour} per {unitOfMeasure}");
    Console.WriteLine($"Monthly cost for 1 TiB: ${monthlyCost:F2}");
}
```

---

## Testing

To test a URL, you can use curl:

```bash
curl "https://prices.azure.com/api/retail/prices?\$filter=serviceName eq 'Azure NetApp Files' and skuName eq 'Standard' and meterName eq 'Standard Capacity' and armRegionName eq 'eastus2'"
```

Or open directly in a browser (URL encoding will be handled automatically).

### Testing with jq (JSON processor)

```bash
# Extract just the retailPrice
curl -s "https://prices.azure.com/api/retail/prices?\$filter=serviceName eq 'Azure NetApp Files' and skuName eq 'Standard' and meterName eq 'Standard Capacity' and armRegionName eq 'eastus2'" | jq '.Items[0].retailPrice'

# Extract price, unit, and meter name
curl -s "https://prices.azure.com/api/retail/prices?\$filter=serviceName eq 'Azure NetApp Files' and skuName eq 'Standard' and meterName eq 'Standard Capacity' and armRegionName eq 'eastus2'" | jq '.Items[0] | {price: .retailPrice, unit: .unitOfMeasure, meter: .meterName}'
```

---

## References

- [Azure Retail Prices API Documentation](https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices)
- [STORAGE_TIER_COST_PERMUTATIONS.md](./STORAGE_TIER_COST_PERMUTATIONS.md) - Cost calculation formulas
- [PRICING_SYSTEM.md](./PRICING_SYSTEM.md) - Pricing system architecture
- [RetailPricingAPISearches.md](../RetailPricingAPISearches.md) - Additional API search examples

---

## Revision History

| Date | Change | Author |
|------|--------|--------|
| 2026-01-28 | Initial creation with all storage tier API URLs | Warp AI |
