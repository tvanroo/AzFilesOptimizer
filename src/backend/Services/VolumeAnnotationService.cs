using Azure.Storage.Blobs;
using AzFilesOptimizer.Backend.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace AzFilesOptimizer.Backend.Services;

public class VolumeAnnotationService
{
    private readonly ILogger _logger;
    private readonly BlobContainerClient _blobContainer;
    private readonly WorkloadProfileService _workloadProfileService;

    public VolumeAnnotationService(string connectionString, ILogger logger)
    {
        _logger = logger;
        var blobServiceClient = new BlobServiceClient(connectionString);
        _blobContainer = blobServiceClient.GetBlobContainerClient("discovery-data");
        _blobContainer.CreateIfNotExists();
        _workloadProfileService = new WorkloadProfileService(connectionString, logger);
    }

    public async Task<DiscoveryData?> GetDiscoveryDataAsync(string discoveryJobId)
    {
        try
        {
            var blobClient = _blobContainer.GetBlobClient($"jobs/{discoveryJobId}/discovered-volumes.json");
            
            if (!await blobClient.ExistsAsync())
            {
                _logger.LogWarning("Discovery data not found for job: {JobId}", discoveryJobId);
                return null;
            }
            
            var response = await blobClient.DownloadContentAsync();
            var json = response.Value.Content.ToString();
            return JsonSerializer.Deserialize<DiscoveryData>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading discovery data for job: {JobId}", discoveryJobId);
            throw;
        }
    }

    public async Task<VolumeListResponse> GetVolumesWithFiltersAsync(
        string discoveryJobId,
        string? workloadFilter = null,
        string? statusFilter = null,
        double? confidenceMin = null,
        int page = 1,
        int pageSize = 50)
    {
        var data = await GetDiscoveryDataAsync(discoveryJobId);
        if (data == null)
        {
            return new VolumeListResponse { TotalCount = 0, Page = page, PageSize = pageSize };
        }

        var filtered = data.Volumes.AsEnumerable();

        // Apply filters
        if (!string.IsNullOrEmpty(workloadFilter))
        {
            filtered = filtered.Where(v => 
                v.AiAnalysis?.SuggestedWorkloadId == workloadFilter ||
                v.UserAnnotations?.ConfirmedWorkloadId == workloadFilter);
        }

        if (!string.IsNullOrEmpty(statusFilter))
        {
            filtered = filtered.Where(v => 
                v.UserAnnotations?.MigrationStatus?.ToString() == statusFilter);
        }

        if (confidenceMin.HasValue)
        {
            filtered = filtered.Where(v => 
                v.AiAnalysis?.ConfidenceScore >= confidenceMin.Value);
        }

        var totalCount = filtered.Count();
        var paged = filtered.Skip((page - 1) * pageSize).Take(pageSize);

        var volumes = paged.Select(v => new VolumeWithAnalysis
        {
            VolumeId = v.VolumeId,
            VolumeData = v.Volume,
            AiAnalysis = v.AiAnalysis,
            UserAnnotations = v.UserAnnotations,
            // List view does not currently need full history; omit for payload size.
            AnnotationHistory = null
        }).ToList();

        return new VolumeListResponse
        {
            Volumes = volumes,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task UpdateVolumeAnnotationsAsync(
        string discoveryJobId,
        string volumeId,
        UserAnnotations annotations,
        string userId)
    {
        var data = await GetDiscoveryDataAsync(discoveryJobId);
        if (data == null)
            throw new InvalidOperationException($"Discovery job {discoveryJobId} not found");

        var volume = data.Volumes.FirstOrDefault(v => v.VolumeId == volumeId);
        if (volume == null)
            throw new InvalidOperationException($"Volume {volumeId} not found");

        // Populate ConfirmedWorkloadName if ConfirmedWorkloadId is provided
        if (!string.IsNullOrEmpty(annotations.ConfirmedWorkloadId))
        {
            var profile = await _workloadProfileService.GetProfileAsync(annotations.ConfirmedWorkloadId);
            if (profile != null)
            {
                annotations.ConfirmedWorkloadName = profile.Name;
            }
        }

        annotations.ReviewedBy = userId;
        annotations.ReviewedAt = DateTime.UtcNow;
        volume.UserAnnotations = annotations;

        // Append history entry
        volume.AnnotationHistory ??= new List<AnnotationHistoryEntry>();
        volume.AnnotationHistory.Add(new AnnotationHistoryEntry
        {
            Timestamp = annotations.ReviewedAt ?? DateTime.UtcNow,
            UserId = userId,
            ConfirmedWorkloadId = annotations.ConfirmedWorkloadId,
            ConfirmedWorkloadName = annotations.ConfirmedWorkloadName,
            MigrationStatus = annotations.MigrationStatus,
            CustomTags = annotations.CustomTags,
            Notes = annotations.Notes,
            Source = "Update"
        });

        await SaveDiscoveryDataAsync(data);
        
        _logger.LogInformation("Updated annotations for volume {VolumeId} in job {JobId}", volumeId, discoveryJobId);
    }

    public async Task BulkUpdateAnnotationsAsync(
        string discoveryJobId,
        string[] volumeIds,
        UpdateAnnotationsRequest annotations,
        string userId)
    {
        var data = await GetDiscoveryDataAsync(discoveryJobId);
        if (data == null)
            throw new InvalidOperationException($"Discovery job {discoveryJobId} not found");

        foreach (var volumeId in volumeIds)
        {
            var volume = data.Volumes.FirstOrDefault(v => v.VolumeId == volumeId);
            if (volume != null)
            {
                volume.UserAnnotations ??= new UserAnnotations();
                
                if (annotations.ConfirmedWorkloadId != null)
                    volume.UserAnnotations.ConfirmedWorkloadId = annotations.ConfirmedWorkloadId;
                
                if (annotations.CustomTags != null)
                    volume.UserAnnotations.CustomTags = annotations.CustomTags;
                
                if (annotations.MigrationStatus.HasValue)
                    volume.UserAnnotations.MigrationStatus = annotations.MigrationStatus;
                
                if (annotations.Notes != null)
                    volume.UserAnnotations.Notes = annotations.Notes;
                
                if (annotations.TargetCapacityGiB.HasValue)
                    volume.UserAnnotations.TargetCapacityGiB = annotations.TargetCapacityGiB;
                
                if (annotations.TargetThroughputMiBps.HasValue)
                    volume.UserAnnotations.TargetThroughputMiBps = annotations.TargetThroughputMiBps;
                
                volume.UserAnnotations.ReviewedBy = userId;
                volume.UserAnnotations.ReviewedAt = DateTime.UtcNow;

                // Append history entry per volume
                volume.AnnotationHistory ??= new List<AnnotationHistoryEntry>();
                volume.AnnotationHistory.Add(new AnnotationHistoryEntry
                {
                    Timestamp = volume.UserAnnotations.ReviewedAt ?? DateTime.UtcNow,
                    UserId = userId,
                    ConfirmedWorkloadId = volume.UserAnnotations.ConfirmedWorkloadId,
                    ConfirmedWorkloadName = volume.UserAnnotations.ConfirmedWorkloadName,
                    MigrationStatus = volume.UserAnnotations.MigrationStatus,
                    CustomTags = volume.UserAnnotations.CustomTags,
                    Notes = volume.UserAnnotations.Notes,
                    Source = "BulkUpdate"
                });
            }
        }

        await SaveDiscoveryDataAsync(data);
        
        _logger.LogInformation("Bulk updated {Count} volumes in job {JobId}", volumeIds.Length, discoveryJobId);
    }

    public async Task<string> ExportVolumesAsync(string discoveryJobId, string format)
    {
        var data = await GetDiscoveryDataAsync(discoveryJobId);
        if (data == null)
            throw new InvalidOperationException($"Discovery job {discoveryJobId} not found");

        return format.ToLowerInvariant() switch
        {
            "json" => ExportAsJson(data),
            "csv" => ExportAsCsv(data),
            _ => throw new ArgumentException($"Unsupported format: {format}")
        };
    }

    private string ExportAsJson(DiscoveryData data)
    {
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }

    private string ExportAsCsv(DiscoveryData data)
    {
        var sb = new StringBuilder();
        
        // Header
        sb.AppendLine("Volume Name,Storage Account,Resource Group,Size (GiB),Used Capacity,Access Tier," +
                     "AI Workload,AI Confidence,User Workload,Migration Status,Custom Tags,Notes");

        // Data rows
        foreach (var volume in data.Volumes)
        {
            sb.Append(CsvEscape(volume.Volume.ShareName ?? ""));
            sb.Append(',');
            sb.Append(CsvEscape(volume.Volume.StorageAccountName ?? ""));
            sb.Append(',');
            sb.Append(CsvEscape(volume.Volume.ResourceGroup ?? ""));
            sb.Append(',');
            sb.Append(volume.Volume.ShareQuotaGiB ?? 0);
            sb.Append(',');
            sb.Append(FormatBytes(volume.Volume.ShareUsageBytes ?? 0));
            sb.Append(',');
            sb.Append(CsvEscape(volume.Volume.AccessTier ?? ""));
            sb.Append(',');
            sb.Append(CsvEscape(volume.AiAnalysis?.SuggestedWorkloadName ?? ""));
            sb.Append(',');
            sb.Append(volume.AiAnalysis?.ConfidenceScore.ToString("P0") ?? "");
            sb.Append(',');
            sb.Append(CsvEscape(volume.UserAnnotations?.ConfirmedWorkloadName ?? ""));
            sb.Append(',');
            sb.Append(CsvEscape(volume.UserAnnotations?.MigrationStatus?.ToString() ?? ""));
            sb.Append(',');
            sb.Append(CsvEscape(volume.UserAnnotations?.CustomTags != null ? 
                string.Join("; ", volume.UserAnnotations.CustomTags) : ""));
            sb.Append(',');
            sb.Append(CsvEscape(volume.UserAnnotations?.Notes ?? ""));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
            
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        
        return value;
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
}
