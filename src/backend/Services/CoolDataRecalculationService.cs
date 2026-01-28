using AzFilesOptimizer.Backend.Models;
using Microsoft.Extensions.Logging;

namespace AzFilesOptimizer.Backend.Services;

/// <summary>
/// Handles recalculation of costs when cool data assumptions are changed
/// </summary>
public class CoolDataRecalculationService
{
    private readonly IDiscoveredResourceStorageService _volumeStorage;
    private readonly CostCollectionService _costService;
    private readonly VolumeCostAnalysisStorageService _costStorage;
    private readonly ILogger<CoolDataRecalculationService> _logger;
    
    public CoolDataRecalculationService(
        IDiscoveredResourceStorageService volumeStorage,
        CostCollectionService costService,
        VolumeCostAnalysisStorageService costStorage,
        ILogger<CoolDataRecalculationService> logger)
    {
        _volumeStorage = volumeStorage;
        _costService = costService;
        _costStorage = costStorage;
        _logger = logger;
    }
    
    /// <summary>
    /// Recalculate costs for all cool volumes in a job (except those with volume-level overrides)
    /// Triggered when job-level assumptions are changed
    /// </summary>
    public async Task<int> RecalculateJobAsync(string jobId)
    {
        _logger.LogInformation("Recalculating costs for all cool volumes in job {JobId}", jobId);
        
        // Load all volumes for this job
        var volumes = await _volumeStorage.GetVolumesByJobIdAsync(jobId);
        
        // Filter to only cool access volumes
        var coolVolumes = volumes.Where(v => v.CoolAccessEnabled == true).ToList();
        
        if (coolVolumes.Count == 0)
        {
            _logger.LogInformation("No cool access volumes found in job {JobId}", jobId);
            return 0;
        }
        
        // Filter out volumes with volume-level overrides (they should not be affected by job-level changes)
        var volumesToRecalculate = coolVolumes.Where(v => 
            !v.CoolDataPercentageOverride.HasValue || 
            !v.CoolDataRetrievalPercentageOverride.HasValue).ToList();
        
        _logger.LogInformation(
            "Found {TotalCool} cool volumes, {ToRecalculate} without volume-level overrides will be recalculated",
            coolVolumes.Count, volumesToRecalculate.Count);
        
        int recalculated = 0;
        foreach (var volume in volumesToRecalculate)
        {
            try
            {
                await RecalculateVolumeAsync(jobId, volume.ResourceId);
                recalculated++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to recalculate volume {Volume}", volume.VolumeName);
            }
        }
        
        _logger.LogInformation("Recalculated {Count} volumes for job {JobId}", recalculated, jobId);
        return recalculated;
    }
    
    /// <summary>
    /// Recalculate cost for a specific volume
    /// Triggered when volume-level assumptions are changed
    /// </summary>
    public async Task RecalculateVolumeAsync(string jobId, string volumeResourceId)
    {
        _logger.LogInformation("Recalculating cost for volume {VolumeId} in job {JobId}", volumeResourceId, jobId);
        
        // Load volume
        var volumes = await _volumeStorage.GetVolumesByJobIdAsync(jobId);
        var volume = volumes.FirstOrDefault(v => v.ResourceId == volumeResourceId);
        
        if (volume == null)
        {
            throw new InvalidOperationException($"Volume {volumeResourceId} not found in job {jobId}");
        }
        
        // Recalculate cost using cost collection service
        var periodStart = DateTime.UtcNow.AddDays(-30);
        var periodEnd = DateTime.UtcNow;
        
        var costAnalysis = await _costService.GetAnfVolumeCostAsync(volume, periodStart, periodEnd, jobId);
        
        // Update stored cost analysis
        await _costStorage.SaveCostAnalysisAsync(jobId, costAnalysis);
        
        _logger.LogInformation(
            "Recalculated cost for volume {Volume}: ${Cost:F2}/month (HasMetrics: {HasMetrics})",
            volume.VolumeName, costAnalysis.TotalCostForPeriod, costAnalysis.HasMetrics);
    }
}
