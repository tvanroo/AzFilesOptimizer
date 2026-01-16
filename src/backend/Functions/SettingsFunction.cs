using System.Net;
using System.Text.Json;
using System.Diagnostics;
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

    // Reused test prompt so we can both send it to the model and echo it back
    // in the TestApiKey response.
    private const string TestApiKeyUserPrompt = "In one short sentence, confirm that you can respond to requests for storage analysis and optimization recommendations.";

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

    [Function("TestApiKey")]
    public async Task<HttpResponseData> TestApiKey(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "settings/openai-key/test")] HttpRequestData req)
    {
        _logger.LogInformation("Testing API key and preferred model");

        var userId = GetUserIdFromRequest(req);

        try
        {
            var config = await _apiKeyStorage.GetApiKeyAsync(userId);
            if (config == null)
            {
                var notConfigured = req.CreateResponse(HttpStatusCode.BadRequest);
                await notConfigured.WriteAsJsonAsync(new { ok = false, error = "API key is not configured" });
                return notConfigured;
            }

            var apiKey = await _keyVaultService.GetApiKeyAsync(userId);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                var missingSecret = req.CreateResponse(HttpStatusCode.BadRequest);
                await missingSecret.WriteAsJsonAsync(new { ok = false, error = "API key secret not found in Key Vault" });
                return missingSecret;
            }

            var provider = config.Provider;
            var endpoint = config.Endpoint;
            var modelToUse = ResolvePreferredModel(config);

            if (string.IsNullOrWhiteSpace(modelToUse))
            {
                var noModel = req.CreateResponse(HttpStatusCode.BadRequest);
                await noModel.WriteAsJsonAsync(new { ok = false, error = "No preferred model is configured. Please select a model on the Settings page." });
                return noModel;
            }

            if (!IsSupportedModel(modelToUse))
            {
                var unsupported = req.CreateResponse(HttpStatusCode.BadRequest);
                await unsupported.WriteAsJsonAsync(new
                {
                    ok = false,
                    error = "The selected model is not a supported GPT-5 family chat model. Please choose a GPT-5 series model (for example: gpt-5.1, gpt-5, gpt-5-mini, or gpt-5-nano)."
                });
                return unsupported;
            }

            var stopwatch = Stopwatch.StartNew();
            string rawResponse;

            try
            {
                rawResponse = await CallAiForTestAsync(apiKey, provider, endpoint, modelToUse);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "API key test failed for provider {Provider} and model {Model}", provider, modelToUse);

                var failed = req.CreateResponse(HttpStatusCode.BadRequest);
                await failed.WriteAsJsonAsync(new
                {
                    ok = false,
                    error = ex.Message,
                    provider,
                    model = modelToUse
                });
                return failed;
            }

            stopwatch.Stop();

            // Extract assistant content from the raw response JSON. If parsing fails for any reason,
            // fall back to a generic message so callers still see that the model responded.
            var assistantText = ExtractAssistantContentFromRawResponse(rawResponse);

            var truncatedSample = string.IsNullOrEmpty(assistantText)
                ? "(Model responded but content could not be parsed.)"
                : (assistantText.Length > 200 ? assistantText.Substring(0, 200) + "..." : assistantText);

            var success = req.CreateResponse(HttpStatusCode.OK);
            await success.WriteAsJsonAsync(new
            {
                ok = true,
                provider,
                model = modelToUse,
                latencyMs = stopwatch.Elapsed.TotalMilliseconds,
                prompt = TestApiKeyUserPrompt,
                sampleResponse = truncatedSample
            });
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while testing API key");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { ok = false, error = "Failed to test API key", details = ex.Message });
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

        // Fallback: prefer a GPT-5 model name if configured; otherwise, return empty and let callers handle.
        return string.Empty;
    }

    private async Task<string> CallAiForTestAsync(string apiKey, string provider, string? endpoint, string modelToUse)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(20);

        string apiUrl;
        if (string.Equals(provider, "AzureOpenAI", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(endpoint))
        {
            apiUrl = $"{endpoint.TrimEnd('/')}/openai/deployments/{modelToUse}/chat/completions?api-version=2024-02-15-preview";
            httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        }
        else
        {
            apiUrl = "https://api.openai.com/v1/chat/completions";
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        var useMaxCompletionTokens = ModelRequiresMaxCompletionTokens(modelToUse);

        var messages = new[]
        {
            new { role = "system", content = "You are AzFilesOptimizer's AI assistant. Answer clearly and concisely." },
            new { role = "user", content = TestApiKeyUserPrompt }
        };

        object requestPayload;

        if (string.Equals(provider, "AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            if (useMaxCompletionTokens)
            {
                // GPT-5 / O-series on Azure: align with working pattern from manual test.
                // Use explicit reasoning_effort=low and a higher max_completion_tokens so that
                // not all tokens are consumed as reasoning tokens.
                requestPayload = new
                {
                    messages,
                    reasoning_effort = "low",
                    max_completion_tokens = 128
                };
            }
            else
            {
                // Older/non-reasoning models: keep a simpler schema with classic max_tokens.
                requestPayload = new
                {
                    messages,
                    temperature = 0.7,
                    max_tokens = 128
                };
            }
        }
        else
        {
            if (useMaxCompletionTokens)
            {
                // GPT-5 / O-series on OpenAI: same pattern that worked in your CLI test.
                requestPayload = new
                {
                    model = modelToUse,
                    messages,
                    reasoning_effort = "low",
                    max_completion_tokens = 128
                };
            }
            else
            {
                requestPayload = new
                {
                    model = modelToUse,
                    messages,
                    temperature = 0.7,
                    max_tokens = 128
                };
            }
        }

        var requestBody = JsonSerializer.Serialize(requestPayload);
        var content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

        _logger.LogInformation("[Settings/TestApiKey] Calling AI provider {Provider} at {Url} with model {Model}", provider, apiUrl, modelToUse);

        var response = await httpClient.PostAsync(apiUrl, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var truncated = responseBody.Length > 2000
                ? responseBody.Substring(0, 2000) + "... (truncated)"
                : responseBody;

            throw new InvalidOperationException($"AI API call failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {truncated}");
        }

        // For TestApiKey we want the raw response so that we can both parse out assistant
        // content and, if needed, inspect the JSON structure when debugging.
        return responseBody;
    }

    private static string ExtractAssistantContentFromRawResponse(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(rawResponse);
            var root = doc.RootElement;

            // Standard chat.completions format: choices[0].message.content
            if (root.TryGetProperty("choices", out var choicesEl) &&
                choicesEl.ValueKind == JsonValueKind.Array &&
                choicesEl.GetArrayLength() > 0)
            {
                var messageEl = choicesEl[0].GetProperty("message");

                if (messageEl.TryGetProperty("content", out var contentEl))
                {
                    if (contentEl.ValueKind == JsonValueKind.String)
                    {
                        return contentEl.GetString() ?? string.Empty;
                    }

                    if (contentEl.ValueKind == JsonValueKind.Array)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var part in contentEl.EnumerateArray())
                        {
                            if (part.ValueKind == JsonValueKind.String)
                            {
                                sb.Append(part.GetString());
                            }
                            else if (part.ValueKind == JsonValueKind.Object)
                            {
                                // Some schemas nest text under a "text" or "value" property.
                                if (part.TryGetProperty("text", out var textEl))
                                {
                                    if (textEl.ValueKind == JsonValueKind.String)
                                    {
                                        sb.Append(textEl.GetString());
                                    }
                                    else if (textEl.ValueKind == JsonValueKind.Object &&
                                             textEl.TryGetProperty("value", out var valueEl))
                                    {
                                        sb.Append(valueEl.GetString());
                                    }
                                }
                            }
                        }

                        return sb.ToString();
                    }
                }
            }
        }
        catch
        {
            // Swallow and fall through to empty string; caller will show a generic message.
        }

        return string.Empty;
    }

    private static bool ModelRequiresMaxCompletionTokens(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            return false;

        var name = modelName.Trim().ToLowerInvariant();
        return name.StartsWith("gpt-5") || name.StartsWith("o3") || name.StartsWith("o4");
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
