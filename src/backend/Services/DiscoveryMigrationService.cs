using Azure.Storage.Blobs;
using AzFilesOptimizer.Backend.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AzFilesOptimizer.Backend.Services;

public class DiscoveryMigrationService
{
    private readonly ILogger _logger;
    private readonly DiscoveredResourceStorageService _resourceStorage;
    private readonly BlobContainerClient _blobContainer;

    public DiscoveryMigrationService(string connectionString, ILogger logger)
    {
        _logger = logger;
        _resourceStorage = new DiscoveredResourceStorageService(connectionString);
        
        var blobServiceClient = new BlobServiceClient(connectionString);
        _blobContainer = blobServiceClient.GetBlobContainerClient("discovery-data");
        _blobContainer.CreateIfNotExists();
    }

    public async Task<bool> MigrateJobVolumesToBlobAsync(string discoveryJobId)
    {
        try
        {
            _logger.LogInformation("Migrating volumes for job {JobId} from Tables to Blob storage", discoveryJobId);

            // Load existing discovery data (if any) so we can preserve AI analysis and annotations
            var blobClient = _blobContainer.GetBlobClient($"jobs/{discoveryJobId}/discovered-volumes.json");
            DiscoveryData? existingData = null;
            try
            {
                if (await blobClient.ExistsAsync())
                {
                    _logger.LogInformation("Existing discovery data found for job {JobId}; merging with latest discovery results.", discoveryJobId);
                    var existingContent = await blobClient.DownloadContentAsync();
                    var existingJson = existingContent.Value.Content.ToString();
                    var existingOptions = new JsonSerializerOptions
                    {
                        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                    };
                    existingData = JsonSerializer.Deserialize<DiscoveryData>(existingJson, existingOptions);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load existing discovery data for job {JobId}; proceeding with fresh migration.", discoveryJobId);
            }

            // Get all resource types from Azure Tables
            var shares = await _resourceStorage.GetSharesByJobIdAsync(discoveryJobId);
            var anfVolumes = await _resourceStorage.GetVolumesByJobIdAsync(discoveryJobId);
            var disks = await _resourceStorage.GetDisksByJobIdAsync(discoveryJobId);

            if (shares.Count == 0 && anfVolumes.Count == 0 && disks.Count == 0)
            {
                _logger.LogWarning("No resources found in Tables for job {JobId}", discoveryJobId);

                // If we already had discovery data, keep it as-is
                if (existingData != null)
                {
                    var keepOptions = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                    };
                    var keepJson = JsonSerializer.Serialize(existingData, keepOptions);
                    await blobClient.UploadAsync(BinaryData.FromString(keepJson), overwrite: true);
                    return true;
                }

                return false;
            }

            _logger.LogInformation("Found {ShareCount} shares, {AnfCount} ANF volumes, {DiskCount} disks to migrate", 
                shares.Count, anfVolumes.Count, disks.Count);

            // Convert latest discovery results to unified volume format
            var newVolumes = new List<DiscoveredVolumeWithAnalysis>();

            // Add Azure Files shares
            newVolumes.AddRange(shares.Select(share =>
            {
                var volume = new DiscoveredVolumeWithAnalysis
                {
                    VolumeType = "AzureFiles",
                    VolumeData = share,
                    AiAnalysis = null,
                    UserAnnotations = new UserAnnotations(),
                    AnnotationHistory = new List<AnnotationHistoryEntry>()
                };
                volume.ComputeVolumeIdFromResource();
                return volume;
            }));

            // Add ANF volumes
            newVolumes.AddRange(anfVolumes.Select(volume =>
            {
                var vol = new DiscoveredVolumeWithAnalysis
                {
                    VolumeType = "ANF",
                    VolumeData = volume,
                    AiAnalysis = null,
                    UserAnnotations = new UserAnnotations(),
                    AnnotationHistory = new List<AnnotationHistoryEntry>()
                };
                vol.ComputeVolumeIdFromResource();
                return vol;
            }));

            // Add Managed Disks
            newVolumes.AddRange(disks.Select(disk =>
            {
                var vol = new DiscoveredVolumeWithAnalysis
                {
                    VolumeType = "ManagedDisk",
                    VolumeData = disk,
                    AiAnalysis = null,
                    UserAnnotations = new UserAnnotations(),
                    AnnotationHistory = new List<AnnotationHistoryEntry>()
                };
                vol.ComputeVolumeIdFromResource();
                return vol;
            }));

            // If existing discovery data is present, preserve AI analysis and annotations per volume
            if (existingData != null && existingData.Volumes != null && existingData.Volumes.Count > 0)
            {
                var existingById = existingData.Volumes
                    .Where(v => !string.IsNullOrEmpty(v.VolumeId))
                    .ToDictionary(v => v.VolumeId, v => v);

                foreach (var vol in newVolumes)
                {
                    if (existingById.TryGetValue(vol.VolumeId, out var existingVol))
                    {
                        vol.AiAnalysis = existingVol.AiAnalysis;
                        vol.UserAnnotations = existingVol.UserAnnotations ?? new UserAnnotations();
                        vol.AnnotationHistory = existingVol.AnnotationHistory ?? new List<AnnotationHistoryEntry>();
                    }
                }
            }

            var discoveryData = new DiscoveryData
            {
                JobId = discoveryJobId,
                DiscoveredAt = DateTime.UtcNow,
                Volumes = newVolumes
            };

            // Save to blob storage (always overwrite with latest discovery + preserved annotations)
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            var json = JsonSerializer.Serialize(discoveryData, options);
            await blobClient.UploadAsync(BinaryData.FromString(json), overwrite: true);

            _logger.LogInformation("Successfully migrated {TotalCount} resources ({ShareCount} shares, {AnfCount} ANF volumes, {DiskCount} disks) for job {JobId}", 
                newVolumes.Count, shares.Count, anfVolumes.Count, disks.Count, discoveryJobId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error migrating volumes for job {JobId}", discoveryJobId);
            throw;
        }
    }
}
