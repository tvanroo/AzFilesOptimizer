@description('Azure region where all resources will be deployed.')
param location string = resourceGroup().location

@allowed([
  'dev'
  'test'
  'prod'
])
@description('Logical environment name used for tagging and friendly naming only.')
param environment string = 'dev'

@description('Tags applied to all resources created by this template.')
param tags object = {
  environment: environment
  project: 'AzFilesOptimizer'
}

// Common prefix for all resources in this deployment (Azure Files Optimizer = azfo).
var basePrefix = toLower('azfo')
// Stable, per-resource-group suffix to help ensure global uniqueness.
var nameSuffix = toLower(uniqueString(resourceGroup().id))
var shortSuffix = substring(nameSuffix, 0, 5)

// Resource names are derived from a fixed prefix + environment + a short random suffix.
var storageAccountName = '${basePrefix}${environment}st${shortSuffix}'
var hostingPlanName = '${basePrefix}-${environment}-func-plan-${shortSuffix}'
var functionAppName = '${basePrefix}-${environment}-func-${shortSuffix}'
var keyVaultName = '${basePrefix}-${environment}-kv-${shortSuffix}'
var appInsightsName = '${basePrefix}-${environment}-appi-${shortSuffix}'

// Storage account for Tables/Queues/Blobs used by AzFilesOptimizer.
resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  tags: tags
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

// Consumption plan for the Function App.
resource funcPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: hostingPlanName
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  kind: 'functionapp'
  tags: tags
}

// Application Insights instance for telemetry.
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
  }
}

// Storage connection string for Azure Functions (required for triggers and state).
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${az.environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}'

// Function App hosting the backend APIs and background jobs.
resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  tags: tags
  properties: {
    serverFarmId: funcPlan.id
    httpsOnly: true
    siteConfig: {
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: storageConnectionString
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: appInsights.properties.InstrumentationKey
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
      ]
    }
  }
}

// Key Vault for secrets (LLM keys, configuration).
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    softDeleteRetentionInDays: 90
    enabledForTemplateDeployment: true
    publicNetworkAccess: 'Enabled'
  }
}

// Grant the Function App managed identity permission to read secrets from the Key Vault.
var kvSecretsUserRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6') // Key Vault Secrets User

resource keyVaultSecretsRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, functionApp.name, kvSecretsUserRoleDefinitionId)
  scope: keyVault
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: kvSecretsUserRoleDefinitionId
    principalType: 'ServicePrincipal'
  }
}

output storageAccountName string = storage.name
output functionAppName string = functionApp.name
output keyVaultName string = keyVault.name
output appInsightsName string = appInsights.name
