using Azure.Storage.Blobs;
using AzFilesOptimizer.Backend.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace AzFilesOptimizer.Backend.Services;

public class AnalysisLogService
{
    private readonly BlobContainerClient _blobContainer;
    private readonly ILogger _logger;

    public AnalysisLogService(string connectionString, ILogger logger)
    {
        _logger = logger;
        var blobServiceClient = new BlobServiceClient(connectionString);
        _blobContainer = blobServiceClient.GetBlobContainerClient("analysis-logs");
        _blobContainer.CreateIfNotExists();
    }

    public async Task LogProgressAsync(string analysisJobId, string message, string level = "INFO")
    {
        try
        {
            var logEntry = new AnalysisLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = level,
                Message = message
            };

            await AppendLogEntryAsync(analysisJobId, logEntry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write analysis log for job {JobId}", analysisJobId);
        }
    }

    public async Task LogVolumeStartAsync(string analysisJobId, string volumeName, int index, int total)
    {
        await LogProgressAsync(analysisJobId, $"[{index}/{total}] Starting analysis of volume: {volumeName}");
    }

    public async Task LogPromptExecutionAsync(string analysisJobId, string volumeName, string promptName, string result)
    {
        await LogProgressAsync(analysisJobId, $"  [{volumeName}] Prompt '{promptName}': {result}");
    }

    public async Task LogVolumeCompleteAsync(string analysisJobId, string volumeName, string workloadName, double confidence)
    {
        await LogProgressAsync(analysisJobId, $"  [{volumeName}] ✓ Complete - Workload: {workloadName}, Confidence: {confidence:P0}");
    }

    public async Task LogVolumeErrorAsync(string analysisJobId, string volumeName, string error)
    {
        await LogProgressAsync(analysisJobId, $"  [{volumeName}] ✗ Error: {error}", "ERROR");
    }

    public async Task<List<AnalysisLogEntry>> GetLogsAsync(string analysisJobId)
    {
        try
        {
            var blobClient = _blobContainer.GetBlobClient($"{analysisJobId}/analysis.log");
            
            if (!await blobClient.ExistsAsync())
            {
                return new List<AnalysisLogEntry>();
            }

            var response = await blobClient.DownloadContentAsync();
            var json = response.Value.Content.ToString();
            
            var logs = JsonSerializer.Deserialize<List<AnalysisLogEntry>>(json);
            return logs ?? new List<AnalysisLogEntry>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading analysis logs for job {JobId}", analysisJobId);
            return new List<AnalysisLogEntry>();
        }
    }

    private async Task AppendLogEntryAsync(string analysisJobId, AnalysisLogEntry entry)
    {
        var blobClient = _blobContainer.GetBlobClient($"{analysisJobId}/analysis.log");
        
        // Read existing logs
        List<AnalysisLogEntry> logs;
        if (await blobClient.ExistsAsync())
        {
            var response = await blobClient.DownloadContentAsync();
            var json = response.Value.Content.ToString();
            logs = JsonSerializer.Deserialize<List<AnalysisLogEntry>>(json) ?? new List<AnalysisLogEntry>();
        }
        else
        {
            logs = new List<AnalysisLogEntry>();
        }

        // Append new entry
        logs.Add(entry);

        // Write back
        var updatedJson = JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true });
        await blobClient.UploadAsync(BinaryData.FromString(updatedJson), overwrite: true);
    }

    public async Task ClearLogsAsync(string analysisJobId)
    {
        try
        {
            var blobClient = _blobContainer.GetBlobClient($"{analysisJobId}/analysis.log");
            await blobClient.DeleteIfExistsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing logs for job {JobId}", analysisJobId);
        }
    }
}

public class AnalysisLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "INFO";
    public string Message { get; set; } = string.Empty;
}
