using Azure.Storage.Blobs;
using AzFilesOptimizer.Backend.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace AzFilesOptimizer.Backend.Services;

public class ChatAssistantService
{
    private readonly ILogger _logger;
    private readonly BlobContainerClient _blobContainer;
    private readonly WorkloadProfileService _profileService;

    public ChatAssistantService(
        string connectionString,
        WorkloadProfileService profileService,
        ILogger logger)
    {
        _logger = logger;
        _profileService = profileService;
        
        var blobServiceClient = new BlobServiceClient(connectionString);
        _blobContainer = blobServiceClient.GetBlobContainerClient("discovery-data");
    }

    public async Task<string> SendMessageAsync(
        string discoveryJobId,
        string userMessage,
        List<ChatMessage> chatHistory,
        string apiKey,
        string provider,
        string? endpoint)
    {
        _logger.LogInformation("Processing chat message for job: {JobId}", discoveryJobId);

        // Load discovery data
        var discoveryData = await LoadDiscoveryDataAsync(discoveryJobId);
        if (discoveryData == null)
        {
            return "I couldn't find any volume data for this discovery job. Please make sure the job has completed.";
        }

        // Load workload profiles for context
        var profiles = await _profileService.GetAllProfilesAsync();

        // Build system prompt with volume data context
        var systemPrompt = BuildSystemPrompt(discoveryData, profiles);

        // Build conversation history
        var messages = new List<object>();
        messages.Add(new { role = "system", content = systemPrompt });

        // Add chat history
        foreach (var msg in chatHistory)
        {
            messages.Add(new { role = msg.Role, content = msg.Content });
        }

        // Add current user message
        messages.Add(new { role = "user", content = userMessage });

        // Call AI
        try
        {
            var response = await CallAIAsync(messages, apiKey, provider, endpoint);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling AI for chat");
            return $"I encountered an error processing your request: {ex.Message}";
        }
    }

    private string BuildSystemPrompt(DiscoveryData discoveryData, List<WorkloadProfile> profiles)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("You are an Azure storage expert assistant helping analyze Azure Files volumes for potential migration to Azure NetApp Files (ANF).");
        sb.AppendLine();
        sb.AppendLine("AVAILABLE VOLUME DATA:");
        sb.AppendLine($"Total Volumes: {discoveryData.Volumes.Count}");
        sb.AppendLine();

        // Summary statistics
        var totalSize = discoveryData.Volumes.Sum(v => v.Volume.ShareQuotaGiB ?? 0);
        var analyzedCount = discoveryData.Volumes.Count(v => v.AiAnalysis != null);
        var withWorkload = discoveryData.Volumes.Count(v => !string.IsNullOrEmpty(v.AiAnalysis?.SuggestedWorkloadId));
        
        sb.AppendLine($"Total Capacity: {totalSize} GiB");
        sb.AppendLine($"Analyzed Volumes: {analyzedCount}");
        sb.AppendLine($"Volumes with AI Workload: {withWorkload}");
        sb.AppendLine();

        // Workload summary
        var workloadGroups = discoveryData.Volumes
            .Where(v => v.AiAnalysis?.SuggestedWorkloadName != null)
            .GroupBy(v => v.AiAnalysis.SuggestedWorkloadName)
            .Select(g => new { Workload = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count);

        if (workloadGroups.Any())
        {
            sb.AppendLine("WORKLOAD DISTRIBUTION:");
            foreach (var group in workloadGroups)
            {
                sb.AppendLine($"- {group.Workload}: {group.Count} volumes");
            }
            sb.AppendLine();
        }

        // Available workload profiles
        sb.AppendLine("KNOWN WORKLOAD TYPES:");
        foreach (var profile in profiles.Where(p => !p.IsExclusionProfile))
        {
            sb.AppendLine($"- {profile.Name}");
        }
        sb.AppendLine();

        // Sample volumes (first 5 for context)
        sb.AppendLine("SAMPLE VOLUMES:");
        foreach (var volume in discoveryData.Volumes.Take(5))
        {
            sb.AppendLine($"- {volume.Volume.ShareName}:");
            sb.AppendLine($"  Size: {volume.Volume.ShareQuotaGiB} GiB");
            sb.AppendLine($"  Storage Account: {volume.Volume.StorageAccountName}");
            if (volume.AiAnalysis?.SuggestedWorkloadName != null)
            {
                sb.AppendLine($"  AI Classification: {volume.AiAnalysis.SuggestedWorkloadName} ({(volume.AiAnalysis.ConfidenceScore * 100):F0}% confidence)");
            }
        }
        sb.AppendLine();

        sb.AppendLine("INSTRUCTIONS:");
        sb.AppendLine("- Answer questions about the discovered volumes");
        sb.AppendLine("- Provide insights about workload distribution");
        sb.AppendLine("- Help identify volumes that need review");
        sb.AppendLine("- Suggest migration strategies based on the data");
        sb.AppendLine("- Be concise and actionable");
        sb.AppendLine("- If asked for specific volumes, you can reference them by name");
        sb.AppendLine();

        return sb.ToString();
    }

    private async Task<string> CallAIAsync(
        List<object> messages,
        string apiKey,
        string provider,
        string? endpoint)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        string apiUrl;
        if (provider == "AzureOpenAI" && !string.IsNullOrEmpty(endpoint))
        {
            apiUrl = $"{endpoint.TrimEnd('/')}/openai/deployments/gpt-4/chat/completions?api-version=2024-02-15-preview";
            httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        }
        else
        {
            apiUrl = "https://api.openai.com/v1/chat/completions";
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        var requestPayload = new
        {
            model = provider == "AzureOpenAI" ? "" : "gpt-4",
            messages = messages,
            temperature = 0.7,
            max_tokens = 800
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestPayload),
            Encoding.UTF8,
            "application/json");

        var response = await httpClient.PostAsync(apiUrl, content);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        var jsonResponse = JsonDocument.Parse(responseBody);

        var messageContent = jsonResponse.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return messageContent ?? "I couldn't generate a response.";
    }

    private async Task<DiscoveryData?> LoadDiscoveryDataAsync(string jobId)
    {
        try
        {
            var blobClient = _blobContainer.GetBlobClient($"jobs/{jobId}/discovered-volumes.json");

            if (!await blobClient.ExistsAsync())
            {
                return null;
            }

            var response = await blobClient.DownloadContentAsync();
            var json = response.Value.Content.ToString();
            return JsonSerializer.Deserialize<DiscoveryData>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading discovery data for job: {JobId}", jobId);
            return null;
        }
    }
}

public class ChatMessage
{
    public string Role { get; set; } = "user"; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
}

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public List<ChatMessage> History { get; set; } = new();
}

public class ChatResponse
{
    public string Response { get; set; } = string.Empty;
}
