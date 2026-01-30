// This file contains helper methods for the DiscoveryService class.

namespace AzFilesOptimizer.Backend.Services;

public partial class DiscoveryService
{
    private static string? ExtractRedundancyFromSku(string? skuName)
    {
        if (string.IsNullOrWhiteSpace(skuName)) return null;
        
        var skuLower = skuName.ToLowerInvariant();
        
        if (skuLower.Contains("_gzrs")) return "GZRS";
        if (skuLower.Contains("_grs")) return "GRS";
        if (skuLower.Contains("_zrs")) return "ZRS";
        if (skuLower.Contains("_lrs")) return "LRS";
        
        return null;
    }

    private static string? GetProvisionedTier(string? skuName)
    {
        if (string.IsNullOrWhiteSpace(skuName)) return null;

        var skuLower = skuName.ToLowerInvariant();

        if (skuLower.Contains("premium"))
        {
            // Assuming Premium storage for Files is ProvisionedV1, unless V2 is specified
            return "ProvisionedV1"; 
        }

        return null;
    }

    private static string? MapDiskSizeToPricingTier(long diskSizeGB, string diskSku)
    {
        var skuLower = diskSku.ToLowerInvariant();
        
        // Premium SSD v2 and Ultra SSD don't use tier-based pricing
        if (skuLower.Contains("premiumv2") || skuLower.Contains("ultrassd"))
        {
            return null; // These use capacity + IOPS + throughput pricing
        }
        
        if (diskSku.Contains("Standard"))
        {
            if (diskSizeGB <= 32) return "S4";
            if (diskSizeGB <= 64) return "S6";
            if (diskSizeGB <= 128) return "S10";
            if (diskSizeGB <= 256) return "S15";
            if (diskSizeGB <= 512) return "S20";
            if (diskSizeGB <= 1024) return "S30";
            if (diskSizeGB <= 2048) return "S40";
            if (diskSizeGB <= 4096) return "S50";
            return "S60"; // Up to 32TB
        }
        if (diskSku.Contains("Premium"))
        {
            if (diskSizeGB <= 32) return "P4";
            if (diskSizeGB <= 64) return "P6";
            if (diskSizeGB <= 128) return "P10";
            if (diskSizeGB <= 256) return "P15";
            if (diskSizeGB <= 512) return "P20";
            if (diskSizeGB <= 1024) return "P30";
            if (diskSizeGB <= 2048) return "P40";
            if (diskSizeGB <= 4096) return "P50";
            return "P60"; // Up to 32TB
        }
        return null;
    }

    private static string? GetManagedDiskType(string? diskSku)
    {
        if (string.IsNullOrWhiteSpace(diskSku)) return null;

        var skuLower = diskSku.ToLowerInvariant();

        // Check for v2 variants first (before generic premium check)
        if (skuLower.Contains("premiumv2")) return "PremiumSSDv2";
        if (skuLower.Contains("ultrassd")) return "UltraDisk";
        
        // Then check for standard types
        if (skuLower.Contains("premium")) return "PremiumSSD";
        if (skuLower.Contains("standardssd")) return "StandardSSD";
        if (skuLower.Contains("standard")) return "StandardHDD";

        return null;
    }
}
