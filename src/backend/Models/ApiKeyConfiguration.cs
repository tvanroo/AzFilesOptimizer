namespace AzFilesOptimizer.Backend.Models;

public class ApiKeyConfiguration
{
    public string RowKey { get; set; } = string.Empty; // User ID
    public string PartitionKey { get; set; } = "ApiKey";
    public string Provider { get; set; } = "OpenAI"; // "OpenAI" or "AzureOpenAI"
    public string EncryptedApiKey { get; set; } = string.Empty;
    public string? Endpoint { get; set; } // For Azure OpenAI
    public string[]? AvailableModels { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastValidatedAt { get; set; }
}

public class SetApiKeyRequest
{
    public string Provider { get; set; } = "OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string? Endpoint { get; set; } // Required for AzureOpenAI
}

public class ApiKeyStatus
{
    public bool Configured { get; set; }
    public string? Provider { get; set; }
    public string? Endpoint { get; set; }
    public string[]? AvailableModels { get; set; }
    public DateTime? LastValidatedAt { get; set; }
    public string MaskedKey { get; set; } = string.Empty;
}
