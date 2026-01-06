using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace AzFilesOptimizer.Backend.Services;

public class KeyVaultService
{
    private readonly SecretClient _secretClient;

    public KeyVaultService()
    {
        var keyVaultUri = Environment.GetEnvironmentVariable("KEYVAULT_URI") 
            ?? throw new InvalidOperationException("KEYVAULT_URI environment variable is not set");
        
        var credential = new DefaultAzureCredential();
        _secretClient = new SecretClient(new Uri(keyVaultUri), credential);
    }

    public async Task<string> StoreApiKeyAsync(string userId, string apiKey)
    {
        var secretName = GetSecretName(userId);
        await _secretClient.SetSecretAsync(secretName, apiKey);
        return secretName;
    }

    public async Task<string?> GetApiKeyAsync(string userId)
    {
        try
        {
            var secretName = GetSecretName(userId);
            var secret = await _secretClient.GetSecretAsync(secretName);
            return secret.Value.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteApiKeyAsync(string userId)
    {
        try
        {
            var secretName = GetSecretName(userId);
            var operation = await _secretClient.StartDeleteSecretAsync(secretName);
            await operation.WaitForCompletionAsync();
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Already deleted or doesn't exist
        }
    }

    private static string GetSecretName(string userId)
    {
        // Key Vault secret names can only contain alphanumeric characters and dashes
        var sanitized = userId.Replace("_", "-").Replace("@", "-at-").Replace(".", "-");
        return $"openai-key-{sanitized}";
    }
}
