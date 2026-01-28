# ANF Cost Permutation System - Testing Plan

**Deployment Date**: 2026-01-28T22:40:34 UTC  
**Deployment Status**: ✅ Successful  
**Function App**: azfo-dev-func-xy76b (Running)  
**Commit**: 2e0b7f5

---

## Prerequisites

1. ✅ **Deployment Verified**
   - GitHub Actions workflow completed successfully
   - Azure Functions deployed and running
   - Health check passed

2. **Required Access**
   - Azure subscription with ANF volumes
   - Function app endpoint: `https://azfo-dev-func-xy76b.azurewebsites.net`
   - Storage account for discovery results

3. **Test Data Requirements**
   - At least one ANF volume with each permutation (ideally)
   - Existing test volume: `test1-smb-50-12-cool` (Flexible with Cool Access - Permutation 11)

---

## Phase 1: Smoke Tests (5 minutes)

### Test 1.1: Health Check ✅
```bash
curl -s https://azfo-dev-func-xy76b.azurewebsites.net/api/health | jq
```

**Expected Result**: Status "healthy"  
**Actual Result**: ✅ Passed

### Test 1.2: Verify Function List
```bash
az functionapp function list --name azfo-dev-func-xy76b \
  --resource-group azfilesopt-dev-rg \
  --query "[].name" --output table
```

**Expected Result**: All 55 functions listed  
**Status**: ✅ Verified

---

## Phase 2: Discovery Job Tests (15-30 minutes)

### Test 2.1: Create Discovery Job

```bash
# Get subscription IDs
az account list --query "[].{Name:name, ID:id}" --output table

# Create discovery job
curl -X POST "https://azfo-dev-func-xy76b.azurewebsites.net/api/CreateDiscoveryJob" \
  -H "Content-Type: application/json" \
  -d '{
    "subscriptionIds": ["<your-subscription-id>"],
    "includeAzureFiles": false,
    "includeAnf": true,
    "includeManagedDisks": false
  }' | jq
```

**Expected Output**:
```json
{
  "jobId": "<guid>",
  "status": "Created",
  "createdAt": "2026-01-28T22:45:00Z"
}
```

**Action**: Save the `jobId` for subsequent tests

### Test 2.2: Monitor Job Progress

```bash
# Poll job status (every 30 seconds)
JOB_ID="<job-id-from-2.1>"

curl -s "https://azfo-dev-func-xy76b.azurewebsites.net/api/GetJob?jobId=$JOB_ID" | jq '.status'
```

**Expected Statuses**: `Created` → `InProgress` → `Completed`

**Monitoring Script**:
```bash
#!/bin/bash
JOB_ID="<your-job-id>"
while true; do
  STATUS=$(curl -s "https://azfo-dev-func-xy76b.azurewebsites.net/api/GetJob?jobId=$JOB_ID" | jq -r '.status')
  echo "[$(date +%H:%M:%S)] Job Status: $STATUS"
  
  if [ "$STATUS" = "Completed" ] || [ "$STATUS" = "Failed" ]; then
    break
  fi
  
  sleep 30
done
```

### Test 2.3: Retrieve Job Results

```bash
# Get job summary
curl -s "https://azfo-dev-func-xy76b.azurewebsites.net/api/GetJob?jobId=$JOB_ID" | jq

# Get discovered volumes
curl -s "https://azfo-dev-func-xy76b.azurewebsites.net/api/GetVolumes?jobId=$JOB_ID" | jq '.[] | {
  volumeId: .volumeId,
  volumeName: .volumeName,
  serviceLevel: .volumeData.serviceLevel,
  coolAccessEnabled: .volumeData.coolAccessEnabled,
  encryptionKeySource: .volumeData.encryptionKeySource
}'
```

**Expected Result**: List of ANF volumes with their properties

---

## Phase 3: Permutation Identification Tests (10 minutes)

### Test 3.1: Identify Permutations in Discovered Volumes

Download a volume JSON and analyze its permutation:

```bash
# Get volume detail
VOLUME_ID="<volume-id-from-discovery>"

curl -s "https://azfo-dev-func-xy76b.azurewebsites.net/api/GetVolumeDetail?jobId=$JOB_ID&volumeId=$VOLUME_ID" \
  > volume-detail.json

# Analyze permutation (manual check)
cat volume-detail.json | jq '{
  serviceLevel: .volumeData.serviceLevel,
  coolAccessEnabled: .volumeData.coolAccessEnabled,
  encryptionKeySource: .volumeData.encryptionKeySource,
  isDoubleEncryption: (.volumeData.encryptionKeySource != "Microsoft.NetApp")
}'
```

**Manual Mapping to Permutation**:
```
Service Level: Flexible
Cool Access: true
Double Encryption: false (EncryptionKeySource = "Microsoft.NetApp")
→ Permutation 11: ANF Flexible with Cool Access
```

### Test 3.2: Verify Permutation for Each Discovered Volume

For each discovered ANF volume, verify:

| Volume Name | Service Level | Cool Access | Double Enc | Expected Permutation |
|-------------|---------------|-------------|------------|---------------------|
| test1-smb-50-12-cool | Flexible | true | false | 11 |
| (add your volumes) | ... | ... | ... | ... |

---

## Phase 4: Cost Calculation Tests (15 minutes)

### Test 4.1: Verify Cost Components in JSON

Check if the cost calculation includes permutation details:

```bash
cat volume-detail.json | jq '{
  costSummary: .costSummary,
  costCalculationInputs: .costCalculationInputs,
  detailedCostAnalysis: .detailedCostAnalysis
}'
```

**Expected Output Structure** (if integrated):
```json
{
  "costCalculationInputs": {
    "permutationId": 11,
    "permutationName": "ANF Flexible with Cool Access",
    "provisionedCapacityGiB": 50.0,
    "hotCapacityGiB": 50.0,
    "coolCapacityGiB": 0.0,
    "requiredThroughputMiBps": 12.0
  }
}
```

### Test 4.2: Manual Cost Calculation Verification

Using the volume JSON, manually calculate expected cost:

```bash
# Extract key metrics
cat volume-detail.json | jq '{
  provisionedGiB: (.volumeData.provisionedSizeBytes / (1024*1024*1024)),
  throughputMiBps: .volumeData.throughputMibps,
  serviceLevel: .volumeData.serviceLevel,
  coolAccess: .volumeData.coolAccessEnabled,
  region: .volumeData.location
}'
```

**Manual Calculation** (Permutation 11 - Flexible with Cool Access):
```
Provisioned: 50 GiB
Throughput: 12 MiB/s (below 128 MiB/s base)
Region: southcentralus

Formula:
  Hot Capacity = 50 GiB × $0.000205/GiB/hour × 720 = $7.38
  Cool Capacity = 0 GiB (no cool data)
  Throughput = 0 (12 < 128 base)
  
Expected Total: ~$7.38 for 30 days
```

Compare with actual cost in JSON: `costSummary.totalCost30Days`

### Test 4.3: Test Pricing API Integration

```bash
# Test if pricing data is available
curl -s "https://azfo-dev-func-xy76b.azurewebsites.net/api/TestPricing?region=southcentralus&serviceType=anf" | jq
```

---

## Phase 5: Edge Case Testing (10 minutes)

### Test 5.1: Different Permutations

If you have access to multiple ANF volumes, test different permutations:

**Permutation 1** - Standard Regular:
```bash
# Find a Standard volume without cool access or double encryption
curl -s "https://azfo-dev-func-xy76b.azurewebsites.net/api/GetVolumes?jobId=$JOB_ID" | \
  jq '.[] | select(.volumeData.serviceLevel == "Standard" and 
                    .volumeData.coolAccessEnabled == false)'
```

**Permutation 4** - Premium Regular:
```bash
# Find a Premium volume
curl -s "https://azfo-dev-func-xy76b.azurewebsites.net/api/GetVolumes?jobId=$JOB_ID" | \
  jq '.[] | select(.volumeData.serviceLevel == "Premium")'
```

### Test 5.2: Validation Tests

Create test scenarios for validation errors:

1. **Minimum Capacity**: Volume < 50 GiB
2. **Cool Access Minimum**: Cool access enabled with < 2,400 GiB
3. **Flexible Throughput**: Flexible tier without throughput data

---

## Phase 6: Integration Testing (15 minutes)

### Test 6.1: Cost Analysis Workflow

Trigger a full cost analysis on the discovered job:

```bash
curl -X POST "https://azfo-dev-func-xy76b.azurewebsites.net/api/TriggerCostAnalysis" \
  -H "Content-Type: application/json" \
  -d "{\"jobId\": \"$JOB_ID\"}" | jq
```

### Test 6.2: Export Results

Export the discovered volumes with cost data:

```bash
curl -s "https://azfo-dev-func-xy76b.azurewebsites.net/api/ExportVolumes?jobId=$JOB_ID&format=json" \
  > discovered-volumes-export.json

# Check for permutation data in export
cat discovered-volumes-export.json | jq '.[0] | keys' | grep -i permutation
```

---

## Phase 7: Validation & Reporting (10 minutes)

### Test 7.1: Validate All Test Results

Create a test results summary:

```bash
cat > test-results.md << 'EOF'
# ANF Cost Permutation Testing Results

## Test Summary
- **Date**: 2026-01-28
- **Job ID**: <your-job-id>
- **Volumes Discovered**: <count>
- **Permutations Found**: <list>

## Test Results

### Phase 1: Smoke Tests
- [x] Health Check
- [x] Function List

### Phase 2: Discovery
- [ ] Job Created
- [ ] Job Completed
- [ ] Volumes Retrieved

### Phase 3: Permutation Identification
- [ ] Permutation 1 (Standard)
- [ ] Permutation 4 (Premium)
- [ ] Permutation 7 (Ultra)
- [ ] Permutation 10 (Flexible)
- [ ] Permutation 11 (Flexible + Cool)

### Phase 4: Cost Calculations
- [ ] Cost components match permutation formula
- [ ] Throughput handling (Flexible tier)
- [ ] Cool access calculations

### Phase 5: Edge Cases
- [ ] Validation errors triggered correctly
- [ ] Minimum capacity checks
- [ ] Double encryption detection

## Issues Found
<list any issues>

## Recommendations
<list recommendations>
EOF
```

### Test 7.2: Compare with Previous Cost Calculations

If you have existing cost data, compare:

```bash
# Get old cost calculation
echo "Old Cost: $6.6065 (from existing JSON)"

# Get new cost calculation
NEW_COST=$(cat volume-detail.json | jq -r '.costSummary.totalCost30Days')
echo "New Cost: $NEW_COST"

# Calculate difference
echo "Difference: $(echo "$NEW_COST - 6.6065" | bc) USD"
```

---

## Phase 8: Documentation & Sign-off (5 minutes)

### Test 8.1: Update Documentation

Document findings:
- Which permutations are present in your environment
- Any discrepancies in cost calculations
- Performance observations
- Any bugs or issues

### Test 8.2: Create Test Report

```bash
# Generate final report
cat > final-test-report.md << 'EOF'
# ANF Cost Permutation System - Test Report

**Test Date**: 2026-01-28
**Tester**: [Your Name]
**Environment**: azfo-dev-func-xy76b

## Executive Summary
- ✅ Deployment successful
- ✅ All functions operational
- Total volumes tested: <count>
- Permutations identified: <list>
- Cost accuracy: <assessment>

## Detailed Results
[Paste results from each phase]

## Recommendations
1. [Integration with existing cost calculator]
2. [UI updates to show permutation info]
3. [Additional testing for permutations X, Y, Z]

## Sign-off
- [ ] All critical tests passed
- [ ] No blocking issues found
- [ ] Ready for production
EOF
```

---

## Quick Test Script

For a rapid end-to-end test:

```bash
#!/bin/bash
set -e

echo "=== ANF Permutation Testing Quick Script ==="

# 1. Create discovery job
echo "Creating discovery job..."
JOB_RESPONSE=$(curl -s -X POST "https://azfo-dev-func-xy76b.azurewebsites.net/api/CreateDiscoveryJob" \
  -H "Content-Type: application/json" \
  -d '{"subscriptionIds":["<sub-id>"],"includeAnf":true}')

JOB_ID=$(echo $JOB_RESPONSE | jq -r '.jobId')
echo "Job ID: $JOB_ID"

# 2. Wait for completion
echo "Waiting for job completion..."
while true; do
  STATUS=$(curl -s "https://azfo-dev-func-xy76b.azurewebsites.net/api/GetJob?jobId=$JOB_ID" | jq -r '.status')
  echo "Status: $STATUS"
  
  if [ "$STATUS" = "Completed" ]; then
    break
  fi
  
  sleep 30
done

# 3. Get results
echo "Retrieving volumes..."
curl -s "https://azfo-dev-func-xy76b.azurewebsites.net/api/GetVolumes?jobId=$JOB_ID" \
  > volumes.json

# 4. Analyze permutations
echo "Analyzing permutations..."
cat volumes.json | jq -r '.[] | "\(.volumeName): \(.volumeData.serviceLevel) + Cool:\(.volumeData.coolAccessEnabled)"'

echo "=== Test Complete ==="
echo "Results saved to: volumes.json"
echo "Job ID: $JOB_ID"
```

---

## Expected Timeline

| Phase | Duration | Status |
|-------|----------|--------|
| Phase 1: Smoke Tests | 5 min | ⏱️ Ready |
| Phase 2: Discovery | 15-30 min | ⏱️ Ready |
| Phase 3: Permutation ID | 10 min | ⏱️ Ready |
| Phase 4: Cost Calc | 15 min | ⏱️ Ready |
| Phase 5: Edge Cases | 10 min | ⏱️ Ready |
| Phase 6: Integration | 15 min | ⏱️ Ready |
| Phase 7: Validation | 10 min | ⏱️ Ready |
| Phase 8: Documentation | 5 min | ⏱️ Ready |
| **Total** | **85-100 min** | |

---

## Success Criteria

✅ **Must Have**:
1. Discovery job completes successfully
2. All ANF volumes are discovered
3. Permutations are correctly identified
4. Cost calculations align with formulas
5. No critical errors in logs

✅ **Nice to Have**:
1. Test all 11 permutations
2. Performance benchmarks
3. Cost comparison with old system
4. UI displays permutation info

---

## Troubleshooting

### Issue: Discovery Job Fails
```bash
# Check job logs
curl -s "https://azfo-dev-func-xy76b.azurewebsites.net/api/GetJobLogs?jobId=$JOB_ID" | jq

# Check function app logs
az monitor activity-log list --resource-group azfilesopt-dev-rg --output table
```

### Issue: Cost Calculation Incorrect
```bash
# Enable detailed logging
# Check CostCalculationInputs in volume JSON
# Verify pricing API responses
```

### Issue: Permutation Not Detected
```bash
# Verify EncryptionKeySource field
# Check ServiceLevel value
# Verify CoolAccessEnabled flag
```

---

## Next Steps After Testing

1. **If All Tests Pass**:
   - Update production deployment
   - Create user documentation
   - Train support team on new permutations

2. **If Issues Found**:
   - Document issues in GitHub
   - Create fix branch
   - Re-test after fixes

3. **Enhancement Opportunities**:
   - Add permutation visualization in UI
   - Create cost optimization recommendations per permutation
   - Add automated permutation-specific testing

---

**Ready to Begin Testing!** 🚀

Start with Phase 1 and work through systematically. Document all findings in `test-results.md`.
