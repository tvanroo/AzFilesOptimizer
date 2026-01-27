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
    private readonly DiscoveredResourceStorageService _resourceStorage;

    public VolumeAnnotationService(string connectionString, ILogger logger)
    {
        _logger = logger;
        var blobServiceClient = new BlobServiceClient(connectionString);
        _blobContainer = blobServiceClient.GetBlobContainerClient("discovery-data");
        _blobContainer.CreateIfNotExists();
        _workloadProfileService = new WorkloadProfileService(connectionString, logger);
        _resourceStorage = new DiscoveredResourceStorageService(connectionString);
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
            var options = new JsonSerializerOptions
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            var data = JsonSerializer.Deserialize<DiscoveryData>(json, options);

            if (data != null)
            {
                // When loaded from Blob, VolumeData may be JsonElement. Rehydrate into strongly-typed
                // models so downstream 'is DiscoveredAnfVolume' / 'is DiscoveredManagedDisk' checks work.
                RehydrateVolumeData(data);

                var needsSave = false;
                foreach (var volume in data.Volumes)
                {
                    if (string.IsNullOrEmpty(volume.VolumeId))
                    {
                        volume.ComputeVolumeIdFromResource();
                        needsSave = true;
                    }
                }

                if (needsSave)
                {
                    await SaveDiscoveryDataAsync(data);
                    _logger.LogInformation("Updated VolumeIds for job {JobId}", discoveryJobId);
                }
            }

            return data;
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

        // Preload latest cost analyses for this job so we can attach summaries per volume.
        var costAnalyses = await _resourceStorage.GetVolumeCostsByJobIdAsync(discoveryJobId);
        var costByVolumeId = costAnalyses
            .Where(c => !string.IsNullOrWhiteSpace(c.VolumeId))
            .GroupBy(c => c.VolumeId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(c => c.AnalysisTimestamp).First());

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

        var volumes = paged.Select(v =>
        {
            // Map discovery volume to API DTO
            var dto = new VolumeWithAnalysis
            {
                VolumeId = v.VolumeId,
                VolumeType = v.VolumeType,
                VolumeData = v.VolumeData,  // Support all volume types: Azure Files, ANF, Managed Disk
                AiAnalysis = v.AiAnalysis,
                UserAnnotations = v.UserAnnotations,
                // List view does not currently need full history; omit for payload size.
                AnnotationHistory = null
            };

            // Attach cost summary if available (matching by normalized VolumeId)
            if (!string.IsNullOrWhiteSpace(v.VolumeId) && costByVolumeId.TryGetValue(v.VolumeId, out var cost))
            {
                dto.CostSummary = new CostSummary
                {
                    TotalCost30Days = cost.TotalCostForPeriod,
                    DailyAverage = cost.TotalCostPerDay,
                    Currency = cost.CostComponents.FirstOrDefault()?.Currency ?? "USD",
                    IsActual = cost.CostComponents.Count > 0 && cost.CostComponents.All(c => !c.IsEstimated),
                    PeriodStart = cost.PeriodStart,
                    PeriodEnd = cost.PeriodEnd
                };
                // Include full detailed cost analysis with all debugging information
                dto.DetailedCostAnalysis = cost;
                dto.CostStatus = "Completed";
            }
            else
            {
                dto.CostStatus = "Pending";
            }

            // Derive required capacity/throughput from AI sizing when available,
            // otherwise fall back to discovered properties per volume type.
            var sizing = v.AiAnalysis?.CapacitySizing;
            if (sizing != null && sizing.HasSufficientData)
            {
                dto.RequiredCapacityGiB = sizing.RecommendedCapacityGiB;
                dto.RequiredThroughputMiBps = sizing.RecommendedThroughputMiBps;
            }

            const double bufferFactor = 1.05; // 5% buffer above observed/estimated used capacity

            // Derive current throughput/IOPS and, when sizing is missing, approximate required values
            switch (v.VolumeType)
            {
                case "AzureFiles":
                    if (v.VolumeData is DiscoveredAzureFileShare share)
                    {
                        // --- Required Capacity Calculation ---
                        // Base on current usage + buffer.
                        if (dto.RequiredCapacityGiB == null)
                        {
                            if (share.ShareUsageBytes.HasValue && share.ShareUsageBytes.Value > 0)
                            {
                                var usedGiB = share.ShareUsageBytes.Value / (1024.0 * 1024.0 * 1024.0);
                                dto.RequiredCapacityGiB = usedGiB * bufferFactor;
                            }
                            else
                            {
                                // For zero/unknown usage, use minimum 1 GB + buffer instead of full quota
                                var minimumUsageGiB = 1.0; // Assume minimum 1 GB usage
                                dto.RequiredCapacityGiB = minimumUsageGiB * bufferFactor; // 1.05 GB minimum
                            }
                        }

                        // --- Throughput Calculation with Proportional Allocation ---
                        if (dto.RequiredThroughputMiBps == null)
                        {
                            if (!string.IsNullOrEmpty(share.HistoricalMetricsSummary))
                            {
                                try
                                {
                                    var metrics = System.Text.Json.JsonDocument.Parse(share.HistoricalMetricsSummary);
                                    if (metrics.RootElement.TryGetProperty("Ingress", out var ingressMetric) &&
                                        ingressMetric.TryGetProperty("max", out var maxIngress) &&
                                        maxIngress.TryGetDouble(out var maxIngressBytes) &&
                                        metrics.RootElement.TryGetProperty("Egress", out var egressMetric) &&
                                        egressMetric.TryGetProperty("max", out var maxEgress) &&
                                        maxEgress.TryGetDouble(out var maxEgressBytes))
                                    {
                                        var totalPeakBytes = maxIngressBytes + maxEgressBytes;
                                        var totalPeakMiBps = (totalPeakBytes / (1024.0 * 1024.0)) / 3600.0;
                                        
                                        // For Standard storage, this is an account-level metric. We need to allocate it.
                                        var accountShares = data.Volumes
                                            .Where(vol => vol.VolumeType == "AzureFiles" && (vol.VolumeData as DiscoveredAzureFileShare)?.StorageAccountName == share.StorageAccountName)
                                            .Select(vol => vol.VolumeData as DiscoveredAzureFileShare)
                                            .Where(s => s != null)
                                            .ToList();

                                        var totalUsedBytes = accountShares.Sum(s => s?.ShareUsageBytes ?? 0);

                                        if (totalUsedBytes > 0)
                                        {
                                            var shareProportion = (double)(share.ShareUsageBytes ?? 0) / totalUsedBytes;
                                            dto.RequiredThroughputMiBps = (totalPeakMiBps * shareProportion) * bufferFactor;
                                            if (accountShares.Count > 1)
                                            {
                                                dto.ThroughputCalculationNote = "Based on peak Ingress+Egress for the storage account, allocated proportionally by this share's capacity.";
                                            }
                                            else
                                            {
                                                dto.ThroughputCalculationNote = "Based on peak Ingress+Egress for the storage account.";
                                            }
                                        }
                                        else
                                        {
                                            dto.RequiredThroughputMiBps = totalPeakMiBps * bufferFactor;
                                            dto.ThroughputCalculationNote = "Based on peak Ingress+Egress for the storage account.";
                                        }
                                    }
                                }
                                catch (JsonException ex)
                                {
                                    _logger.LogWarning(ex, "Failed to parse HistoricalMetricsSummary for throughput calculation on share {ShareName}", share.ShareName);
                                }
                            }
                        }
                        
                        // Current throughput: prefer provisioned, then estimated
                        double? currentThroughput = null;
                        if (share.ProvisionedBandwidthMiBps.HasValue)
                        {
                            currentThroughput = share.ProvisionedBandwidthMiBps.Value;
                        }
                        else if (share.EstimatedThroughputMiBps.HasValue)
                        {
                            currentThroughput = share.EstimatedThroughputMiBps.Value;
                        }
                        dto.CurrentThroughputMiBps = currentThroughput;
                        dto.RequiredThroughputMiBps ??= currentThroughput;

                        // Current IOPS: prefer provisioned, then estimated, or -1 for unmetered standard
                        double? currentIops = null;
                        if (share.ProvisionedIops.HasValue)
                        {
                            currentIops = share.ProvisionedIops.Value;
                        }
                        else if (share.EstimatedIops.HasValue)
                        {
                            currentIops = share.EstimatedIops.Value;
                        }
                        else if (!string.Equals(share.AccessTier, "Premium", StringComparison.OrdinalIgnoreCase))
                        {
                            currentIops = -1; // Unmetered for standard tiers
                        }
                        dto.CurrentIops = currentIops;
                    }
                    break;

                case "ANF":
                    if (v.VolumeData is DiscoveredAnfVolume anf)
                    {
                        // For ANF, check if we have actual usage data from metrics first
                        var provisionedGiB = anf.ProvisionedSizeBytes / (1024.0 * 1024.0 * 1024.0);
                        
                        // Try to get actual usage from historical metrics if available
                        double? actualUsedGiB = null;
                        if (!string.IsNullOrEmpty(anf.HistoricalMetricsSummary))
                        {
                            try
                            {
                                var metrics = System.Text.Json.JsonDocument.Parse(anf.HistoricalMetricsSummary);
                                if (metrics.RootElement.TryGetProperty("VolumeLogicalSize", out var logicalSizeMetric) &&
                                    logicalSizeMetric.TryGetProperty("max", out var maxLogicalSize) &&
                                    maxLogicalSize.TryGetDouble(out var maxLogicalSizeBytes))
                                {
                                    actualUsedGiB = maxLogicalSizeBytes / (1024.0 * 1024.0 * 1024.0);
                                }
                            }
                            catch (JsonException ex)
                            {
                                _logger.LogWarning(ex, "Failed to parse HistoricalMetricsSummary for capacity calculation on ANF volume {VolumeName}", anf.VolumeName);
                            }
                        }
                        
                        if (actualUsedGiB.HasValue && actualUsedGiB.Value > 0)
                        {
                            // Use actual usage + buffer
                            dto.RequiredCapacityGiB ??= actualUsedGiB.Value * bufferFactor;
                        }
                        else
                        {
                            // No usage data available, use minimum 1 GB + buffer instead of full provisioned
                            var minimumUsageGiB = 1.0; // Assume minimum 1 GB usage
                            dto.RequiredCapacityGiB ??= minimumUsageGiB * bufferFactor; // 1.05 GB minimum
                        }

                        // Current throughput: prefer actual, then configured max, then estimated, then service level heuristic
                        double? currentThroughput = null;
                        if (anf.ActualThroughputMibps.HasValue)
                        {
                            currentThroughput = anf.ActualThroughputMibps.Value;
                        }
                        else if (anf.ThroughputMibps.HasValue)
                        {
                            currentThroughput = anf.ThroughputMibps.Value;
                        }
                        else if (anf.EstimatedThroughputMiBps.HasValue)
                        {
                            currentThroughput = anf.EstimatedThroughputMiBps.Value;
                        }
                        else if (!string.IsNullOrEmpty(anf.ServiceLevel))
                        {
                            var level = anf.ServiceLevel.ToLowerInvariant();
                            if (level.Contains("ultra"))
                                currentThroughput = 128;
                            else if (level.Contains("premium"))
                                currentThroughput = 64;
                            else
                                currentThroughput = 16; // standard
                        }
                        dto.CurrentThroughputMiBps = currentThroughput;

                        // Current IOPS from estimated ANF IOPS when available
                        if (anf.EstimatedIops.HasValue)
                        {
                            dto.CurrentIops = anf.EstimatedIops.Value;
                        }

                        dto.RequiredThroughputMiBps ??= currentThroughput;
                    }
                    break;

                case "ManagedDisk":
                    if (v.VolumeData is DiscoveredManagedDisk disk)
                    {
                        // Capacity: prefer used+buffer, fall back to disk size when used is unknown,
                        // unless AI sizing already set it.
                        double? usedGiB = null;
                        if (disk.UsedBytes.HasValue && disk.UsedBytes.Value > 0)
                        {
                            usedGiB = disk.UsedBytes.Value / (1024.0 * 1024.0 * 1024.0);
                        }

                        if (usedGiB.HasValue)
                        {
                            var recommendedGiB = usedGiB.Value * bufferFactor;
                            var currentGiB = (double)disk.DiskSizeGB;
                            dto.RequiredCapacityGiB ??= Math.Max(recommendedGiB, currentGiB);
                        }
                        else
                        {
                            dto.RequiredCapacityGiB ??= disk.DiskSizeGB;
                        }

                        // Current throughput from estimated throughput, with SKU-based fallback
                        if (disk.EstimatedThroughputMiBps.HasValue)
                        {
                            dto.CurrentThroughputMiBps = disk.EstimatedThroughputMiBps.Value;
                        }
                        else if (!string.IsNullOrEmpty(disk.DiskSku))
                        {
                            var sku = disk.DiskSku.ToLowerInvariant();
                            if (sku.Contains("premium"))
                                dto.CurrentThroughputMiBps = 100;
                            else if (sku.Contains("standardssd"))
                                dto.CurrentThroughputMiBps = 60;
                            else
                                dto.CurrentThroughputMiBps = 30;
                        }

                        // Current IOPS from estimated disk IOPS
                        if (disk.EstimatedIops.HasValue)
                        {
                            dto.CurrentIops = disk.EstimatedIops.Value;
                        }

                        dto.RequiredThroughputMiBps ??= dto.CurrentThroughputMiBps;
                    }
                    break;
            }

            return dto;
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

    private static string GetVolumeResourceId(DiscoveredVolumeWithAnalysis volume)
    {
        return volume.VolumeType switch
        {
            "AzureFiles" => (volume.VolumeData as DiscoveredAzureFileShare)?.ResourceId ?? string.Empty,
            "ANF" => (volume.VolumeData as DiscoveredAnfVolume)?.ResourceId ?? string.Empty,
            "ManagedDisk" => (volume.VolumeData as DiscoveredManagedDisk)?.ResourceId ?? string.Empty,
            _ => string.Empty
        };
    }

    private string ExportAsCsv(DiscoveryData data)
    {
        var sb = new StringBuilder();
        
        // Header
        sb.AppendLine("Volume Type,Volume Name,Storage Account/Account,Resource Group,Size (GiB),Used Capacity," +
                     "AI Workload,AI Confidence,User Workload,Migration Status,Custom Tags,Notes");

        // Data rows - handle all three volume types
        foreach (var volume in data.Volumes)
        {
            string volumeType = volume.VolumeType;
            string volumeName = GetCsvVolumeName(volume);
            string storageAccount = GetCsvStorageAccount(volume);
            string resourceGroup = GetCsvResourceGroup(volume);
            string size = GetCsvSize(volume);
            string usedCapacity = GetCsvUsedCapacity(volume);
            
            sb.Append(CsvEscape(volumeType ?? ""));
            sb.Append(',');
            sb.Append(CsvEscape(volumeName));
            sb.Append(',');
            sb.Append(CsvEscape(storageAccount));
            sb.Append(',');
            sb.Append(CsvEscape(resourceGroup));
            sb.Append(',');
            sb.Append(size);
            sb.Append(',');
            sb.Append(usedCapacity);
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
    
    private string GetCsvVolumeName(DiscoveredVolumeWithAnalysis volume)
    {
        return volume.VolumeType switch
        {
            "AzureFiles" => (volume.VolumeData as DiscoveredAzureFileShare)?.ShareName ?? "",
            "ANF" => GetShortAnfVolumeName((volume.VolumeData as DiscoveredAnfVolume)?.VolumeName),
            "ManagedDisk" => (volume.VolumeData as DiscoveredManagedDisk)?.DiskName ?? "",
        };
    }

    private static string GetShortAnfVolumeName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "";
        var parts = fullName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : fullName;
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

    private string GetCsvStorageAccount(DiscoveredVolumeWithAnalysis volume)
    {
        return volume.VolumeType switch
        {
            "AzureFiles" => (volume.VolumeData as DiscoveredAzureFileShare)?.StorageAccountName ?? "",
            "ANF" => (volume.VolumeData as DiscoveredAnfVolume)?.NetAppAccountName ?? "",
            "ManagedDisk" => (volume.VolumeData as DiscoveredManagedDisk)?.DiskName ?? "",
            _ => ""
        };
    }
    
    private string GetCsvResourceGroup(DiscoveredVolumeWithAnalysis volume)
    {
        return volume.VolumeType switch
        {
            "AzureFiles" => (volume.VolumeData as DiscoveredAzureFileShare)?.ResourceGroup ?? "",
            "ANF" => (volume.VolumeData as DiscoveredAnfVolume)?.ResourceGroup ?? "",
            "ManagedDisk" => (volume.VolumeData as DiscoveredManagedDisk)?.ResourceGroup ?? "",
            _ => ""
        };
    }
    
    private string GetCsvSize(DiscoveredVolumeWithAnalysis volume)
    {
        return volume.VolumeType switch
        {
            "AzureFiles" => ((volume.VolumeData as DiscoveredAzureFileShare)?.ShareQuotaGiB ?? 0).ToString(),
            "ANF" => (((volume.VolumeData as DiscoveredAnfVolume)?.ProvisionedSizeBytes ?? 0) / (1024 * 1024 * 1024)).ToString(), // Convert bytes to GiB
            "ManagedDisk" => ((volume.VolumeData as DiscoveredManagedDisk)?.DiskSizeGB ?? 0).ToString(),
            _ => "0"
        };
    }
    
    private string GetCsvUsedCapacity(DiscoveredVolumeWithAnalysis volume)
    {
        return volume.VolumeType switch
        {
            "AzureFiles" => FormatBytes((volume.VolumeData as DiscoveredAzureFileShare)?.ShareUsageBytes ?? 0),
            "ANF" => (((volume.VolumeData as DiscoveredAnfVolume)?.ProvisionedSizeBytes ?? 0) / (1024 * 1024 * 1024)).ToString() + " GiB", // Use provisioned size as capacity
            "ManagedDisk" => "N/A",
            _ => ""
        };
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
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            var json = JsonSerializer.Serialize(data, options);
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
