using Azure.Core;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AzFilesOptimizer.Backend.Services;

public class MetricsCollectionService
{
    private readonly ILogger _logger;
    private readonly TokenCredential _credential;

    private const string MetricsApiVersion = "2021-05-01";
    private const string MetricDefsApiVersion = "2018-01-01";

    public MetricsCollectionService(ILogger logger, TokenCredential credential)
    {
        _logger = logger;
        _credential = credential;
    }

    public async Task<(bool hasData, int? daysAvailable, string? metricsSummary)> CollectStorageAccountMetricsAsync(
        string storageAccountResourceId, string storageAccountName)
    {
        try
        {
            var endTime = DateTime.UtcNow;
            var startTime = endTime.AddDays(-30);
            
            // Azure Files metrics are at the fileServices sub-resource level
            var fileServicesResourceId = $"{storageAccountResourceId}/fileServices/default";
            _logger.LogInformation("Collecting metrics for {Account} using fileServices path", storageAccountName);
            
            // Ask Azure Monitor which metrics are actually supported for this resource
            var supported = await GetSupportedMetricNamesAsync($"{fileServicesResourceId}");
            // Preferred metrics for Azure Files
            var preferred = new (string name, string aggregation)[]
            {
                ("Transactions","Total"),
                ("Ingress","Total"),
                ("Egress","Total"),
                ("SuccessServerLatency","Average"),
                ("SuccessE2ELatency","Average"),
                ("Availability","Average"),
                ("FileCapacity","Average")
            };
            var metrics = preferred.Where(p => supported.Contains(p.name, StringComparer.OrdinalIgnoreCase))
                                   .Distinct()
                                   .ToList();

            var metricsData = new Dictionary<string, object>();
            bool hasAnyData = false;
            int oldestDataDays = 0;

            using var httpClient = new HttpClient();
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://management.azure.com/.default" }), default);
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

            foreach (var metric in metrics)
            {
                try
                {
                    var metricName = metric.name;
                    var aggregation = metric.aggregation;
                    var timespan = $"{startTime:yyyy-MM-ddTHH:mm:ssZ}/{endTime:yyyy-MM-ddTHH:mm:ssZ}";
                    var apiUrl = $"https://management.azure.com{fileServicesResourceId}/providers/Microsoft.Insights/metrics" +
                        $"?api-version={MetricsApiVersion}&timespan={Uri.EscapeDataString(timespan)}" +
                        $"&interval=PT1H&metricNamespace=microsoft.storage%2Fstorageaccounts%2Ffileservices&metricnames={metricName}&aggregation={aggregation}";
                    
                    _logger.LogDebug("Fetching metric {MetricName} with {Aggregation} from: {Url}", metricName, aggregation, apiUrl);
                    var response = await httpClient.GetAsync(apiUrl);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var result = JsonSerializer.Deserialize<MetricsApiResponse>(content, options);
                        
                        if (result?.value?.Any() == true)
                        {
                            foreach (var metricValue in result.value)
                            {
                                // Check if there are any timeseries with data
                                var allTimeseries = metricValue.timeseries ?? Array.Empty<Timeseries>();
                                var dataPoints = new List<DataPoint>();
                                
                                // Collect data from all timeseries (not just the first one)
                                foreach (var timeseries in allTimeseries)
                                {
                                    if (timeseries?.data?.Any() == true)
                                    {
                                        dataPoints.AddRange(timeseries.data.Where(d => d.total.HasValue || d.average.HasValue));
                                    }
                                }
                                
                                if (dataPoints.Any())
                                {
                                    hasAnyData = true;
                                    var oldestPoint = dataPoints.Min(d => DateTime.Parse(d.timeStamp));
                                    oldestDataDays = Math.Max(oldestDataDays, (endTime - oldestPoint).Days);
                                    
                                    metricsData[metricName] = new
                                    {
                                        average = dataPoints.Average(d => d.average ?? d.total ?? 0),
                                        max = dataPoints.Max(d => d.average ?? d.total ?? 0),
                                        total = dataPoints.Where(d => d.total.HasValue).Sum(d => d.total ?? 0),
                                        dataPointCount = dataPoints.Count
                                    };
                                    _logger.LogDebug("Collected {Count} data points for {MetricName}", dataPoints.Count, metricName);
                                }
                                else
                                {
                                    _logger.LogDebug("Metric {MetricName} returned but has no valid data points", metricName);
                                }
                            }
                        }
                        else
                        {
                            _logger.LogDebug("No metric values in response for {MetricName}", metricName);
                        }
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogWarning("Metrics API failed for {MetricName} on {Account}. Status: {Status}, Error: {Error}", 
                            metricName, storageAccountName, (int)response.StatusCode, 
                            errorContent.Length > 200 ? errorContent.Substring(0, 200) + "..." : errorContent);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to collect metric {MetricName} for {Account}", metricName, storageAccountName);
                }
            }

            if (!hasAnyData)
            {
                _logger.LogInformation("No historical metrics found for {Account} (no data in Azure Monitor)", storageAccountName);
                return (false, null, null);
            }
            
            var jsonSummary = JsonSerializer.Serialize(metricsData);
            _logger.LogInformation("Successfully collected {Count} metrics for {Account}: {MetricNames}", 
                metricsData.Count, storageAccountName, string.Join(", ", metricsData.Keys));
            return (true, oldestDataDays > 0 ? oldestDataDays : 30, jsonSummary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting metrics for {Account}", storageAccountName);
            return (false, null, null);
        }
    }

    public async Task<(bool hasData, int? daysAvailable, string? metricsSummary)> CollectAnfVolumeMetricsAsync(
        string resourceId, string volumeName)
    {
        try
        {
            var endTime = DateTime.UtcNow;
            var startTime = endTime.AddDays(-30);
            
            // Ask which metrics are supported for this ANF volume
            var supported = await GetSupportedMetricNamesAsync(resourceId);
            // Preferred metrics (will be filtered by what exists)
            var preferred = new (string name, string aggregation)[]
            {
                ("VolumeLogicalSize","Average"),
                ("ReadIOPS","Average"),
                ("WriteIOPS","Average"),
                ("ReadThroughput","Average"),
                ("WriteThroughput","Average"),
                ("VolumeThroughputReadBytes","Average"),
                ("VolumeThroughputWriteBytes","Average")
            };
            var metrics = preferred.Where(p => supported.Contains(p.name, StringComparer.OrdinalIgnoreCase))
                                   .Distinct()
                                   .ToList();

            var metricsData = new Dictionary<string, object>();
            bool hasAnyData = false;
            int oldestDataDays = 0;

            using var httpClient = new HttpClient();
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://management.azure.com/.default" }), default);
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

            foreach (var metric in metrics)
            {
                try
                {
                    var metricName = metric.name;
                    var aggregation = metric.aggregation;
                    var timespan = $"{startTime:yyyy-MM-ddTHH:mm:ssZ}/{endTime:yyyy-MM-ddTHH:mm:ssZ}";
                    var apiUrl = $"https://management.azure.com{resourceId}/providers/Microsoft.Insights/metrics" +
                        $"?api-version={MetricsApiVersion}&timespan={Uri.EscapeDataString(timespan)}" +
                        $"&interval=PT1H&metricNamespace=microsoft.netapp%2Fnetappaccounts%2Fcapacitypools%2Fvolumes&metricnames={metricName}&aggregation={aggregation}";
                    
                    _logger.LogDebug("Fetching ANF metric {MetricName} with {Aggregation} from: {Url}", metricName, aggregation, apiUrl);
                    var response = await httpClient.GetAsync(apiUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var result = JsonSerializer.Deserialize<MetricsApiResponse>(content, options);
                        
                        if (result?.value?.Any() == true)
                        {
                            foreach (var metricValue in result.value)
                            {
                                // Check all timeseries (not just the first one)
                                var allTimeseries = metricValue.timeseries ?? Array.Empty<Timeseries>();
                                var dataPoints = new List<DataPoint>();
                                
                                foreach (var timeseries in allTimeseries)
                                {
                                    if (timeseries?.data?.Any() == true)
                                    {
                                        dataPoints.AddRange(timeseries.data.Where(d => d.total.HasValue || d.average.HasValue));
                                    }
                                }
                                
                                if (dataPoints.Any())
                                {
                                    hasAnyData = true;
                                    var oldestPoint = dataPoints.Min(d => DateTime.Parse(d.timeStamp));
                                    oldestDataDays = Math.Max(oldestDataDays, (endTime - oldestPoint).Days);
                                    
                                    metricsData[metricName] = new
                                    {
                                        average = dataPoints.Average(d => d.average ?? d.total ?? 0),
                                        max = dataPoints.Max(d => d.average ?? d.total ?? 0),
                                        total = dataPoints.Where(d => d.total.HasValue).Sum(d => d.total ?? 0),
                                        dataPointCount = dataPoints.Count
                                    };
                                    _logger.LogDebug("Collected {Count} data points for ANF metric {MetricName}", dataPoints.Count, metricName);
                                }
                                else
                                {
                                    _logger.LogDebug("ANF metric {MetricName} returned but has no valid data points", metricName);
                                }
                            }
                        }
                        else
                        {
                            _logger.LogInformation("No metric data returned for {MetricName} on ANF volume {Volume}. Response: {Response}", 
                                metricName, volumeName, content.Length > 500 ? content.Substring(0, 500) : content);
                        }
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogWarning("Failed to fetch metric {MetricName} for ANF volume {Volume}. Status: {Status}, Error: {Error}", 
                            metricName, volumeName, (int)response.StatusCode, errorContent.Length > 500 ? errorContent.Substring(0, 500) : errorContent);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to collect metric {MetricName} for ANF {Volume}", metricName, volumeName);
                }
            }

            if (!hasAnyData) return (false, null, null);
            return (true, oldestDataDays > 0 ? oldestDataDays : 30, JsonSerializer.Serialize(metricsData));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting metrics for ANF {Volume}", volumeName);
            return (false, null, null);
        }
    }

    private class MetricsApiResponse
    {
        public Metric[]? value { get; set; }
    }

    private class Metric
    {
        public Timeseries[]? timeseries { get; set; }
    }

    private class Timeseries
    {
        public DataPoint[]? data { get; set; }
    }

    private class DataPoint
    {
        public string timeStamp { get; set; } = "";
        public double? average { get; set; }
        public double? total { get; set; }
    }

    private class MetricDefinitionsResponse
    {
        public MetricDefinition[]? value { get; set; }
    }

    private class MetricDefinition
    {
        public NameObj name { get; set; } = new();
    }

    private class NameObj
    {
        public string value { get; set; } = string.Empty;
    }

    private async Task<HashSet<string>> GetSupportedMetricNamesAsync(string resourceId)
    {
        try
        {
            using var httpClient = new HttpClient();
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://management.azure.com/.default" }), default);
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

            var url = $"https://management.azure.com{resourceId}/providers/Microsoft.Insights/metricDefinitions?api-version={MetricDefsApiVersion}";
            var resp = await httpClient.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                var error = await resp.Content.ReadAsStringAsync();
                _logger.LogDebug("metricDefinitions failed for {Resource}: {Status} {Err}", resourceId, (int)resp.StatusCode, error);
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            var json = await resp.Content.ReadAsStringAsync();
            var defs = JsonSerializer.Deserialize<MetricDefinitionsResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in defs?.value ?? Array.Empty<MetricDefinition>())
            {
                if (!string.IsNullOrEmpty(d.name?.value)) set.Add(d.name.value);
            }
            _logger.LogDebug("Supported metrics for {Resource}: {Names}", resourceId, string.Join(", ", set));
            return set;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get metric definitions for {Resource}", resourceId);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
