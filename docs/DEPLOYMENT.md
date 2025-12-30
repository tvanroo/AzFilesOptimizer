# AzFilesOptimizer Deployment Guide (Azure-Native)

> Status: initial skeleton for the Azure-native deployment flow. This will be refined as infra and code are implemented.

## 1. Deployment models

AzFilesOptimizer is designed to be deployed into **each customer’s own Azure subscription**. There is no shared multi-tenant service.

Planned deployment flows:

1. **Deploy to Azure button (recommended for customers)**
   - A button in the GitHub `README.md` will launch an Azure Portal deployment experience using a Bicep/ARM template in this repo.

2. **Manual deployment (for developers / operators)**
   - Use Azure CLI or Azure Portal to deploy the same template from the `infra/` folder.

## 2. Prerequisites

To deploy AzFilesOptimizer into a subscription (including lab environments), you need:

- An **Azure subscription** where you can create resources:
  - Resource groups
  - Storage accounts
  - Function Apps and App Service plans
  - Key Vaults
  - Application Insights resources
- **RBAC permissions** (e.g., Owner or Contributor) on the target resource group.
- (Later phases) Rights to create or configure a **Microsoft Entra ID app registration** for the web UI.
- Azure CLI installed locally (recommended) if deploying from the command line.

## 3. Resources deployed by `infra/main.bicep`

The `infra/main.bicep` template creates the core infrastructure for AzFilesOptimizer in a single resource group:

- **Storage Account** (general-purpose v2)
  - Used for Table, Queue, and Blob storage.
  - Enforces HTTPS-only and disables public blob access.
- **Azure Functions Consumption Plan**
  - `Microsoft.Web/serverfarms` with SKU `Y1` (Dynamic).
- **Function App**
  - `Microsoft.Web/sites` with kind `functionapp`.
  - System-assigned managed identity enabled.
  - Configured with:
    - `AzureWebJobsStorage` connection string for the Storage Account.
    - Application Insights instrumentation key + connection string.
- **Azure Key Vault**
  - RBAC-enabled (`enableRbacAuthorization = true`).
  - Intended to store LLM API keys and other secrets.
- **Application Insights**
  - For telemetry and diagnostics.
- **Role assignment**
  - Grants the Function App’s managed identity the **Key Vault Secrets User** role on the Key Vault.

### Template parameters

`infra/main.bicep` accepts the following parameters:

- `location` (string, default = resource group location)
  - Azure region for all resources.
- `environment` (string, default = `dev`)
  - Logical environment label for tagging and human-readable naming (allowed: `dev`, `test`, `prod`).
- `tags` (object, default with `environment` + `project`)
  - Tags applied to all resources.

You **do not need to pick resource names**. The template auto-generates names using a fixed prefix (`azfo` for Azure Files Optimizer), the environment, and a short random suffix derived from the resource group ID to ensure global uniqueness. For example:

- Storage account: `azfodevstabc12`
- Function plan: `azfo-dev-func-plan-abc12`
- Function app: `azfo-dev-func-abc12`
- Key Vault: `azfo-dev-kv-abc12`
- App Insights: `azfo-dev-appi-abc12`

A sample parameter file is provided at `infra/parameters/dev.json`.

## 4. Deploying the core infrastructure (Phase 1)

This section shows how to deploy `infra/main.bicep` into a resource group using the Azure CLI. Adjust names and locations as needed for your lab environment.

1. **Create a resource group** (if you don’t already have one):

   ```powershell path=null start=null
   $rgName = "azfilesopt-dev-rg"
   $location = "eastus"    # or your preferred region

   az group create --name $rgName --location $location
   ```

2. **Deploy the template with the sample parameters file**:

   ```powershell path=null start=null
   az deployment group create \
     --resource-group $rgName \
     --template-file infra/main.bicep \
     --parameters @infra/parameters/dev.json
   ```

   You can override parameters on the command line if needed, for example:

   ```powershell path=null start=null
   az deployment group create \
     --resource-group $rgName \
     --template-file infra/main.bicep \
     --parameters baseName=azfilesopt environment=dev location=$location
   ```

3. **Review deployment outputs**

   The deployment will output the names of the core resources:

   - `storageAccountName`
   - `functionAppName`
   - `keyVaultName`
   - `appInsightsName`

   These values will be used later when you configure and deploy the backend/ frontend code.

## 5. Post-deployment configuration (high level)

After Phase 1, the core infrastructure exists but no code is deployed yet. At a high level, the next steps will be:

- Create and configure an Entra ID app registration for the web UI.
- Configure Key Vault secrets (e.g., OpenAI/Azure OpenAI keys, any external endpoints).
- Deploy backend (Functions) and frontend (Static Web App) code using CI/CD or manual publish.

These are covered in later phases of the project plan and will be documented in more detail as the code is implemented.

## 6. Local development

### 6.1 Prerequisites for local development

To run AzFilesOptimizer locally for development and testing:

**Backend (Azure Functions):**
- .NET 8.0 SDK
- Azure Functions Core Tools (v4.x)
- Azure CLI (for deployment and resource management)

**Frontend (Static Web App):**
- Any modern web browser
- A local web server (e.g., Python's `http.server`, Node.js `http-server`, or VS Code Live Server extension)

### 6.2 Running the backend locally

1. **Navigate to the backend directory:**

   ```powershell path=null start=null
   cd src/backend
   ```

2. **Configure local settings:**

   Create or update `local.settings.json` with your local or cloud connection strings:

   ```json
   {
     "IsEncrypted": false,
     "Values": {
       "AzureWebJobsStorage": "UseDevelopmentStorage=true",
       "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated"
     }
   }
   ```

   For cloud storage, replace `UseDevelopmentStorage=true` with your Azure Storage connection string.

3. **Build and start the Functions runtime:**

   ```powershell path=null start=null
   func start
   ```

   The backend will start on `http://localhost:7071`. You should see:
   - Health endpoint at `http://localhost:7071/api/health`
   - JobProcessor queue trigger registered

4. **Test the health endpoint:**

   ```powershell path=null start=null
   curl http://localhost:7071/api/health
   ```

### 6.3 Running the frontend locally

1. **Navigate to the frontend directory:**

   ```powershell path=null start=null
   cd src/frontend
   ```

2. **Start a local web server:**

   Using Python (if installed):
   ```powershell path=null start=null
   python -m http.server 8080
   ```

   Using Node.js http-server (install with `npm install -g http-server`):
   ```powershell path=null start=null
   http-server -p 8080
   ```

   Or use VS Code's Live Server extension (right-click on `index.html` and select "Open with Live Server").

3. **Open the application:**

   Navigate to `http://localhost:8080` in your browser.

4. **Verify functionality:**

   - The Home page should display the backend health status
   - Navigate to the Jobs page to see mock job data
   - Click on individual jobs to view detailed information

**Note:** The frontend is configured to automatically detect when running locally and will call the backend at `http://localhost:7071/api`. Make sure the backend is running before testing the frontend.

### 6.4 End-to-end local testing

For full local testing:

1. Start the backend in one terminal:
   ```powershell path=null start=null
   cd src/backend
   func start
   ```

2. Start the frontend in another terminal:
   ```powershell path=null start=null
   cd src/frontend
   python -m http.server 8080
   ```

3. Access the application at `http://localhost:8080`

### 6.5 Connecting to cloud resources

To test the backend against real Azure resources:

1. Update `local.settings.json` with your deployed Azure Storage connection string
2. Add any required Key Vault references or additional configuration
3. Ensure your Azure CLI is authenticated: `az login`
4. Grant your local user identity appropriate RBAC roles on the target resources

## 7. Troubleshooting and logs (to be detailed)

- Where to find logs in Application Insights.
- Common errors and recovery steps.
