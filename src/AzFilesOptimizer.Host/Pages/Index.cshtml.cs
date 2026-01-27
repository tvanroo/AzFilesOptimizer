using AzFilesOptimizer.Host.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AzFilesOptimizer.Host.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly IAppConfigurationStore _configurationStore;

    public IndexModel(ILogger<IndexModel> logger, IAppConfigurationStore configurationStore)
    {
        _logger = logger;
        _configurationStore = configurationStore;
    }

    public string AzureCloudDisplay { get; private set; } = "Not configured";

    public async Task OnGetAsync()
    {
        var config = await _configurationStore.LoadAsync();
        AzureCloudDisplay = string.IsNullOrWhiteSpace(config.AzureCloud)
            ? "Not configured"
            : config.AzureCloud;
    }
}
