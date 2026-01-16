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
    private readonly KeyVaultService _keyVaultService;

    public SettingsFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<SettingsFunction>();
        
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
        _apiKeyStorage = new ApiKeyStorageService(connectionString);
        _keyVaultService = new KeyVaultService();
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

            // Get actual API key from Key Vault
            var apiKey = await _keyVaultService.GetApiKeyAsync(userId);
            
            var status = new ApiKeyStatus
            {
                Configured = true,
                Provider = config.Provider,
                Endpoint = config.Endpoint,
                AvailableModels = config.AvailableModels,
                Preferences = config.Preferences,
                LastValidatedAt = config.LastValidatedAt,
                MaskedKey = apiKey != null ? ApiKeyStorageService.MaskApiKey(apiKey) : "****"
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

            // Store API key in Key Vault
            var secretName = await _keyVaultService.StoreApiKeyAsync(userId, request.ApiKey);
            
            // Save metadata to Table Storage with empty preferences
            await _apiKeyStorage.SaveApiKeyMetadataAsync(
                userId, 
                request.Provider, 
                secretName, 
                request.Endpoint, 
                models,
                new ModelPreferences());
            
            _logger.LogInformation("API key saved for user {UserId} in Key Vault secret {SecretName}", userId, secretName);

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
            
            // Delete from both Key Vault and Table Storage
            await _keyVaultService.DeleteApiKeyAsync(userId);
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
                var allModels = modelsResponse?.data?.Select(m => m.id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToArray() ?? Array.Empty<string>();

                if (allModels.Length == 0)
                {
                    return (false, null, "API key is valid, but no models are available for this account.");
                }

                // Restrict to GPT-5 family chat models that are compatible with this app's
                // Chat Completions usage (e.g., gpt-5.1, gpt-5, gpt-5-mini, gpt-5-nano).
                var supported = allModels.Where(IsSupportedModel).ToArray();
                if (supported.Length == 0)
                {
                    return (
                        false,
                        null,
                        "API key is valid, but no GPT-5 family chat models are available. " +
                        "Please enable at least one GPT-5 series model (for example: gpt-5.1, gpt-5, gpt-5-mini, or gpt-5-nano) and try again."
                    );
                }

                return (true, supported, null);
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
                var allModels = deploymentsResponse?.data?.Select(d => d.model)
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .ToArray() ?? Array.Empty<string>();

                if (allModels.Length == 0)
                {
                    return (false, null, "API key is valid, but no deployments are available for this Azure OpenAI resource.");
                }

                var supported = allModels.Where(IsSupportedModel).ToArray();
                if (supported.Length == 0)
                {
                    return (
                        false,
                        null,
                        "API key is valid, but no GPT-5 family chat models are available in this Azure OpenAI resource. " +
                        "Please deploy at least one GPT-5 series model (for example: gpt-5.1, gpt-5, gpt-5-mini, or gpt-5-nano) and try again."
                    );
                }

                return (true, supported, null);
            }

            return (false, null, "Unknown provider");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating API key");
            return (false, null, $"Validation error: {ex.Message}");
        }
    }

    [Function("GetModelPreferences")]
    public async Task<HttpResponseData> GetModelPreferences(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "settings/model-preferences")] HttpRequestData req)
    {
        _logger.LogInformation("Getting model preferences");

        try
        {
            var userId = GetUserIdFromRequest(req);
            var config = await _apiKeyStorage.GetApiKeyAsync(userId);
            
            if (config == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "API key not configured" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                preferences = config.Preferences,
                availableModels = config.AvailableModels ?? Array.Empty<string>()
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting model preferences");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to get model preferences", details = ex.Message });
            return errorResponse;
        }
    }

    [Function("UpdateModelPreferences")]
    public async Task<HttpResponseData> UpdateModelPreferences(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "settings/model-preferences")] HttpRequestData req)
    {
        _logger.LogInformation("Updating model preferences");

        try
        {
            var userId = GetUserIdFromRequest(req);
            
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var request = JsonSerializer.Deserialize<UpdatePreferencesRequest>(requestBody, options);

            if (request == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid request body" });
                return badRequest;
            }

            // Validate that models aren't in multiple categories
            var allModels = request.PreferredModels
                .Concat(request.AllowedModels)
                .Concat(request.BlockedModels)
                .ToList();
            
            if (allModels.Count != allModels.Distinct().Count())
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Models cannot be in multiple categories" });
                return badRequest;
            }

            // Require at least one GPT-5 family model in the preferred list so that
            // downstream analysis and chat calls use the new GPT-5-style parameters.
            if (request.PreferredModels == null ||
                !request.PreferredModels.Any(m => IsSupportedModel(m)))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new
                {
                    error = "Model preference must include at least one GPT-5 family chat model (for example: gpt-5.1, gpt-5, gpt-5-mini, or gpt-5-nano)."
                });
                return badRequest;
            }

            var preferences = new ModelPreferences
            {
                PreferredModels = request.PreferredModels,
                AllowedModels = request.AllowedModels,
                BlockedModels = request.BlockedModels
            };

            await _apiKeyStorage.UpdatePreferencesAsync(userId, preferences);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Model preferences updated successfully", preferences });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating model preferences");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to update model preferences", details = ex.Message });
            return errorResponse;
        }
    }

    private static bool IsSupportedModel(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            return false;

        var name = modelName.Trim().ToLowerInvariant();

        // Exclude responses-only / Codex variants which are not compatible with this app's
        // Chat Completions usage.
        if (name.Contains("codex"))
            return false;

        // Treat GPT-5 family chat models as supported: gpt-5, gpt-5.1, gpt-5-mini, gpt-5-nano, etc.
        return name.StartsWith("gpt-5");
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
