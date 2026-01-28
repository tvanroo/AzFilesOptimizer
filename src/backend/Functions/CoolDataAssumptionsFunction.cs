using System.Net;
using System.Text.Json;
using AzFilesOptimizer.Backend.Models;
using AzFilesOptimizer.Backend.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AzFilesOptimizer.Backend.Functions;

public class CoolDataAssumptionsFunction
{
    private readonly ILogger _logger;
    private readonly CoolDataAssumptionsService _assumptionsService;
    private readonly CoolDataRecalculationService _recalcService;
    
    public CoolDataAssumptionsFunction(
        ILoggerFactory loggerFactory,
        CoolDataAssumptionsService assumptionsService,
        CoolDataRecalculationService recalcService)
    {
        _logger = loggerFactory.CreateLogger<CoolDataAssumptionsFunction>();
        _assumptionsService = assumptionsService;
        _recalcService = recalcService;
    }
    
    /// <summary>
    /// GET /api/cool-assumptions/global
    /// Get global default assumptions
    /// </summary>
    [Function("GetGlobalCoolAssumptions")]
    public async Task<HttpResponseData> GetGlobalAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cool-assumptions/global")] HttpRequestData req)
    {
        try
        {
            var assumptions = await _assumptionsService.GetGlobalAssumptionsAsync();
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(assumptions);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting global cool data assumptions");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }
    
    /// <summary>
    /// PUT /api/cool-assumptions/global
    /// Update global default assumptions
    /// </summary>
    [Function("SetGlobalCoolAssumptions")]
    public async Task<HttpResponseData> SetGlobalAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "cool-assumptions/global")] HttpRequestData req)
    {
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var assumptions = JsonSerializer.Deserialize<CoolDataAssumptions>(body, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });
            
            if (assumptions == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Invalid request body");
                return badRequest;
            }
            
            await _assumptionsService.SetGlobalAssumptionsAsync(assumptions, "API");
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Global assumptions updated successfully" });
            return response;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid assumptions provided");
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteStringAsync($"Invalid assumptions: {ex.Message}");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting global cool data assumptions");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }
    
    /// <summary>
    /// GET /api/cool-assumptions/job/{jobId}
    /// Get assumptions for a specific job (returns job override or global)
    /// </summary>
    [Function("GetJobCoolAssumptions")]
    public async Task<HttpResponseData> GetJobAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cool-assumptions/job/{jobId}")] HttpRequestData req,
        string jobId)
    {
        try
        {
            var assumptions = await _assumptionsService.GetJobAssumptionsAsync(jobId);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(assumptions);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting job cool data assumptions for {JobId}", jobId);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }
    
    /// <summary>
    /// PUT /api/cool-assumptions/job/{jobId}
    /// Set assumptions for a specific job (triggers recalculation)
    /// </summary>
    [Function("SetJobCoolAssumptions")]
    public async Task<HttpResponseData> SetJobAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "cool-assumptions/job/{jobId}")] HttpRequestData req,
        string jobId)
    {
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var assumptions = JsonSerializer.Deserialize<CoolDataAssumptions>(body, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });
            
            if (assumptions == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Invalid request body");
                return badRequest;
            }
            
            // Set assumptions
            await _assumptionsService.SetJobAssumptionsAsync(jobId, assumptions, "API");
            
            // Trigger recalculation
            var recalculated = await _recalcService.RecalculateJobAsync(jobId);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new 
            { 
                message = "Job assumptions updated successfully",
                recalculatedVolumes = recalculated
            });
            return response;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Job {JobId} not found", jobId);
            var response = req.CreateResponse(HttpStatusCode.NotFound);
            await response.WriteStringAsync($"Job {jobId} not found");
            return response;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid assumptions provided for job {JobId}", jobId);
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteStringAsync($"Invalid assumptions: {ex.Message}");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting job cool data assumptions for {JobId}", jobId);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }
    
    /// <summary>
    /// DELETE /api/cool-assumptions/job/{jobId}
    /// Clear job-level assumptions (revert to global)
    /// </summary>
    [Function("ClearJobCoolAssumptions")]
    public async Task<HttpResponseData> ClearJobAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "cool-assumptions/job/{jobId}")] HttpRequestData req,
        string jobId)
    {
        try
        {
            await _assumptionsService.ClearJobAssumptionsAsync(jobId);
            
            // Trigger recalculation with global defaults
            var recalculated = await _recalcService.RecalculateJobAsync(jobId);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new 
            { 
                message = "Job assumptions cleared (reverted to global)",
                recalculatedVolumes = recalculated
            });
            return response;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Job {JobId} not found", jobId);
            var response = req.CreateResponse(HttpStatusCode.NotFound);
            await response.WriteStringAsync($"Job {jobId} not found");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing job cool data assumptions for {JobId}", jobId);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }
    
    /// <summary>
    /// POST /api/cool-assumptions/recalculate-job/{jobId}
    /// Manually trigger recalculation for all cool volumes in a job
    /// </summary>
    [Function("RecalculateJobCosts")]
    public async Task<HttpResponseData> RecalculateJobAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cool-assumptions/recalculate-job/{jobId}")] HttpRequestData req,
        string jobId)
    {
        try
        {
            var recalculated = await _recalcService.RecalculateJobAsync(jobId);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new 
            { 
                message = $"Recalculated {recalculated} cool volumes",
                recalculatedVolumes = recalculated
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recalculating costs for job {JobId}", jobId);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }
}
