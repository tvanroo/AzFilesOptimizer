# ANF Cost Permutation System - Example Usage

This document shows how the system works with your example ANF volume JSON.

## Input Volume JSON

From: `job-50979b9f-da29-462f-860e-3fc2b1470b54-volume-97343087f5c03bc9(2).json`

```json
{
  "VolumeData": {
    "ServiceLevel": "Flexible",
    "CoolAccessEnabled": true,
    "EncryptionKeySource": "Microsoft.NetApp",
    "ProvisionedSizeBytes": 53687091200,
    "ThroughputMibps": 12,
    "Location": "southcentralus",
    "HistoricalMetricsSummary": "{
      \"VolumeCoolTierSize\": {\"average\": 0, \"max\": 0, \"min\": 0},
      \"VolumeCoolTierDataReadSize\": {\"average\": 0, \"max\": 0, \"min\": 0, \"total\": 0},
      \"VolumeCoolTierDataWriteSize\": {\"average\": 0, \"max\": 0, \"min\": 0, \"total\": 0}
    }"
  }
}
```

## Step 1: Permutation Identification

```csharp
// Extract volume properties
string serviceLevel = "Flexible";
bool coolAccessEnabled = true;
string encryptionKeySource = "Microsoft.NetApp";

// Detect double encryption
bool isDoubleEncryption = encryptionKeySource != null &&
    !encryptionKeySource.Equals("Microsoft.NetApp", StringComparison.OrdinalIgnoreCase);
// Result: isDoubleEncryption = false

// Identify permutation
var permutation = AnfCostPermutation.Identify(
    serviceLevel: "Flexible",
    coolAccessEnabled: true,
    doubleEncryptionEnabled: false
);

// Result: Permutation 11 - ANF Flexible with Cool Access
Console.WriteLine($"Permutation ID: {permutation.PermutationId}"); // 11
Console.WriteLine($"Name: {permutation.Name}"); // "ANF Flexible with Cool Access"
```

## Step 2: Create Universal Inputs

```csharp
var inputs = new UniversalAnfCostInputs
{
    PermutationId = 11,
    Permutation = permutation,
    Region = "southcentralus",
    VolumeId = "97343087f5c03bc9",
    VolumeName = "anf-scus/vanRoojen-temp-test/test1-smb-50-12-cool",
    ResourceId = "/subscriptions/.../test1-smb-50-12-cool",
    
    // Capacity (50 GiB provisioned)
    ProvisionedCapacityGiB = 53687091200 / (1024.0 * 1024.0 * 1024.0), // = 50.0 GiB
    SnapshotSizeGiB = 0,
    
    // Cool access (all capacity is hot since cool tier metrics show 0)
    HotCapacityGiB = 50.0,
    CoolCapacityGiB = 0.0,
    DataTieredToCoolGiB = 0.0,
    DataRetrievedFromCoolGiB = 0.0,
    
    // Flexible tier throughput
    RequiredThroughputMiBps = 12.0,
    
    // Billing period
    BillingPeriodDays = 30
};

// Validation
var errors = inputs.Validate();
// Potential warning: "Cool access requires minimum 2,400 GiB provisioned capacity"
// Current volume is only 50 GiB
```

## Step 3: Calculate Cost (Permutation 11 Formula)

### Formula for Permutation 11: ANF Flexible with Cool Access

```
Total 720-Hour Cost = 
  (Hot Capacity (GiB) × Flexible Hot Capacity Price ($/GiB/hour) × 720)
  + (Cool Capacity (GiB) × Flexible Cool Capacity Price ($/GiB/hour) × 720)
  + (Data Tiered to Cool (GiB) × Tiering Price ($/GiB))
  + (Data Retrieved from Cool (GiB) × Retrieval Price ($/GiB))
  + (Throughput Above Base (MiB/s) × Flexible Throughput Price ($/MiB/s/hour) × 720)

Where:
  Hot Capacity = 50.0 GiB
  Cool Capacity = 0.0 GiB
  Data Tiered = 0.0 GiB
  Data Retrieved = 0.0 GiB
  Throughput Above Base = max(0, 12 - 128) = 0 MiB/s (below base)
```

### Calculation Example

Assuming example pricing (actual prices from Azure Retail Prices API):
- Flexible Capacity: $0.000205/GiB/hour
- Cool Capacity: $0.000015/GiB/hour
- Cool Data Transfer: $0.02/GiB
- Flexible Throughput: $0.015/MiB/s/hour

```csharp
var calculator = new AnfPermutationCostCalculator(pricesClient, logger);
var estimate = await calculator.CalculateAsync(inputs);

// Results:
estimate.PermutationId = 11;
estimate.PermutationName = "ANF Flexible with Cool Access";

// Cost components:
estimate.CostComponents = [
    {
        ComponentName: "Hot Tier Capacity",
        ComponentType: "capacity_hot",
        Description: "Flexible hot capacity (50.00 GiB @ $0.000205/GiB/hour)",
        Quantity: 50.0,
        Unit: "GiB",
        UnitPrice: 0.000205,
        Hours: 720,
        Cost: 50.0 × 0.000205 × 720 = $7.38
    }
    // No cool capacity cost (0 GiB)
    // No data transfer costs (0 GiB)
    // No throughput cost (12 MiB/s < 128 MiB/s base)
];

estimate.TotalCost = $7.38;
estimate.IncludedThroughput = 128.0; // MiB/s (flat base)
estimate.RequiredThroughput = 12.0; // MiB/s (actual usage)

estimate.Notes = [
    "Cool access enabled with independent throughput pricing",
    "Base included throughput: 128 MiB/s"
];
```

## Comparison with Current Cost in JSON

From the JSON:
```json
"CostSummary": {
  "TotalCost30Days": 6.6065,
  "DailyAverage": 0.22021666666666664,
  "Currency": "USD"
}
```

**Current calculation**: $6.61 for 30 days  
**New permutation-based calculation**: ~$7.38 for 30 days (example pricing)

The difference depends on the actual pricing rates retrieved from the Azure Retail Prices API.

## Enhanced JSON Output

The enhanced JSON would include:

```json
{
  "VolumeId": "97343087f5c03bc9",
  "VolumeType": "ANF",
  "CostCalculationInputs": {
    "PermutationId": 11,
    "PermutationName": "ANF Flexible with Cool Access",
    "Region": "southcentralus",
    "ProvisionedCapacityGiB": 50.0,
    "HotCapacityGiB": 50.0,
    "CoolCapacityGiB": 0.0,
    "DataTieredToCoolGiB": 0.0,
    "DataRetrievedFromCoolGiB": 0.0,
    "RequiredThroughputMiBps": 12.0,
    "BaseThroughputMiBps": 128.0,
    "ThroughputAboveBase": 0.0,
    "BillingPeriodDays": 30,
    "BillingPeriodHours": 720
  },
  "DetailedCostAnalysis": {
    "PermutationId": 11,
    "PermutationName": "ANF Flexible with Cool Access",
    "TotalCost": 7.38,
    "IncludedThroughput": 128.0,
    "RequiredThroughput": 12.0,
    "CostComponents": [
      {
        "ComponentName": "Hot Tier Capacity",
        "ComponentType": "capacity_hot",
        "Description": "Flexible hot capacity (50.00 GiB @ $0.000205/GiB/hour)",
        "Quantity": 50.0,
        "Unit": "GiB",
        "UnitPrice": 0.000205,
        "Hours": 720,
        "Cost": 7.38
      }
    ],
    "Notes": [
      "Cool access enabled with independent throughput pricing",
      "Base included throughput: 128 MiB/s",
      "Required throughput (12 MiB/s) is below base - no additional throughput cost"
    ],
    "ValidationErrors": []
  }
}
```

## Key Differences from Original Calculator

1. **Permutation Awareness**: Explicitly identifies which of 11 permutations applies
2. **Accurate Formula**: Uses the exact formula for Flexible + Cool Access
3. **Throughput Handling**: Correctly handles Flexible tier's flat 128 MiB/s base
4. **Component Breakdown**: Separates hot/cool capacity, data transfers, and throughput
5. **Validation**: Warns about minimum capacity requirements (2,400 GiB for cool access)
6. **Metadata**: Includes permutation ID and name for tracking

## Testing Different Permutations

### Example: Standard with Double Encryption (Permutation 2)

```csharp
var inputs2 = new UniversalAnfCostInputs
{
    Permutation = AnfCostPermutation.Identify("Standard", false, true),
    ProvisionedCapacityGiB = 500.0,
    Region = "southcentralus"
};

// Formula: Provisioned Capacity × Standard Double Encrypted Price × 720
// Cost: 500.0 × $0.000183 × 720 = $65.88
```

### Example: Premium with Cool Access (Permutation 6)

```csharp
var inputs3 = new UniversalAnfCostInputs
{
    Permutation = AnfCostPermutation.Identify("Premium", true, false),
    ProvisionedCapacityGiB = 3000.0,
    HotCapacityGiB = 2500.0,
    CoolCapacityGiB = 500.0,
    DataTieredToCoolGiB = 100.0,
    DataRetrievedFromCoolGiB = 50.0,
    Region = "southcentralus"
};

// Components:
// 1. Hot capacity: 2500 × $0.000323 × 720 = $581.40
// 2. Cool capacity: 500 × $0.000015 × 720 = $5.40
// 3. Data tiering: 100 × $0.02 = $2.00
// 4. Data retrieval: 50 × $0.02 = $1.00
// Total: $589.80
// Throughput: 36 MiB/s per TiB (reduced from 64)
```

## Summary

The permutation-based system:

✅ **Identifies** the correct permutation (11 in this case)  
✅ **Validates** inputs against permutation-specific rules  
✅ **Calculates** costs using the exact formula for that permutation  
✅ **Provides** detailed component breakdown  
✅ **Tracks** throughput characteristics (base, required, included)  
✅ **Documents** calculation method for auditability  

This ensures accurate cost estimates that align with Azure's actual billing model for each specific ANF configuration.
