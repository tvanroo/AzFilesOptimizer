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

## 5. Configuring Azure AD Authentication

### 5.1 Overview

AzFilesOptimizer uses **user authentication with delegated permissions**. Users sign in with their Azure AD account, and the application acts on their behalf to discover and analyze Azure resources. No service principal creation is required from users.

**Authentication Flow:**
1. User clicks "Sign In" in the web UI
2. User is redirected to Azure AD to authenticate
3. User consents to delegated permissions (if first time)
4. Azure AD returns an access token
5. Frontend sends the token with API requests
6. Backend validates the token and acts on behalf of the user

### 5.2 Create an Entra ID App Registration

**Prerequisites:**
- Azure AD tenant administrator or Application Developer role
- Access to Azure Portal

**Steps:**

1. **Navigate to Azure AD App Registrations:**
   - Go to [Azure Portal](https://portal.azure.com)
   - Search for "App registrations" or navigate to **Azure Active Directory > App registrations**
   - Click **+ New registration**

2. **Register the application:**
   - **Name:** `AzFilesOptimizer` (or your preferred name)
   - **Supported account types:** Select one of:
     - "Accounts in this organizational directory only" (Single tenant - recommended for internal use)
     - "Accounts in any organizational directory" (Multi-tenant - if deploying for multiple organizations)
   - **Redirect URI:** 
     - Platform: **Single-page application (SPA)**
     - For local development: `http://localhost:8080`
     - For production: Your deployed Static Web App URL (e.g., `https://azfilesopt.azurestaticapps.net`)
   - Click **Register**

3. **Note the Application (client) ID and Tenant ID:**
   - On the **Overview** page, copy:
     - **Application (client) ID** - You'll need this for frontend configuration
     - **Directory (tenant) ID** - You'll need this for frontend configuration

4. **Configure API permissions (delegated):**
   - Navigate to **API permissions**
   - Click **+ Add a permission**
   - Select **Azure Service Management**
   - Check **user_impersonation** ("Access Azure Service Management as organization users")
   - Click **Add permissions**
   - Optionally click **Grant admin consent for [Your Tenant]** to pre-consent for all users

5. **Configure additional redirect URIs (if needed):**
   - Navigate to **Authentication**
   - Under **Single-page application**, add additional redirect URIs:
     - `http://localhost:8080` (local development)
     - `https://your-app.azurestaticapps.net` (production)
   - Under **Advanced settings**:
     - Enable **Access tokens** (used for implicit flows)
     - Enable **ID tokens** (used for user identification)

6. **Configure optional claims (recommended):**
   - Navigate to **Token configuration**
   - Click **+ Add optional claim**
   - Select **ID** token type
   - Add: `email`, `family_name`, `given_name`
   - Click **Add**

### 5.3 Configure the Frontend

Create a configuration file `src/frontend/js/auth-config.js`:

```javascript
const msalConfig = {
    auth: {
        clientId: "YOUR_APPLICATION_CLIENT_ID",
        authority: "https://login.microsoftonline.com/YOUR_TENANT_ID",
        redirectUri: window.location.origin
    },
    cache: {
        cacheLocation: "localStorage",
        storeAuthStateInCookie: false
    }
};

const loginRequest = {
    scopes: [
        "https://management.azure.com/user_impersonation",  // Azure Resource Manager
        "User.Read"  // Microsoft Graph (for user profile)
    ]
};
```

Replace:
- `YOUR_APPLICATION_CLIENT_ID` with the Application (client) ID from step 3
- `YOUR_TENANT_ID` with the Directory (tenant) ID from step 3

### 5.4 Configure the Backend

Update `src/backend/local.settings.json` for local development:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AzureAd:TenantId": "YOUR_TENANT_ID",
    "AzureAd:ClientId": "YOUR_APPLICATION_CLIENT_ID"
  }
}
```

For deployed environments, set these as Application Settings in the Function App.

### 5.5 Required User Permissions

Users who sign in must have appropriate RBAC roles on Azure subscriptions/resource groups to discover resources:

- **Reader** (minimum) - to enumerate Azure Files and ANF resources
- **Contributor** (recommended) - if future features require write operations

Users assign these roles via Azure Portal > Subscriptions > Access control (IAM).

### 5.6 Testing Authentication

1. Start the frontend and backend locally
2. Navigate to `http://localhost:8080`
3. Click "Sign In"
4. You should be redirected to Microsoft login
5. After signing in, you should see your name/email in the UI
6. Backend should validate your token on API calls

## 6. Post-deployment configuration

After deploying infrastructure and configuring authentication:

- Configure Key Vault secrets (e.g., OpenAI/Azure OpenAI keys)
- Deploy backend (Functions) and frontend (Static Web App) code using CI/CD
- Update Function App settings with Azure AD configuration

These are covered in later phases of the project plan.

## 7. Local development

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

## 8. Troubleshooting and logs (to be detailed)

- Where to find logs in Application Insights.
- Common errors and recovery steps.
