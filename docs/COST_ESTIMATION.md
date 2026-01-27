# Enhanced 30-Day Cost Estimation System

## Overview

The AzFilesOptimizer includes a sophisticated cost estimation system that provides accurate 30-day cost forecasts for Azure storage resources. The system prioritizes actual billing data for Managed Disks while using real-time retail pricing for Azure Files shares and ANF volumes.

## Architecture

### Components

1. **AzureRetailPricesClient** - Fetches real-time pricing from Azure Retail Prices API
2. **AzureFilesCostCalculator** - Calculates costs for Azure Files shares
3. **AnfCostCalculator** - Calculates costs for Azure NetApp Files volumes
4. **ManagedDiskCostCalculator** - Calculates costs for Managed Disks (prioritizes actual billing data)
5. **AccurateCostEstimationService** - Orchestrates all calculators and integrates with discovery

### Data Flow

```
DiscoveredResource → AccurateCostEstimationService
                    ↓
       ┌────────────┼────────────┐
       ↓            ↓            ↓
AzureFiles      ANF         ManagedDisk
Calculator   Calculator    Calculator
       ↓            ↓            ↓
       └────────────┼────────────┘
                    ↓
          VolumeCostEstimate
```

## Cost Calculation Methods

### Azure Files Shares

#### Pay-as-you-go (Hot, Cool, Transaction Optimized)
- **Storage Capacity**: Calculated based on actual usage (or provisioned if usage unavailable)
- **Transactions**: Estimated from Azure Monitor metrics or heuristics
- **Snapshots**: Differential snapshot size × price per GB
- **Data Egress**: Estimated from Azure Monitor or assumed minimal

#### Provisioned (Premium Files)
- **Provisioned Capacity**: Minimum 100 GB, includes all transactions and performance
- **Snapshots**: Billed separately from provisioned capacity
- **Data Egress**: Based on actual or estimated egress

**Confidence Factors**:
- High (80-100%): Actual usage metrics available, pricing data current
- Medium (50-79%): Using provisioned capacity instead of usage, some metrics missing
- Low (<50%): Missing transaction data, no pricing information

### Azure NetApp Files

#### Standard/Premium/Ultra Tiers
- **Provisioned Capacity**: Minimum 50 GB, includes throughput at tier rate
- **Included Throughput**: Calculated per TiB (16/64/128 MiB/s for Standard/Premium/Ultra)
- **Snapshots**: Included in volume capacity (no separate charge)

#### Cool Access Enabled
- **Hot Storage**: Standard tier pricing
- **Cool Storage**: Significantly lower per-GB cost
- **Data Tiering**: Cost per GB when data moves from hot to cool
- **Data Retrieval**: Cost per GB when data is retrieved from cool to hot
- **Throughput Impact**: Reduced included throughput (36 MiB/s for Premium, 68 for Ultra)

**Confidence Factors**:
- High (80-100%): Pricing data available, capacity known
- Medium (50-79%): Cool access enabled but data distribution unknown
- Low (<50%): Missing pricing data, multiple warnings

### Managed Disks

#### Actual Billing Data (Preferred)
When available from Cost Management API:
- **Actual Cost**: Last 30 days of actual billing charges
- **Confidence**: 95%

#### Retail Pricing (Fallback)
When actual billing unavailable:
- **Disk Tier**: Calculated based on size (P1-P80, E1-E80, S4-S80)
- **Capacity**: Fixed per-tier pricing
- **Snapshots**: Differential size × price per GB
- **Transactions**: For Standard HDD/SSD only

**Flexible Pricing (Premium v2, Ultra)**:
- **Capacity**: Per-GB pricing
- **IOPS**: Per-IOPS pricing (if provisioned)
- **Throughput**: Per-MiB/s pricing (if provisioned)

**Confidence Factors**:
- High (95%): Actual billing data from Cost Management API
- Medium (75%): Retail pricing with all parameters
- Low (<50%): Missing IOPS/throughput for flexible pricing

## Pricing Data Sources

### Azure Retail Prices API
- **Endpoint**: `https://prices.azure.com/api/retail/prices`
- **Cache Duration**: 24 hours
- **Filters**: Region, service, product name, SKU
- **Update Frequency**: Real-time (cached locally)

### Azure Cost Management API
- **Use Case**: Actual billing data for Managed Disks
- **Lookback Period**: 30 days
- **Accuracy**: Actual costs (highest confidence)

## Confidence Scoring

Confidence scores range from 10% to 100% and are based on:

| Factor | Impact | Example |
|--------|--------|---------|
| Data source | ±25% | Actual billing vs. estimates |
| Missing metrics | -20 to -30% | No transaction counts |
| Pricing availability | -50% | No pricing data found |
| Warnings | -5% each | Multiple assumptions |

## Integration Points

### Discovery Service
```csharp
var estimationService = new AccurateCostEstimationService(...);
var estimate = await estimationService.Calculate30DayCostEstimateAsync(discoveredResource);
```

### Bulk Estimation
```csharp
var estimates = await estimationService.CalculateBulkEstimatesAsync(resources);
```

### Cost Components
Each estimate includes:
- `TotalEstimatedCost`: Total 30-day cost
- `ConfidenceLevel`: 0-100 score
- `EstimationMethod`: Description of methodology
- `CostComponents`: Itemized breakdown
- `Notes`: Important assumptions
- `Warnings`: Data quality issues

## Frontend Display

### Job Results Page
Cost estimates are displayed with:
- **Total Cost**: Bold with currency formatting
- **Confidence Badge**: Color-coded (Green/Orange/Red)
- **Cost Breakdown Table**: Component-level details
- **Notes Section**: Blue info box with assumptions
- **Warnings Section**: Orange alert box with caveats
- **Method Footer**: Estimation methodology and data source

### Confidence Colors
- 🟢 **Green (High)**: 80-100% confidence
- 🟠 **Orange (Medium)**: 50-79% confidence
- 🔴 **Red (Low)**: <50% confidence

## Example Output

```json
{
  "VolumeId": "/subscriptions/.../fileShares/share1",
  "VolumeName": "share1",
  "ResourceType": "AzureFile",
  "Region": "eastus",
  "EstimationMethod": "Pay-as-you-go Pricing",
  "TotalEstimatedCost": 45.67,
  "ConfidenceLevel": 85,
  "PeriodDays": 30,
  "CostComponents": [
    {
      "ComponentType": "storage",
      "Description": "Storage capacity (500.00 GB)",
      "Quantity": 500,
      "Unit": "GB/month",
      "UnitPrice": 0.06,
      "EstimatedCost": 30.00,
      "DataSource": "Azure Retail Prices API"
    },
    {
      "ComponentType": "transactions",
      "Description": "Transactions (15,000,000 operations)",
      "Quantity": 1500,
      "Unit": "10K transactions",
      "UnitPrice": 0.01,
      "EstimatedCost": 15.00,
      "DataSource": "Azure Retail Prices API (averaged)"
    }
  ],
  "Notes": [
    "Using actual usage metrics from Azure Monitor"
  ],
  "Warnings": [],
  "EstimatedAt": "2026-01-22T19:00:00Z"
}
```

## Best Practices

### For Accurate Estimates
1. **Enable Azure Monitor metrics collection** for actual usage data
2. **Maintain resource tags** for easier cost attribution
3. **Review estimates regularly** as pricing changes
4. **Compare estimates to actual billing** to validate accuracy

### For Cost Optimization
1. **Review low-confidence estimates** - they may indicate missing data
2. **Check transaction counts** for pay-as-you-go shares
3. **Evaluate tier selection** based on usage patterns
4. **Monitor snapshot growth** as it impacts costs

### For Development
1. **Clear pricing cache** when testing: `_pricesClient.ClearCache()`
2. **Mock Azure Monitor** for unit tests
3. **Test edge cases**: empty shares, very large volumes, missing metrics
4. **Validate confidence scoring** with known scenarios

## Limitations

- **Transaction estimates** may be inaccurate without actual metrics
- **Egress costs** are often minimal and hard to predict
- **Cool access tiering** requires usage pattern data
- **Pricing changes** occur quarterly; cache may be stale
- **Regional variations** require accurate region mapping

## Future Enhancements

- [ ] Integration with Azure Monitor for real metrics
- [ ] Historical cost trending and forecasting
- [ ] Multi-region pricing comparison
- [ ] Reserved capacity calculations
- [ ] Commitment tier recommendations
- [ ] Cost anomaly detection
- [ ] Savings recommendations based on usage patterns

## Troubleshooting

### "No pricing data available"
- Check internet connectivity
- Verify region name is correct (armRegionName format)
- Confirm Azure Retail Prices API is accessible
- Check cache expiration (24 hours)

### "Confidence level is low"
- Enable Azure Monitor metrics
- Verify resource properties are complete
- Check for missing SKU or tier information
- Review warnings in the estimate

### "Actual costs differ significantly from estimates"
- Compare estimation method with actual usage
- Check for unexpected transaction spikes
- Verify egress charges in billing
- Review snapshot policies
- Confirm no mid-month pricing changes

## References

- [Azure Retail Prices API Documentation](https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices)
- [Azure Files Pricing](https://azure.microsoft.com/en-us/pricing/details/storage/files/)
- [Azure NetApp Files Pricing](https://azure.microsoft.com/en-us/pricing/details/netapp/)
- [Managed Disks Pricing](https://azure.microsoft.com/en-us/pricing/details/managed-disks/)
- [AZURE_PRICING_MODELS.md](./AZURE_PRICING_MODELS.md) - Detailed pricing model reference
