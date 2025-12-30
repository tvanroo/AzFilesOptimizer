# AzFilesOptimizer Deployment Guide (Azure-Native)

> Status: initial skeleton for the Azure-native deployment flow. This will be refined as infra and code are implemented.

## 1. Deployment models

AzFilesOptimizer is designed to be deployed into **each customer’s own Azure subscription**. There is no shared multi-tenant service.

Planned deployment flows:

1. **Deploy to Azure button (recommended for customers)**
   - A button in the GitHub `README.md` will launch an Azure Portal deployment experience using a Bicep/ARM template in this repo.

2. **Manual deployment (for developers / operators)**
   - Use Azure CLI or Azure Portal to deploy the same template from the `infra/` folder.

## 2. Prerequisites (to be detailed)

- Azure subscription with permissions to create:
  - Resource groups
  - Storage accounts
  - Function Apps
  - Key Vaults
  - Application Insights resources
- Rights to create or configure a Microsoft Entra ID app registration.

## 3. Resources deployed by the template (to be detailed)

- Resource group (optional, or expected to pre-exist).
- Storage Account (Tables/Queues/Blobs).
- Azure Functions App + hosting plan + managed identity.
- Azure Key Vault.
- Application Insights.
- Any necessary role assignments.

## 4. Post-deployment configuration (to be detailed)

- Create and configure Entra ID app registration.
- Configure Key Vault secrets (e.g., OpenAI/Azure OpenAI keys).
- Deploy frontend and backend code (via CI/CD pipeline or manual publication).

## 5. Local development (to be detailed)

- Running the backend (Functions) locally.
- Running the frontend locally.
- Connecting to either local or cloud resources for testing.

## 6. Troubleshooting and logs (to be detailed)

- Where to find logs in Application Insights.
- Common errors and recovery steps.
