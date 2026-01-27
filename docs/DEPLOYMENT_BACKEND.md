# Backend Deployment Guide

## Problem
The `func azure functionapp publish` command with `--force` flag changes the `FUNCTIONS_WORKER_RUNTIME` setting from `dotnet-isolated` to `dotnet`, which causes the function app to crash with HTTP 503 errors.

## Root Cause
The Azure Functions Core Tools `func` command detects the project type and automatically sets the worker runtime. When using `--force`, it overwrites the correct `dotnet-isolated` setting with `dotnet`, which is incompatible with our .NET 8 isolated worker process project.

## Correct Deployment Process

### Option 1: Manual Deployment (RECOMMENDED)
Use Azure Functions Core Tools without the `--force` flag and fix the runtime immediately after:

```powershell
# Navigate to backend directory
cd src\backend

# Deploy without --force flag
func azure functionapp publish azfo-dev-func-xy76b --csharp

# Immediately fix the worker runtime setting
az functionapp config appsettings set `
  --name azfo-dev-func-xy76b `
  --resource-group azfilesopt-dev-rg `
  --settings FUNCTIONS_WORKER_RUNTIME=dotnet-isolated

# Wait for app to restart (about 30 seconds)
Start-Sleep -Seconds 30

# Verify it's working
curl https://azfo-dev-func-xy76b.azurewebsites.net/api/jobs
```

### Option 2: Azure CLI Zip Deploy (ALTERNATIVE)
Deploy using Azure CLI without touching the worker runtime:

```powershell
# Navigate to backend directory
cd src\backend

# Build and publish locally
dotnet publish -c Release -o ./publish

# Create zip file
Compress-Archive -Path ./publish/* -DestinationPath ./publish.zip -Force

# Deploy via Azure CLI
az functionapp deployment source config-zip `
  --name azfo-dev-func-xy76b `
  --resource-group azfilesopt-dev-rg `
  --src ./publish.zip

# Cleanup
Remove-Item ./publish.zip
Remove-Item -Recurse ./publish
```

### Option 3: Fix Worker Runtime Setting Permanently in Bicep
Update the infrastructure to prevent this issue:

1. Edit `infra/main.bicep` and ensure the Function App settings include:

```bicep
{
  name: 'FUNCTIONS_WORKER_RUNTIME'
  value: 'dotnet-isolated'
}
```

2. Redeploy infrastructure if needed:

```powershell
az deployment group create `
  --resource-group azfilesopt-dev-rg `
  --template-file infra/main.bicep `
  --parameters environment=dev
```

## Verification Steps

After any deployment, verify the function app is working:

1. **Check Worker Runtime:**
```powershell
az functionapp config appsettings list `
  --name azfo-dev-func-xy76b `
  --resource-group azfilesopt-dev-rg `
  --query "[?name=='FUNCTIONS_WORKER_RUNTIME'].{name:name,value:value}" `
  -o table
```

Should show: `dotnet-isolated`

2. **Test API Endpoint:**
```powershell
curl https://azfo-dev-func-xy76b.azurewebsites.net/api/jobs
```

Should return HTTP 200 with JSON data.

3. **Check Application Insights Logs:**
```powershell
az monitor app-insights query `
  --app azfo-dev-appi-xy76b `
  --analytics-query "traces | where timestamp > ago(5m) | order by timestamp desc | limit 20"
```

## Emergency Recovery

If the backend is down (HTTP 503):

```powershell
# Fix the worker runtime
az functionapp config appsettings set `
  --name azfo-dev-func-xy76b `
  --resource-group azfilesopt-dev-rg `
  --settings FUNCTIONS_WORKER_RUNTIME=dotnet-isolated

# Restart the function app
az functionapp restart `
  --name azfo-dev-func-xy76b `
  --resource-group azfilesopt-dev-rg

# Wait and test
Start-Sleep -Seconds 30
curl https://azfo-dev-func-xy76b.azurewebsites.net/api/jobs
```

## Required Environment Variables

After deployment, ensure these settings are configured:

```powershell
az functionapp config appsettings set `
  --name azfo-dev-func-xy76b `
  --resource-group azfilesopt-dev-rg `
  --settings `
    FUNCTIONS_WORKER_RUNTIME=dotnet-isolated `
    KEYVAULT_URI=https://azfo-dev-kv-xy76b.vault.azure.net/
```

## GitHub Actions Workflow (Future)

To automate this and prevent the issue, update `.github/workflows/deploy-functions.yml` to use zip deploy method or ensure worker runtime is set correctly after deployment.

## Notes

- The "Error calling sync triggers (BadRequest)" message at the end of `func publish` is a known Azure Functions issue and does NOT indicate deployment failure
- Always verify the deployment with actual API calls
- The function app takes 20-30 seconds to fully restart after settings changes
- Using `--force` flag should be avoided unless absolutely necessary, and always followed by runtime fix
