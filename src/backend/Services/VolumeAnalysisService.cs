using Azure.Storage.Blobs;
using AzFilesOptimizer.Backend.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AzFilesOptimizer.Backend.Services;

public class VolumeAnalysisService
{
    private readonly ILogger _logger;
    private readonly BlobContainerClient _blobContainer;
    private readonly CapacityAnalysisService _capacityAnalysisService;
    private AnalysisLogService? _logService;
    private Azure.Data.Tables.TableClient? _analysisJobsTable;
    private string? _currentAnalysisJobId;

    public VolumeAnalysisService(
        string connectionString,
        ILogger logger,
        CapacityAnalysisService? capacityAnalysisService = null)
    {
        _logger = logger;
        _capacityAnalysisService = capacityAnalysisService ?? new CapacityAnalysisService(logger);
        
        var blobServiceClient = new BlobServiceClient(connectionString);
        _blobContainer = blobServiceClient.GetBlobContainerClient("discovery-data");
        _blobContainer.CreateIfNotExists();
    }

    public async Task AnalyzeVolumesAsync(string discoveryJobId, string userId, string apiKey, string provider, string? endpoint, string? analysisJobId = null, string? preferredModel = null, double bufferPercent = 30.0)
    {
        _logger.LogInformation("Starting analysis for discovery job: {JobId}", discoveryJobId);
        
        // Initialize log service and job tracking if analysisJobId provided
        if (!string.IsNullOrEmpty(analysisJobId))
        {
            _currentAnalysisJobId = analysisJobId;
            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
            _logService = new AnalysisLogService(connectionString, _logger);
            await _logService.LogProgressAsync(analysisJobId, $"Starting analysis for discovery job: {discoveryJobId}");
            
            // Initialize table client for progress updates
            var tableServiceClient = new Azure.Data.Tables.TableServiceClient(connectionString);
            _analysisJobsTable = tableServiceClient.GetTableClient("AnalysisJobs");
        }

        // Load discovery data
        var discoveryData = await LoadDiscoveryDataAsync(discoveryJobId);
        if (discoveryData == null || discoveryData.Volumes.Count == 0)
        {
            _logger.LogWarning("No volumes found for discovery job: {JobId}", discoveryJobId);
            if (_logService != null && !string.IsNullOrEmpty(analysisJobId))
                await _logService.LogProgressAsync(analysisJobId, "No volumes found for analysis", "WARNING");
            return;
        }

        // When DiscoveryData is loaded from Blob storage, VolumeData is deserialized as JsonElement
        // because its static type is 'object'. Normalize it back to strongly-typed models so that
        // downstream 'is DiscoveredAnfVolume' / 'is DiscoveredManagedDisk' checks succeed.
        RehydrateVolumeData(discoveryData);

        // Partition volumes by type. DiscoveryData may contain Azure Files, ANF volumes,
        // and managed disks. We currently support AI analysis for all three types.
        var azureFileVolumes = discoveryData.Volumes
            .Where(v => string.Equals(v.VolumeType, "AzureFiles", StringComparison.OrdinalIgnoreCase)
                        && v.Volume != null)
            .ToList();

        var anfVolumes = discoveryData.Volumes
            .Where(v => string.Equals(v.VolumeType, "ANF", StringComparison.OrdinalIgnoreCase)
                        && v.VolumeData is DiscoveredAnfVolume)
            .ToList();

        var managedDiskVolumes = discoveryData.Volumes
            .Where(v => string.Equals(v.VolumeType, "ManagedDisk", StringComparison.OrdinalIgnoreCase)
                        && v.VolumeData is DiscoveredManagedDisk)
            .ToList();

        var volumesToAnalyze = azureFileVolumes
            .Concat(anfVolumes)
            .Concat(managedDiskVolumes)
            .ToList();

        if (volumesToAnalyze.Count == 0)
        {
            _logger.LogWarning("Discovery job {JobId} contains no supported volumes (Azure Files, ANF, Managed Disks) to analyze.", discoveryJobId);
            if (_logService != null && !string.IsNullOrEmpty(analysisJobId))
                await _logService.LogProgressAsync(analysisJobId, "No supported volumes found for analysis", "WARNING");
            return;
        }

        _logger.LogInformation("Analysis prompt feature removed; continuing without prompts.");
        if (_logService != null && !string.IsNullOrEmpty(analysisJobId))
        {
            await _logService.LogProgressAsync(
                analysisJobId,
                $"Found {azureFileVolumes.Count} Azure Files shares, {anfVolumes.Count} ANF volumes, and {managedDiskVolumes.Count} managed disks to analyze");
        }

        int processedCount = 0;
        int failedCount = 0;
        int totalCount = volumesToAnalyze.Count;

        // Analyze each volume according to its type
        foreach (var volumeWrapper in volumesToAnalyze)
        {
            processedCount++;

            try
            {
                if (string.Equals(volumeWrapper.VolumeType, "AzureFiles", StringComparison.OrdinalIgnoreCase)
                    && volumeWrapper.Volume != null)
                {
                    var share = volumeWrapper.Volume!;
                    var volumeName = share.ShareName ?? "Unknown";

                    _logger.LogInformation("Analyzing Azure Files volume: {VolumeName}", volumeName);
                    if (_logService != null && !string.IsNullOrEmpty(analysisJobId))
                    {
                        await _logService.LogVolumeStartAsync(analysisJobId, volumeName, processedCount, totalCount);
                    }

                    var analysis = await AnalyzeSingleVolumeAsync(
                        share,
                        apiKey,
                        provider,
                        endpoint,
                        analysisJobId,
                        volumeName,
                        preferredModel,
                        bufferPercent);

                    volumeWrapper.AiAnalysis = analysis;
                    volumeWrapper.UserAnnotations ??= new UserAnnotations();

                    if (_logService != null && !string.IsNullOrEmpty(analysisJobId))
                    {
                        var workloadName = analysis.SuggestedWorkloadName ?? "Unclassified";
                        await _logService.LogVolumeCompleteAsync(analysisJobId, volumeName, workloadName, analysis.ConfidenceScore);
                    }
                }
                else if (string.Equals(volumeWrapper.VolumeType, "ANF", StringComparison.OrdinalIgnoreCase)
                         && volumeWrapper.VolumeData is DiscoveredAnfVolume anfVolume)
                {
                    var volumeName = anfVolume.VolumeName ?? "Unknown";

                    _logger.LogInformation("Analyzing ANF volume: {VolumeName}", volumeName);
                    if (_logService != null && !string.IsNullOrEmpty(analysisJobId))
                    {
                        await _logService.LogVolumeStartAsync(analysisJobId, volumeName, processedCount, totalCount);
                    }

                    var analysis = await AnalyzeAnfVolumeAsync(
                        anfVolume,
                        apiKey,
                        provider,
                        endpoint,
                        analysisJobId,
                        volumeName,
                        preferredModel,
                        bufferPercent);

                    volumeWrapper.AiAnalysis = analysis;
                    volumeWrapper.UserAnnotations ??= new UserAnnotations();

                    if (_logService != null && !string.IsNullOrEmpty(analysisJobId))
                    {
                        var workloadName = analysis.SuggestedWorkloadName ?? "Unclassified";
                        await _logService.LogVolumeCompleteAsync(analysisJobId, volumeName, workloadName, analysis.ConfidenceScore);
                    }
                }
                else if (string.Equals(volumeWrapper.VolumeType, "ManagedDisk", StringComparison.OrdinalIgnoreCase)
                         && volumeWrapper.VolumeData is DiscoveredManagedDisk disk)
                {
                    var volumeName = disk.DiskName ?? "Unknown";

                    _logger.LogInformation("Analyzing Managed Disk: {VolumeName}", volumeName);
                    if (_logService != null && !string.IsNullOrEmpty(analysisJobId))
                    {
                        await _logService.LogVolumeStartAsync(analysisJobId, volumeName, processedCount, totalCount);
                    }

                    var analysis = await AnalyzeManagedDiskAsync(
                        disk,
                        apiKey,
                        provider,
                        endpoint,
                        analysisJobId,
                        volumeName,
                        preferredModel,
                        bufferPercent);

                    volumeWrapper.AiAnalysis = analysis;
                    volumeWrapper.UserAnnotations ??= new UserAnnotations();

                    if (_logService != null && !string.IsNullOrEmpty(analysisJobId))
                    {
                        var workloadName = analysis.SuggestedWorkloadName ?? "Unclassified";
                        await _logService.LogVolumeCompleteAsync(analysisJobId, volumeName, workloadName, analysis.ConfidenceScore);
                    }
                }

                // Update job progress after each volume regardless of type
                await UpdateJobProgressAsync(processedCount, failedCount);
            }
            catch (Exception ex)
            {
                var volumeName = volumeWrapper.VolumeType switch
                {
                    "AzureFiles" => volumeWrapper.Volume?.ShareName ?? "Unknown",
                    "ANF" => (volumeWrapper.VolumeData as DiscoveredAnfVolume)?.VolumeName ?? "Unknown",
                    "ManagedDisk" => (volumeWrapper.VolumeData as DiscoveredManagedDisk)?.DiskName ?? "Unknown",
                    _ => "Unknown"
                };

                _logger.LogError(ex, "Failed to analyze volume: {VolumeType} {VolumeName}", volumeWrapper.VolumeType, volumeName);
                if (_logService != null && !string.IsNullOrEmpty(analysisJobId))
                {
                    await _logService.LogVolumeErrorAsync(analysisJobId, volumeName, ex.Message);
                }

                volumeWrapper.AiAnalysis = new AiAnalysisResult
                {
                    LastAnalyzed = DateTime.UtcNow,
                    ErrorMessage = ex.Message
                };
                failedCount++;
            }
        }

        // Save updated discovery data
        discoveryData.LastAnalyzed = DateTime.UtcNow;
        await SaveDiscoveryDataAsync(discoveryData);

        _logger.LogInformation("Analysis complete. Processed: {Processed}, Failed: {Failed}", processedCount, failedCount);
        if (_logService != null && !string.IsNullOrEmpty(analysisJobId))
            await _logService.LogProgressAsync(analysisJobId, $"✓ Analysis complete! Processed: {processedCount}, Failed: {failedCount}");
    }

    public async Task<AiAnalysisResult> AnalyzeSingleVolumeAsync(
        DiscoveredAzureFileShare volume,
        string apiKey,
        string provider,
        string? endpoint,
        string? analysisJobId = null,
        string? volumeName = null,
        string? preferredModel = null,
        double bufferPercent = 30.0,
        string? metricsJsonOverride = null,
        int? monitoringDaysOverride = null,
        string metricsVolumeType = "AzureFiles")
    {
        var result = new AiAnalysisResult
        {
            LastAnalyzed = DateTime.UtcNow,
            AppliedPrompts = Array.Empty<PromptExecutionResult>()
        };
        
        // Perform capacity and throughput sizing analysis
        try
        {
            if (_logService != null && !string.IsNullOrEmpty(analysisJobId) && !string.IsNullOrEmpty(volumeName))
            {
                await _logService.LogProgressAsync(analysisJobId, $"  [{volumeName}] → Analyzing capacity and throughput sizing");
            }
            
            var capacitySizing = _capacityAnalysisService.AnalyzeMetrics(
                metricsJsonOverride ?? volume.HistoricalMetricsSummary,
                bufferPercent,
                monitoringDaysOverride ?? (volume.MonitoringDataAvailableDays ?? 30),
                metricsVolumeType);
            
            // Enhance with AI analysis if we have workload classification
            if (!string.IsNullOrEmpty(result.SuggestedWorkloadName) && capacitySizing.HasSufficientData)
            {
                capacitySizing = await _capacityAnalysisService.EnhanceWithAiAnalysisAsync(
                    capacitySizing,
                    result.SuggestedWorkloadName,
                    apiKey,
                    provider,
                    endpoint,
                    preferredModel);
            }
            
            result.CapacitySizing = capacitySizing;
            
            if (_logService != null && !string.IsNullOrEmpty(analysisJobId) && !string.IsNullOrEmpty(volumeName))
            {
                var summary = capacitySizing.HasSufficientData
                    ? $"Capacity: {capacitySizing.RecommendedCapacityInUnit:0.##} {capacitySizing.CapacityUnit}, Throughput: {capacitySizing.RecommendedThroughputMiBps:0.##} MiB/s, Service Level: {capacitySizing.SuggestedServiceLevel ?? "Unknown"}"
                    : "Insufficient metrics data for sizing";
                await _logService.LogProgressAsync(analysisJobId, $"  [{volumeName}] ✓ Sizing: {summary}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to perform capacity analysis for volume {VolumeName}", volume.ShareName);
            if (_logService != null && !string.IsNullOrEmpty(analysisJobId) && !string.IsNullOrEmpty(volumeName))
            {
                await _logService.LogProgressAsync(analysisJobId, $"  [{volumeName}] ⚠ Capacity analysis failed: {ex.Message}", "WARNING");
            }
            // Don't fail the entire analysis if capacity sizing fails
        }

        return result;
    }

    public async Task<AiAnalysisResult> AnalyzeAnfVolumeAsync(
        DiscoveredAnfVolume volume,
        string apiKey,
        string provider,
        string? endpoint,
        string? analysisJobId,
        string? volumeName,
        string? preferredModel,
        double bufferPercent)
    {
        // Map ANF properties into a pseudo Azure Files share so existing prompt variables
        // ({VolumeName}, {Size}, {Protocols}, etc.) continue to work without duplicating logic.
        var pseudoShare = new DiscoveredAzureFileShare
        {
            SubscriptionId = volume.SubscriptionId,
            ResourceGroup = volume.ResourceGroup,
            StorageAccountName = volume.NetAppAccountName,
            ShareName = volume.VolumeName,
            ResourceId = volume.ResourceId,
            Location = volume.Location,
            StorageAccountSku = volume.ServiceLevel,
            StorageAccountKind = "ANF",
            ShareQuotaGiB = volume.ProvisionedSizeBytes / (1024L * 1024L * 1024L),
            EnabledProtocols = volume.ProtocolTypes,
            MinimumTlsVersion = volume.MinimumTlsVersion,
            SnapshotCount = volume.SnapshotCount,
            TotalSnapshotSizeBytes = volume.TotalSnapshotSizeBytes,
            ChurnRateBytesPerDay = volume.ChurnRateBytesPerDay,
            BackupPolicyConfigured = volume.BackupPolicyConfigured,
            MonitoringEnabled = volume.MonitoringEnabled,
            MonitoringDataAvailableDays = volume.MonitoringDataAvailableDays,
            HistoricalMetricsSummary = volume.HistoricalMetricsSummary,
            Tags = volume.Tags,
            DiscoveredAt = volume.DiscoveredAt
        };

        return await AnalyzeSingleVolumeAsync(
            pseudoShare,
            apiKey,
            provider,
            endpoint,
            analysisJobId,
            volumeName ?? volume.VolumeName,
            preferredModel,
            bufferPercent,
            volume.HistoricalMetricsSummary,
            volume.MonitoringDataAvailableDays ?? 30,
            "ANF");
    }

    public async Task<AiAnalysisResult> AnalyzeManagedDiskAsync(
        DiscoveredManagedDisk disk,
        string apiKey,
        string provider,
        string? endpoint,
        string? analysisJobId,
        string? volumeName,
        string? preferredModel,
        double bufferPercent)
    {
        // Map managed disk properties into a pseudo Azure Files share for prompt variables.
        var pseudoShare = new DiscoveredAzureFileShare
        {
            TenantId = disk.TenantId,
            SubscriptionId = disk.SubscriptionId,
            ResourceGroup = disk.ResourceGroup,
            StorageAccountName = disk.AttachedVmName ?? "Unattached",
            ShareName = disk.DiskName,
            ResourceId = disk.ResourceId,
            Location = disk.Location,
            StorageAccountSku = disk.DiskSku,
            StorageAccountKind = "ManagedDisk",
            ShareQuotaGiB = disk.DiskSizeGB,
            // Treat used bytes (if present) as usage; otherwise leave at 0.
            ShareUsageBytes = disk.UsedBytes,
            EnabledProtocols = new[] { "Block" },
            Metadata = disk.Tags,
            Tags = disk.VmTags,
            MonitoringEnabled = disk.MonitoringEnabled,
            MonitoringDataAvailableDays = disk.MonitoringDataAvailableDays,
            HistoricalMetricsSummary = disk.HistoricalMetricsSummary,
            DiscoveredAt = disk.DiscoveredAt
        };

        // For now, managed disk metrics are treated similarly to Azure Files for sizing
        // purposes; CapacityAnalysisService will either extract what it can or mark
        // the result as having insufficient data.
        return await AnalyzeSingleVolumeAsync(
            pseudoShare,
            prompts,
            apiKey,
            provider,
            endpoint,
            analysisJobId,
            volumeName ?? disk.DiskName,
            preferredModel,
            bufferPercent,
            disk.HistoricalMetricsSummary,
            disk.MonitoringDataAvailableDays ?? 30,
            "AzureFiles");
    }


    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private string FormatDictionary(Dictionary<string, string>? dict)
    {
        if (dict == null || dict.Count == 0)
            return "None";

        return string.Join(", ", dict.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
    }

    private string FormatMetricsSummary(string? metricsJson)
    {
        if (string.IsNullOrWhiteSpace(metricsJson))
            return "No historical metrics available.";

        try
        {
            using var doc = JsonDocument.Parse(metricsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return metricsJson; // fallback to raw JSON

            var parts = new List<string>();
            foreach (var metricProp in root.EnumerateObject())
            {
                var name = metricProp.Name;
                var obj = metricProp.Value;
                double avg = obj.TryGetProperty("average", out var avgEl) && avgEl.TryGetDouble(out var a) ? a : 0;
                double max = obj.TryGetProperty("max", out var maxEl) && maxEl.TryGetDouble(out var m) ? m : 0;
                double total = obj.TryGetProperty("total", out var totEl) && totEl.TryGetDouble(out var t) ? t : 0;
                int count = obj.TryGetProperty("dataPointCount", out var cEl) && cEl.TryGetInt32(out var c) ? c : 0;

                parts.Add($"{name}: avg={avg:0.##}, max={max:0.##}, total={total:0.##}, points={count}");
            }

            return parts.Count == 0
                ? "No historical metrics available."
                : "Historical metrics summary: " + string.Join("; ", parts);
        }
        catch
        {
            // If parsing fails, return the raw JSON
            return metricsJson;
        }
    }

    private async Task<DiscoveryData?> LoadDiscoveryDataAsync(string jobId)
    {
        try
        {
            var blobClient = _blobContainer.GetBlobClient($"jobs/{jobId}/discovered-volumes.json");
            
            if (!await blobClient.ExistsAsync())
            {
                _logger.LogWarning("Discovery data not found for job: {JobId}", jobId);
                return null;
            }
            
            var response = await blobClient.DownloadContentAsync();
            var json = response.Value.Content.ToString();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            return JsonSerializer.Deserialize<DiscoveryData>(json, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading discovery data for job: {JobId}", jobId);
            throw;
        }
    }

    private void RehydrateVolumeData(DiscoveryData discoveryData)
    {
        foreach (var volume in discoveryData.Volumes)
        {
            if (volume.VolumeData is JsonElement element)
            {
                try
                {
                    switch (volume.VolumeType)
                    {
                        case "AzureFiles":
                            volume.VolumeData = element.Deserialize<DiscoveredAzureFileShare>() ?? new DiscoveredAzureFileShare();
                            break;
                        case "ANF":
                            volume.VolumeData = element.Deserialize<DiscoveredAnfVolume>() ?? new DiscoveredAnfVolume();
                            break;
                        case "ManagedDisk":
                            volume.VolumeData = element.Deserialize<DiscoveredManagedDisk>() ?? new DiscoveredManagedDisk();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to rehydrate VolumeData for volume {VolumeId} of type {VolumeType}", volume.VolumeId, volume.VolumeType);
                }
            }
        }
    }

    private async Task SaveDiscoveryDataAsync(DiscoveryData data)
    {
        try
        {
            var blobClient = _blobContainer.GetBlobClient($"jobs/{data.JobId}/discovered-volumes.json");
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await blobClient.UploadAsync(BinaryData.FromString(json), overwrite: true);
            
            _logger.LogInformation("Saved discovery data for job: {JobId}", data.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving discovery data for job: {JobId}", data.JobId);
            throw;
        }
    }
    
    private async Task UpdateJobProgressAsync(int processedCount, int failedCount)
    {
        if (_analysisJobsTable == null || string.IsNullOrEmpty(_currentAnalysisJobId))
            return;
            
        try
        {
            var job = await _analysisJobsTable.GetEntityAsync<AnalysisJob>("AnalysisJob", _currentAnalysisJobId);
            var analysisJob = job.Value;
            
            analysisJob.ProcessedVolumes = processedCount;
            analysisJob.FailedVolumes = failedCount;
            
            await _analysisJobsTable.UpdateEntityAsync(analysisJob, Azure.ETag.All, Azure.Data.Tables.TableUpdateMode.Replace);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update job progress for {JobId}", _currentAnalysisJobId);
        }
    }
}
