using System;
using System.Collections.Generic;

namespace AzFilesOptimizer.Backend.Models;

/// <summary>
/// Represents all possible Azure Files cost permutations based on tier and redundancy
/// </summary>
public enum AzureFilesPermutationId
{
    // Pay-as-you-go Hot Tier (4 permutations)
    HotLRS = 1,
    HotZRS = 2,
    HotGRS = 3,
    HotGZRS = 4,
    
    // Pay-as-you-go Cool Tier (4 permutations)
    CoolLRS = 5,
    CoolZRS = 6,
    CoolGRS = 7,
    CoolGZRS = 8,
    
    // Pay-as-you-go Transaction Optimized (Standard) Tier (4 permutations)
    TransactionOptimizedLRS = 9,
    TransactionOptimizedZRS = 10,
    TransactionOptimizedGRS = 11,
    TransactionOptimizedGZRS = 12,
    
    // Provisioned Premium v1 (2 permutations)
    PremiumLRS = 13,
    PremiumZRS = 14,
    
    // Provisioned v2 - SSD (2 permutations)
    ProvisionedV2_SSD_LRS = 15,
    ProvisionedV2_SSD_ZRS = 16,
    
    // Provisioned v2 - HDD (4 permutations)
    ProvisionedV2_HDD_LRS = 17,
    ProvisionedV2_HDD_ZRS = 18,
    ProvisionedV2_HDD_GRS = 19,
    ProvisionedV2_HDD_GZRS = 20
}

/// <summary>
/// Detailed information about an Azure Files cost permutation
/// </summary>
public class AzureFilesPermutation
{
    public AzureFilesPermutationId PermutationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsProvisioned { get; set; }
    public string? ProvisionedType { get; set; } // null, "v1", "v2-SSD", "v2-HDD"
    public string? AccessTier { get; set; } // Hot, Cool, TransactionOptimized, Premium
    public string Redundancy { get; set; } = string.Empty; // LRS, ZRS, GRS, GZRS
    public string RetailApiSkuName { get; set; } = string.Empty;
    public Dictionary<string, string> CostComponents { get; set; } = new();

    /// <summary>
    /// All Azure Files permutations with their characteristics
    /// </summary>
    public static readonly Dictionary<AzureFilesPermutationId, AzureFilesPermutation> All = new()
    {
        // Pay-as-you-go Hot Tier
        {
            AzureFilesPermutationId.HotLRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.HotLRS,
                Name = "Azure Files - Hot LRS",
                Description = "Pay-as-you-go Hot tier with Locally Redundant Storage",
                IsProvisioned = false,
                AccessTier = "Hot",
                Redundancy = "LRS",
                RetailApiSkuName = "Hot LRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "Hot LRS Data Stored" },
                    { "WriteOps", "Hot LRS Write Operations" },
                    { "ReadOps", "Hot Read Operations" },
                    { "ListOps", "Hot LRS List Operations" },
                    { "OtherOps", "Hot Other Operations" },
                    { "Metadata", "LRS Metadata" }
                }
            }
        },
        {
            AzureFilesPermutationId.HotZRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.HotZRS,
                Name = "Azure Files - Hot ZRS",
                Description = "Pay-as-you-go Hot tier with Zone Redundant Storage",
                IsProvisioned = false,
                AccessTier = "Hot",
                Redundancy = "ZRS",
                RetailApiSkuName = "Hot ZRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "Hot ZRS Data Stored" },
                    { "WriteOps", "Hot ZRS Write Operations" },
                    { "ReadOps", "Hot Read Operations" },
                    { "ListOps", "Hot ZRS List Operations" },
                    { "OtherOps", "Hot Other Operations" },
                    { "Metadata", "ZRS Metadata" }
                }
            }
        },
        {
            AzureFilesPermutationId.HotGRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.HotGRS,
                Name = "Azure Files - Hot GRS",
                Description = "Pay-as-you-go Hot tier with Geo Redundant Storage",
                IsProvisioned = false,
                AccessTier = "Hot",
                Redundancy = "GRS",
                RetailApiSkuName = "Hot GRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "Hot GRS Data Stored" },
                    { "WriteOps", "Hot GRS Write Operations" },
                    { "ReadOps", "Hot Read Operations" },
                    { "ListOps", "Hot GRS List Operations" },
                    { "OtherOps", "Hot Other Operations" },
                    { "Metadata", "GRS Metadata" }
                }
            }
        },
        {
            AzureFilesPermutationId.HotGZRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.HotGZRS,
                Name = "Azure Files - Hot GZRS",
                Description = "Pay-as-you-go Hot tier with Geo-Zone Redundant Storage",
                IsProvisioned = false,
                AccessTier = "Hot",
                Redundancy = "GZRS",
                RetailApiSkuName = "Hot GZRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "Hot GZRS Data Stored" },
                    { "WriteOps", "Hot GZRS Write Operations" },
                    { "ReadOps", "Hot Read Operations" },
                    { "ListOps", "Hot GZRS List Operations" },
                    { "OtherOps", "Hot GZRS Other Operations" },
                    { "Metadata", "GZRS Metadata" }
                }
            }
        },
        
        // Pay-as-you-go Cool Tier
        {
            AzureFilesPermutationId.CoolLRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.CoolLRS,
                Name = "Azure Files - Cool LRS",
                Description = "Pay-as-you-go Cool tier with Locally Redundant Storage",
                IsProvisioned = false,
                AccessTier = "Cool",
                Redundancy = "LRS",
                RetailApiSkuName = "Cool LRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "Cool LRS Data Stored" },
                    { "WriteOps", "Cool LRS Write Operations" },
                    { "ReadOps", "Cool Read Operations" },
                    { "ListOps", "Cool LRS List Operations" },
                    { "OtherOps", "Cool Other Operations" },
                    { "DataRetrieval", "Cool Data Retrieval" },
                    { "Metadata", "LRS Metadata" }
                }
            }
        },
        {
            AzureFilesPermutationId.CoolZRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.CoolZRS,
                Name = "Azure Files - Cool ZRS",
                Description = "Pay-as-you-go Cool tier with Zone Redundant Storage",
                IsProvisioned = false,
                AccessTier = "Cool",
                Redundancy = "ZRS",
                RetailApiSkuName = "Cool ZRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "Cool ZRS Data Stored" },
                    { "WriteOps", "Cool ZRS Write Operations" },
                    { "ReadOps", "Cool Read Operations" },
                    { "ListOps", "Cool ZRS List Operations" },
                    { "OtherOps", "Cool Other Operations" },
                    { "DataRetrieval", "Cool Data Retrieval" },
                    { "Metadata", "ZRS Metadata" }
                }
            }
        },
        {
            AzureFilesPermutationId.CoolGRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.CoolGRS,
                Name = "Azure Files - Cool GRS",
                Description = "Pay-as-you-go Cool tier with Geo Redundant Storage",
                IsProvisioned = false,
                AccessTier = "Cool",
                Redundancy = "GRS",
                RetailApiSkuName = "Cool GRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "Cool GRS Data Stored" },
                    { "WriteOps", "Cool GRS Write Operations" },
                    { "ReadOps", "Cool Read Operations" },
                    { "ListOps", "Cool GRS List Operations" },
                    { "OtherOps", "Cool Other Operations" },
                    { "DataRetrieval", "Cool Data Retrieval" },
                    { "Metadata", "GRS Metadata" }
                }
            }
        },
        {
            AzureFilesPermutationId.CoolGZRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.CoolGZRS,
                Name = "Azure Files - Cool GZRS",
                Description = "Pay-as-you-go Cool tier with Geo-Zone Redundant Storage",
                IsProvisioned = false,
                AccessTier = "Cool",
                Redundancy = "GZRS",
                RetailApiSkuName = "Cool GZRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "Cool GZRS Data Stored" },
                    { "WriteOps", "Cool GZRS Write Operations" },
                    { "ReadOps", "Cool Read Operations" },
                    { "ListOps", "Cool GZRS List Operations" },
                    { "OtherOps", "Cool GZRS Other Operations" },
                    { "DataRetrieval", "Cool GZRS Data Retrieval" },
                    { "Metadata", "GZRS Metadata" }
                }
            }
        },
        
        // Pay-as-you-go Transaction Optimized (Standard) Tier
        {
            AzureFilesPermutationId.TransactionOptimizedLRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.TransactionOptimizedLRS,
                Name = "Azure Files - Transaction Optimized LRS",
                Description = "Pay-as-you-go Transaction Optimized (Standard) tier with LRS",
                IsProvisioned = false,
                AccessTier = "TransactionOptimized",
                Redundancy = "LRS",
                RetailApiSkuName = "Standard LRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "LRS Data Stored" },
                    { "WriteOps", "LRS Write Operations" },
                    { "ReadOps", "Read Operations" },
                    { "ListOps", "List Operations" },
                    { "DeleteOps", "Delete Operations" },
                    { "ProtocolOps", "Protocol Operations" }
                }
            }
        },
        {
            AzureFilesPermutationId.TransactionOptimizedZRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.TransactionOptimizedZRS,
                Name = "Azure Files - Transaction Optimized ZRS",
                Description = "Pay-as-you-go Transaction Optimized (Standard) tier with ZRS",
                IsProvisioned = false,
                AccessTier = "TransactionOptimized",
                Redundancy = "ZRS",
                RetailApiSkuName = "Standard ZRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "ZRS Data Stored" },
                    { "WriteOps", "ZRS Write Operations" },
                    { "ReadOps", "ZRS Read Operations" },
                    { "ListOps", "ZRS List Operations" },
                    { "DeleteOps", "Delete Operations" },
                    { "ProtocolOps", "ZRS Protocol Operations" }
                }
            }
        },
        {
            AzureFilesPermutationId.TransactionOptimizedGRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.TransactionOptimizedGRS,
                Name = "Azure Files - Transaction Optimized GRS",
                Description = "Pay-as-you-go Transaction Optimized (Standard) tier with GRS",
                IsProvisioned = false,
                AccessTier = "TransactionOptimized",
                Redundancy = "GRS",
                RetailApiSkuName = "Standard GRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "GRS Data Stored" },
                    { "WriteOps", "GRS Write Operations" },
                    { "ReadOps", "Read Operations" },
                    { "ListOps", "List Operations" },
                    { "DeleteOps", "Delete Operations" },
                    { "ProtocolOps", "Protocol Operations" }
                }
            }
        },
        {
            AzureFilesPermutationId.TransactionOptimizedGZRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.TransactionOptimizedGZRS,
                Name = "Azure Files - Transaction Optimized GZRS",
                Description = "Pay-as-you-go Transaction Optimized (Standard) tier with GZRS",
                IsProvisioned = false,
                AccessTier = "TransactionOptimized",
                Redundancy = "GZRS",
                RetailApiSkuName = "Standard GZRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "GZRS Data Stored" },
                    { "WriteOps", "GZRS Write Operations" },
                    { "ReadOps", "GZRS Read Operations" },
                    { "ListOps", "GZRS List Operations" },
                    { "DeleteOps", "Delete Operations" },
                    { "ProtocolOps", "GZRS Protocol Operations" }
                }
            }
        },
        
        // Provisioned Premium v1
        {
            AzureFilesPermutationId.PremiumLRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.PremiumLRS,
                Name = "Azure Files - Premium LRS",
                Description = "Provisioned Premium Files with LRS (v1)",
                IsProvisioned = true,
                ProvisionedType = "v1",
                AccessTier = "Premium",
                Redundancy = "LRS",
                RetailApiSkuName = "Premium LRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "Premium LRS Provisioned" },
                    { "Snapshots", "Premium LRS Snapshots" }
                }
            }
        },
        {
            AzureFilesPermutationId.PremiumZRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.PremiumZRS,
                Name = "Azure Files - Premium ZRS",
                Description = "Provisioned Premium Files with ZRS (v1)",
                IsProvisioned = true,
                ProvisionedType = "v1",
                AccessTier = "Premium",
                Redundancy = "ZRS",
                RetailApiSkuName = "Premium ZRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "Premium ZRS Provisioned" },
                    { "Snapshots", "Premium ZRS Snapshots" }
                }
            }
        },
        
        // Provisioned v2 - SSD
        {
            AzureFilesPermutationId.ProvisionedV2_SSD_LRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.ProvisionedV2_SSD_LRS,
                Name = "Azure Files - Provisioned v2 SSD LRS",
                Description = "Provisioned v2 SSD with LRS",
                IsProvisioned = true,
                ProvisionedType = "v2-SSD",
                AccessTier = "ProvisionedV2",
                Redundancy = "LRS",
                RetailApiSkuName = "SSD LRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "SSD LRS Provisioned Storage" },
                    { "IOPS", "SSD LRS Provisioned IOPS" },
                    { "Throughput", "SSD LRS Provisioned Throughput MiBPS" },
                    { "Snapshots", "SSD LRS Overflow Snapshot Usage" }
                }
            }
        },
        {
            AzureFilesPermutationId.ProvisionedV2_SSD_ZRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.ProvisionedV2_SSD_ZRS,
                Name = "Azure Files - Provisioned v2 SSD ZRS",
                Description = "Provisioned v2 SSD with ZRS",
                IsProvisioned = true,
                ProvisionedType = "v2-SSD",
                AccessTier = "ProvisionedV2",
                Redundancy = "ZRS",
                RetailApiSkuName = "SSD ZRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "SSD ZRS Provisioned Storage" },
                    { "IOPS", "SSD ZRS Provisioned IOPS" },
                    { "Throughput", "SSD ZRS Provisioned Throughput MiBPS" },
                    { "Snapshots", "SSD ZRS Overflow Snapshot Usage" }
                }
            }
        },
        
        // Provisioned v2 - HDD
        {
            AzureFilesPermutationId.ProvisionedV2_HDD_LRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.ProvisionedV2_HDD_LRS,
                Name = "Azure Files - Provisioned v2 HDD LRS",
                Description = "Provisioned v2 HDD with LRS",
                IsProvisioned = true,
                ProvisionedType = "v2-HDD",
                AccessTier = "ProvisionedV2",
                Redundancy = "LRS",
                RetailApiSkuName = "HDD LRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "HDD LRS Provisioned Storage" },
                    { "IOPS", "HDD LRS Provisioned IOPS" },
                    { "Throughput", "HDD LRS Provisioned Throughput MiBPS" },
                    { "Snapshots", "HDD LRS Overflow Snapshot Usage" }
                }
            }
        },
        {
            AzureFilesPermutationId.ProvisionedV2_HDD_ZRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.ProvisionedV2_HDD_ZRS,
                Name = "Azure Files - Provisioned v2 HDD ZRS",
                Description = "Provisioned v2 HDD with ZRS",
                IsProvisioned = true,
                ProvisionedType = "v2-HDD",
                AccessTier = "ProvisionedV2",
                Redundancy = "ZRS",
                RetailApiSkuName = "HDD ZRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "HDD ZRS Provisioned Storage" },
                    { "IOPS", "HDD ZRS Provisioned IOPS" },
                    { "Throughput", "HDD ZRS Provisioned Throughput MiBPS" },
                    { "Snapshots", "HDD ZRS Overflow Snapshot Usage" }
                }
            }
        },
        {
            AzureFilesPermutationId.ProvisionedV2_HDD_GRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.ProvisionedV2_HDD_GRS,
                Name = "Azure Files - Provisioned v2 HDD GRS",
                Description = "Provisioned v2 HDD with GRS",
                IsProvisioned = true,
                ProvisionedType = "v2-HDD",
                AccessTier = "ProvisionedV2",
                Redundancy = "GRS",
                RetailApiSkuName = "HDD GRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "HDD GRS Provisioned Storage" },
                    { "IOPS", "HDD GRS Provisioned IOPS" },
                    { "Throughput", "HDD GRS Provisioned Throughput MiBPS" },
                    { "Snapshots", "HDD GRS Overflow Snapshot Usage" }
                }
            }
        },
        {
            AzureFilesPermutationId.ProvisionedV2_HDD_GZRS,
            new AzureFilesPermutation
            {
                PermutationId = AzureFilesPermutationId.ProvisionedV2_HDD_GZRS,
                Name = "Azure Files - Provisioned v2 HDD GZRS",
                Description = "Provisioned v2 HDD with GZRS",
                IsProvisioned = true,
                ProvisionedType = "v2-HDD",
                AccessTier = "ProvisionedV2",
                Redundancy = "GZRS",
                RetailApiSkuName = "HDD GZRS",
                CostComponents = new Dictionary<string, string>
                {
                    { "Storage", "HDD GZRS Provisioned Storage" },
                    { "IOPS", "HDD GZRS Provisioned IOPS" },
                    { "Throughput", "HDD GZRS Provisioned Throughput MiBPS" },
                    { "Snapshots", "HDD GZRS Overflow Snapshot Usage" }
                }
            }
        }
    };

    /// <summary>
    /// Identify which Azure Files permutation applies to a given share configuration
    /// </summary>
    public static AzureFilesPermutation? IdentifyPermutation(
        string storageAccountSku,
        string storageAccountKind,
        bool isProvisioned,
        string? provisionedTier,
        string? accessTier)
    {
        // Parse redundancy from storage account SKU
        string redundancy = ParseRedundancy(storageAccountSku);
        
        // Determine if this is Premium (FileStorage account kind)
        bool isPremiumAccount = storageAccountKind?.Equals("FileStorage", StringComparison.OrdinalIgnoreCase) == true;
        
        if (isProvisioned)
        {
            // Provisioned v2
            if (provisionedTier?.StartsWith("ProvisionedV2", StringComparison.OrdinalIgnoreCase) == true ||
                provisionedTier?.Contains("SSD", StringComparison.OrdinalIgnoreCase) == true ||
                provisionedTier?.Contains("HDD", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Determine SSD vs HDD
                bool isSSD = provisionedTier.Contains("SSD", StringComparison.OrdinalIgnoreCase);
                
                if (isSSD)
                {
                    return redundancy switch
                    {
                        "LRS" => All[AzureFilesPermutationId.ProvisionedV2_SSD_LRS],
                        "ZRS" => All[AzureFilesPermutationId.ProvisionedV2_SSD_ZRS],
                        _ => null
                    };
                }
                else
                {
                    return redundancy switch
                    {
                        "LRS" => All[AzureFilesPermutationId.ProvisionedV2_HDD_LRS],
                        "ZRS" => All[AzureFilesPermutationId.ProvisionedV2_HDD_ZRS],
                        "GRS" or "RAGRS" => All[AzureFilesPermutationId.ProvisionedV2_HDD_GRS],
                        "GZRS" or "RAGZRS" => All[AzureFilesPermutationId.ProvisionedV2_HDD_GZRS],
                        _ => null
                    };
                }
            }
            
            // Provisioned v1 (Premium Files)
            if (isPremiumAccount || accessTier?.Equals("Premium", StringComparison.OrdinalIgnoreCase) == true)
            {
                return redundancy switch
                {
                    "LRS" => All[AzureFilesPermutationId.PremiumLRS],
                    "ZRS" => All[AzureFilesPermutationId.PremiumZRS],
                    _ => null
                };
            }
        }
        else
        {
            // Pay-as-you-go tiers
            var tierLower = accessTier?.ToLowerInvariant() ?? "transactionoptimized";
            
            if (tierLower == "hot")
            {
                return redundancy switch
                {
                    "LRS" => All[AzureFilesPermutationId.HotLRS],
                    "ZRS" => All[AzureFilesPermutationId.HotZRS],
                    "GRS" or "RAGRS" => All[AzureFilesPermutationId.HotGRS],
                    "GZRS" or "RAGZRS" => All[AzureFilesPermutationId.HotGZRS],
                    _ => null
                };
            }
            else if (tierLower == "cool")
            {
                return redundancy switch
                {
                    "LRS" => All[AzureFilesPermutationId.CoolLRS],
                    "ZRS" => All[AzureFilesPermutationId.CoolZRS],
                    "GRS" or "RAGRS" => All[AzureFilesPermutationId.CoolGRS],
                    "GZRS" or "RAGZRS" => All[AzureFilesPermutationId.CoolGZRS],
                    _ => null
                };
            }
            else // TransactionOptimized (default)
            {
                return redundancy switch
                {
                    "LRS" => All[AzureFilesPermutationId.TransactionOptimizedLRS],
                    "ZRS" => All[AzureFilesPermutationId.TransactionOptimizedZRS],
                    "GRS" or "RAGRS" => All[AzureFilesPermutationId.TransactionOptimizedGRS],
                    "GZRS" or "RAGZRS" => All[AzureFilesPermutationId.TransactionOptimizedGZRS],
                    _ => null
                };
            }
        }
        
        return null;
    }

    /// <summary>
    /// Parse redundancy type from storage account SKU
    /// Examples: Standard_LRS -> LRS, Standard_RAGRS -> RAGRS, Premium_ZRS -> ZRS
    /// </summary>
    private static string ParseRedundancy(string storageAccountSku)
    {
        if (string.IsNullOrEmpty(storageAccountSku))
            return "LRS"; // Default
        
        // Extract redundancy from SKU (format: Standard_LRS, Premium_ZRS, etc.)
        var parts = storageAccountSku.Split('_');
        if (parts.Length >= 2)
        {
            return parts[1].ToUpperInvariant();
        }
        
        return "LRS"; // Default
    }
}
