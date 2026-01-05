using Azure;
using Azure.Data.Tables;
using AzFilesOptimizer.Backend.Models;
using System.Security.Cryptography;
using System.Text;

namespace AzFilesOptimizer.Backend.Services;

public class ApiKeyStorageService
{
    private readonly TableClient _tableClient;
    private readonly string _encryptionKey;

    public ApiKeyStorageService(string connectionString)
    {
        var serviceClient = new TableServiceClient(connectionString);
        _tableClient = serviceClient.GetTableClient("ApiKeyConfigurations");
        _tableClient.CreateIfNotExists();
        
        // Get encryption key from environment or generate temporary one
        _encryptionKey = Environment.GetEnvironmentVariable("API_KEY_ENCRYPTION_KEY") 
            ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    public async Task<ApiKeyConfiguration?> GetApiKeyAsync(string userId)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>("ApiKey", userId);
            var entity = response.Value;
            
            return new ApiKeyConfiguration
            {
                RowKey = entity.GetString("RowKey") ?? "",
                PartitionKey = entity.GetString("PartitionKey") ?? "ApiKey",
                Provider = entity.GetString("Provider") ?? "OpenAI",
                EncryptedApiKey = entity.GetString("EncryptedApiKey") ?? "",
                Endpoint = entity.GetString("Endpoint"),
                AvailableModels = entity.GetString("AvailableModels")?.Split(','),
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

    public async Task SaveApiKeyAsync(string userId, string provider, string apiKey, string? endpoint, string[]? models)
    {
        var encryptedKey = EncryptString(apiKey);
        
        var entity = new TableEntity("ApiKey", userId)
        {
            ["Provider"] = provider,
            ["EncryptedApiKey"] = encryptedKey,
            ["Endpoint"] = endpoint,
            ["AvailableModels"] = models != null ? string.Join(",", models) : null,
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

    public string DecryptApiKey(string encryptedKey)
    {
        return DecryptString(encryptedKey);
    }

    private string EncryptString(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = Convert.FromBase64String(_encryptionKey);
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        
        // Write IV first
        ms.Write(aes.IV, 0, aes.IV.Length);
        
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
        {
            sw.Write(plainText);
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    private string DecryptString(string cipherText)
    {
        var fullCipher = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = Convert.FromBase64String(_encryptionKey);

        // Extract IV from the beginning
        var iv = new byte[aes.IV.Length];
        Array.Copy(fullCipher, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);
        
        return sr.ReadToEnd();
    }

    public static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length <= 8)
            return "****";
        
        return apiKey.Substring(0, 4) + "..." + apiKey.Substring(apiKey.Length - 4);
    }
}
