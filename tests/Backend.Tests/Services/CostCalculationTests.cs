using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using AzFilesOptimizer.Backend.Services;
using AzFilesOptimizer.Backend.Models;

namespace AzFilesOptimizer.Backend.Tests.Services;

public class AzureFilesCostCalculatorTests
{
    private readonly Mock<AzureRetailPricesClient> _mockPricesClient;
    private readonly Mock<ILogger<AzureFilesCostCalculator>> _mockLogger;
    private readonly AzureFilesCostCalculator _calculator;

    public AzureFilesCostCalculatorTests()
    {
        _mockPricesClient = new Mock<AzureRetailPricesClient>(
            Mock.Of<HttpClient>(),
            Mock.Of<IMemoryCache>(),
            Mock.Of<ILogger<AzureRetailPricesClient>>());
        
        _mockLogger = new Mock<ILogger<AzureFilesCostCalculator>>();
        _calculator = new AzureFilesCostCalculator(_mockPricesClient.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task CalculateAsync_ProvisionedShare_ReturnsAccurateEstimate()
    {
        // Arrange
        var volumeInfo = new AzureFilesVolumeInfo
        {
            VolumeId = "test-volume",
            VolumeName = "Test Share",
            Region = "eastus",
            IsProvisioned = true,
            Redundancy = "LRS",
            ProvisionedCapacityGb = 500,
            SnapshotSizeGb = 50
        };

        var mockPrices = new List<PriceItem>
        {
            new PriceItem
            {
                MeterName = "Provisioned Capacity",
                RetailPrice = 0.15,
                UnitOfMeasure = "1 GB/Month"
            },
            new PriceItem
            {
                MeterName = "Snapshot",
                RetailPrice = 0.02,
                UnitOfMeasure = "1 GB/Month"
            }
        };

        _mockPricesClient
            .Setup(x => x.GetPremiumFilesPricingAsync("eastus", "LRS"))
            .ReturnsAsync(mockPrices);

        // Act
        var result = await _calculator.CalculateAsync(volumeInfo);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test Share", result.VolumeName);
        Assert.Equal("AzureFile", result.ResourceType);
        Assert.Equal(2, result.CostComponents.Count);
        
        var capacityCost = result.CostComponents.First(c => c.ComponentType == "storage");
        Assert.Equal(500, capacityCost.Quantity);
        Assert.Equal(75, capacityCost.EstimatedCost); // 500 * 0.15
        
        var snapshotCost = result.CostComponents.First(c => c.ComponentType == "snapshots");
        Assert.Equal(50, snapshotCost.Quantity);
        Assert.Equal(1, snapshotCost.EstimatedCost); // 50 * 0.02
        
        Assert.Equal(76, result.TotalEstimatedCost);
        Assert.True(result.ConfidenceLevel >= 80); // High confidence for provisioned
    }

    [Fact]
    public async Task CalculateAsync_PayAsYouGo_WithTransactions_ReturnsCompleteEstimate()
    {
        // Arrange
        var volumeInfo = new AzureFilesVolumeInfo
        {
            VolumeId = "test-volume",
            VolumeName = "Hot Share",
            Region = "westus",
            IsProvisioned = false,
            Tier = "Hot",
            Redundancy = "LRS",
            ProvisionedCapacityGb = 1000,
            UsedCapacityGb = 750,
            TransactionsPerMonth = 10000000, // 10 million
            SnapshotSizeGb = 100
        };

        var mockPrices = new List<PriceItem>
        {
            new PriceItem
            {
                MeterName = "Data Stored",
                RetailPrice = 0.0184,
                UnitOfMeasure = "1 GB/Month"
            },
            new PriceItem
            {
                MeterName = "Write Operations",
                RetailPrice = 0.065,
                UnitOfMeasure = "10K"
            },
            new PriceItem
            {
                MeterName = "Snapshot",
                RetailPrice = 0.017,
                UnitOfMeasure = "1 GB/Month"
            }
        };

        _mockPricesClient
            .Setup(x => x.GetAzureFilesPricingAsync("westus", "Hot", "LRS"))
            .ReturnsAsync(mockPrices);

        // Act
        var result = await _calculator.CalculateAsync(volumeInfo);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.CostComponents.Count);
        
        var storageCost = result.CostComponents.First(c => c.ComponentType == "storage");
        Assert.Equal(750, storageCost.Quantity); // Uses actual usage
        Assert.Equal(13.80, storageCost.EstimatedCost, 2); // 750 * 0.0184
        
        var transactionCost = result.CostComponents.First(c => c.ComponentType == "transactions");
        Assert.Equal(1000, transactionCost.Quantity); // 10M / 10K
        Assert.Equal(65, transactionCost.EstimatedCost); // 1000 * 0.065
        
        Assert.True(result.TotalEstimatedCost > 0);
        Assert.Equal("Pay-as-you-go Pricing", result.EstimationMethod);
    }

    [Fact]
    public async Task CalculateAsync_NoPricingData_ReturnsLowConfidence()
    {
        // Arrange
        var volumeInfo = new AzureFilesVolumeInfo
        {
            VolumeId = "test-volume",
            VolumeName = "Test Share",
            Region = "unknownregion",
            IsProvisioned = false,
            Tier = "Hot",
            Redundancy = "LRS",
            ProvisionedCapacityGb = 100
        };

        _mockPricesClient
            .Setup(x => x.GetAzureFilesPricingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<PriceItem>());

        // Act
        var result = await _calculator.CalculateAsync(volumeInfo);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.CostComponents);
        Assert.True(result.ConfidenceLevel < 50);
        Assert.Contains(result.Warnings, w => w.Contains("No pricing data"));
    }
}

public class AnfCostCalculatorTests
{
    private readonly Mock<AzureRetailPricesClient> _mockPricesClient;
    private readonly Mock<ILogger<AnfCostCalculator>> _mockLogger;
    private readonly AnfCostCalculator _calculator;

    public AnfCostCalculatorTests()
    {
        _mockPricesClient = new Mock<AzureRetailPricesClient>(
            Mock.Of<HttpClient>(),
            Mock.Of<IMemoryCache>(),
            Mock.Of<ILogger<AzureRetailPricesClient>>());
        
        _mockLogger = new Mock<ILogger<AnfCostCalculator>>();
        _calculator = new AnfCostCalculator(_mockPricesClient.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task CalculateAsync_StandardTier_ReturnsCorrectThroughput()
    {
        // Arrange
        var volumeInfo = new AnfVolumeInfo
        {
            VolumeId = "test-volume",
            VolumeName = "ANF Volume",
            Region = "eastus",
            ServiceLevel = "Standard",
            ProvisionedCapacityGb = 4096, // 4 TiB
            CoolAccessEnabled = false
        };

        var mockPrices = new List<PriceItem>
        {
            new PriceItem
            {
                MeterName = "Provisioned Capacity",
                RetailPrice = 0.000202,
                UnitOfMeasure = "1 GB/Month"
            }
        };

        _mockPricesClient
            .Setup(x => x.GetAnfPricingAsync("eastus", "Standard"))
            .ReturnsAsync(mockPrices);

        // Act
        var result = await _calculator.CalculateAsync(volumeInfo);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.CostComponents);
        
        var capacityCost = result.CostComponents.First();
        Assert.Equal(4096, capacityCost.Quantity);
        Assert.Equal(0.827392, capacityCost.EstimatedCost, 6); // 4096 * 0.000202
        
        // Check throughput note
        Assert.Contains(result.Notes, n => n.Contains("64 MiB/s")); // 4 TiB * 16 MiB/s
        Assert.True(result.ConfidenceLevel >= 80);
    }

    [Fact]
    public async Task CalculateAsync_CoolAccessEnabled_IncludesCoolComponents()
    {
        // Arrange
        var volumeInfo = new AnfVolumeInfo
        {
            VolumeId = "test-volume",
            VolumeName = "ANF Cool Volume",
            Region = "eastus",
            ServiceLevel = "Premium",
            ProvisionedCapacityGb = 10240, // 10 TiB
            CoolAccessEnabled = true,
            HotDataGb = 2048,
            CoolDataGb = 8192,
            DataTieredToCoolGb = 500,
            DataRetrievedFromCoolGb = 100
        };

        var mockPrices = new List<PriceItem>
        {
            new PriceItem
            {
                MeterName = "Provisioned Capacity",
                RetailPrice = 0.000404,
                UnitOfMeasure = "1 GB/Month"
            },
            new PriceItem
            {
                MeterName = "Cool Storage",
                RetailPrice = 0.0001,
                UnitOfMeasure = "1 GB/Month"
            },
            new PriceItem
            {
                MeterName = "Cool Tiering",
                RetailPrice = 0.01,
                UnitOfMeasure = "1 GB"
            },
            new PriceItem
            {
                MeterName = "Cool Retrieval",
                RetailPrice = 0.02,
                UnitOfMeasure = "1 GB"
            }
        };

        _mockPricesClient
            .Setup(x => x.GetAnfPricingAsync("eastus", "Premium"))
            .ReturnsAsync(mockPrices);

        // Act
        var result = await _calculator.CalculateAsync(volumeInfo);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(4, result.CostComponents.Count);
        
        Assert.Contains(result.CostComponents, c => c.ComponentType == "storage");
        Assert.Contains(result.CostComponents, c => c.ComponentType == "storage_cool");
        Assert.Contains(result.CostComponents, c => c.ComponentType == "cool_tiering");
        Assert.Contains(result.CostComponents, c => c.ComponentType == "cool_retrieval");
        
        // Verify throughput is reduced (36 instead of 64 MiB/s for Premium)
        Assert.Contains(result.Notes, n => n.Contains("36 MiB/s"));
    }
}

public class ManagedDiskCostCalculatorTests
{
    private readonly Mock<AzureRetailPricesClient> _mockPricesClient;
    private readonly Mock<CostCollectionService> _mockCostService;
    private readonly Mock<ILogger<ManagedDiskCostCalculator>> _mockLogger;
    private readonly ManagedDiskCostCalculator _calculator;

    public ManagedDiskCostCalculatorTests()
    {
        _mockPricesClient = new Mock<AzureRetailPricesClient>(
            Mock.Of<HttpClient>(),
            Mock.Of<IMemoryCache>(),
            Mock.Of<ILogger<AzureRetailPricesClient>>());
        
        _mockCostService = new Mock<CostCollectionService>(
            Mock.Of<ILogger<CostCollectionService>>());
        
        _mockLogger = new Mock<ILogger<ManagedDiskCostCalculator>>();
        
        _calculator = new ManagedDiskCostCalculator(
            _mockPricesClient.Object,
            _mockCostService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task CalculateAsync_WithActualBillingData_UsesActualCost()
    {
        // Arrange
        var diskInfo = new ManagedDiskVolumeInfo
        {
            VolumeId = "/subscriptions/sub1/disks/disk1",
            VolumeName = "Premium Disk",
            Region = "eastus",
            DiskType = "Premium SSD",
            DiskSizeGb = 512,
            SubscriptionId = "sub1",
            ResourceGroupName = "rg1"
        };

        var actualCosts = new List<CostEntry>
        {
            new CostEntry { Cost = 45.50 },
            new CostEntry { Cost = 45.50 }
        };

        _mockCostService
            .Setup(x => x.GetResourceCostAsync(
                "sub1",
                "/subscriptions/sub1/disks/disk1",
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>()))
            .ReturnsAsync(actualCosts);

        // Act
        var result = await _calculator.CalculateAsync(diskInfo);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(91, result.TotalEstimatedCost); // 45.50 + 45.50
        Assert.Equal(95, result.ConfidenceLevel); // Highest confidence for actual data
        Assert.Contains("Actual billing", result.EstimationMethod);
        Assert.Contains(result.Notes, n => n.Contains("actual billing data"));
    }

    [Fact]
    public async Task CalculateAsync_NoActualData_UsesRetailPricing()
    {
        // Arrange
        var diskInfo = new ManagedDiskVolumeInfo
        {
            VolumeId = "/subscriptions/sub1/disks/disk1",
            VolumeName = "Premium Disk",
            Region = "eastus",
            DiskType = "Premium SSD",
            DiskSizeGb = 512,
            SubscriptionId = "sub1",
            ResourceGroupName = "rg1"
        };

        _mockCostService
            .Setup(x => x.GetResourceCostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync((List<CostEntry>)null);

        var mockPrices = new List<PriceItem>
        {
            new PriceItem
            {
                MeterName = "P20 Disk",
                SkuName = "P20",
                RetailPrice = 75.65,
                UnitOfMeasure = "1/Month"
            }
        };

        _mockPricesClient
            .Setup(x => x.GetManagedDiskPricingAsync("eastus", "Premium SSD"))
            .ReturnsAsync(mockPrices);

        // Act
        var result = await _calculator.CalculateAsync(diskInfo);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Retail pricing", result.EstimationMethod);
        Assert.Contains(result.Warnings, w => w.Contains("Actual billing data not available"));
        Assert.True(result.ConfidenceLevel < 95); // Lower than actual data
        Assert.Contains(result.Notes, n => n.Contains("P20"));
    }

    [Fact]
    public async Task GetDiskTier_ReturnsCorrectTier()
    {
        // This would test the private method through reflection or by testing the public behavior
        // For demonstration, testing via public behavior:
        
        var diskInfo = new ManagedDiskVolumeInfo
        {
            VolumeId = "test",
            VolumeName = "Test",
            Region = "eastus",
            DiskType = "Premium SSD",
            DiskSizeGb = 128 // Should be P10
        };

        _mockCostService
            .Setup(x => x.GetResourceCostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync((List<CostEntry>)null);

        var mockPrices = new List<PriceItem>
        {
            new PriceItem { MeterName = "P10 Disk", RetailPrice = 19.71, UnitOfMeasure = "1/Month" }
        };

        _mockPricesClient
            .Setup(x => x.GetManagedDiskPricingAsync("eastus", "Premium SSD"))
            .ReturnsAsync(mockPrices);

        var result = await _calculator.CalculateAsync(diskInfo);

        Assert.Contains(result.Notes, n => n.Contains("P10"));
    }
}

// Placeholder for CostEntry class (should be defined in your models)
public class CostEntry
{
    public double Cost { get; set; }
}
