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

            // Check if blob already exists
            var blobClient = _blobContainer.GetBlobClient($"jobs/{discoveryJobId}/discovered-volumes.json");
            if (await blobClient.ExistsAsync())
            {
                _logger.LogInformation("Discovery data already exists for job {JobId}", discoveryJobId);
                return true;
            }

            // Get all resource types from Azure Tables
            var shares = await _resourceStorage.GetSharesByJobIdAsync(discoveryJobId);
            var anfVolumes = await _resourceStorage.GetVolumesByJobIdAsync(discoveryJobId);
            var disks = await _resourceStorage.GetDisksByJobIdAsync(discoveryJobId);

            if (shares.Count == 0 && anfVolumes.Count == 0 && disks.Count == 0)
            {
                _logger.LogWarning("No resources found in Tables for job {JobId}", discoveryJobId);
                return false;
            }

            _logger.LogInformation("Found {ShareCount} shares, {AnfCount} ANF volumes, {DiskCount} disks to migrate", 
                shares.Count, anfVolumes.Count, disks.Count);

            // Convert all to unified volume format
            var volumes = new List<DiscoveredVolumeWithAnalysis>();
            
            // Add Azure Files shares
            volumes.AddRange(shares.Select(share => new DiscoveredVolumeWithAnalysis
            {
                VolumeType = "AzureFiles",
                VolumeData = share,
                AiAnalysis = null,
                UserAnnotations = new UserAnnotations(),
                AnnotationHistory = new List<AnnotationHistoryEntry>()
            }));
            
            // Add ANF volumes
            volumes.AddRange(anfVolumes.Select(volume => new DiscoveredVolumeWithAnalysis
            {
                VolumeType = "ANF",
                VolumeData = volume,
                AiAnalysis = null,
                UserAnnotations = new UserAnnotations(),
                AnnotationHistory = new List<AnnotationHistoryEntry>()
            }));
            
            // Add Managed Disks
            volumes.AddRange(disks.Select(disk => new DiscoveredVolumeWithAnalysis
            {
                VolumeType = "ManagedDisk",
                VolumeData = disk,
                AiAnalysis = null,
                UserAnnotations = new UserAnnotations(),
                AnnotationHistory = new List<AnnotationHistoryEntry>()
            }));

            var discoveryData = new DiscoveryData
            {
                JobId = discoveryJobId,
                DiscoveredAt = DateTime.UtcNow,
                Volumes = volumes
            };

            // Save to blob storage
            var json = JsonSerializer.Serialize(discoveryData, new JsonSerializerOptions { WriteIndented = true });
            await blobClient.UploadAsync(BinaryData.FromString(json), overwrite: false);

            _logger.LogInformation("Successfully migrated {TotalCount} resources ({ShareCount} shares, {AnfCount} ANF volumes, {DiskCount} disks) for job {JobId}", 
                volumes.Count, shares.Count, anfVolumes.Count, disks.Count, discoveryJobId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error migrating volumes for job {JobId}", discoveryJobId);
            throw;
        }
    }
}
