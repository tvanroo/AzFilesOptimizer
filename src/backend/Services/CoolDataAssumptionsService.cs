using Azure.Data.Tables;
using AzFilesOptimizer.Backend.Models;
using Microsoft.Extensions.Logging;

namespace AzFilesOptimizer.Backend.Services;

/// <summary>
/// Manages cool data assumptions configuration with 3-tier hierarchy: Global → Job → Volume
/// </summary>
public class CoolDataAssumptionsService
{
    private readonly TableClient _globalAssumptionsTable;
    private readonly TableClient _jobsTable;
    private readonly DiscoveredResourceStorageService _volumeStorage;
    private readonly ILogger<CoolDataAssumptionsService> _logger;
    
    // Cache for global assumptions (refreshed every 5 minutes)
    private CoolDataAssumptions? _cachedGlobalAssumptions;
    private DateTime? _cacheExpiry;
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(5);
    
    public CoolDataAssumptionsService(
        TableServiceClient tableServiceClient,
        DiscoveredResourceStorageService volumeStorage,
        ILogger<CoolDataAssumptionsService> logger)
    {
        _globalAssumptionsTable = tableServiceClient.GetTableClient("CoolDataGlobalAssumptions");
        _globalAssumptionsTable.CreateIfNotExists();
        
        _jobsTable = tableServiceClient.GetTableClient("DiscoveryJobStatus");
        _volumeStorage = volumeStorage;
        _logger = logger;
        
        // Seed global defaults on initialization
        _ = EnsureGlobalDefaultsExistAsync();
    }
    
    /// <summary>
    /// Get global default assumptions
    /// </summary>
    public async Task<CoolDataAssumptions> GetGlobalAssumptionsAsync()
    {
        // Check cache
        if (_cachedGlobalAssumptions != null && _cacheExpiry.HasValue && DateTime.UtcNow < _cacheExpiry.Value)
        {
            return _cachedGlobalAssumptions;
        }
        
        try
        {
            var entity = await _globalAssumptionsTable.GetEntityAsync<TableEntity>("global", "current");
            
            var assumptions = new CoolDataAssumptions
            {
                CoolDataPercentage = entity.Value.GetDouble("CoolDataPercentage") ?? 80.0,
                CoolDataRetrievalPercentage = entity.Value.GetDouble("CoolDataRetrievalPercentage") ?? 15.0,
                Source = AssumptionSource.Global,
                LastModifiedAt = entity.Value.GetDateTime("LastModifiedAt") ?? DateTime.UtcNow,
                LastModifiedBy = entity.Value.GetString("LastModifiedBy")
            };
            
            // Update cache
            _cachedGlobalAssumptions = assumptions;
            _cacheExpiry = DateTime.UtcNow.Add(_cacheLifetime);
            
            return assumptions;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Not found - create defaults
            return await CreateGlobalDefaultsAsync();
        }
    }
    
    /// <summary>
    /// Set global default assumptions
    /// </summary>
    public async Task SetGlobalAssumptionsAsync(CoolDataAssumptions assumptions, string? modifiedBy = null)
    {
        var errors = assumptions.Validate();
        if (errors.Any())
        {
            throw new ArgumentException($"Invalid assumptions: {string.Join(", ", errors)}");
        }
        
        var entity = new TableEntity("global", "current")
        {
            { "CoolDataPercentage", assumptions.CoolDataPercentage },
            { "CoolDataRetrievalPercentage", assumptions.CoolDataRetrievalPercentage },
            { "LastModifiedAt", DateTime.UtcNow },
            { "LastModifiedBy", modifiedBy ?? "System" }
        };
        
        await _globalAssumptionsTable.UpsertEntityAsync(entity);
        
        // Invalidate cache
        _cachedGlobalAssumptions = null;
        _cacheExpiry = null;
        
        _logger.LogInformation(
            "Global cool data assumptions updated: {CoolPercent}% cool data, {RetrievalPercent}% retrieval",
            assumptions.CoolDataPercentage, assumptions.CoolDataRetrievalPercentage);
    }
    
    /// <summary>
    /// Get assumptions for a specific job (returns job override or global)
    /// </summary>
    public async Task<CoolDataAssumptions> GetJobAssumptionsAsync(string jobId)
    {
        try
        {
            var jobEntity = await _jobsTable.GetEntityAsync<TableEntity>("DiscoveryJob", jobId);
            
            var coolPercent = jobEntity.Value.GetDouble("CoolDataPercentage");
            var retrievalPercent = jobEntity.Value.GetDouble("CoolDataRetrievalPercentage");
            
            // If job has overrides, return them
            if (coolPercent.HasValue && retrievalPercent.HasValue)
            {
                return new CoolDataAssumptions
                {
                    CoolDataPercentage = coolPercent.Value,
                    CoolDataRetrievalPercentage = retrievalPercent.Value,
                    Source = AssumptionSource.Job,
                    LastModifiedAt = jobEntity.Value.GetDateTime("CoolAssumptionsModifiedAt") ?? DateTime.UtcNow,
                    LastModifiedBy = "User"
                };
            }
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Job {JobId} not found when getting assumptions", jobId);
        }
        
        // No job-level override, return global
        return await GetGlobalAssumptionsAsync();
    }
    
    /// <summary>
    /// Set assumptions for a specific job
    /// </summary>
    public async Task SetJobAssumptionsAsync(string jobId, CoolDataAssumptions assumptions, string? modifiedBy = null)
    {
        var errors = assumptions.Validate();
        if (errors.Any())
        {
            throw new ArgumentException($"Invalid assumptions: {string.Join(", ", errors)}");
        }
        
        try
        {
            // Get existing job entity
            var jobEntity = await _jobsTable.GetEntityAsync<TableEntity>("DiscoveryJob", jobId);
            
            // Update assumptions fields
            jobEntity.Value["CoolDataPercentage"] = assumptions.CoolDataPercentage;
            jobEntity.Value["CoolDataRetrievalPercentage"] = assumptions.CoolDataRetrievalPercentage;
            jobEntity.Value["CoolAssumptionsModifiedAt"] = DateTime.UtcNow;
            
            await _jobsTable.UpdateEntityAsync(jobEntity.Value, jobEntity.Value.ETag);
            
            _logger.LogInformation(
                "Job {JobId} cool data assumptions updated: {CoolPercent}% cool data, {RetrievalPercent}% retrieval",
                jobId, assumptions.CoolDataPercentage, assumptions.CoolDataRetrievalPercentage);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException($"Job {jobId} not found", ex);
        }
    }
    
    /// <summary>
    /// Clear job-level assumptions (revert to global)
    /// </summary>
    public async Task ClearJobAssumptionsAsync(string jobId)
    {
        try
        {
            var jobEntity = await _jobsTable.GetEntityAsync<TableEntity>("DiscoveryJob", jobId);
            
            // Remove assumption fields
            jobEntity.Value.Remove("CoolDataPercentage");
            jobEntity.Value.Remove("CoolDataRetrievalPercentage");
            jobEntity.Value.Remove("CoolAssumptionsModifiedAt");
            
            await _jobsTable.UpdateEntityAsync(jobEntity.Value, jobEntity.Value.ETag);
            
            _logger.LogInformation("Job {JobId} cool data assumptions cleared (reverted to global)", jobId);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException($"Job {jobId} not found", ex);
        }
    }
    
    /// <summary>
    /// Get assumptions for a specific volume (returns volume > job > global)
    /// </summary>
    public async Task<CoolDataAssumptions> GetVolumeAssumptionsAsync(string jobId, string volumeResourceId)
    {
        // Try to get volume from storage
        var volumes = await _volumeStorage.GetVolumesByJobIdAsync(jobId);
        var volume = volumes.FirstOrDefault(v => v.ResourceId == volumeResourceId);
        
        if (volume != null && 
            volume.CoolDataPercentageOverride.HasValue && 
            volume.CoolDataRetrievalPercentageOverride.HasValue)
        {
            return new CoolDataAssumptions
            {
                CoolDataPercentage = volume.CoolDataPercentageOverride.Value,
                CoolDataRetrievalPercentage = volume.CoolDataRetrievalPercentageOverride.Value,
                Source = AssumptionSource.Volume,
                LastModifiedAt = volume.CoolAssumptionsModifiedAt ?? DateTime.UtcNow,
                LastModifiedBy = "User"
            };
        }
        
        // No volume-level override, return job-level (or global)
        return await GetJobAssumptionsAsync(jobId);
    }
    
    /// <summary>
    /// Resolve assumptions for a volume using the hierarchy (Volume > Job > Global)
    /// </summary>
    public async Task<CoolDataAssumptions> ResolveAssumptionsAsync(string? jobId = null, string? volumeResourceId = null)
    {
        // If volume specified, try volume-level first
        if (!string.IsNullOrEmpty(jobId) && !string.IsNullOrEmpty(volumeResourceId))
        {
            return await GetVolumeAssumptionsAsync(jobId, volumeResourceId);
        }
        
        // If job specified, try job-level
        if (!string.IsNullOrEmpty(jobId))
        {
            return await GetJobAssumptionsAsync(jobId);
        }
        
        // Fallback to global
        return await GetGlobalAssumptionsAsync();
    }
    
    /// <summary>
    /// Ensure global defaults exist (called on initialization)
    /// </summary>
    private async Task EnsureGlobalDefaultsExistAsync()
    {
        try
        {
            await _globalAssumptionsTable.GetEntityAsync<TableEntity>("global", "current");
            _logger.LogInformation("Global cool data assumptions already exist");
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("Creating default global cool data assumptions: 80% cool data, 15% retrieval");
            await CreateGlobalDefaultsAsync();
        }
    }
    
    /// <summary>
    /// Create global defaults (80% cool data, 15% retrieval)
    /// </summary>
    private async Task<CoolDataAssumptions> CreateGlobalDefaultsAsync()
    {
        var defaults = CoolDataAssumptions.CreateGlobalDefaults();
        await SetGlobalAssumptionsAsync(defaults, "System");
        return defaults;
    }
}
