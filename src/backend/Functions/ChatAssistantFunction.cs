using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
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
            var request = await JsonSerializer.DeserializeAsync<ChatRequest>(req.Body);
            if (request == null || string.IsNullOrEmpty(request.Message))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Invalid request");
                return badRequest;
            }

            // Get API key configuration
            var apiKeyConfig = await _apiKeyService.GetApiKeyAsync("system");
            if (apiKeyConfig == null)
            {
                var noKeyResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await noKeyResponse.WriteStringAsync("No API key configured");
                return noKeyResponse;
            }

            // Get API key from environment (TODO: Key Vault integration)
            var apiKey = Environment.GetEnvironmentVariable("OpenAI_ApiKey");
            if (string.IsNullOrEmpty(apiKey))
            {
                var noKeyResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await noKeyResponse.WriteStringAsync("API key not found");
                return noKeyResponse;
            }

            // Create chat service
            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
            var chatService = new ChatAssistantService(connectionString, _profileService, _logger);

            // Send message
            var aiResponse = await chatService.SendMessageAsync(
                jobId,
                request.Message,
                request.History,
                apiKey,
                apiKeyConfig.Provider,
                apiKeyConfig.Endpoint);

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
}
