# ANF Cost Permutation System

This document describes the ANF cost calculation system that implements all 11 cost permutations from `STORAGE_TIER_COST_PERMUTATIONS.md`.

## Overview

The system consists of three main components:

1. **AnfCostPermutation** - Identifies which of the 11 permutations applies to a volume
2. **UniversalAnfCostInputs** - Normalized input format that works across all permutations
3. **AnfPermutationCostCalculator** - Implements the cost calculation formulas for each permutation

## The 11 ANF Permutations

| ID | Name | Service Level | Cool Access | Double Encryption |
|----|------|---------------|-------------|-------------------|
| 1  | ANF Standard (Regular) | Standard | No | No |
| 2  | ANF Standard with Double Encryption | Standard | No | Yes |
| 3  | ANF Standard with Cool Access | Standard | Yes | No |
| 4  | ANF Premium (Regular) | Premium | No | No |
| 5  | ANF Premium with Double Encryption | Premium | No | Yes |
| 6  | ANF Premium with Cool Access | Premium | Yes | No |
| 7  | ANF Ultra (Regular) | Ultra | No | No |
| 8  | ANF Ultra with Double Encryption | Ultra | No | Yes |
| 9  | ANF Ultra with Cool Access | Ultra | Yes | No |
| 10 | ANF Flexible (Regular) | Flexible | No | No |
| 11 | ANF Flexible with Cool Access | Flexible | Yes | No |

## Key Characteristics by Permutation

### Throughput

- **Standard**: 16 MiB/s per TiB (no reduction with cool access)
- **Premium**: 64 MiB/s per TiB (reduces to 36 MiB/s with cool access)
- **Ultra**: 128 MiB/s per TiB (reduces to 68 MiB/s with cool access)
- **Flexible**: 128 MiB/s base (flat, not per TiB) + pay for additional throughput

### Pricing Components

#### Regular Permutations (1, 4, 7, 10)
- **Capacity**: `Provisioned GiB × $/GiB/hour × 720`
- **Flexible adds**: `Throughput above 128 MiB/s × $/MiB/s/hour × 720`

#### Double Encrypted Permutations (2, 5, 8)
- **Capacity**: `Provisioned GiB × Double Encrypted $/GiB/hour × 720`
- Single all-in-one price (not base + premium)

#### Cool Access Permutations (3, 6, 9, 11)
- **Hot Capacity**: `Hot GiB × $/GiB/hour × 720`
- **Cool Capacity**: `Cool GiB × Cool $/GiB/hour × 720`
- **Data Tiering**: `GiB tiered × $/GiB` (one-time)
- **Data Retrieval**: `GiB retrieved × $/GiB` (one-time)
- **Flexible also adds**: `Throughput above 128 MiB/s × $/MiB/s/hour × 720`

## Usage Example

```csharp
// From a discovered ANF volume
var discoveredVolume = /* ... discovered volume ... */;

// Step 1: Create universal inputs
var inputs = UniversalAnfCostInputs.FromDiscoveredVolume(discoveredVolume);

// The system automatically:
// - Detects if double encryption is enabled (EncryptionKeySource != "Microsoft.NetApp")
// - Identifies the correct permutation (1-11)
// - Extracts cool access metrics if available
// - Sets up Flexible tier throughput requirements

// Step 2: Calculate cost using the permutation-aware calculator
var calculator = new AnfPermutationCostCalculator(pricesClient, logger);
var estimate = await calculator.CalculateAsync(inputs);

// Step 3: Access results
Console.WriteLine($"Permutation: {estimate.PermutationName} (#{estimate.PermutationId})");
Console.WriteLine($"Total Cost (30 days): ${estimate.TotalCost:F2}");
Console.WriteLine($"Included Throughput: {estimate.IncludedThroughput:N2} MiB/s");

foreach (var component in estimate.CostComponents)
{
    Console.WriteLine($"  {component.ComponentName}: ${component.Cost:F2}");
    Console.WriteLine($"    {component.Description}");
}
```

## Universal Input Format

The `UniversalAnfCostInputs` class provides a consistent interface regardless of permutation:

### Common Inputs (All Permutations)
- `ProvisionedCapacityGiB` - Provisioned capacity
- `SnapshotSizeGiB` - Snapshot size (informational, no separate charge)
- `Region` - Azure region for pricing
- `BillingPeriodDays` - Billing period (default 30)

### Cool Access Inputs (Permutations 3, 6, 9, 11)
- `HotCapacityGiB` - Hot tier capacity
- `CoolCapacityGiB` - Cool tier capacity
- `DataTieredToCoolGiB` - Data moved to cool (one-time charge)
- `DataRetrievedFromCoolGiB` - Data retrieved from cool (one-time charge)

### Flexible Tier Inputs (Permutations 10, 11)
- `RequiredThroughputMiBps` - Required throughput
- `ThroughputAboveBaseGiBps` - Calculated: throughput above 128 MiB/s base

## Permutation Detection

The system automatically detects the correct permutation based on volume properties:

```csharp
// Double encryption detection
bool isDoubleEncryption = volume.EncryptionKeySource != null &&
    !volume.EncryptionKeySource.Equals("Microsoft.NetApp", StringComparison.OrdinalIgnoreCase);

// Permutation identification
var permutation = AnfCostPermutation.Identify(
    volume.ServiceLevel,           // "Standard", "Premium", "Ultra", "Flexible"
    volume.CoolAccessEnabled,      // true/false
    isDoubleEncryption             // true/false
);
```

## Cost Formula Implementation

Each permutation has its own calculation method in `AnfPermutationCostCalculator`:

- `CalculateStandardRegularAsync()` - Permutation 1
- `CalculateStandardDoubleEncryptedAsync()` - Permutation 2
- `CalculateStandardCoolAccessAsync()` - Permutation 3
- ... and so on for all 11 permutations

All formulas follow the specifications in `STORAGE_TIER_COST_PERMUTATIONS.md`:

### Example: Permutation 10 (Flexible Regular)
```
Total 720-Hour Cost = 
  (Provisioned Capacity (GiB) × Flexible Capacity Price ($/GiB/hour) × 720)
  + (Throughput Above Base (MiB/s) × Flexible Throughput Price ($/MiB/s/hour) × 720)

Where:
  Throughput Above Base = max(0, Required Throughput - 128 MiB/s)
```

## Validation

The system validates inputs for each permutation:

```csharp
var errors = inputs.Validate();

// Example validation rules:
// - Minimum 50 GiB for all ANF volumes
// - Minimum 2,400 GiB for cool access volumes
// - Throughput required for Flexible tier
// - Cool access and double encryption are mutually exclusive
```

## Integration with Existing Code

### Discovered Volume JSON Format

The existing JSON format already contains the necessary fields:

```json
{
  "ServiceLevel": "Flexible",
  "CoolAccessEnabled": true,
  "EncryptionKeySource": "Microsoft.NetApp",
  "ProvisionedSizeBytes": 53687091200,
  "ThroughputMibps": 12,
  "HistoricalMetricsSummary": "{...cool tier metrics...}"
}
```

### Missing Fields to Add

To properly identify permutations and calculate costs, consider adding:

1. **EncryptionKeySource** - Already present in discovered JSON
2. **IsDoubleEncryptionEnabled** - Can be calculated from EncryptionKeySource
3. **CoolTierMetrics** - Already captured in HistoricalMetricsSummary

### Enhanced CostCalculationInputs

The existing `CostCalculationInputs` in the JSON should be extended to include:

```json
{
  "CostCalculationInputs": {
    "PermutationId": 11,
    "PermutationName": "ANF Flexible with Cool Access",
    "HotCapacityGiB": 45.5,
    "CoolCapacityGiB": 4.5,
    "DataTieredToCoolGiB": 0,
    "DataRetrievedFromCoolGiB": 0,
    "RequiredThroughputMiBps": 12
  }
}
```

## Files Created

1. **AnfCostPermutation.cs** - Permutation identification and definitions
2. **UniversalAnfCostInputs.cs** - Universal input format (embedded in AnfCostPermutation.cs)
3. **AnfPermutationCostCalculator.cs** - Permutation-based cost calculator

## Next Steps

1. **Test with Real Data**: Use the provided volume JSON to test cost calculations
2. **Update Discovery Service**: Ensure EncryptionKeySource is captured during discovery
3. **Integrate with UI**: Display permutation information in volume details
4. **Update Existing Calculator**: Replace or extend existing `AnfCostCalculator` with permutation-aware version
5. **Add Unit Tests**: Test each permutation calculation against known values
6. **Document API Pricing Queries**: Ensure Azure Retail Prices API queries match the patterns in STORAGE_TIER_COST_PERMUTATIONS.md

## Benefits

1. **Accurate Costing**: Each permutation uses its exact formula
2. **Comprehensive Coverage**: All 11 permutations are supported
3. **Extensible**: Easy to add new permutations or modify existing ones
4. **Type-Safe**: Strong typing prevents invalid configurations
5. **Documented**: Each calculation references the source documentation
6. **Testable**: Clear separation of concerns enables unit testing
