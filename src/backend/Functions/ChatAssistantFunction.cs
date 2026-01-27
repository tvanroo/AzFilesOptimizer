using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using AzFilesOptimizer.Backend.Models;
using AzFilesOptimizer.Backend.Services;

namespace AzFilesOptimizer.Backend.Functions;

public class ChatAssistantFunction
{
    private readonly ILogger _logger;
    private readonly WorkloadProfileService _profileService;
    private readonly ApiKeyStorageService _apiKeyService;

    public ChatAssistantFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ChatAssistantFunction>();
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
        
        _profileService = new WorkloadProfileService(connectionString, _logger);
        _apiKeyService = new ApiKeyStorageService(connectionString);
    }

    [Function("ChatAssistant")]
    public async Task<HttpResponseData> Chat(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "discovery/{jobId}/chat")] HttpRequestData req,
        string jobId)
    {
        _logger.LogInformation("Chat request for job: {JobId}", jobId);

        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var request = JsonSerializer.Deserialize<ChatRequest>(body, options);
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Invalid request");
                return badRequest;
            }

            // Determine user ID (align with settings / analysis)
            var userId = "default-user";

            // Get API key configuration
            var apiKeyConfig = await _apiKeyService.GetApiKeyAsync(userId);
            if (apiKeyConfig == null)
            {
                var noKeyResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await noKeyResponse.WriteStringAsync("No API key configured");
                return noKeyResponse;
            }

            // Resolve preferred model for chat
            var modelToUse = ResolvePreferredModel(apiKeyConfig);
            _logger.LogInformation("Chat assistant using model '{Model}' for provider {Provider}", modelToUse, apiKeyConfig.Provider);

            // Get API key from Key Vault (same as analysis pipeline)
            var keyVaultService = new KeyVaultService();
            var apiKey = await keyVaultService.GetApiKeyAsync(userId);
            if (string.IsNullOrEmpty(apiKey))
            {
                var noKeyResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await noKeyResponse.WriteStringAsync("API key not found");
                return noKeyResponse;
            }

            // Create chat service
            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
            var chatService = new ChatAssistantService(connectionString, _profileService, _logger);

            // Normalize history
            var history = request.ConversationHistory ?? new List<ChatMessage>();

            // Send message
            var aiResponse = await chatService.SendMessageAsync(
                jobId,
                request.Message,
                history,
                apiKey,
                apiKeyConfig.Provider,
                apiKeyConfig.Endpoint,
                modelToUse);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new ChatResponse
            {
                Response = aiResponse
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat request for job {JobId}", jobId);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    private static string ResolvePreferredModel(ApiKeyConfiguration config)
    {
        var preferred = config.Preferences?.PreferredModels?.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred.Trim();
        }

        var available = config.AvailableModels?.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
        if (!string.IsNullOrWhiteSpace(available))
        {
            return available.Trim();
        }

        // Fallbacks
        if (string.Equals(config.Provider, "AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return "gpt-4";
        }

        return "gpt-4";
    }
}
