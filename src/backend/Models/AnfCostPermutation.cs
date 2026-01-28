namespace AzFilesOptimizer.Backend.Models;

/// <summary>
/// Identifies which of the 11 ANF cost permutations applies to a volume
/// Based on service level, cool access, and double encryption settings
/// </summary>
public class AnfCostPermutation
{
    /// <summary>
    /// Unique identifier for the permutation (1-11)
    /// </summary>
    public int PermutationId { get; set; }
    
    /// <summary>
    /// Human-readable name for the permutation
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Service level: Standard, Premium, Ultra, or Flexible
    /// </summary>
    public string ServiceLevel { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether cool access is enabled
    /// </summary>
    public bool CoolAccessEnabled { get; set; }
    
    /// <summary>
    /// Whether double encryption is enabled
    /// </summary>
    public bool DoubleEncryptionEnabled { get; set; }
    
    /// <summary>
    /// Base throughput per TiB (MiB/s) when cool access is NOT enabled
    /// </summary>
    public double BaseThroughputPerTib { get; set; }
    
    /// <summary>
    /// Throughput per TiB (MiB/s) when cool access IS enabled (reduced for Premium/Ultra)
    /// </summary>
    public double? CoolAccessThroughputPerTib { get; set; }
    
    /// <summary>
    /// For Flexible tier: base included throughput (flat, not per TiB)
    /// </summary>
    public double? FlexibleBaseThroughput { get; set; }
    
    /// <summary>
    /// Whether this permutation includes separate throughput pricing (Flexible tier only)
    /// </summary>
    public bool HasSeparateThroughputPricing => ServiceLevel == "Flexible";
    
    /// <summary>
    /// Whether cool access and double encryption are mutually exclusive
    /// </summary>
    public bool CoolAccessAndDoubleEncryptionIncompatible => true;
    
    /// <summary>
    /// Description of the permutation for display
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Create permutation from volume properties
    /// </summary>
    public static AnfCostPermutation Identify(string serviceLevel, bool coolAccessEnabled, bool doubleEncryptionEnabled)
    {
        var normalizedLevel = serviceLevel.ToUpperInvariant();
        
        // Validate incompatible combinations
        if (coolAccessEnabled && doubleEncryptionEnabled)
        {
            throw new InvalidOperationException("Cool Access and Double Encryption cannot be enabled together");
        }
        
        return (normalizedLevel, coolAccessEnabled, doubleEncryptionEnabled) switch
        {
            // Standard permutations (1-3)
            ("STANDARD", false, false) => new AnfCostPermutation
            {
                PermutationId = 1,
                Name = "ANF Standard (Regular)",
                ServiceLevel = "Standard",
                CoolAccessEnabled = false,
                DoubleEncryptionEnabled = false,
                BaseThroughputPerTib = 16.0,
                Description = "Standard tier with no cool access or double encryption"
            },
            ("STANDARD", false, true) => new AnfCostPermutation
            {
                PermutationId = 2,
                Name = "ANF Standard with Double Encryption",
                ServiceLevel = "Standard",
                CoolAccessEnabled = false,
                DoubleEncryptionEnabled = true,
                BaseThroughputPerTib = 16.0,
                Description = "Standard tier with double encryption enabled"
            },
            ("STANDARD", true, false) => new AnfCostPermutation
            {
                PermutationId = 3,
                Name = "ANF Standard with Cool Access",
                ServiceLevel = "Standard",
                CoolAccessEnabled = true,
                DoubleEncryptionEnabled = false,
                BaseThroughputPerTib = 16.0,
                CoolAccessThroughputPerTib = 16.0, // No reduction for Standard
                Description = "Standard tier with cool access enabled"
            },
            
            // Premium permutations (4-6)
            ("PREMIUM", false, false) => new AnfCostPermutation
            {
                PermutationId = 4,
                Name = "ANF Premium (Regular)",
                ServiceLevel = "Premium",
                CoolAccessEnabled = false,
                DoubleEncryptionEnabled = false,
                BaseThroughputPerTib = 64.0,
                Description = "Premium tier with no cool access or double encryption"
            },
            ("PREMIUM", false, true) => new AnfCostPermutation
            {
                PermutationId = 5,
                Name = "ANF Premium with Double Encryption",
                ServiceLevel = "Premium",
                CoolAccessEnabled = false,
                DoubleEncryptionEnabled = true,
                BaseThroughputPerTib = 64.0,
                Description = "Premium tier with double encryption enabled"
            },
            ("PREMIUM", true, false) => new AnfCostPermutation
            {
                PermutationId = 6,
                Name = "ANF Premium with Cool Access",
                ServiceLevel = "Premium",
                CoolAccessEnabled = true,
                DoubleEncryptionEnabled = false,
                BaseThroughputPerTib = 64.0,
                CoolAccessThroughputPerTib = 36.0, // Reduced from 64 to 36
                Description = "Premium tier with cool access enabled (reduced throughput)"
            },
            
            // Ultra permutations (7-9)
            ("ULTRA", false, false) => new AnfCostPermutation
            {
                PermutationId = 7,
                Name = "ANF Ultra (Regular)",
                ServiceLevel = "Ultra",
                CoolAccessEnabled = false,
                DoubleEncryptionEnabled = false,
                BaseThroughputPerTib = 128.0,
                Description = "Ultra tier with no cool access or double encryption"
            },
            ("ULTRA", false, true) => new AnfCostPermutation
            {
                PermutationId = 8,
                Name = "ANF Ultra with Double Encryption",
                ServiceLevel = "Ultra",
                CoolAccessEnabled = false,
                DoubleEncryptionEnabled = true,
                BaseThroughputPerTib = 128.0,
                Description = "Ultra tier with double encryption enabled"
            },
            ("ULTRA", true, false) => new AnfCostPermutation
            {
                PermutationId = 9,
                Name = "ANF Ultra with Cool Access",
                ServiceLevel = "Ultra",
                CoolAccessEnabled = true,
                DoubleEncryptionEnabled = false,
                BaseThroughputPerTib = 128.0,
                CoolAccessThroughputPerTib = 68.0, // Reduced from 128 to 68
                Description = "Ultra tier with cool access enabled (reduced throughput)"
            },
            
            // Flexible permutations (10-11)
            ("FLEXIBLE", false, false) => new AnfCostPermutation
            {
                PermutationId = 10,
                Name = "ANF Flexible (Regular)",
                ServiceLevel = "Flexible",
                CoolAccessEnabled = false,
                DoubleEncryptionEnabled = false,
                FlexibleBaseThroughput = 128.0, // Flat 128 MiB/s base
                Description = "Flexible tier with independent capacity and throughput pricing"
            },
            ("FLEXIBLE", true, false) => new AnfCostPermutation
            {
                PermutationId = 11,
                Name = "ANF Flexible with Cool Access",
                ServiceLevel = "Flexible",
                CoolAccessEnabled = true,
                DoubleEncryptionEnabled = false,
                FlexibleBaseThroughput = 128.0, // Flat 128 MiB/s base
                Description = "Flexible tier with cool access and independent throughput pricing"
            },
            
            _ => throw new NotSupportedException($"Unsupported ANF configuration: {serviceLevel}, CoolAccess={coolAccessEnabled}, DoubleEncryption={doubleEncryptionEnabled}")
        };
    }
    
    /// <summary>
    /// Get actual throughput per TiB considering cool access
    /// </summary>
    public double GetEffectiveThroughputPerTib()
    {
        if (ServiceLevel == "Flexible")
        {
            throw new InvalidOperationException("Flexible tier does not use per-TiB throughput calculation");
        }
        
        return CoolAccessEnabled && CoolAccessThroughputPerTib.HasValue
            ? CoolAccessThroughputPerTib.Value
            : BaseThroughputPerTib;
    }
    
    /// <summary>
    /// All 11 ANF permutations
    /// </summary>
    public static IReadOnlyList<AnfCostPermutation> AllPermutations => new[]
    {
        Identify("Standard", false, false),
        Identify("Standard", false, true),
        Identify("Standard", true, false),
        Identify("Premium", false, false),
        Identify("Premium", false, true),
        Identify("Premium", true, false),
        Identify("Ultra", false, false),
        Identify("Ultra", false, true),
        Identify("Ultra", true, false),
        Identify("Flexible", false, false),
        Identify("Flexible", true, false)
    };
}

/// <summary>
/// Universal cost calculation inputs for ANF volumes
/// Normalized structure that works across all 11 permutations
/// </summary>
public class UniversalAnfCostInputs
{
    /// <summary>
    /// Permutation identifier (1-11)
    /// </summary>
    public int PermutationId { get; set; }
    
    /// <summary>
    /// Permutation details
    /// </summary>
    public AnfCostPermutation Permutation { get; set; } = null!;
    
    /// <summary>
    /// Region for pricing lookup
    /// </summary>
    public string Region { get; set; } = string.Empty;
    
    // --- Capacity inputs (all permutations) ---
    
    /// <summary>
    /// Provisioned capacity in GiB
    /// </summary>
    public double ProvisionedCapacityGiB { get; set; }
    
    // --- Cool access inputs (permutations 3, 6, 9, 11) ---
    
    /// <summary>
    /// Hot tier capacity in GiB (provisioned - cool)
    /// Only for cool access permutations
    /// </summary>
    public double? HotCapacityGiB { get; set; }
    
    /// <summary>
    /// Cool tier capacity in GiB
    /// Only for cool access permutations
    /// </summary>
    public double? CoolCapacityGiB { get; set; }
    
    /// <summary>
    /// Data tiered to cool during the billing period (GiB)
    /// One-time charge per GiB moved
    /// </summary>
    public double? DataTieredToCoolGiB { get; set; }
    
    /// <summary>
    /// Data retrieved from cool during the billing period (GiB)
    /// One-time charge per GiB retrieved
    /// </summary>
    public double? DataRetrievedFromCoolGiB { get; set; }
    
    // --- Flexible tier throughput inputs (permutations 10-11) ---
    
    /// <summary>
    /// Required throughput in MiB/s
    /// Only for Flexible tier
    /// </summary>
    public double? RequiredThroughputMiBps { get; set; }
    
    /// <summary>
    /// Base throughput included free (128 MiB/s for Flexible)
    /// </summary>
    public double? BaseThroughputMiBps => Permutation?.FlexibleBaseThroughput;
    
    /// <summary>
    /// Throughput above base that will be charged
    /// </summary>
    public double ThroughputAboveBaseGiBps => 
        (RequiredThroughputMiBps.GetValueOrDefault() - BaseThroughputMiBps.GetValueOrDefault()) > 0
            ? RequiredThroughputMiBps.GetValueOrDefault() - BaseThroughputMiBps.GetValueOrDefault()
            : 0;
    
    // --- Snapshot inputs (all permutations) ---
    
    /// <summary>
    /// Snapshot size in GiB
    /// Note: ANF snapshots consume volume capacity, no separate charge
    /// </summary>
    public double SnapshotSizeGiB { get; set; }
    
    // --- Metadata ---
    
    /// <summary>
    /// Volume ID
    /// </summary>
    public string VolumeId { get; set; } = string.Empty;
    
    /// <summary>
    /// Volume name
    /// </summary>
    public string VolumeName { get; set; } = string.Empty;
    
    /// <summary>
    /// Resource ID
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;
    
    /// <summary>
    /// Billing period in days (default 30)
    /// </summary>
    public int BillingPeriodDays { get; set; } = 30;
    
    /// <summary>
    /// Billing period in hours (720 for 30 days)
    /// </summary>
    public int BillingPeriodHours => BillingPeriodDays * 24;
    
    /// <summary>
    /// Create inputs from discovered volume
    /// </summary>
    public static UniversalAnfCostInputs FromDiscoveredVolume(DiscoveredAnfVolume volume)
    {
        // Determine if double encryption is enabled (pool-level setting)
        bool isDoubleEncryption = volume.PoolEncryptionType != null &&
                                  volume.PoolEncryptionType.Equals("Double", StringComparison.OrdinalIgnoreCase);
        
        // Identify permutation
        var permutation = AnfCostPermutation.Identify(
            volume.ServiceLevel,
            volume.CoolAccessEnabled ?? false,
            isDoubleEncryption
        );
        
        var inputs = new UniversalAnfCostInputs
        {
            PermutationId = permutation.PermutationId,
            Permutation = permutation,
            Region = volume.Location,
            VolumeId = volume.ResourceId,
            VolumeName = volume.VolumeName,
            ResourceId = volume.ResourceId,
            ProvisionedCapacityGiB = volume.ProvisionedSizeBytes / (1024.0 * 1024.0 * 1024.0),
            SnapshotSizeGiB = (volume.TotalSnapshotSizeBytes ?? 0) / (1024.0 * 1024.0 * 1024.0)
        };
        
        // Parse metrics for cool access data if available
        if (permutation.CoolAccessEnabled && !string.IsNullOrEmpty(volume.HistoricalMetricsSummary))
        {
            try
            {
                var metrics = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
                    volume.HistoricalMetricsSummary);
                
                if (metrics != null)
                {
                    // Extract cool tier metrics
                    var coolTierSize = GetMetricAverage(metrics, "VolumeCoolTierSize");
                    var coolDataReadSize = GetMetricTotal(metrics, "VolumeCoolTierDataReadSize");
                    var coolDataWriteSize = GetMetricTotal(metrics, "VolumeCoolTierDataWriteSize");
                    
                    inputs.CoolCapacityGiB = coolTierSize / (1024.0 * 1024.0 * 1024.0); // Convert bytes to GiB
                    inputs.HotCapacityGiB = inputs.ProvisionedCapacityGiB - inputs.CoolCapacityGiB.GetValueOrDefault();
                    inputs.DataTieredToCoolGiB = coolDataWriteSize / (1024.0 * 1024.0 * 1024.0);
                    inputs.DataRetrievedFromCoolGiB = coolDataReadSize / (1024.0 * 1024.0 * 1024.0);
                }
            }
            catch
            {
                // If metrics parsing fails, assume all capacity is hot
                inputs.HotCapacityGiB = inputs.ProvisionedCapacityGiB;
                inputs.CoolCapacityGiB = 0;
            }
        }
        
        // Set throughput for Flexible tier
        if (permutation.ServiceLevel == "Flexible")
        {
            inputs.RequiredThroughputMiBps = volume.ThroughputMibps ?? volume.ActualThroughputMibps ?? 0;
        }
        
        return inputs;
    }
    
    private static double GetMetricAverage(Dictionary<string, object> metrics, string key)
    {
        if (metrics.TryGetValue(key, out var value) && value is System.Text.Json.JsonElement element)
        {
            if (element.ValueKind == System.Text.Json.JsonValueKind.Object &&
                element.TryGetProperty("average", out var avgElement))
            {
                return avgElement.GetDouble();
            }
        }
        return 0;
    }
    
    private static double GetMetricTotal(Dictionary<string, object> metrics, string key)
    {
        if (metrics.TryGetValue(key, out var value) && value is System.Text.Json.JsonElement element)
        {
            if (element.ValueKind == System.Text.Json.JsonValueKind.Object &&
                element.TryGetProperty("total", out var totalElement))
            {
                return totalElement.GetDouble();
            }
        }
        return 0;
    }
    
    /// <summary>
    /// Validate inputs for the identified permutation
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        
        if (ProvisionedCapacityGiB < 50)
        {
            errors.Add("ANF volumes require minimum 50 GiB provisioned capacity");
        }
        
        if (Permutation.CoolAccessEnabled)
        {
            if (!HotCapacityGiB.HasValue || !CoolCapacityGiB.HasValue)
            {
                errors.Add("Cool access enabled but hot/cool capacity breakdown not provided");
            }
            
            if (ProvisionedCapacityGiB < 2400)
            {
                errors.Add("Cool access requires minimum 2,400 GiB provisioned capacity");
            }
        }
        
        if (Permutation.ServiceLevel == "Flexible" && !RequiredThroughputMiBps.HasValue)
        {
            errors.Add("Flexible tier requires throughput specification");
        }
        
        return errors;
    }
}
