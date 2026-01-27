using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using AzFilesOptimizer.Backend.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        // Register HTTP client for Azure Retail Prices API
        services.AddHttpClient<AzureRetailPricesClient>();
        
        // Register memory cache for pricing data
        services.AddMemoryCache();
        
        // Register cost calculation services
        services.AddScoped<AzureRetailPricesClient>();
        services.AddScoped<AzureFilesCostCalculator>();
        services.AddScoped<AnfCostCalculator>();
        services.AddScoped<ManagedDiskCostCalculator>();
        services.AddScoped<AccurateCostEstimationService>();
    })
    .Build();

host.Run();
