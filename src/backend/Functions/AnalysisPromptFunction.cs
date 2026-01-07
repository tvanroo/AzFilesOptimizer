using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using AzFilesOptimizer.Backend.Models;
using AzFilesOptimizer.Backend.Services;

namespace AzFilesOptimizer.Backend.Functions;

public class AnalysisPromptFunction
{
    private readonly ILogger _logger;
    private readonly AnalysisPromptService _promptService;

    public AnalysisPromptFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<AnalysisPromptFunction>();
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
        _promptService = new AnalysisPromptService(connectionString, _logger);
    }

    [Function("GetAnalysisPrompts")]
    public async Task<HttpResponseData> GetAll(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "analysis-prompts")] HttpRequestData req)
    {
        try
        {
            var prompts = await _promptService.GetAllPromptsAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(prompts);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting analysis prompts");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetAnalysisPrompt")]
    public async Task<HttpResponseData> GetById(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "analysis-prompts/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var prompt = await _promptService.GetPromptAsync(id);
            if (prompt == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                return notFoundResponse;
            }
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(prompt);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting analysis prompt {PromptId}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("CreateAnalysisPrompt")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "analysis-prompts")] HttpRequestData req)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<CreatePromptRequest>(req.Body);
            if (request == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                return badRequest;
            }
            
            var prompt = new AnalysisPrompt
            {
                Name = request.Name,
                Priority = request.Priority,
                Category = request.Category.ToString(),
                PromptTemplate = request.PromptTemplate,
                Enabled = request.Enabled,
                StopCondition = request.StopConditions ?? new StopConditions()
            };
            
            var created = await _promptService.CreatePromptAsync(prompt);
            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(created);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating analysis prompt");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("UpdateAnalysisPrompt")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Function, "put", Route = "analysis-prompts/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<UpdatePromptRequest>(req.Body);
            if (request == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                return badRequest;
            }
            
            var prompt = new AnalysisPrompt
            {
                RowKey = id,
                Name = request.Name,
                Priority = request.Priority,
                Category = request.Category.ToString(),
                PromptTemplate = request.PromptTemplate,
                Enabled = request.Enabled,
                StopCondition = request.StopConditions ?? new StopConditions()
            };
            
            var updated = await _promptService.UpdatePromptAsync(prompt);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(updated);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating analysis prompt {PromptId}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("DeleteAnalysisPrompt")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "analysis-prompts/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            await _promptService.DeletePromptAsync(id);
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting analysis prompt {PromptId}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("ReorderAnalysisPrompts")]
    public async Task<HttpResponseData> Reorder(
        [HttpTrigger(AuthorizationLevel.Function, "put", Route = "analysis-prompts/reorder")] HttpRequestData req)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<ReorderPromptsRequest>(req.Body);
            if (request == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                return badRequest;
            }
            
            await _promptService.ReorderPromptsAsync(request.Priorities);
            var response = req.CreateResponse(HttpStatusCode.OK);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering analysis prompts");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }
}
