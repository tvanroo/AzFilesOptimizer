#!/bin/bash
# Quick test script to verify ANF permutation logging in discovery job output
# Usage: ./test-permutation-logging.sh <subscription-id>

set -e

SUBSCRIPTION_ID="${1:-c560a042-4311-40cf-beb5-edc67991179e}"
API_BASE="https://azfo-dev-func-xy76b.azurewebsites.net/api"

echo "======================================================================"
echo "ANF Cost Permutation Logging Test"
echo "======================================================================"
echo ""

# Step 1: Create discovery job
echo "Step 1: Creating discovery job for subscription $SUBSCRIPTION_ID..."
JOB_RESPONSE=$(curl -s -X POST "$API_BASE/CreateDiscoveryJob" \
  -H "Content-Type: application/json" \
  -d "{
    \"subscriptionIds\": [\"$SUBSCRIPTION_ID\"],
    \"includeAzureFiles\": false,
    \"includeAnf\": true,
    \"includeManagedDisks\": false,
    \"resourceGroupFilters\": [\"southcentral-core.rg\"]
  }")

JOB_ID=$(echo "$JOB_RESPONSE" | jq -r '.jobId')
echo "✓ Job created: $JOB_ID"
echo ""

# Step 2: Wait for completion
echo "Step 2: Monitoring job progress..."
ATTEMPT=0
MAX_ATTEMPTS=40

while [ $ATTEMPT -lt $MAX_ATTEMPTS ]; do
  STATUS=$(curl -s "$API_BASE/GetJob?jobId=$JOB_ID" | jq -r '.status')
  echo "[$(date +%H:%M:%S)] Status: $STATUS"
  
  if [ "$STATUS" = "Completed" ]; then
    echo "✓ Job completed successfully!"
    break
  elif [ "$STATUS" = "Failed" ]; then
    echo "✗ Job failed!"
    exit 1
  fi
  
  ATTEMPT=$((ATTEMPT + 1))
  sleep 15
done

if [ $ATTEMPT -eq $MAX_ATTEMPTS ]; then
  echo "✗ Timeout waiting for job completion"
  exit 1
fi

echo ""

# Step 3: Get job logs and search for permutation info
echo "Step 3: Extracting permutation information from job logs..."
echo "----------------------------------------------------------------------"

curl -s "$API_BASE/GetJobLogs?jobId=$JOB_ID" | jq -r '.[]' | \
  grep -E "🔍|📊|⚡|💰|Permutation" | \
  head -20

echo "----------------------------------------------------------------------"
echo ""

# Step 4: Get volume details and check for permutation in JSON
echo "Step 4: Checking volume JSON for permutation data..."
VOLUMES=$(curl -s "$API_BASE/GetVolumes?jobId=$JOB_ID")
VOLUME_COUNT=$(echo "$VOLUMES" | jq -r 'length')

if [ "$VOLUME_COUNT" -gt 0 ]; then
  echo "✓ Found $VOLUME_COUNT volume(s)"
  
  # Get first volume details
  VOLUME_ID=$(echo "$VOLUMES" | jq -r '.[0].volumeId')
  echo "  Analyzing volume: $VOLUME_ID"
  
  VOLUME_DETAIL=$(curl -s "$API_BASE/GetVolumeDetail?jobId=$JOB_ID&volumeId=$VOLUME_ID")
  
  # Extract permutation info
  echo ""
  echo "  Permutation Details:"
  echo "  -------------------"
  echo "$VOLUME_DETAIL" | jq -r '
    if .costCalculationInputs then
      "  Permutation ID: " + (.costCalculationInputs.PermutationId // "N/A" | tostring),
      "  Permutation Name: " + (.costCalculationInputs.PermutationName // "N/A"),
      "  Service Level: " + (.costCalculationInputs.ServiceLevel // "N/A"),
      "  Cool Access: " + (.costCalculationInputs.CoolAccessEnabled // false | tostring),
      "  Double Encryption: " + (.costCalculationInputs.DoubleEncryptionEnabled // false | tostring)
    else
      "  ⚠️  No cost calculation inputs found (may need to wait for cost analysis)"
    end
  '
  
  # Extract cost summary
  echo ""
  echo "  Cost Summary:"
  echo "  -------------"
  echo "$VOLUME_DETAIL" | jq -r '
    if .costSummary then
      "  Total Cost (30 days): $" + (.costSummary.totalCost30Days | tostring),
      "  Daily Average: $" + (.costSummary.dailyAverage | tostring)
    else
      "  ⚠️  No cost summary found"
    end
  '
  
  echo ""
else
  echo "✗ No volumes found"
fi

echo ""
echo "======================================================================"
echo "Test Complete!"
echo "======================================================================"
echo ""
echo "Summary:"
echo "  Job ID: $JOB_ID"
echo "  Volumes Discovered: $VOLUME_COUNT"
echo ""
echo "Next steps:"
echo "  1. Review the job logs above for permutation identification"
echo "  2. Check the volume JSON for permutation details in costCalculationInputs"
echo "  3. Verify cost calculations match the permutation formula"
echo ""
echo "View full job details:"
echo "  curl -s '$API_BASE/GetJob?jobId=$JOB_ID' | jq"
echo ""
echo "View full volume details:"
if [ "$VOLUME_COUNT" -gt 0 ]; then
  echo "  curl -s '$API_BASE/GetVolumeDetail?jobId=$JOB_ID&volumeId=$VOLUME_ID' | jq"
fi
echo ""
