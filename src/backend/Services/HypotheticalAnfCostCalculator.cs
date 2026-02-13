using Microsoft.Extensions.Logging;
using AzFilesOptimizer.Backend.Models;

namespace AzFilesOptimizer.Backend.Services;

/// <summary>
/// Calculates hypothetical cost for migrating any volume to ANF Flexible Tier
/// Used for "what-if" analysis across Azure Files, ANF, and Managed Disk volumes
/// </summary>
public class HypotheticalAnfCostCalculator
{
    private readonly AzureRetailPricesClient _pricesClient;
    private readonly CoolDataAssumptionsService _assumptionsService;
    private readonly ILogger<HypotheticalAnfCostCalculator> _logger;

    public HypotheticalAnfCostCalculator(
        AzureRetailPricesClient pricesClient,
        CoolDataAssumptionsService assumptionsService,
        ILogger<HypotheticalAnfCostCalculator> logger)
    {
        _pricesClient = pricesClient;
        _assumptionsService = assumptionsService;
        _logger = logger;
    }

    /// <summary>
    /// Calculate hypothetical cost for an ANF Flexible volume with given capacity and throughput
    /// </summary>
    public async Task<HypotheticalCostResult> CalculateFlexibleTierCostAsync(
        double requiredCapacityGiB,
        double requiredThroughputMiBps,
        string region,
        bool coolAccessEnabled,
        CoolDataAssumptions? assumptions = null,
        string? volumeId = null,
        string? jobId = null)
    {
        var result = new HypotheticalCostResult
        {
            CoolAccessEnabled = coolAccessEnabled,
            CostComponents = new List<CostComponentEstimate>(),
            CalculationNotes = ""
        };

        try
        {
            // Get ANF Flexible pricing for region
            var pricing = await _pricesClient.GetAnfPricingAsync(region, "Flexible");
            if (!pricing.Any())
            {
                result.CalculationNotes = $"No ANF Flexible pricing available for {region}";
                result.TotalMonthlyCost = 0;
                return result;
            }

            // Apply minimum capacity constraints
            var adjustedCapacity = ApplyCapacityMinimums(requiredCapacityGiB, coolAccessEnabled, result);

            // Calculate capacity cost
            await CalculateCapacityCostAsync(adjustedCapacity, coolAccessEnabled, region, pricing, result, assumptions, volumeId, jobId);

            // Calculate throughput cost (Flexible has separate throughput pricing)
            await CalculateThroughputCostAsync(requiredThroughputMiBps, pricing, result);

            // Sum total cost
            result.TotalMonthlyCost = result.CostComponents.Sum(c => c.EstimatedCost);

            _logger.LogInformation("Calculated hypothetical ANF Flexible cost: ${Cost:F2} for {Capacity:F2} GiB, {Throughput:F2} MiB/s, Cool={Cool}",
                result.TotalMonthlyCost, adjustedCapacity, requiredThroughputMiBps, coolAccessEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate hypothetical ANF Flexible cost");
            result.CalculationNotes = $"Calculation error: {ex.Message}";
            result.TotalMonthlyCost = 0;
        }

        return result;
    }

    /// <summary>
    /// Apply ANF capacity minimums: 50 GiB regular, 2400 GiB with cool access
    /// </summary>
    private double ApplyCapacityMinimums(double requestedCapacity, bool coolAccessEnabled, HypotheticalCostResult result)
    {
        double minCapacity = coolAccessEnabled ? 2400.0 : 50.0;
        
        if (requestedCapacity < minCapacity)
        {
            result.CalculationNotes += $"Applied minimum capacity: {minCapacity:N0} GiB (requested: {requestedCapacity:N2} GiB). ";
            return minCapacity;
        }

        return requestedCapacity;
    }

    /// <summary>
    /// Calculate capacity cost with optional cool tier breakdown
    /// </summary>
    private async Task CalculateCapacityCostAsync(
        double capacityGiB,
        bool coolAccessEnabled,
        string region,
        List<PriceItem> pricing,
        HypotheticalCostResult result,
        CoolDataAssumptions? assumptions,
        string? volumeId,
        string? jobId)
    {
        // Get capacity price (per GiB/hour)
        var capacityPrice = pricing.FirstOrDefault(p =>
            p.MeterName.Contains("Capacity", StringComparison.OrdinalIgnoreCase) &&
            !p.MeterName.Contains("Cool", StringComparison.OrdinalIgnoreCase));

        if (capacityPrice == null)
        {
            result.CalculationNotes += "Capacity pricing not found. ";
            return;
        }

        if (!coolAccessEnabled)
        {
            // Simple capacity cost (all hot tier)
            var monthlyCost = capacityGiB * capacityPrice.RetailPrice * 730; // 730 hours/month average

            result.CostComponents.Add(new CostComponentEstimate
            {
                ComponentType = "storage",
                Description = $"Flexible capacity ({capacityGiB:N2} GiB)",
                Quantity = capacityGiB,
                Unit = "GiB",
                UnitPrice = capacityPrice.RetailPrice,
                EstimatedCost = monthlyCost,
                DataSource = "Azure Retail Prices API"
            });

            result.CalculationNotes += $"Included throughput: 128 MiB/s base. ";
        }
        else
        {
            // Cool access enabled - need to split hot/cool and calculate tiering/retrieval
            var resolvedAssumptions = assumptions ?? await ResolveAssumptionsAsync(volumeId, jobId);
            result.AppliedAssumptions = resolvedAssumptions;

            await CalculateCoolAccessCostsAsync(capacityGiB, region, pricing, resolvedAssumptions, result);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Calculate cool access costs: hot/cool capacity, tiering, and retrieval
    /// </summary>
    private async Task CalculateCoolAccessCostsAsync(
        double totalCapacityGiB,
        string region,
        List<PriceItem> pricing,
        CoolDataAssumptions assumptions,
        HypotheticalCostResult result)
    {
        // Split capacity into hot and cool based on assumptions
        double coolDataGiB = totalCapacityGiB * (assumptions.CoolDataPercentage / 100.0);
        double hotDataGiB = totalCapacityGiB - coolDataGiB;

        // Hot tier capacity cost
        var hotCapacityPrice = pricing.FirstOrDefault(p =>
            p.MeterName.Contains("Capacity", StringComparison.OrdinalIgnoreCase) &&
            !p.MeterName.Contains("Cool", StringComparison.OrdinalIgnoreCase));

        if (hotCapacityPrice != null)
        {
            var hotCost = hotDataGiB * hotCapacityPrice.RetailPrice * 730;
            result.CostComponents.Add(new CostComponentEstimate
            {
                ComponentType = "storage_hot",
                Description = $"Hot tier capacity ({hotDataGiB:N2} GiB, {100 - assumptions.CoolDataPercentage:N0}% of total)",
                Quantity = hotDataGiB,
                Unit = "GiB",
                UnitPrice = hotCapacityPrice.RetailPrice,
                EstimatedCost = hotCost,
                DataSource = "Azure Retail Prices API"
            });
        }

        // Cool tier capacity cost (significantly lower)
        var coolCapacityPrice = pricing.FirstOrDefault(p =>
            p.MeterName.Contains("Cool", StringComparison.OrdinalIgnoreCase) &&
            p.MeterName.Contains("Storage", StringComparison.OrdinalIgnoreCase));

        if (coolCapacityPrice != null && coolDataGiB > 0)
        {
            var coolCost = coolDataGiB * coolCapacityPrice.RetailPrice * 730;
            result.CostComponents.Add(new CostComponentEstimate
            {
                ComponentType = "storage_cool",
                Description = $"Cool tier capacity ({coolDataGiB:N2} GiB, {assumptions.CoolDataPercentage:N0}% of total)",
                Quantity = coolDataGiB,
                Unit = "GiB",
                UnitPrice = coolCapacityPrice.RetailPrice,
                EstimatedCost = coolCost,
                DataSource = "Azure Retail Prices API"
            });
        }

        // Data retrieval cost (cool → hot reads)
        var retrievalPrice = pricing.FirstOrDefault(p =>
            p.MeterName.Contains("Retrieval", StringComparison.OrdinalIgnoreCase) ||
            (p.MeterName.Contains("Cool", StringComparison.OrdinalIgnoreCase) &&
             p.MeterName.Contains("Read", StringComparison.OrdinalIgnoreCase)));

        if (retrievalPrice != null && coolDataGiB > 0)
        {
            double retrievedGiB = coolDataGiB * (assumptions.CoolDataRetrievalPercentage / 100.0);
            var retrievalCost = retrievedGiB * retrievalPrice.RetailPrice;

            result.CostComponents.Add(new CostComponentEstimate
            {
                ComponentType = "cool_retrieval",
                Description = $"Cool data retrieval ({retrievedGiB:N2} GiB, {assumptions.CoolDataRetrievalPercentage:N0}% of cool data)",
                Quantity = retrievedGiB,
                Unit = "GiB",
                UnitPrice = retrievalPrice.RetailPrice,
                EstimatedCost = retrievalCost,
                DataSource = "Azure Retail Prices API"
            });
        }

        result.CalculationNotes += $"Cool assumptions: {assumptions.CoolDataPercentage:N0}% cool data, {assumptions.CoolDataRetrievalPercentage:N0}% retrieval. ";

        await Task.CompletedTask;
    }

    /// <summary>
    /// Calculate throughput cost for Flexible tier (separate from capacity)
    /// </summary>
    private async Task CalculateThroughputCostAsync(
        double requiredThroughputMiBps,
        List<PriceItem> pricing,
        HypotheticalCostResult result)
    {
        // Flexible tier includes 128 MiB/s base throughput
        const double baseThroughput = 128.0;

        if (requiredThroughputMiBps <= baseThroughput)
        {
            result.CalculationNotes += $"Required throughput ({requiredThroughputMiBps:N2} MiB/s) within base 128 MiB/s (no additional charge). ";
            return;
        }

        // Additional throughput beyond base
        double additionalThroughput = requiredThroughputMiBps - baseThroughput;

        var throughputPrice = pricing.FirstOrDefault(p =>
            p.MeterName.Contains("Throughput", StringComparison.OrdinalIgnoreCase) ||
            p.MeterName.Contains("Bandwidth", StringComparison.OrdinalIgnoreCase));

        if (throughputPrice != null)
        {
            var throughputCost = additionalThroughput * throughputPrice.RetailPrice * 730; // 730 hours/month

            result.CostComponents.Add(new CostComponentEstimate
            {
                ComponentType = "throughput",
                Description = $"Additional throughput ({additionalThroughput:N2} MiB/s beyond 128 MiB/s base)",
                Quantity = additionalThroughput,
                Unit = "MiB/s",
                UnitPrice = throughputPrice.RetailPrice,
                EstimatedCost = throughputCost,
                DataSource = "Azure Retail Prices API"
            });

            result.CalculationNotes += $"Additional throughput: {additionalThroughput:N2} MiB/s charged separately. ";
        }
        else
        {
            result.CalculationNotes += "Throughput pricing not found (assuming included in capacity). ";
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Resolve cool data assumptions using hierarchy: Volume → Job → Global
    /// </summary>
    private async Task<CoolDataAssumptions> ResolveAssumptionsAsync(string? volumeId, string? jobId)
    {
        // Try volume-specific first
        if (!string.IsNullOrEmpty(volumeId) && !string.IsNullOrEmpty(jobId))
        {
            var volumeAssumptions = await _assumptionsService.GetVolumeAssumptionsAsync(jobId, volumeId);
            if (volumeAssumptions != null && volumeAssumptions.Source == AssumptionSource.Volume)
            {
                return volumeAssumptions;
            }
        }

        // Try job-level
        if (!string.IsNullOrEmpty(jobId))
        {
            var jobAssumptions = await _assumptionsService.GetJobAssumptionsAsync(jobId);
            if (jobAssumptions != null && jobAssumptions.Source == AssumptionSource.Job)
            {
                return jobAssumptions;
            }
        }

        // Fall back to global defaults
        return await _assumptionsService.GetGlobalDefaultsAsync();
    }
}
