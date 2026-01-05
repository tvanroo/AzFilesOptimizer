using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using AzFilesOptimizer.Backend.Models;
using AzFilesOptimizer.Backend.Services;

namespace AzFilesOptimizer.Backend.Functions;

public class SettingsFunction
{
    private readonly ILogger _logger;
    private readonly ApiKeyStorageService _apiKeyStorage;

    public SettingsFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<SettingsFunction>();
        
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
        _apiKeyStorage = new ApiKeyStorageService(connectionString);
    }

    [Function("GetApiKeyStatus")]
    public async Task<HttpResponseData> GetApiKeyStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "settings/openai-key")] HttpRequestData req)
    {
        _logger.LogInformation("Getting API key status");

        try
        {
            // Get user ID from auth context (placeholder - should extract from JWT token)
            var userId = GetUserIdFromRequest(req);
            
            var config = await _apiKeyStorage.GetApiKeyAsync(userId);
            
            if (config == null)
            {
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new ApiKeyStatus { Configured = false });
                return response;
            }

            var status = new ApiKeyStatus
            {
                Configured = true,
                Provider = config.Provider,
                Endpoint = config.Endpoint,
                AvailableModels = config.AvailableModels,
                LastValidatedAt = config.LastValidatedAt,
                MaskedKey = ApiKeyStorageService.MaskApiKey(
                    _apiKeyStorage.DecryptApiKey(config.EncryptedApiKey))
            };

            var okResponse = req.CreateResponse(HttpStatusCode.OK);
            await okResponse.WriteAsJsonAsync(status);
            return okResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting API key status");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to get API key status", details = ex.Message });
            return errorResponse;
        }
    }

    [Function("SetApiKey")]
    public async Task<HttpResponseData> SetApiKey(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "settings/openai-key")] HttpRequestData req)
    {
        _logger.LogInformation("Setting API key");

        try
        {
            var userId = GetUserIdFromRequest(req);
            
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var request = JsonSerializer.Deserialize<SetApiKeyRequest>(requestBody, options);

            if (request == null || string.IsNullOrWhiteSpace(request.ApiKey))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "API key is required" });
                return badRequest;
            }

            // Validate the API key
            var (isValid, models, errorMessage) = await ValidateOpenAIKey(request.Provider, request.ApiKey, request.Endpoint);
            
            if (!isValid)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = errorMessage ?? "Invalid API key" });
                return badRequest;
            }

            // Save the encrypted key
            await _apiKeyStorage.SaveApiKeyAsync(userId, request.Provider, request.ApiKey, request.Endpoint, models);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new 
            { 
                message = "API key saved successfully",
                availableModels = models
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting API key");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to set API key", details = ex.Message });
            return errorResponse;
        }
    }

    [Function("DeleteApiKey")]
    public async Task<HttpResponseData> DeleteApiKey(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "settings/openai-key")] HttpRequestData req)
    {
        _logger.LogInformation("Deleting API key");

        try
        {
            var userId = GetUserIdFromRequest(req);
            await _apiKeyStorage.DeleteApiKeyAsync(userId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "API key deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting API key");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to delete API key", details = ex.Message });
            return errorResponse;
        }
    }

    private async Task<(bool isValid, string[]? models, string? errorMessage)> ValidateOpenAIKey(
        string provider, string apiKey, string? endpoint)
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            if (provider == "OpenAI")
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                var response = await client.GetAsync("https://api.openai.com/v1/models");
                
                if (!response.IsSuccessStatusCode)
                {
                    return (false, null, $"Invalid API key: {response.StatusCode}");
                }

                var content = await response.Content.ReadAsStringAsync();
                var modelsResponse = JsonSerializer.Deserialize<OpenAIModelsResponse>(content);
                var models = modelsResponse?.data?.Select(m => m.id).ToArray() ?? Array.Empty<string>();
                
                return (true, models, null);
            }
            else if (provider == "AzureOpenAI")
            {
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    return (false, null, "Endpoint is required for Azure OpenAI");
                }

                client.DefaultRequestHeaders.Add("api-key", apiKey);
                var response = await client.GetAsync($"{endpoint}/openai/deployments?api-version=2023-05-15");
                
                if (!response.IsSuccessStatusCode)
                {
                    return (false, null, $"Invalid API key or endpoint: {response.StatusCode}");
                }

                var content = await response.Content.ReadAsStringAsync();
                var deploymentsResponse = JsonSerializer.Deserialize<AzureOpenAIDeploymentsResponse>(content);
                var models = deploymentsResponse?.data?.Select(d => d.model).ToArray() ?? Array.Empty<string>();
                
                return (true, models, null);
            }

            return (false, null, "Unknown provider");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating API key");
            return (false, null, $"Validation error: {ex.Message}");
        }
    }

    private string GetUserIdFromRequest(HttpRequestData req)
    {
        // TODO: Extract user ID from JWT token in Authorization header
        // For now, use a placeholder
        return "default-user";
    }

    // Response models for OpenAI/Azure OpenAI APIs
    private class OpenAIModelsResponse
    {
        public ModelData[]? data { get; set; }
        
        public class ModelData
        {
            public string id { get; set; } = "";
        }
    }

    private class AzureOpenAIDeploymentsResponse
    {
        public DeploymentData[]? data { get; set; }
        
        public class DeploymentData
        {
            public string model { get; set; } = "";
        }
    }
}
