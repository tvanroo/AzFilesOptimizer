using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using System.Linq;
using AzFilesOptimizer.Backend.Models;

namespace AzFilesOptimizer.Backend.Services;

/// <summary>
/// Centralized runner for executing an analysis job end-to-end. This logic is shared by the
/// queue-triggered AnalysisProcessor function and any HTTP fallback paths that need to execute
/// an analysis inline (e.g., when the queue pipeline is temporarily bypassed).
/// </summary>
public class AnalysisJobRunner
{
    private readonly ILogger _logger;
    private readonly string _connectionString;

    private TableClient? _analysisJobsTable;
    private AnalysisLogService? _analysisLogService;
    private WorkloadProfileService? _profileService;
    private AnalysisPromptService? _promptService;
    private ApiKeyStorageService? _apiKeyService;

    public AnalysisJobRunner(string connectionString, ILogger logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task RunAsync(string analysisJobId, string discoveryJobId, string userId = "default-user", double bufferPercent = 30.0)
    {
        EnsureCoreServicesInitialized();

        var jobResponse = await _analysisJobsTable!.GetEntityAsync<AnalysisJob>("AnalysisJob", analysisJobId);
        var analysisJob = jobResponse.Value;

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
            await _analysisLogService.LogProgressAsync(analysisJobId, $"[Processor] Loading API key configuration for user '{userId}'...");
            var apiKeyConfig = await _apiKeyService!.GetApiKeyAsync(userId);
            if (apiKeyConfig == null)
            {
                await _analysisLogService.LogProgressAsync(analysisJobId, $"[Processor] No API key record found for user '{userId}'", "ERROR");
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
            var modelToUse = ResolvePreferredModel(apiKeyConfig);
            await _analysisLogService.LogProgressAsync(
                analysisJobId,
                $"[Processor] Using preferred model '{modelToUse}' for provider {apiKeyConfig.Provider}.");

            // 3) Fetch API key from Key Vault
            await _analysisLogService.LogProgressAsync(analysisJobId, $"[Processor] Fetching API key secret '{apiKeyConfig.KeyVaultSecretName}' from Key Vault...");
            var keyVaultService = new KeyVaultService();
            var apiKey = await keyVaultService.GetApiKeyAsync(userId);

            if (string.IsNullOrEmpty(apiKey))
            {
                await _analysisLogService.LogProgressAsync(analysisJobId, "[Processor] Retrieved API key was empty/null", "ERROR");
                throw new InvalidOperationException("Failed to retrieve API key from Key Vault");
            }

            await _analysisLogService.LogProgressAsync(
                analysisJobId,
                "[Processor] Successfully retrieved API key from Key Vault (value not logged).\n[Processor] Creating VolumeAnalysisService and starting analysis...");

            // 4) Create analysis service and run analysis
            var analysisService = new VolumeAnalysisService(
                _connectionString,
                _profileService!,
                _promptService!,
                _logger);

            await analysisService.AnalyzeVolumesAsync(
                discoveryJobId,
                userId,
                apiKey!,
                apiKeyConfig.Provider,
                apiKeyConfig.Endpoint,
                analysisJobId,
                modelToUse,
                bufferPercent);

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

            if (_analysisLogService != null)
            {
                await _analysisLogService.LogProgressAsync(analysisJobId, $"[Processor] Analysis job failed: {ex.Message}", "ERROR");
            }

            analysisJob.Status = AnalysisJobStatus.Failed.ToString();
            analysisJob.CompletedAt = DateTime.UtcNow;
            analysisJob.ErrorMessage = ex.Message;

            await _analysisJobsTable!.UpdateEntityAsync(analysisJob, ETag.All, TableUpdateMode.Replace);

            throw;
        }
    }

    private void EnsureCoreServicesInitialized()
    {
        if (_analysisJobsTable == null)
        {
            var tableServiceClient = new TableServiceClient(_connectionString);
            _analysisJobsTable = tableServiceClient.GetTableClient("AnalysisJobs");
            _analysisJobsTable.CreateIfNotExists();
        }

        _analysisLogService ??= new AnalysisLogService(_connectionString, _logger);
        _profileService ??= new WorkloadProfileService(_connectionString, _logger);
        _promptService ??= new AnalysisPromptService(_connectionString, _logger);
        _apiKeyService ??= new ApiKeyStorageService(_connectionString);
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

        // Fallbacks
        if (string.Equals(config.Provider, "AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return "gpt-4";
        }

        return "gpt-4";
    }
}
