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
    private readonly string _connectionString;

    // Lazily-initialized services so that constructor stays cheap and all failures
    // happen inside the function execution path (where we can surface them to logs/UI).
    private TableClient? _analysisJobsTable;
    private WorkloadProfileService? _profileService;
    private AnalysisPromptService? _promptService;
    private ApiKeyStorageService? _apiKeyService;
    private AnalysisLogService? _analysisLogService;

    public AnalysisProcessorFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<AnalysisProcessorFunction>();
        _connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            // Do NOT throw here; throwing in the constructor prevents the host from invoking
            // the function at all and silently sends messages to the poison queue without any
            // application-level diagnostics. Instead, we log a clear error and let the
            // execution path handle the failure in a controlled way.
            _logger.LogError("AzureWebJobsStorage connection string is missing or empty. AnalysisProcessor will fail to access storage.");
        }
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

        // Lazily initialize core services inside the execution path so that any
        // configuration/storage issues are surfaced as function errors (and can be
        // written to both the function logs and the analysis log UI), rather than
        // causing constructor failures that silently poison messages.
        if (_analysisJobsTable == null)
        {
            _logger.LogInformation("[Processor] Initializing AnalysisJobs table client...");
            var tableServiceClient = new TableServiceClient(_connectionString);
            _analysisJobsTable = tableServiceClient.GetTableClient("AnalysisJobs");
            _analysisJobsTable.CreateIfNotExists();
        }

        if (_analysisLogService == null)
        {
            _logger.LogInformation("[Processor] Initializing AnalysisLogService (Blob container: analysis-logs)...");
            _analysisLogService = new AnalysisLogService(_connectionString, _logger);
        }

        if (_profileService == null)
        {
            _logger.LogInformation("[Processor] Initializing WorkloadProfileService...");
            _profileService = new WorkloadProfileService(_connectionString, _logger);
        }

        if (_promptService == null)
        {
            _logger.LogInformation("[Processor] Initializing AnalysisPromptService...");
            _promptService = new AnalysisPromptService(_connectionString, _logger);
        }

        if (_apiKeyService == null)
        {
            _logger.LogInformation("[Processor] Initializing ApiKeyStorageService...");
            _apiKeyService = new ApiKeyStorageService(_connectionString);
        }

        // At this point, _analysisJobsTable and _analysisLogService should be non-null.
        var job = await _analysisJobsTable.GetEntityAsync<AnalysisJob>("AnalysisJob", analysisJobId);
        var analysisJob = job.Value;

        try
        {
            // High-level start log
            await _analysisLogService!.LogProgressAsync(analysisJobId, $"[Processor] Starting analysis job for discovery job {discoveryJobId}");

            // 1) Load discovery data / volume count
            await _analysisLogService.LogProgressAsync(analysisJobId, "[Processor] Migrating discovery volumes to Blob storage...");
            var migrationService = new DiscoveryMigrationService(_connectionString, _logger);
            await migrationService.MigrateJobVolumesToBlobAsync(discoveryJobId);
            
            var annotationService = new VolumeAnnotationService(_connectionString, _logger);
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
            var apiKeyConfig = await _apiKeyService!.GetApiKeyAsync("default-user");
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
                _connectionString,
                _profileService!,
                _promptService!,
                _logger);

            await analysisService.AnalyzeVolumesAsync(
                discoveryJobId,
                "default-user",
                apiKey!,
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
            if (_analysisLogService != null)
            {
                await _analysisLogService.LogProgressAsync(analysisJobId, $"[Processor] Analysis job failed: {ex.Message}", "ERROR");
            }

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
