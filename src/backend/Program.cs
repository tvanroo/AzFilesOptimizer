using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using AzFilesOptimizer.Backend.Services;
using Azure.Data.Tables;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        // Get storage connection string
        var storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? string.Empty;
        
        // Register HTTP client for Azure Retail Prices API
        services.AddHttpClient<AzureRetailPricesClient>();
        
        // Register memory cache for pricing data
        services.AddMemoryCache();
        
        // Register Azure Table Storage client (required by CoolDataAssumptionsService)
        services.AddSingleton(sp => new TableServiceClient(storageConnectionString));
        
        // Register DiscoveredResourceStorageService (required by CoolDataAssumptionsService)
        services.AddScoped(sp => new DiscoveredResourceStorageService(storageConnectionString));
        
        // Register cost calculation services
        services.AddScoped<AzureRetailPricesClient>();
        services.AddScoped<AzureFilesCostCalculator>();
        services.AddScoped<AnfCostCalculator>();
        services.AddScoped<ManagedDiskCostCalculator>();
        services.AddScoped<AccurateCostEstimationService>();
        services.AddScoped<HypotheticalAnfCostCalculator>();
        services.AddScoped<CoolDataAssumptionsService>();
        services.AddScoped<VolumeAnnotationService>(sp => 
            new VolumeAnnotationService(
                storageConnectionString,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<VolumeAnnotationService>>()));
    })
    .Build();

host.Run();
