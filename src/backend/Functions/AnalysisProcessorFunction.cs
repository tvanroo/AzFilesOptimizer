using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Azure.Data.Tables;
using AzFilesOptimizer.Backend.Models;
using AzFilesOptimizer.Backend.Services;

namespace AzFilesOptimizer.Backend.Functions;

public class AnalysisProcessorFunction
{
    private readonly ILogger _logger;
    private readonly TableClient _analysisJobsTable;
    private readonly WorkloadProfileService _profileService;
    private readonly AnalysisPromptService _promptService;
    private readonly ApiKeyStorageService _apiKeyService;

    public AnalysisProcessorFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<AnalysisProcessorFunction>();
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
        
        var tableServiceClient = new TableServiceClient(connectionString);
        _analysisJobsTable = tableServiceClient.GetTableClient("AnalysisJobs");
        _analysisJobsTable.CreateIfNotExists();
        
        _profileService = new WorkloadProfileService(connectionString, _logger);
        _promptService = new AnalysisPromptService(connectionString, _logger);
        _apiKeyService = new ApiKeyStorageService(connectionString);
    }

    [Function("AnalysisProcessor")]
    public async Task Run(
        [QueueTrigger("analysis-queue", Connection = "AzureWebJobsStorage")] string message,
        FunctionContext context)
    {
        _logger.LogInformation("AnalysisProcessor triggered. Message: {Message}", message);

        try
        {
            var jobMessage = JsonSerializer.Deserialize<AnalysisQueueMessage>(message);
            if (jobMessage == null || string.IsNullOrEmpty(jobMessage.AnalysisJobId))
            {
                _logger.LogError("Invalid analysis message format");
                return;
            }

            await ProcessAnalysisJobAsync(jobMessage.AnalysisJobId, jobMessage.DiscoveryJobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing analysis message");
        }
    }

    private async Task ProcessAnalysisJobAsync(string analysisJobId, string discoveryJobId)
    {
        _logger.LogInformation("Processing analysis job: {AnalysisJobId}", analysisJobId);

        var job = await _analysisJobsTable.GetEntityAsync<AnalysisJob>("AnalysisJob", analysisJobId);
        var analysisJob = job.Value;

        try
        {
            // Update job status to Running
            analysisJob.Status = AnalysisJobStatus.Running.ToString();
            analysisJob.StartedAt = DateTime.UtcNow;
            await _analysisJobsTable.UpdateEntityAsync(analysisJob, analysisJob.ETag, TableUpdateMode.Replace);

            // Get API key configuration (use a system user ID or get from job)
            var apiKeyConfig = await _apiKeyService.GetApiKeyAsync("system");
            if (apiKeyConfig == null)
            {
                throw new InvalidOperationException("No API key configured");
            }

            // Get API key from Key Vault
            var apiKey = await GetApiKeyFromKeyVaultAsync(apiKeyConfig.KeyVaultSecretName);
            
            // Create analysis service
            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
            var analysisService = new VolumeAnalysisService(
                connectionString,
                _profileService,
                _promptService,
                _logger);

            // Execute analysis
            await analysisService.AnalyzeVolumesAsync(
                discoveryJobId,
                "system",
                apiKey,
                apiKeyConfig.Provider,
                apiKeyConfig.Endpoint);

            // Update job with completion
            analysisJob.Status = AnalysisJobStatus.Completed.ToString();
            analysisJob.CompletedAt = DateTime.UtcNow;
            await _analysisJobsTable.UpdateEntityAsync(analysisJob, ETag.All, TableUpdateMode.Replace);

            _logger.LogInformation("Analysis job completed: {AnalysisJobId}", analysisJobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis job failed: {AnalysisJobId}", analysisJobId);

            analysisJob.Status = AnalysisJobStatus.Failed.ToString();
            analysisJob.CompletedAt = DateTime.UtcNow;
            analysisJob.ErrorMessage = ex.Message;

            await _analysisJobsTable.UpdateEntityAsync(analysisJob, ETag.All, TableUpdateMode.Replace);
        }
    }

    private async Task<string> GetApiKeyFromKeyVaultAsync(string secretName)
    {
        // TODO: Implement Key Vault integration
        // For now, return from environment variable
        var apiKey = Environment.GetEnvironmentVariable("OpenAI_ApiKey");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("API key not found in environment variables");
        }
        return apiKey;
    }

    private class AnalysisQueueMessage
    {
        public string AnalysisJobId { get; set; } = string.Empty;
        public string DiscoveryJobId { get; set; } = string.Empty;
    }
}
