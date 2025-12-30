using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace AzFilesOptimizer.Host.Configuration;

public interface IAppConfigurationStore
{
    Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default);
}

/// <summary>
/// Simple file-system backed configuration store that reads/writes JSON under the state/ directory
/// next to the application content root. This is intentionally minimal for the initial MVP.
/// </summary>
public sealed class FileSystemAppConfigurationStore(IHostEnvironment hostEnvironment) : IAppConfigurationStore
{
    private readonly string _configFilePath = Path.Combine(hostEnvironment.ContentRootPath, "state", "appsettings.json");

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_configFilePath)!;
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(_configFilePath))
        {
            // Return defaults; caller can choose whether to persist.
            return new AppConfiguration();
        }

        await using var stream = File.OpenRead(_configFilePath);
        var config = await JsonSerializer.DeserializeAsync<AppConfiguration>(stream, SerializerOptions, cancellationToken)
                     ?? new AppConfiguration();

        return config;
    }

    public async Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_configFilePath)!;
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_configFilePath);
        await JsonSerializer.SerializeAsync(stream, configuration, SerializerOptions, cancellationToken);
    }
}
