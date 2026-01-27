# Cost Estimation System - Deployment & Testing Guide

## Prerequisites

- .NET 8.0 SDK
- Azure Functions Core Tools v4
- Azure subscription with:
  - Storage Account (for Azure Functions and Table Storage)
  - App Service or Function App (for hosting)
- Optional: Visual Studio 2022 or VS Code with Azure Functions extension

## Quick Start

### 1. Build the Backend

```powershell
cd C:\Users\tobyv\Sync\GitSync\GitHub\AzFilesOptimizer\src\backend
dotnet build
```

### 2. Run Locally

```powershell
# Start Azure Functions locally
func start
```

The API will be available at `http://localhost:7071`

### 3. Test the APIs

Open `http://localhost:7071/api/pricing/test?region=eastus&service=files` in your browser to test the Pricing API.

Or navigate to the test page (after starting the static web server for frontend):
```powershell
cd C:\Users\tobyv\Sync\GitSync\GitHub\AzFilesOptimizer\src\frontend
# Use your preferred static web server
python -m http.server 8080
# Or use live-server, http-server, etc.
```

Then open: `http://localhost:8080/cost-estimation-test.html`

## API Endpoints

### 1. Test Pricing API
**GET** `/api/pricing/test`

Query Parameters:
- `region` - Azure region (default: eastus)
- `service` - Service type: files, premium-files, anf, disk (default: files)

Example:
```
GET http://localhost:7071/api/pricing/test?region=westus&service=anf
```

### 2. Get Job Cost Estimates
**GET** `/api/discovery/{jobId}/cost-estimates`

Returns 30-day cost estimates for all resources in a discovery job.

Example:
```
GET http://localhost:7071/api/discovery/abc123/cost-estimates
```

Response:
```json
{
  "jobId": "abc123",
  "summary": {
    "TotalEstimatedCost": 1234.56,
    "AverageConfidence": 85.5,
    "ResourceCount": 15,
    "HighConfidenceCount": 10,
    "MediumConfidenceCount": 3,
    "LowConfidenceCount": 2
  },
  "estimates": [...]
}
```

### 3. Calculate Single Resource Estimate
**POST** `/api/cost-estimates/calculate`

Body: DiscoveredResource JSON

Example:
```json
{
  "ResourceId": "/subscriptions/.../shares/share1",
  "Name": "share1",
  "ResourceType": "AzureFile",
  "Location": "eastus",
  "CapacityGb": 500,
  "UsedGb": 350,
  "Properties": {
    "tier": "Hot",
    "sku": "Standard_LRS"
  }
}
```

## Testing Workflow

### Step 1: Test Pricing API

1. Navigate to `http://localhost:8080/cost-estimation-test.html`
2. In **Test 1**, select a region and service type
3. Click "Test Pricing API"
4. Verify that pricing data is returned

Expected result: JSON with price items from Azure Retail Prices API

### Step 2: Test Single Resource Estimate

1. In **Test 3**, select a resource type
2. Click "Load Sample" to populate the JSON
3. Modify values as needed
4. Click "Calculate"
5. Review the estimate with confidence level

Expected result:
```json
{
  "VolumeId": "...",
  "VolumeName": "share1",
  "TotalEstimatedCost": 45.67,
  "ConfidenceLevel": 85,
  "CostComponents": [...],
  "Notes": [...],
  "Warnings": []
}
```

### Step 3: Test Job Estimates

1. Run a discovery job first (use existing job functionality)
2. In **Test 2**, click "Use Latest Job" or enter a job ID
3. Click "Calculate Estimates"
4. Review the summary and individual resource estimates

## Deployment to Azure

### Option 1: Deploy via Azure Portal

1. Create a new Function App in Azure Portal
2. Configure Application Settings:
   - `AzureWebJobsStorage` - Connection string for storage account
   - Any other required settings

3. Deploy using VS Code:
   - Right-click on the `backend` folder
   - Select "Deploy to Function App..."
   - Choose your Function App

### Option 2: Deploy via Azure CLI

```powershell
# Login to Azure
az login

# Set variables
$resourceGroup = "your-resource-group"
$functionAppName = "your-function-app"
$storageAccount = "your-storage-account"
$location = "eastus"

# Create resources if needed
az group create --name $resourceGroup --location $location

az storage account create `
  --name $storageAccount `
  --resource-group $resourceGroup `
  --location $location `
  --sku Standard_LRS

az functionapp create `
  --name $functionAppName `
  --resource-group $resourceGroup `
  --consumption-plan-location $location `
  --runtime dotnet-isolated `
  --runtime-version 8 `
  --functions-version 4 `
  --storage-account $storageAccount

# Deploy
cd C:\Users\tobyv\Sync\GitSync\GitHub\AzFilesOptimizer\src\backend
func azure functionapp publish $functionAppName
```

### Option 3: Deploy via GitHub Actions

Create `.github/workflows/deploy-backend.yml`:

```yaml
name: Deploy Backend

on:
  push:
    branches: [ main ]
    paths:
      - 'src/backend/**'

jobs:
  deploy:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      
      - name: Build
        run: dotnet build src/backend/AzFilesOptimizer.Backend.csproj --configuration Release
      
      - name: Publish
        run: dotnet publish src/backend/AzFilesOptimizer.Backend.csproj --configuration Release --output ./output
      
      - name: Deploy to Azure Functions
        uses: Azure/functions-action@v1
        with:
          app-name: ${{ secrets.AZURE_FUNCTIONAPP_NAME }}
          package: './output'
          publish-profile: ${{ secrets.AZURE_FUNCTIONAPP_PUBLISH_PROFILE }}
```

## Verification

After deployment, verify the system is working:

1. **Test Pricing API**:
   ```powershell
   curl "https://your-function-app.azurewebsites.net/api/pricing/test?region=eastus&service=files"
   ```

2. **Check Function Logs**:
   - In Azure Portal, go to your Function App
   - Navigate to Functions > CostEstimationFunction > Monitor
   - Review logs for any errors

3. **Test with Real Data**:
   - Run a discovery job
   - Call `/api/discovery/{jobId}/cost-estimates`
   - Verify cost estimates are calculated

## Troubleshooting

### Issue: "No pricing data available"

**Cause**: Cannot reach Azure Retail Prices API

**Solutions**:
- Check internet connectivity from Function App
- Verify no firewall blocking `prices.azure.com`
- Check Function App logs for detailed error

### Issue: "Failed to get resource costs"

**Cause**: Missing dependency injection registration

**Solutions**:
- Verify `Program.cs` has all service registrations
- Rebuild and redeploy
- Check Application Insights for detailed errors

### Issue: Low confidence estimates

**Cause**: Missing metrics or pricing data

**Solutions**:
- Enable Azure Monitor metrics collection
- Verify resource properties are populated
- Check warnings in estimate response

### Issue: Different costs than expected

**Causes**:
- Using estimated transactions vs actual
- Missing egress data
- Snapshot sizes not included

**Solutions**:
- Compare estimate breakdown with actual billing
- Enable Azure Monitor for actual metrics
- Review Notes and Warnings in estimate

## Performance Considerations

- **Pricing Cache**: 24-hour cache reduces API calls
- **Bulk Estimates**: Process multiple resources in parallel
- **Rate Limiting**: Azure Retail Prices API has limits (rarely hit)

## Monitoring

Set up Application Insights to track:
- API response times
- Error rates
- Pricing API call volumes
- Confidence score distributions

Example KQL query:
```kusto
traces
| where message contains "Calculated 30-day"
| extend confidence = extract("([0-9.]+)% confidence", 1, message)
| summarize avgConfidence = avg(todouble(confidence)), count() by bin(timestamp, 1h)
```

## Next Steps

1. **Integration**: Wire cost estimates into job results page
2. **Automation**: Schedule periodic estimate updates
3. **Alerts**: Set up cost anomaly detection
4. **Reporting**: Create cost trend dashboards
5. **Optimization**: Use estimates to recommend tier changes

## Support

For issues or questions:
- Check logs in Application Insights
- Review [COST_ESTIMATION.md](./COST_ESTIMATION.md) for details
- Test locally first before deploying
- Verify Azure Retail Prices API is accessible
