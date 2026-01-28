using Microsoft.Extensions.Logging;
using AzFilesOptimizer.Backend.Models;

namespace AzFilesOptimizer.Backend.Services;

/// <summary>
/// Calculates ANF costs based on specific permutation (1-11)
/// Implements formulas from docs/STORAGE_TIER_COST_PERMUTATIONS.md
/// </summary>
public class AnfPermutationCostCalculator
{
    private readonly AzureRetailPricesClient _pricesClient;
    private readonly ILogger<AnfPermutationCostCalculator> _logger;

    public AnfPermutationCostCalculator(
        AzureRetailPricesClient pricesClient,
        ILogger<AnfPermutationCostCalculator> logger)
    {
        _pricesClient = pricesClient;
        _logger = logger;
    }

    /// <summary>
    /// Calculate cost for an ANF volume using the universal inputs format
    /// </summary>
    public async Task<AnfCostEstimate> CalculateAsync(UniversalAnfCostInputs inputs)
    {
        var validationErrors = inputs.Validate();
        if (validationErrors.Any())
        {
            _logger.LogWarning("Cost calculation validation failed for {Volume}: {Errors}",
                inputs.VolumeName, string.Join(", ", validationErrors));
        }

        var estimate = new AnfCostEstimate
        {
            VolumeId = inputs.VolumeId,
            VolumeName = inputs.VolumeName,
            Region = inputs.Region,
            PermutationId = inputs.PermutationId,
            PermutationName = inputs.Permutation.Name,
            BillingPeriodDays = inputs.BillingPeriodDays,
            BillingPeriodHours = inputs.BillingPeriodHours
        };

        // Get pricing data for this permutation
        var pricing = await GetPermutationPricingAsync(inputs);

        // Calculate cost based on permutation
        estimate = inputs.Permutation.PermutationId switch
        {
            1 => await CalculateStandardRegularAsync(inputs, pricing, estimate),
            2 => await CalculateStandardDoubleEncryptedAsync(inputs, pricing, estimate),
            3 => await CalculateStandardCoolAccessAsync(inputs, pricing, estimate),
            4 => await CalculatePremiumRegularAsync(inputs, pricing, estimate),
            5 => await CalculatePremiumDoubleEncryptedAsync(inputs, pricing, estimate),
            6 => await CalculatePremiumCoolAccessAsync(inputs, pricing, estimate),
            7 => await CalculateUltraRegularAsync(inputs, pricing, estimate),
            8 => await CalculateUltraDoubleEncryptedAsync(inputs, pricing, estimate),
            9 => await CalculateUltraCoolAccessAsync(inputs, pricing, estimate),
            10 => await CalculateFlexibleRegularAsync(inputs, pricing, estimate),
            11 => await CalculateFlexibleCoolAccessAsync(inputs, pricing, estimate),
            _ => throw new NotSupportedException($"Permutation {inputs.PermutationId} not supported")
        };

        estimate.TotalCost = estimate.CostComponents.Sum(c => c.Cost);
        estimate.ValidationErrors = validationErrors;

        _logger.LogInformation("Calculated ANF cost for {Volume} (Permutation {Id}): ${Total} for {Days} days",
            inputs.VolumeName, inputs.PermutationId, estimate.TotalCost, inputs.BillingPeriodDays);

        return estimate;
    }

    // ==================== PERMUTATION 1: ANF Standard (Regular) ====================
    private async Task<AnfCostEstimate> CalculateStandardRegularAsync(
        UniversalAnfCostInputs inputs,
        PermutationPricing pricing,
        AnfCostEstimate estimate)
    {
        // Formula: Provisioned Capacity (GiB) × Standard Capacity Price ($/GiB/hour) × 720
        var capacityCost = inputs.ProvisionedCapacityGiB * pricing.CapacityPricePerGiBHour * inputs.BillingPeriodHours;

        estimate.CostComponents.Add(new AnfCostComponent
        {
            ComponentName = "Capacity",
            ComponentType = "capacity",
            Description = $"Standard capacity ({inputs.ProvisionedCapacityGiB:N2} GiB @ ${pricing.CapacityPricePerGiBHour:F6}/GiB/hour)",
            Quantity = inputs.ProvisionedCapacityGiB,
            Unit = "GiB",
            UnitPrice = pricing.CapacityPricePerGiBHour,
            Hours = inputs.BillingPeriodHours,
            Cost = capacityCost
        });

        estimate.IncludedThroughput = (inputs.ProvisionedCapacityGiB / 1024.0) * 16.0; // 16 MiB/s per TiB
        estimate.Notes.Add("Minimum capacity: 50 GiB");
        estimate.Notes.Add($"Included throughput: {estimate.IncludedThroughput:N2} MiB/s (16 MiB/s per TiB)");
        estimate.Notes.Add("Snapshots consume volume capacity (no separate charge)");

        return await Task.FromResult(estimate);
    }

    // ==================== PERMUTATION 2: ANF Standard with Double Encryption ====================
    private async Task<AnfCostEstimate> CalculateStandardDoubleEncryptedAsync(
        UniversalAnfCostInputs inputs,
        PermutationPricing pricing,
        AnfCostEstimate estimate)
    {
        // Formula: Provisioned Capacity (GiB) × Standard Double Encrypted Capacity Price ($/GiB/hour) × 720
        var capacityCost = inputs.ProvisionedCapacityGiB * pricing.DoubleEncryptedCapacityPricePerGiBHour * inputs.BillingPeriodHours;

        estimate.CostComponents.Add(new AnfCostComponent
        {
            ComponentName = "Capacity (Double Encrypted)",
            ComponentType = "capacity",
            Description = $"Standard double encrypted capacity ({inputs.ProvisionedCapacityGiB:N2} GiB @ ${pricing.DoubleEncryptedCapacityPricePerGiBHour:F6}/GiB/hour)",
            Quantity = inputs.ProvisionedCapacityGiB,
            Unit = "GiB",
            UnitPrice = pricing.DoubleEncryptedCapacityPricePerGiBHour,
            Hours = inputs.BillingPeriodHours,
            Cost = capacityCost
        });

        estimate.IncludedThroughput = (inputs.ProvisionedCapacityGiB / 1024.0) * 16.0;
        estimate.Notes.Add("Double encryption enabled");
        estimate.Notes.Add($"Included throughput: {estimate.IncludedThroughput:N2} MiB/s (16 MiB/s per TiB)");
        estimate.Notes.Add("Double encryption cannot be combined with Cool Access");

        return await Task.FromResult(estimate);
    }

    // ==================== PERMUTATION 3: ANF Standard with Cool Access ====================
    private async Task<AnfCostEstimate> CalculateStandardCoolAccessAsync(
        UniversalAnfCostInputs inputs,
        PermutationPricing pricing,
        AnfCostEstimate estimate)
    {
        // Hot capacity cost
        var hotCapacity = inputs.HotCapacityGiB ?? inputs.ProvisionedCapacityGiB;
        var hotCost = hotCapacity * pricing.CapacityPricePerGiBHour * inputs.BillingPeriodHours;

        estimate.CostComponents.Add(new AnfCostComponent
        {
            ComponentName = "Hot Tier Capacity",
            ComponentType = "capacity_hot",
            Description = $"Standard hot capacity ({hotCapacity:N2} GiB @ ${pricing.CapacityPricePerGiBHour:F6}/GiB/hour)",
            Quantity = hotCapacity,
            Unit = "GiB",
            UnitPrice = pricing.CapacityPricePerGiBHour,
            Hours = inputs.BillingPeriodHours,
            Cost = hotCost
        });

        // Cool capacity cost
        if (inputs.CoolCapacityGiB.HasValue && inputs.CoolCapacityGiB > 0)
        {
            var coolCost = inputs.CoolCapacityGiB.Value * pricing.CoolCapacityPricePerGiBHour * inputs.BillingPeriodHours;
            estimate.CostComponents.Add(new AnfCostComponent
            {
                ComponentName = "Cool Tier Capacity",
                ComponentType = "capacity_cool",
                Description = $"Standard cool capacity ({inputs.CoolCapacityGiB:N2} GiB @ ${pricing.CoolCapacityPricePerGiBHour:F6}/GiB/hour)",
                Quantity = inputs.CoolCapacityGiB.Value,
                Unit = "GiB",
                UnitPrice = pricing.CoolCapacityPricePerGiBHour,
                Hours = inputs.BillingPeriodHours,
                Cost = coolCost
            });
        }

        // Data tiering cost (one-time per GiB moved)
        if (inputs.DataTieredToCoolGiB.HasValue && inputs.DataTieredToCoolGiB > 0)
        {
            var tieringCost = inputs.DataTieredToCoolGiB.Value * pricing.CoolDataTransferPricePerGiB;
            estimate.CostComponents.Add(new AnfCostComponent
            {
                ComponentName = "Data Tiering",
                ComponentType = "cool_tiering",
                Description = $"Data tiered to cool ({inputs.DataTieredToCoolGiB:N2} GiB @ ${pricing.CoolDataTransferPricePerGiB:F4}/GiB)",
                Quantity = inputs.DataTieredToCoolGiB.Value,
                Unit = "GiB",
                UnitPrice = pricing.CoolDataTransferPricePerGiB,
                Cost = tieringCost
            });
        }

        // Data retrieval cost (one-time per GiB retrieved)
        if (inputs.DataRetrievedFromCoolGiB.HasValue && inputs.DataRetrievedFromCoolGiB > 0)
        {
            var retrievalCost = inputs.DataRetrievedFromCoolGiB.Value * pricing.CoolDataTransferPricePerGiB;
            estimate.CostComponents.Add(new AnfCostComponent
            {
                ComponentName = "Data Retrieval",
                ComponentType = "cool_retrieval",
                Description = $"Data retrieved from cool ({inputs.DataRetrievedFromCoolGiB:N2} GiB @ ${pricing.CoolDataTransferPricePerGiB:F4}/GiB)",
                Quantity = inputs.DataRetrievedFromCoolGiB.Value,
                Unit = "GiB",
                UnitPrice = pricing.CoolDataTransferPricePerGiB,
                Cost = retrievalCost
            });
        }

        estimate.IncludedThroughput = (inputs.ProvisionedCapacityGiB / 1024.0) * 16.0;
        estimate.Notes.Add($"Cool access enabled (hot: {hotCapacity:N2} GiB, cool: {inputs.CoolCapacityGiB ?? 0:N2} GiB)");
        estimate.Notes.Add($"Included throughput: {estimate.IncludedThroughput:N2} MiB/s (16 MiB/s per TiB, no reduction)");
        estimate.Notes.Add("Minimum capacity: 2,400 GiB for cool access");

        return await Task.FromResult(estimate);
    }

    // ==================== PERMUTATION 4: ANF Premium (Regular) ====================
    private async Task<AnfCostEstimate> CalculatePremiumRegularAsync(
        UniversalAnfCostInputs inputs,
        PermutationPricing pricing,
        AnfCostEstimate estimate)
    {
        var capacityCost = inputs.ProvisionedCapacityGiB * pricing.CapacityPricePerGiBHour * inputs.BillingPeriodHours;

        estimate.CostComponents.Add(new AnfCostComponent
        {
            ComponentName = "Capacity",
            ComponentType = "capacity",
            Description = $"Premium capacity ({inputs.ProvisionedCapacityGiB:N2} GiB @ ${pricing.CapacityPricePerGiBHour:F6}/GiB/hour)",
            Quantity = inputs.ProvisionedCapacityGiB,
            Unit = "GiB",
            UnitPrice = pricing.CapacityPricePerGiBHour,
            Hours = inputs.BillingPeriodHours,
            Cost = capacityCost
        });

        estimate.IncludedThroughput = (inputs.ProvisionedCapacityGiB / 1024.0) * 64.0; // 64 MiB/s per TiB
        estimate.Notes.Add($"Included throughput: {estimate.IncludedThroughput:N2} MiB/s (64 MiB/s per TiB)");

        return await Task.FromResult(estimate);
    }

    // ==================== PERMUTATION 5: ANF Premium with Double Encryption ====================
    private async Task<AnfCostEstimate> CalculatePremiumDoubleEncryptedAsync(
        UniversalAnfCostInputs inputs,
        PermutationPricing pricing,
        AnfCostEstimate estimate)
    {
        var capacityCost = inputs.ProvisionedCapacityGiB * pricing.DoubleEncryptedCapacityPricePerGiBHour * inputs.BillingPeriodHours;

        estimate.CostComponents.Add(new AnfCostComponent
        {
            ComponentName = "Capacity (Double Encrypted)",
            ComponentType = "capacity",
            Description = $"Premium double encrypted capacity ({inputs.ProvisionedCapacityGiB:N2} GiB @ ${pricing.DoubleEncryptedCapacityPricePerGiBHour:F6}/GiB/hour)",
            Quantity = inputs.ProvisionedCapacityGiB,
            Unit = "GiB",
            UnitPrice = pricing.DoubleEncryptedCapacityPricePerGiBHour,
            Hours = inputs.BillingPeriodHours,
            Cost = capacityCost
        });

        estimate.IncludedThroughput = (inputs.ProvisionedCapacityGiB / 1024.0) * 64.0;
        estimate.Notes.Add("Double encryption enabled");
        estimate.Notes.Add($"Included throughput: {estimate.IncludedThroughput:N2} MiB/s (64 MiB/s per TiB)");

        return await Task.FromResult(estimate);
    }

    // ==================== PERMUTATION 6: ANF Premium with Cool Access ====================
    private async Task<AnfCostEstimate> CalculatePremiumCoolAccessAsync(
        UniversalAnfCostInputs inputs,
        PermutationPricing pricing,
        AnfCostEstimate estimate)
    {
        // Similar to Standard cool access but with different throughput
        var hotCapacity = inputs.HotCapacityGiB ?? inputs.ProvisionedCapacityGiB;
        var hotCost = hotCapacity * pricing.CapacityPricePerGiBHour * inputs.BillingPeriodHours;

        estimate.CostComponents.Add(new AnfCostComponent
        {
            ComponentName = "Hot Tier Capacity",
            ComponentType = "capacity_hot",
            Description = $"Premium hot capacity ({hotCapacity:N2} GiB @ ${pricing.CapacityPricePerGiBHour:F6}/GiB/hour)",
            Quantity = hotCapacity,
            Unit = "GiB",
            UnitPrice = pricing.CapacityPricePerGiBHour,
            Hours = inputs.BillingPeriodHours,
            Cost = hotCost
        });

        if (inputs.CoolCapacityGiB.HasValue && inputs.CoolCapacityGiB > 0)
        {
            var coolCost = inputs.CoolCapacityGiB.Value * pricing.CoolCapacityPricePerGiBHour * inputs.BillingPeriodHours;
            estimate.CostComponents.Add(new AnfCostComponent
            {
                ComponentName = "Cool Tier Capacity",
                ComponentType = "capacity_cool",
                Description = $"Premium cool capacity ({inputs.CoolCapacityGiB:N2} GiB @ ${pricing.CoolCapacityPricePerGiBHour:F6}/GiB/hour)",
                Quantity = inputs.CoolCapacityGiB.Value,
                Unit = "GiB",
                UnitPrice = pricing.CoolCapacityPricePerGiBHour,
                Hours = inputs.BillingPeriodHours,
                Cost = coolCost
            });
        }

        AddCoolAccessDataTransfers(inputs, pricing, estimate);

        estimate.IncludedThroughput = (inputs.ProvisionedCapacityGiB / 1024.0) * 36.0; // Reduced from 64
        estimate.Notes.Add($"Cool access enabled - throughput reduced to {estimate.IncludedThroughput:N2} MiB/s (36 MiB/s per TiB)");

        return await Task.FromResult(estimate);
    }

    // ==================== PERMUTATION 7-9: Ultra tier (similar to Premium) ====================
    private async Task<AnfCostEstimate> CalculateUltraRegularAsync(
        UniversalAnfCostInputs inputs,
        PermutationPricing pricing,
        AnfCostEstimate estimate)
    {
        var capacityCost = inputs.ProvisionedCapacityGiB * pricing.CapacityPricePerGiBHour * inputs.BillingPeriodHours;

        estimate.CostComponents.Add(new AnfCostComponent
        {
            ComponentName = "Capacity",
            ComponentType = "capacity",
            Description = $"Ultra capacity ({inputs.ProvisionedCapacityGiB:N2} GiB @ ${pricing.CapacityPricePerGiBHour:F6}/GiB/hour)",
            Quantity = inputs.ProvisionedCapacityGiB,
            Unit = "GiB",
            UnitPrice = pricing.CapacityPricePerGiBHour,
            Hours = inputs.BillingPeriodHours,
            Cost = capacityCost
        });

        estimate.IncludedThroughput = (inputs.ProvisionedCapacityGiB / 1024.0) * 128.0; // 128 MiB/s per TiB
        estimate.Notes.Add($"Included throughput: {estimate.IncludedThroughput:N2} MiB/s (128 MiB/s per TiB)");

        return await Task.FromResult(estimate);
    }

    private async Task<AnfCostEstimate> CalculateUltraDoubleEncryptedAsync(
        UniversalAnfCostInputs inputs,
        PermutationPricing pricing,
        AnfCostEstimate estimate)
    {
        var capacityCost = inputs.ProvisionedCapacityGiB * pricing.DoubleEncryptedCapacityPricePerGiBHour * inputs.BillingPeriodHours;

        estimate.CostComponents.Add(new AnfCostComponent
        {
            ComponentName = "Capacity (Double Encrypted)",
            ComponentType = "capacity",
            Description = $"Ultra double encrypted capacity ({inputs.ProvisionedCapacityGiB:N2} GiB @ ${pricing.DoubleEncryptedCapacityPricePerGiBHour:F6}/GiB/hour)",
            Quantity = inputs.ProvisionedCapacityGiB,
            Unit = "GiB",
            UnitPrice = pricing.DoubleEncryptedCapacityPricePerGiBHour,
            Hours = inputs.BillingPeriodHours,
            Cost = capacityCost
        });

        estimate.IncludedThroughput = (inputs.ProvisionedCapacityGiB / 1024.0) * 128.0;
        estimate.Notes.Add("Double encryption enabled");
        estimate.Notes.Add($"Included throughput: {estimate.IncludedThroughput:N2} MiB/s (128 MiB/s per TiB)");

        return await Task.FromResult(estimate);
    }

    private async Task<AnfCostEstimate> CalculateUltraCoolAccessAsync(
        UniversalAnfCostInputs inputs,
        PermutationPricing pricing,
        AnfCostEstimate estimate)
    {
        var hotCapacity = inputs.HotCapacityGiB ?? inputs.ProvisionedCapacityGiB;
        var hotCost = hotCapacity * pricing.CapacityPricePerGiBHour * inputs.BillingPeriodHours;

        estimate.CostComponents.Add(new AnfCostComponent
        {
            ComponentName = "Hot Tier Capacity",
            ComponentType = "capacity_hot",
            Description = $"Ultra hot capacity ({hotCapacity:N2} GiB @ ${pricing.CapacityPricePerGiBHour:F6}/GiB/hour)",
            Quantity = hotCapacity,
            Unit = "GiB",
            UnitPrice = pricing.CapacityPricePerGiBHour,
            Hours = inputs.BillingPeriodHours,
            Cost = hotCost
        });

        if (inputs.CoolCapacityGiB.HasValue && inputs.CoolCapacityGiB > 0)
        {
            var coolCost = inputs.CoolCapacityGiB.Value * pricing.CoolCapacityPricePerGiBHour * inputs.BillingPeriodHours;
            estimate.CostComponents.Add(new AnfCostComponent
            {
                ComponentName = "Cool Tier Capacity",
                ComponentType = "capacity_cool",
                Description = $"Ultra cool capacity ({inputs.CoolCapacityGiB:N2} GiB @ ${pricing.CoolCapacityPricePerGiBHour:F6}/GiB/hour)",
                Quantity = inputs.CoolCapacityGiB.Value,
                Unit = "GiB",
                UnitPrice = pricing.CoolCapacityPricePerGiBHour,
                Hours = inputs.BillingPeriodHours,
                Cost = coolCost
            });
        }

        AddCoolAccessDataTransfers(inputs, pricing, estimate);

        estimate.IncludedThroughput = (inputs.ProvisionedCapacityGiB / 1024.0) * 68.0; // Reduced from 128
        estimate.Notes.Add($"Cool access enabled - throughput reduced to {estimate.IncludedThroughput:N2} MiB/s (68 MiB/s per TiB)");

        return await Task.FromResult(estimate);
    }

    // ==================== PERMUTATION 10: ANF Flexible (Regular) ====================
    private async Task<AnfCostEstimate> CalculateFlexibleRegularAsync(
        UniversalAnfCostInputs inputs,
        PermutationPricing pricing,
        AnfCostEstimate estimate)
    {
        // Capacity cost
        var capacityCost = inputs.ProvisionedCapacityGiB * pricing.CapacityPricePerGiBHour * inputs.BillingPeriodHours;
        estimate.CostComponents.Add(new AnfCostComponent
        {
            ComponentName = "Capacity",
            ComponentType = "capacity",
            Description = $"Flexible capacity ({inputs.ProvisionedCapacityGiB:N2} GiB @ ${pricing.CapacityPricePerGiBHour:F6}/GiB/hour)",
            Quantity = inputs.ProvisionedCapacityGiB,
            Unit = "GiB",
            UnitPrice = pricing.CapacityPricePerGiBHour,
            Hours = inputs.BillingPeriodHours,
            Cost = capacityCost
        });

        // Throughput cost (if above base 128 MiB/s)
        var throughputAboveBase = inputs.ThroughputAboveBaseGiBps;
        if (throughputAboveBase > 0)
        {
            var throughputCost = throughputAboveBase * pricing.FlexibleThroughputPricePerMiBpsHour * inputs.BillingPeriodHours;
            estimate.CostComponents.Add(new AnfCostComponent
            {
                ComponentName = "Throughput",
                ComponentType = "throughput",
                Description = $"Throughput above base ({throughputAboveBase:N2} MiB/s @ ${pricing.FlexibleThroughputPricePerMiBpsHour:F6}/MiB/s/hour)",
                Quantity = throughputAboveBase,
                Unit = "MiB/s",
                UnitPrice = pricing.FlexibleThroughputPricePerMiBpsHour,
                Hours = inputs.BillingPeriodHours,
                Cost = throughputCost
            });
        }

        estimate.IncludedThroughput = 128.0; // Flat 128 MiB/s base
        estimate.RequiredThroughput = inputs.RequiredThroughputMiBps ?? 0;
        estimate.Notes.Add("Base included throughput: 128 MiB/s (flat, not per TiB)");
        estimate.Notes.Add($"Required throughput: {estimate.RequiredThroughput:N2} MiB/s");

        return await Task.FromResult(estimate);
    }

    // ==================== PERMUTATION 11: ANF Flexible with Cool Access ====================
    private async Task<AnfCostEstimate> CalculateFlexibleCoolAccessAsync(
        UniversalAnfCostInputs inputs,
        PermutationPricing pricing,
        AnfCostEstimate estimate)
    {
        // Hot capacity
        var hotCapacity = inputs.HotCapacityGiB ?? inputs.ProvisionedCapacityGiB;
        var hotCost = hotCapacity * pricing.CapacityPricePerGiBHour * inputs.BillingPeriodHours;
        estimate.CostComponents.Add(new AnfCostComponent
        {
            ComponentName = "Hot Tier Capacity",
            ComponentType = "capacity_hot",
            Description = $"Flexible hot capacity ({hotCapacity:N2} GiB @ ${pricing.CapacityPricePerGiBHour:F6}/GiB/hour)",
            Quantity = hotCapacity,
            Unit = "GiB",
            UnitPrice = pricing.CapacityPricePerGiBHour,
            Hours = inputs.BillingPeriodHours,
            Cost = hotCost
        });

        // Cool capacity
        if (inputs.CoolCapacityGiB.HasValue && inputs.CoolCapacityGiB > 0)
        {
            var coolCost = inputs.CoolCapacityGiB.Value * pricing.CoolCapacityPricePerGiBHour * inputs.BillingPeriodHours;
            estimate.CostComponents.Add(new AnfCostComponent
            {
                ComponentName = "Cool Tier Capacity",
                ComponentType = "capacity_cool",
                Description = $"Flexible cool capacity ({inputs.CoolCapacityGiB:N2} GiB @ ${pricing.CoolCapacityPricePerGiBHour:F6}/GiB/hour)",
                Quantity = inputs.CoolCapacityGiB.Value,
                Unit = "GiB",
                UnitPrice = pricing.CoolCapacityPricePerGiBHour,
                Hours = inputs.BillingPeriodHours,
                Cost = coolCost
            });
        }

        AddCoolAccessDataTransfers(inputs, pricing, estimate);

        // Throughput cost (independent of cool access)
        var throughputAboveBase = inputs.ThroughputAboveBaseGiBps;
        if (throughputAboveBase > 0)
        {
            var throughputCost = throughputAboveBase * pricing.FlexibleThroughputPricePerMiBpsHour * inputs.BillingPeriodHours;
            estimate.CostComponents.Add(new AnfCostComponent
            {
                ComponentName = "Throughput",
                ComponentType = "throughput",
                Description = $"Throughput above base ({throughputAboveBase:N2} MiB/s @ ${pricing.FlexibleThroughputPricePerMiBpsHour:F6}/MiB/s/hour)",
                Quantity = throughputAboveBase,
                Unit = "MiB/s",
                UnitPrice = pricing.FlexibleThroughputPricePerMiBpsHour,
                Hours = inputs.BillingPeriodHours,
                Cost = throughputCost
            });
        }

        estimate.IncludedThroughput = 128.0;
        estimate.RequiredThroughput = inputs.RequiredThroughputMiBps ?? 0;
        estimate.Notes.Add("Cool access enabled with independent throughput pricing");
        estimate.Notes.Add("Base included throughput: 128 MiB/s");

        return await Task.FromResult(estimate);
    }

    // ==================== Helper methods ====================

    private void AddCoolAccessDataTransfers(UniversalAnfCostInputs inputs, PermutationPricing pricing, AnfCostEstimate estimate)
    {
        if (inputs.DataTieredToCoolGiB.HasValue && inputs.DataTieredToCoolGiB > 0)
        {
            var tieringCost = inputs.DataTieredToCoolGiB.Value * pricing.CoolDataTransferPricePerGiB;
            estimate.CostComponents.Add(new AnfCostComponent
            {
                ComponentName = "Data Tiering",
                ComponentType = "cool_tiering",
                Description = $"Data tiered to cool ({inputs.DataTieredToCoolGiB:N2} GiB @ ${pricing.CoolDataTransferPricePerGiB:F4}/GiB)",
                Quantity = inputs.DataTieredToCoolGiB.Value,
                Unit = "GiB",
                UnitPrice = pricing.CoolDataTransferPricePerGiB,
                Cost = tieringCost
            });
        }

        if (inputs.DataRetrievedFromCoolGiB.HasValue && inputs.DataRetrievedFromCoolGiB > 0)
        {
            var retrievalCost = inputs.DataRetrievedFromCoolGiB.Value * pricing.CoolDataTransferPricePerGiB;
            estimate.CostComponents.Add(new AnfCostComponent
            {
                ComponentName = "Data Retrieval",
                ComponentType = "cool_retrieval",
                Description = $"Data retrieved from cool ({inputs.DataRetrievedFromCoolGiB:N2} GiB @ ${pricing.CoolDataTransferPricePerGiB:F4}/GiB)",
                Quantity = inputs.DataRetrievedFromCoolGiB.Value,
                Unit = "GiB",
                UnitPrice = pricing.CoolDataTransferPricePerGiB,
                Cost = retrievalCost
            });
        }
    }

    private async Task<PermutationPricing> GetPermutationPricingAsync(UniversalAnfCostInputs inputs)
    {
        var pricing = new PermutationPricing();
        var permutation = inputs.Permutation;

        // Get capacity pricing based on permutation
        if (permutation.DoubleEncryptionEnabled)
        {
            // Query for double encrypted pricing
            var meterName = $"{permutation.ServiceLevel} Double Encrypted Capacity";
            var prices = await _pricesClient.GetAnfPricingAsync(inputs.Region, permutation.ServiceLevel);
            var doubleEncPrice = prices.FirstOrDefault(p => 
                p.MeterName.Contains("Double Encrypted", StringComparison.OrdinalIgnoreCase));
            
            if (doubleEncPrice != null)
            {
                pricing.DoubleEncryptedCapacityPricePerGiBHour = doubleEncPrice.RetailPrice;
            }
        }
        else
        {
            // Regular capacity pricing
            var prices = await _pricesClient.GetAnfPricingAsync(inputs.Region, permutation.ServiceLevel);
            var capacityPrice = prices.FirstOrDefault(p =>
                p.MeterName.Contains("Capacity", StringComparison.OrdinalIgnoreCase) &&
                !p.MeterName.Contains("Cool", StringComparison.OrdinalIgnoreCase) &&
                !p.MeterName.Contains("Double", StringComparison.OrdinalIgnoreCase));
            
            if (capacityPrice != null)
            {
                pricing.CapacityPricePerGiBHour = capacityPrice.RetailPrice;
            }
        }

        // Get cool access pricing if applicable
        if (permutation.CoolAccessEnabled)
        {
            var coolPrices = await _pricesClient.GetAnfPricingAsync(inputs.Region, "Standard");
            
            var coolCapacity = coolPrices.FirstOrDefault(p =>
                p.MeterName.Contains("Cool", StringComparison.OrdinalIgnoreCase) &&
                p.MeterName.Contains("Capacity", StringComparison.OrdinalIgnoreCase));
            if (coolCapacity != null)
            {
                pricing.CoolCapacityPricePerGiBHour = coolCapacity.RetailPrice;
            }

            var coolTransfer = coolPrices.FirstOrDefault(p =>
                p.MeterName.Contains("Cool", StringComparison.OrdinalIgnoreCase) &&
                p.MeterName.Contains("Transfer", StringComparison.OrdinalIgnoreCase));
            if (coolTransfer != null)
            {
                pricing.CoolDataTransferPricePerGiB = coolTransfer.RetailPrice;
            }
        }

        // Get Flexible throughput pricing if applicable
        if (permutation.ServiceLevel == "Flexible")
        {
            var flexPrices = await _pricesClient.GetAnfPricingAsync(inputs.Region, "Flexible");
            var throughputPrice = flexPrices.FirstOrDefault(p =>
                p.MeterName.Contains("Throughput", StringComparison.OrdinalIgnoreCase) &&
                p.MeterName.Contains("MiBps", StringComparison.OrdinalIgnoreCase));
            
            if (throughputPrice != null)
            {
                pricing.FlexibleThroughputPricePerMiBpsHour = throughputPrice.RetailPrice;
            }
        }

        return pricing;
    }
}

/// <summary>
/// Pricing data for a specific ANF permutation
/// </summary>
public class PermutationPricing
{
    public double CapacityPricePerGiBHour { get; set; }
    public double DoubleEncryptedCapacityPricePerGiBHour { get; set; }
    public double CoolCapacityPricePerGiBHour { get; set; }
    public double CoolDataTransferPricePerGiB { get; set; }
    public double FlexibleThroughputPricePerMiBpsHour { get; set; }
}

/// <summary>
/// ANF cost estimate result
/// </summary>
public class AnfCostEstimate
{
    public string VolumeId { get; set; } = string.Empty;
    public string VolumeName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public int PermutationId { get; set; }
    public string PermutationName { get; set; } = string.Empty;
    public int BillingPeriodDays { get; set; }
    public int BillingPeriodHours { get; set; }
    public double TotalCost { get; set; }
    public double IncludedThroughput { get; set; }
    public double RequiredThroughput { get; set; }
    public List<AnfCostComponent> CostComponents { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public List<string> ValidationErrors { get; set; } = new();
}

/// <summary>
/// Individual cost component in ANF estimate
/// </summary>
public class AnfCostComponent
{
    public string ComponentName { get; set; } = string.Empty;
    public string ComponentType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public double UnitPrice { get; set; }
    public int Hours { get; set; }
    public double Cost { get; set; }
}
