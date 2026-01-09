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
            // High-level start log
            await _analysisLogService.LogProgressAsync(analysisJobId, $"[Processor] Starting analysis job for discovery job {discoveryJobId}");

            // 1) Load discovery data / volume count
            await _analysisLogService.LogProgressAsync(analysisJobId, "[Processor] Migrating discovery volumes to Blob storage...");
            var migrationService = new DiscoveryMigrationService(connectionString, _logger);
            await migrationService.MigrateJobVolumesToBlobAsync(discoveryJobId);
            
            var annotationService = new VolumeAnnotationService(connectionString, _logger);
            await _analysisLogService.LogProgressAsync(analysisJobId, "[Processor] Loading discovery data from discovery-data container...");
            var discoveryData = await annotationService.GetDiscoveryDataAsync(discoveryJobId);
            
            int totalVolumes = discoveryData?.Volumes?.Count ?? 0;
            await _analysisLogService.LogProgressAsync(analysisJobId, $"[Processor] Discovery data loaded. Volumes found: {totalVolumes}");
            
            // Update job status to Running with volume count
            analysisJob.Status = AnalysisJobStatus.Running.ToString();
            analysisJob.StartedAt = DateTime.UtcNow;
            analysisJob.TotalVolumes = totalVolumes;
            analysisJob.ProcessedVolumes = 0;
            await _analysisJobsTable.UpdateEntityAsync(analysisJob, analysisJob.ETag, TableUpdateMode.Replace);

            // 2) Resolve API key configuration
            await _analysisLogService.LogProgressAsync(analysisJobId, "[Processor] Loading API key configuration for user 'default-user'...");
            var apiKeyConfig = await _apiKeyService.GetApiKeyAsync("default-user");
            if (apiKeyConfig == null)
            {
                await _analysisLogService.LogProgressAsync(analysisJobId, "[Processor] No API key record found for user 'default-user'", "ERROR");
                throw new InvalidOperationException("No API key configured");
            }

            await _analysisLogService.LogProgressAsync(
                analysisJobId,
                $"[Processor] API key config loaded. Provider={apiKeyConfig.Provider}, Endpoint={(string.IsNullOrEmpty(apiKeyConfig.Endpoint) ? "<none>" : apiKeyConfig.Endpoint)}, SecretName={apiKeyConfig.KeyVaultSecretName}");

            if (string.IsNullOrEmpty(apiKeyConfig.KeyVaultSecretName))
            {
                await _analysisLogService.LogProgressAsync(analysisJobId, "[Processor] API key config is missing KeyVaultSecretName", "ERROR");
                throw new InvalidOperationException("API key not configured in Key Vault");
            }

            // 3) Fetch API key from Key Vault
            await _analysisLogService.LogProgressAsync(analysisJobId, $"[Processor] Fetching API key secret '{apiKeyConfig.KeyVaultSecretName}' from Key Vault...");
            var keyVaultService = new KeyVaultService();
            var apiKey = await keyVaultService.GetApiKeyAsync("default-user");
            
            if (string.IsNullOrEmpty(apiKey))
            {
                await _analysisLogService.LogProgressAsync(analysisJobId, "[Processor] Retrieved API key was empty/null", "ERROR");
                throw new InvalidOperationException("Failed to retrieve API key from Key Vault");
            }
            await _analysisLogService.LogProgressAsync(analysisJobId, "[Processor] Successfully retrieved API key from Key Vault (value not logged).\n[Processor] Creating VolumeAnalysisService and starting analysis...");
            
            // 4) Create analysis service and run analysis
            var analysisService = new VolumeAnalysisService(
                connectionString,
                _profileService,
                _promptService,
                _logger);

            await analysisService.AnalyzeVolumesAsync(
                discoveryJobId,
                "default-user",
                apiKey,
                apiKeyConfig.Provider,
                apiKeyConfig.Endpoint,
                analysisJobId);

            // 5) Update job with completion
            analysisJob.Status = AnalysisJobStatus.Completed.ToString();
            analysisJob.CompletedAt = DateTime.UtcNow;
            await _analysisJobsTable.UpdateEntityAsync(analysisJob, ETag.All, TableUpdateMode.Replace);

            await _analysisLogService.LogProgressAsync(analysisJobId, "[Processor] Analysis job completed successfully.");
            _logger.LogInformation("Analysis job completed: {AnalysisJobId}", analysisJobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis job failed: {AnalysisJobId}", analysisJobId);

            // Also surface the error into the analysis log so it appears in the UI
            await _analysisLogService.LogProgressAsync(analysisJobId, $"[Processor] Analysis job failed: {ex.Message}", "ERROR");

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
