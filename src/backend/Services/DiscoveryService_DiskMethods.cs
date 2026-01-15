using Azure.ResourceManager.Compute;
using AzFilesOptimizer.Backend.Models;
using Microsoft.Extensions.Logging;

namespace AzFilesOptimizer.Backend.Services;

/// <summary>
/// Partial class containing disk discovery implementation
/// </summary>
public partial class DiscoveryService
{
    private async Task<List<DiscoveredManagedDisk>> DiscoverManagedDisksAsync(
        Azure.ResourceManager.Resources.SubscriptionResource subscription,
        string[]? resourceGroupFilters,
        string? tenantId = null)
    {
        var disks = new List<DiscoveredManagedDisk>();
        var vmCache = new Dictionary<string, (string name, string location, string size, int cpu, double memory, string osType, string osDiskId, Dictionary<string, string> tags)>();

        try
        {
            await LogProgressAsync("  • Starting managed disk discovery...");

            // Enumerate resource groups
            await foreach (var rg in subscription.GetResourceGroups())
            {
                // Apply resource group filter
                if (resourceGroupFilters != null && resourceGroupFilters.Length > 0)
                {
                    if (!resourceGroupFilters.Any(f => rg.Data.Name.Equals(f, StringComparison.OrdinalIgnoreCase)))
                        continue;
                }

                await LogProgressAsync($"  • Scanning resource group: {rg.Data.Name}");

                try
                {
                    // Cache VM data in this RG
                    await foreach (var vm in rg.GetVirtualMachines())
                    {
                        try
                        {
                            var vmData = vm.Data;
                            var osType = vmData.OSProfile?.WindowsConfiguration != null ? "Windows" : "Linux";
                            var osDiskId = vmData.StorageProfile?.OSDisk?.ManagedDisk?.Id ?? "";
                            var vmSize = vmData.HardwareProfile?.VmSize?.ToString() ?? "";

                            vmCache[vm.Id.ToString()] = (
                                vm.Data.Name,
                                vm.Data.Location.Name,
                                vmSize,
                                0,
                                0,
                                osType,
                                osDiskId,
                                vm.Data.Tags?.ToDictionary(t => t.Key, t => t.Value) ?? new()
                            );
                        }
                        catch (Exception vmEx)
                        {
                            _logger.LogWarning(vmEx, "Failed to cache VM {VmName}", vm.Data.Name);
                        }
                    }

                    // Discover disks in this RG
                    await foreach (var disk in rg.GetManagedDisks())
                    {
                        try
                        {
                            var diskData = disk.Data;
                            var managedBy = diskData.ManagedBy?.ToString();

                            var discoveredDisk = new DiscoveredManagedDisk
                            {
                                TenantId = tenantId ?? "",
                                SubscriptionId = disk.Id.SubscriptionId ?? "",
                                ResourceGroup = disk.Id.ResourceGroupName ?? "",
                                DiskName = disk.Data.Name,
                                ResourceId = disk.Id.ToString(),
                                Location = disk.Data.Location.Name,
                                DiskSku = diskData.Sku?.Name.ToString() ?? "",
                                DiskTier = ParseDiskTier(diskData.Sku?.Name.ToString() ?? ""),
                                DiskSizeGB = diskData.DiskSizeGB ?? 0,
                                DiskState = diskData.DiskState?.ToString() ?? "",
                                ProvisioningState = diskData.ProvisioningState?.ToString() ?? "",
                                DiskSizeBytes = (diskData.DiskSizeGB ?? 0) * 1024L * 1024L * 1024L,
                                DiskType = ParseDiskType(diskData.Sku?.Name.ToString() ?? ""),
                                BurstingEnabled = diskData.BurstingEnabled,
                                Tags = diskData.Tags?.ToDictionary(t => t.Key, t => t.Value),
                                TimeCreated = diskData.TimeCreated?.UtcDateTime,
                                DiscoveredAt = DateTime.UtcNow,
                                IsAttached = !string.IsNullOrEmpty(managedBy)
                            };

                            // Estimate performance
                            var (estIops, estBw) = EstimateDiskPerformance(discoveredDisk);
                            discoveredDisk.EstimatedIops = estIops;
                            discoveredDisk.EstimatedThroughputMiBps = estBw;

                            // Collect Azure Monitor metrics for managed disks (only if attached)
                            if (_metricsService != null && discoveredDisk.IsAttached)
                            {
                                try
                                {
                                    await LogProgressAsync($"        → Collecting Azure Monitor metrics for disk {disk.Data.Name}...");
                                    var (hasData, daysAvailable, metricsSummary) = await _metricsService
                                        .CollectManagedDiskMetricsAsync(disk.Id.ToString(), disk.Data.Name);

                                    discoveredDisk.MonitoringEnabled = hasData;
                                    discoveredDisk.MonitoringDataAvailableDays = daysAvailable;
                                    discoveredDisk.HistoricalMetricsSummary = metricsSummary;

                                    if (hasData && !string.IsNullOrEmpty(metricsSummary))
                                    {
                                        ApplyDiskMetricsSummary(discoveredDisk, metricsSummary);
                                        await LogProgressAsync($"        ✓ Metrics collected: {daysAvailable} days available");
                                    }
                                    else
                                    {
                                        await LogProgressAsync($"        ⚠ No metrics data available for disk {disk.Data.Name}");
                                    }
                                }
                                catch (Exception metricsEx)
                                {
                                    _logger.LogWarning(metricsEx, "Failed to collect metrics for disk {DiskName}", disk.Data.Name);
                                }
                            }

                            // Check if attached to VM
                            if (discoveredDisk.IsAttached && !string.IsNullOrEmpty(managedBy))
                            {
                                discoveredDisk.AttachedVmId = managedBy;

                                if (vmCache.TryGetValue(managedBy, out var vmInfo))
                                {
                                    discoveredDisk.AttachedVmName = vmInfo.name;
                                    discoveredDisk.VmSize = vmInfo.size;
                                    discoveredDisk.VmCpuCount = vmInfo.cpu;
                                    discoveredDisk.VmMemoryGiB = vmInfo.memory;
                                    discoveredDisk.VmOsType = vmInfo.osType;
                                    discoveredDisk.VmTags = vmInfo.tags;

                                    // Check if this is an OS disk
                                    discoveredDisk.IsOsDisk = !string.IsNullOrEmpty(vmInfo.osDiskId) &&
                                        string.Equals(disk.Id.ToString(), vmInfo.osDiskId, StringComparison.OrdinalIgnoreCase);
                                }
                                else
                                {
                                    discoveredDisk.IsOsDisk = false;
                                }
                            }
                            else
                            {
                                discoveredDisk.IsOsDisk = false;
                            }

                            // Only add data disks (exclude OS disks)
                            if (discoveredDisk.IsOsDisk != true)
                            {
                                disks.Add(discoveredDisk);
                            }
                        }
                        catch (Exception diskEx)
                        {
                            _logger.LogWarning(diskEx, "Failed to process disk {DiskName}", disk.Data.Name);
                        }
                    }

                    var rgDisks = disks.Where(d => d.ResourceGroup == rg.Data.Name).Count();
                    if (rgDisks > 0)
                    {
                        await LogProgressAsync($"    → Found {rgDisks} data disk(s) in {rg.Data.Name}");
                    }
                }
                catch (Exception rgEx)
                {
                    _logger.LogWarning(rgEx, "Failed to scan resource group {RgName}", rg.Data.Name);
                    await LogProgressAsync($"    ⚠ Failed to scan resource group {rg.Data.Name}: {rgEx.Message}");
                }
            }

            await LogProgressAsync($"  → Total: {disks.Count} managed disks discovered (excluding OS disks)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering managed disks");
            await LogProgressAsync($"  ⚠ ERROR during managed disk discovery: {ex.Message}");
            throw;
        }

        return disks;
    }

    private static string ParseDiskTier(string skuName)
    {
        var skuLower = skuName.ToLowerInvariant();
        if (skuLower.Contains("premium")) return "Premium SSD";
        if (skuLower.Contains("ultrassd")) return "Ultra Disk";
        if (skuLower.Contains("standardssd")) return "Standard SSD";
        if (skuLower.Contains("hdd")) return "Standard HDD";
        return "Unknown";
    }

    private static string ParseDiskType(string skuName)
    {
        var skuLower = skuName.ToLowerInvariant();
        if (skuLower.Contains("ultrassd")) return "UltraSSD";
        if (skuLower.Contains("premium")) return "PremiumSSD";
        if (skuLower.Contains("standardssd")) return "StandardSSD";
        if (skuLower.Contains("hdd")) return "StandardHDD";
        return "Unknown";
    }

    private static (int? iops, double? mibps) EstimateDiskPerformance(DiscoveredManagedDisk disk)
    {
        var sku = disk.DiskSku.ToLowerInvariant();
        var sizeGb = disk.DiskSizeGB;

        return sku switch
        {
            var s when s.Contains("ultrassd") => (null, null),
            var s when s.Contains("premium") => sizeGb switch
            {
                <= 128 => (500, 100),
                <= 256 => (1200, 200),
                <= 512 => (2300, 200),
                <= 1024 => (5000, 200),
                <= 2048 => (7500, 250),
                <= 4096 => (16000, 400),
                <= 8192 => (20000, 500),
                <= 16384 => (30000, 750),
                <= 32768 => (80000, 1000),
                _ => (160000, 2000)
            },
            var s when s.Contains("standardssd") => sizeGb switch
            {
                <= 128 => (500, 60),
                <= 256 => (600, 120),
                <= 512 => (1200, 120),
                <= 1024 => (2400, 300),
                <= 2048 => (4800, 500),
                <= 4096 => (9600, 600),
                <= 8192 => (19200, 750),
                <= 16384 => (38400, 750),
                <= 32768 => (76800, 800),
                _ => (153600, 1000)
            },
            _ => (500, 60)
        };
    }

    private static void ApplyDiskMetricsSummary(DiscoveredManagedDisk disk, string metricsSummary)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(metricsSummary);
            var root = doc.RootElement;

            disk.AverageReadIops = GetMetricAverage(root, "Disk Read Operations/Sec");
            disk.AverageWriteIops = GetMetricAverage(root, "Disk Write Operations/Sec");

            var readBytes = GetMetricAverage(root, "Disk Read Bytes");
            var writeBytes = GetMetricAverage(root, "Disk Write Bytes");

            disk.AverageReadThroughputMiBps = readBytes.HasValue ? readBytes.Value / (1024 * 1024) : null;
            disk.AverageWriteThroughputMiBps = writeBytes.HasValue ? writeBytes.Value / (1024 * 1024) : null;

            var usedBytes = GetMetricAverage(root, "Disk Used Capacity");
            disk.UsedBytes = usedBytes.HasValue ? (long?)Math.Round(usedBytes.Value) : null;
        }
        catch (Exception ex)
        {
            // Ignore parse errors; metrics summary is best-effort
            Console.WriteLine($"Failed to parse disk metrics summary: {ex.Message}");
        }
    }

    private static double? GetMetricAverage(System.Text.Json.JsonElement root, string metricName)
    {
        if (root.TryGetProperty(metricName, out var metric) &&
            metric.TryGetProperty("average", out var averageProp) &&
            averageProp.ValueKind == System.Text.Json.JsonValueKind.Number &&
            averageProp.TryGetDouble(out var avg))
        {
            return avg;
        }
        return null;
    }
}
