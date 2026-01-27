using Azure;
using Azure.Data.Tables;
using AzFilesOptimizer.Backend.Models;
using System.Text.Json;

namespace AzFilesOptimizer.Backend.Services;

public class ApiKeyStorageService
{
    private readonly TableClient _tableClient;

    public ApiKeyStorageService(string connectionString)
    {
        var serviceClient = new TableServiceClient(connectionString);
        _tableClient = serviceClient.GetTableClient("ApiKeyConfigurations");
        _tableClient.CreateIfNotExists();
    }

    public async Task<ApiKeyConfiguration?> GetApiKeyAsync(string userId)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>("ApiKey", userId);
            var entity = response.Value;
            
            var preferencesJson = entity.GetString("PreferencesJson");
            ModelPreferences? preferences = null;
            if (!string.IsNullOrWhiteSpace(preferencesJson))
            {
                try
                {
                    preferences = JsonSerializer.Deserialize<ModelPreferences>(preferencesJson);
                }
                catch { /* Ignore deserialization errors */ }
            }

            return new ApiKeyConfiguration
            {
                RowKey = entity.GetString("RowKey") ?? "",
                PartitionKey = entity.GetString("PartitionKey") ?? "ApiKey",
                Provider = entity.GetString("Provider") ?? "OpenAI",
                KeyVaultSecretName = entity.GetString("KeyVaultSecretName") ?? "",
                Endpoint = entity.GetString("Endpoint"),
                AvailableModels = entity.GetString("AvailableModels")?.Split(',', StringSplitOptions.RemoveEmptyEntries),
                Preferences = preferences ?? new ModelPreferences(),
                CreatedAt = entity.GetDateTimeOffset("CreatedAt")?.UtcDateTime ?? DateTime.UtcNow,
                UpdatedAt = entity.GetDateTimeOffset("UpdatedAt")?.UtcDateTime,
                LastValidatedAt = entity.GetDateTimeOffset("LastValidatedAt")?.UtcDateTime
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task SaveApiKeyMetadataAsync(
        string userId, 
        string provider, 
        string keyVaultSecretName, 
        string? endpoint, 
        string[]? models,
        ModelPreferences? preferences = null)
    {
        var entity = new TableEntity("ApiKey", userId)
        {
            ["Provider"] = provider,
            ["KeyVaultSecretName"] = keyVaultSecretName,
            ["Endpoint"] = endpoint,
            ["AvailableModels"] = models != null ? string.Join(",", models) : null,
            ["PreferencesJson"] = preferences != null ? JsonSerializer.Serialize(preferences) : null,
            ["CreatedAt"] = DateTime.UtcNow,
            ["UpdatedAt"] = DateTime.UtcNow,
            ["LastValidatedAt"] = DateTime.UtcNow
        };

        await _tableClient.UpsertEntityAsync(entity);
    }

    public async Task DeleteApiKeyAsync(string userId)
    {
        try
        {
            await _tableClient.DeleteEntityAsync("ApiKey", userId);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already deleted or doesn't exist
        }
    }

    public async Task UpdatePreferencesAsync(string userId, ModelPreferences preferences)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>("ApiKey", userId);
            var entity = response.Value;
            entity["PreferencesJson"] = JsonSerializer.Serialize(preferences);
            entity["UpdatedAt"] = DateTime.UtcNow;
            await _tableClient.UpdateEntityAsync(entity, entity.ETag);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException("API key configuration not found");
        }
    }

    public static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length <= 8)
            return "****";
        
        return apiKey.Substring(0, 4) + "..." + apiKey.Substring(apiKey.Length - 4);
    }
}
