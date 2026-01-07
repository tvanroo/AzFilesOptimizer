using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using AzFilesOptimizer.Backend.Models;
using AzFilesOptimizer.Backend.Services;

namespace AzFilesOptimizer.Backend.Functions;

public class WorkloadProfileFunction
{
    private readonly ILogger _logger;
    private readonly WorkloadProfileService _profileService;

    public WorkloadProfileFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<WorkloadProfileFunction>();
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
        _profileService = new WorkloadProfileService(connectionString, _logger);
    }

    [Function("GetWorkloadProfiles")]
    public async Task<HttpResponseData> GetAll(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "workload-profiles")] HttpRequestData req)
    {
        _logger.LogInformation("Getting all workload profiles");

        try
        {
            var profiles = await _profileService.GetAllProfilesAsync();
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(profiles);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting workload profiles");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetWorkloadProfile")]
    public async Task<HttpResponseData> GetById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "workload-profiles/{id}")] HttpRequestData req,
        string id)
    {
        _logger.LogInformation("Getting workload profile: {ProfileId}", id);

        try
        {
            var profile = await _profileService.GetProfileAsync(id);
            
            if (profile == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync($"Profile {id} not found");
                return notFoundResponse;
            }
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(profile);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting workload profile {ProfileId}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("CreateWorkloadProfile")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "workload-profiles")] HttpRequestData req)
    {
        _logger.LogInformation("Creating workload profile");

        try
        {
            var profile = await JsonSerializer.DeserializeAsync<WorkloadProfile>(req.Body);
            if (profile == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Invalid profile data");
                return badRequest;
            }
            
            var created = await _profileService.CreateProfileAsync(profile);
            
            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(created);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating workload profile");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("UpdateWorkloadProfile")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "workload-profiles/{id}")] HttpRequestData req,
        string id)
    {
        _logger.LogInformation("Updating workload profile: {ProfileId}", id);

        try
        {
            var profile = await JsonSerializer.DeserializeAsync<WorkloadProfile>(req.Body);
            if (profile == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Invalid profile data");
                return badRequest;
            }
            
            profile.RowKey = id; // Ensure ID matches route
            var updated = await _profileService.UpdateProfileAsync(profile);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(updated);
            return response;
        }
        catch (InvalidOperationException ex)
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteStringAsync(ex.Message);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating workload profile {ProfileId}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("DeleteWorkloadProfile")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "workload-profiles/{id}")] HttpRequestData req,
        string id)
    {
        _logger.LogInformation("Deleting workload profile: {ProfileId}", id);

        try
        {
            await _profileService.DeleteProfileAsync(id);
            
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            return response;
        }
        catch (InvalidOperationException ex)
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteStringAsync(ex.Message);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting workload profile {ProfileId}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("SeedWorkloadProfiles")]
    public async Task<HttpResponseData> SeedProfiles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "workload-profiles/seed")] HttpRequestData req)
    {
        _logger.LogInformation("Seeding default workload profiles");

        try
        {
            await _profileService.SeedDefaultProfilesAsync();
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync("Default profiles seeded successfully");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding workload profiles");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }
}
