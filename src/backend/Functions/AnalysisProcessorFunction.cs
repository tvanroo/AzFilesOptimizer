using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Data.Tables;
using Azure;
using System.Text.Json;
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
    private readonly AnalysisLogService _analysisLogService;

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
        _analysisLogService = new AnalysisLogService(connectionString, _logger);
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
        
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";

        try
        {
            // Log start of analysis job for this discovery
            await _analysisLogService.LogProgressAsync(analysisJobId, $"Starting analysis job for discovery job {discoveryJobId}");

            // First, get the discovery data to count volumes
            var migrationService = new DiscoveryMigrationService(connectionString, _logger);
            await migrationService.MigrateJobVolumesToBlobAsync(discoveryJobId);
            
            var annotationService = new VolumeAnnotationService(connectionString, _logger);
            var discoveryData = await annotationService.GetDiscoveryDataAsync(discoveryJobId);
            
            int totalVolumes = discoveryData?.Volumes?.Count ?? 0;
            
            // Update job status to Running with volume count
            analysisJob.Status = AnalysisJobStatus.Running.ToString();
            analysisJob.StartedAt = DateTime.UtcNow;
            analysisJob.TotalVolumes = totalVolumes;
            analysisJob.ProcessedVolumes = 0;
            await _analysisJobsTable.UpdateEntityAsync(analysisJob, analysisJob.ETag, TableUpdateMode.Replace);

            // Get API key configuration (use default-user for now)
            var apiKeyConfig = await _apiKeyService.GetApiKeyAsync("default-user");
            if (apiKeyConfig == null)
            {
                throw new InvalidOperationException("No API key configured");
            }

            if (string.IsNullOrEmpty(apiKeyConfig.KeyVaultSecretName))
            {
                throw new InvalidOperationException("API key not configured in Key Vault");
            }

            // Get API key from Key Vault
            var keyVaultService = new KeyVaultService();
            var apiKey = await keyVaultService.GetApiKeyAsync("default-user");
            
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("Failed to retrieve API key from Key Vault");
            }
            
            // Create analysis service
            var analysisService = new VolumeAnalysisService(
                connectionString,
                _profileService,
                _promptService,
                _logger);

            // Execute analysis
            await analysisService.AnalyzeVolumesAsync(
                discoveryJobId,
                "default-user",
                apiKey,
                apiKeyConfig.Provider,
                apiKeyConfig.Endpoint,
                analysisJobId);

            // Update job with completion
            analysisJob.Status = AnalysisJobStatus.Completed.ToString();
            analysisJob.CompletedAt = DateTime.UtcNow;
            await _analysisJobsTable.UpdateEntityAsync(analysisJob, ETag.All, TableUpdateMode.Replace);

            _logger.LogInformation("Analysis job completed: {AnalysisJobId}", analysisJobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis job failed: {AnalysisJobId}", analysisJobId);

            // Also surface the error into the analysis log so it appears in the UI
            await _analysisLogService.LogProgressAsync(analysisJobId, $"Analysis job failed: {ex.Message}", "ERROR");

            analysisJob.Status = AnalysisJobStatus.Failed.ToString();
            analysisJob.CompletedAt = DateTime.UtcNow;
            analysisJob.ErrorMessage = ex.Message;

            await _analysisJobsTable.UpdateEntityAsync(analysisJob, ETag.All, TableUpdateMode.Replace);
        }
    }


    private class AnalysisQueueMessage
    {
        public string AnalysisJobId { get; set; } = string.Empty;
        public string DiscoveryJobId { get; set; } = string.Empty;
    }
}
