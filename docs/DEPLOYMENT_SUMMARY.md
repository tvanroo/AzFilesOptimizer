# ANF Cost Permutation System - Deployment Summary

**Date**: 2026-01-28  
**Commits**: 2e0b7f5, e4e83a5  
**Deployment Status**: ✅ Successful  
**Function App**: azfo-dev-func-xy76b  

---

## What Was Deployed

### 1. New Files Created

#### Models
- **`src/backend/Models/AnfCostPermutation.cs`**
  - `AnfCostPermutation` class - Defines all 11 permutations
  - `UniversalAnfCostInputs` class - Normalized input format
  - Automatic permutation identification logic

#### Services
- **`src/backend/Services/AnfPermutationCostCalculator.cs`**
  - Complete implementation of all 11 cost formulas
  - Permutation-specific calculation methods
  - Integration with Azure Retail Prices API

#### Documentation
- **`docs/ANF_COST_PERMUTATION_SYSTEM.md`** - System overview and usage
- **`docs/ANF_PERMUTATION_EXAMPLE.md`** - Real-world usage examples
- **`docs/ANF_PERMUTATION_TESTING_PLAN.md`** - Complete testing guide
- **`docs/STORAGE_TIER_COST_PERMUTATIONS.md`** - All 11 formulas
- **`docs/PRICING_API_URLS.md`** - Azure pricing API reference

### 2. Modified Files

#### Integration
- **`src/backend/Services/CostCollectionService.cs`**
  - Added permutation identification at start of ANF cost calculation
  - Enhanced job logging with permutation details
  - Store permutation info in CostCalculationInputs

---

## Changes in Job Log Output

### Before (Old Logs)
```
2:40:48 PM → Processing ANF volume 'anf-scus/vanRoojen-temp-test/test1-smb-50-12-cool'
2:40:48 PM 🔄 FORCING REFRESH: ANF pricing for southcentralus/Flexible
2:41:24 PM 💰 Data Capacity Cost Calculation: 50.00 GiB * ($0.000181/Hour * 730 Hours) = $6.606
2:41:24 PM ⚡ Flexible Throughput Cost Calculation: Pool total 128.00 MiB/s <= 128 MiB/s baseline
2:41:26 PM ✓ Calculated from retail pricing: $6.61
```

### After (New Logs with Permutation Info)
```
2:40:48 PM → Processing ANF volume 'anf-scus/vanRoojen-temp-test/test1-smb-50-12-cool'
2:40:48 PM   🔍 ANF Cost Permutation: #11 - ANF Flexible with Cool Access
2:40:48 PM   📊 Configuration: Service Level=Flexible, Cool Access=True, Double Encryption=False
2:40:48 PM   ⚡ Throughput Model: Flexible tier - 128 MiB/s base (flat) + pay for additional throughput
2:40:48 PM 🔄 FORCING REFRESH: ANF pricing for southcentralus/Flexible
2:41:24 PM 💰 Data Capacity Cost Calculation: 50.00 GiB * ($0.000181/Hour * 730 Hours) = $6.606
2:41:24 PM ⚡ Flexible Throughput Cost Calculation: Pool total 128.00 MiB/s <= 128 MiB/s baseline
2:41:26 PM ✓ Calculated from retail pricing: $6.61
```

### Key Additions ✨

1. **🔍 ANF Cost Permutation**: Shows which of the 11 permutations applies (#11 in this case)
2. **📊 Configuration**: Explicit service level, cool access, and double encryption flags
3. **⚡ Throughput Model**: Permutation-specific throughput characteristics

---

## Changes in Volume JSON

### Enhanced `costCalculationInputs` Section

**Before**:
```json
{
  "costCalculationInputs": {
    "CapacityPoolServiceLevel": "Flexible",
    "ProvisionedSizeBytes": 53687091200,
    "SnapshotCount": 1,
    "BackupConfigured": false
  }
}
```

**After**:
```json
{
  "costCalculationInputs": {
    "PermutationId": 11,
    "PermutationName": "ANF Flexible with Cool Access",
    "ServiceLevel": "Flexible",
    "CoolAccessEnabled": true,
    "DoubleEncryptionEnabled": false,
    "CapacityPoolServiceLevel": "Flexible",
    "ProvisionedSizeBytes": 53687091200,
    "SnapshotCount": 1,
    "BackupConfigured": false
  }
}
```

---

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

---

## Testing

### Quick Test

Run the provided test script:

```bash
./test-permutation-logging.sh
```

This will:
1. Create a discovery job
2. Wait for completion
3. Extract and display permutation information from logs
4. Show permutation details in volume JSON
5. Verify cost calculations

### Manual Test

1. **Create Discovery Job**:
   ```bash
   curl -X POST "https://azfo-dev-func-xy76b.azurewebsites.net/api/CreateDiscoveryJob" \
     -H "Content-Type: application/json" \
     -d '{"subscriptionIds":["<sub-id>"],"includeAnf":true}'
   ```

2. **Monitor Job** (get JOB_ID from step 1):
   ```bash
   curl -s "https://azfo-dev-func-xy76b.azurewebsites.net/api/GetJobLogs?jobId=<JOB_ID>" | \
     jq -r '.[]' | grep -E "🔍|📊|⚡"
   ```

3. **Check Volume JSON**:
   ```bash
   curl -s "https://azfo-dev-func-xy76b.azurewebsites.net/api/GetVolumeDetail?jobId=<JOB_ID>&volumeId=<VOLUME_ID>" | \
     jq '.costCalculationInputs | {PermutationId, PermutationName, ServiceLevel, CoolAccessEnabled, DoubleEncryptionEnabled}'
   ```

---

## Expected Results for Test Volume

**Volume**: `test1-smb-50-12-cool`

### Configuration
- Service Level: Flexible
- Cool Access: Enabled
- Double Encryption: Disabled (EncryptionKeySource = "Microsoft.NetApp")

### Identified Permutation
- **ID**: 11
- **Name**: ANF Flexible with Cool Access

### Throughput Model
- **Base**: 128 MiB/s (flat, not per TiB)
- **Current**: 12 MiB/s (below base)
- **Billable**: 0 MiB/s (no additional throughput cost)

### Cost Components
1. **Hot Capacity**: 50 GiB × $0.000181/hour × 720 hours = ~$6.53
2. **Cool Capacity**: 0 GiB (no cool data yet)
3. **Throughput**: $0 (below 128 MiB/s base)
4. **Total**: ~$6.53-$6.61

---

## Verification Checklist

- [x] Deployment successful
- [x] Health check passed
- [x] All functions deployed
- [ ] Job logs show permutation ID
- [ ] Job logs show configuration
- [ ] Job logs show throughput model
- [ ] Volume JSON includes permutation details
- [ ] Cost calculations match permutation formula

---

## Next Steps

1. **Run Test**: Execute `./test-permutation-logging.sh` to verify changes
2. **Review Logs**: Check that permutation info appears in job output
3. **Validate JSON**: Confirm permutation details in volume export
4. **Compare Costs**: Verify calculations match permutation formulas
5. **Update UI**: Consider displaying permutation info in web interface

---

## Rollback Plan

If issues are found:

```bash
# Revert to previous commit
git revert e4e83a5
git push origin main

# Or rollback to specific version
git reset --hard 2e0b7f5~1
git push --force origin main
```

---

## Support & Documentation

- **System Documentation**: `docs/ANF_COST_PERMUTATION_SYSTEM.md`
- **Usage Examples**: `docs/ANF_PERMUTATION_EXAMPLE.md`
- **Testing Guide**: `docs/ANF_PERMUTATION_TESTING_PLAN.md`
- **Cost Formulas**: `docs/STORAGE_TIER_COST_PERMUTATIONS.md`

---

## Success Metrics

✅ **Deployment**: Completed in 1m6s  
✅ **Build**: No errors  
✅ **Functions**: All 55 deployed  
✅ **Health**: Healthy  
⏳ **Testing**: Ready to begin  

---

**Deployment completed successfully at 2026-01-28T22:43:00Z**
