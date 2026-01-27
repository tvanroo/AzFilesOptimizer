namespace AzFilesOptimizer.Host.Configuration;

/// <summary>
/// Non-secret application configuration persisted locally under the state/ directory.
/// Secrets such as API keys should be handled separately and not stored via this model.
/// </summary>
public sealed class AppConfiguration
{
    /// <summary>
    /// Logical Azure cloud identifier (for example: "AzurePublic", "AzureChina", "AzureUSGovernment").
    /// This will map to concrete endpoints once spec/azure-clouds.yaml exists.
    /// </summary>
    public string AzureCloud { get; set; } = "AzurePublic";

    /// <summary>
    /// Optional preferred tenant ID to pre-select during interactive auth.
    /// </summary>
    public string? PreferredTenantId { get; set; }

    /// <summary>
    /// Optional LLM endpoint/base URL for OpenAI-compatible services.
    /// API keys should not be stored here.
    /// </summary>
    public string? LlmEndpoint { get; set; }
}
