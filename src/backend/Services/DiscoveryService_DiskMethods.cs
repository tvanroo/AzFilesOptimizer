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

            var resourceGroups = subscription.GetResourceGroups();
            await foreach (var rg in resourceGroups)
            {
                if (resourceGroupFilters != null && resourceGroupFilters.Length > 0)
                {
                    bool matchesAnyRg = resourceGroupFilters.Any(f => rg.Data.Name.Equals(f, StringComparison.OrdinalIgnoreCase));
                    if (!matchesAnyRg) continue;
                }

                await LogProgressAsync($"  • Scanning resource group: {rg.Data.Name}");

                try
                {
                    var rgResource = subscription.GetResourceGroup(rg.Data.Name);
                    if (rgResource == null) continue;

                    // Cache VM data first
                    var vms = rgResource.Value.GetVirtualMachines();
                    await foreach (var vm in vms.GetAllAsync())
                    {
                        try
                        {
                            var vmData = vm.Data;
                            var osType = vmData.OSProfile?.WindowsConfiguration != null ? "Windows" : "Linux";
                            var osDiskId = vmData.StorageProfile?.OSDisk?.ManagedDisk?.Id ?? "";
                            var vmSize = vmData.HardwareProfile?.VmSize?.ToString() ?? "";

                            int cpu = 0;
                            double memory = 0;

                            vmCache[vm.Id.ToString()] = (
                                vm.Data.Name,
                                vm.Data.Location.Name,
                                vmSize,
                                cpu,
                                memory,
                                osType,
                                osDiskId,
                                vm.Data.Tags?.ToDictionary(t => t.Key, t => t.Value) ?? new()
                            );
                        }
                        catch (Exception vmEx)
                        {
                            _logger.LogWarning(vmEx, "Failed to cache VM data for {VmName}", vm.Data.Name);
                        }
                    }

                    // Discover disks
                    var allDisks = rgResource.Value.GetDisks();
                    await foreach (var disk in allDisks.GetAllAsync())
                    {
                        try
                        {
                            var diskData = disk.Data;
                            var managedBy = diskData.ManagedBy?.Id?.ToString();

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
                                DiskSizeGB = diskData.DiskSizeGB,
                                DiskState = diskData.DiskState?.ToString() ?? "",
                                ProvisioningState = diskData.ProvisioningState?.ToString() ?? "",
                                DiskSizeBytes = diskData.DiskSizeGB * 1024L * 1024L * 1024L,
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
            var s when s.Contains("ultrassd") => (null, null), // Ultra SSD configured by user
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
            _ => (500, 60) // Standard HDD
        };
    }
}
