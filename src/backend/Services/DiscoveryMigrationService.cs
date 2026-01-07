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

            // Get Azure Files shares from Azure Tables
            var shares = await _resourceStorage.GetSharesByJobIdAsync(discoveryJobId);
            if (shares.Count == 0)
            {
                _logger.LogWarning("No shares found in Tables for job {JobId}", discoveryJobId);
                return false;
            }

            _logger.LogInformation("Found {Count} shares to migrate", shares.Count);

            // Convert to DiscoveryData format
            var discoveryData = new DiscoveryData
            {
                JobId = discoveryJobId,
                DiscoveredAt = DateTime.UtcNow,
                Volumes = shares.Select(share => new DiscoveredVolumeWithAnalysis
                {
                    Volume = share,
                    AiAnalysis = null, // No analysis yet
                    UserAnnotations = new UserAnnotations() // Initialize empty
                }).ToList()
            };

            // Save to blob storage
            var json = JsonSerializer.Serialize(discoveryData, new JsonSerializerOptions { WriteIndented = true });
            await blobClient.UploadAsync(BinaryData.FromString(json), overwrite: false);

            _logger.LogInformation("Successfully migrated {Count} shares for job {JobId}", shares.Count, discoveryJobId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error migrating volumes for job {JobId}", discoveryJobId);
            throw;
        }
    }
}
