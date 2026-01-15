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

             metricsData["_meta"] = new
             {
                 interval = "PT1H",
                 timespan = new { start = startTime, end = endTime }
             };

             using var httpClient = new HttpClient();
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://management.azure.com/.default" }), default);
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

            foreach (var metric in metrics)
            {
                var metricName = metric.name;
                var aggregation = metric.aggregation;
                try
                {
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
            return await CollectMetricsAsync(
                resourceId,
                "microsoft.netapp%2Fnetappaccounts%2Fcapacitypools%2Fvolumes",
                volumeName,
                new (string name, string aggregation)[]
                {
                    ("VolumeLogicalSize","Average,Total,Maximum,Minimum"),
                    ("ReadIOPS","Average,Total,Maximum,Minimum"),
                    ("WriteIOPS","Average,Total,Maximum,Minimum"),
                    ("ReadThroughput","Average,Total,Maximum,Minimum"),
                    ("WriteThroughput","Average,Total,Maximum,Minimum"),
                    ("VolumeThroughputReadBytes","Average,Total,Maximum,Minimum"),
                    ("VolumeThroughputWriteBytes","Average,Total,Maximum,Minimum")
                });
    }

    public async Task<(bool hasData, int? daysAvailable, string? metricsSummary)> CollectManagedDiskMetricsAsync(
        string resourceId, string diskName)
    {
        return await CollectMetricsAsync(
            resourceId,
            "microsoft.compute%2Fdisks",
            diskName,
            new (string name, string aggregation)[]
            {
                ("Disk Read Bytes","Average,Total,Maximum,Minimum"),
                ("Disk Write Bytes","Average,Total,Maximum,Minimum"),
                ("Disk Read Operations/Sec","Average,Total,Maximum,Minimum"),
                ("Disk Write Operations/Sec","Average,Total,Maximum,Minimum"),
                ("Disk Used Capacity","Average,Maximum,Minimum")
            },
            excludeZeroDataPoints: true);
    }

    private async Task<(bool hasData, int? daysAvailable, string? metricsSummary)> CollectMetricsAsync(
        string resourceId,
        string metricNamespace,
        string displayName,
        IEnumerable<(string name, string aggregation)> preferredMetrics,
        bool excludeZeroDataPoints = false)
    {
        try
        {
            var endTime = DateTime.UtcNow;
            var startTime = endTime.AddDays(-30);

            var supported = await GetSupportedMetricNamesAsync(resourceId);
            var metrics = preferredMetrics.Where(p => supported.Contains(p.name, StringComparer.OrdinalIgnoreCase))
                                           .Distinct()
                                           .ToList();

            if (metrics.Count == 0)
            {
                _logger.LogDebug("No supported metrics found for {Resource}. Supported: {Supported}", displayName, string.Join(", ", supported));
                return (false, null, null);
            }

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
                var metricName = metric.name;
                var aggregation = metric.aggregation;
                try
                {
                    var timespan = $"{startTime:yyyy-MM-ddTHH:mm:ssZ}/{endTime:yyyy-MM-ddTHH:mm:ssZ}";
                    var apiUrl = $"https://management.azure.com{resourceId}/providers/Microsoft.Insights/metrics" +
                        $"?api-version={MetricsApiVersion}&timespan={Uri.EscapeDataString(timespan)}" +
                        $"&interval=PT1H&metricNamespace={metricNamespace}&metricnames={Uri.EscapeDataString(metricName)}&aggregation={aggregation}";

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
                                var allTimeseries = metricValue.timeseries ?? Array.Empty<Timeseries>();
                                var dataPoints = new List<DataPoint>();

                                foreach (var timeseries in allTimeseries)
                                {
                                    if (timeseries?.data?.Any() == true)
                                    {
                                        foreach (var dp in timeseries.data)
                                        {
                                            if (dp == null) continue;
                                            if (!dp.HasAnyValue()) continue;
                                            if (excludeZeroDataPoints && dp.IsAllZero()) continue;
                                            dataPoints.Add(dp);
                                        }
                                    }
                                }

                                if (dataPoints.Any())
                                {
                                    hasAnyData = true;
                                    var oldestPoint = dataPoints.Min(d => DateTime.Parse(d.timeStamp));
                                    oldestDataDays = Math.Max(oldestDataDays, (endTime - oldestPoint).Days);

                                    var values = dataPoints
                                        .Select(d => d.GetPrimaryValue())
                                        .Where(v => v.HasValue)
                                        .Select(v => v!.Value)
                                        .ToList();

                                    metricsData[metricName] = new
                                    {
                                        average = values.Count > 0 ? values.Average() : 0,
                                        max = values.Count > 0 ? values.Max() : 0,
                                        min = values.Count > 0 ? values.Min() : 0,
                                        total = dataPoints.Where(d => d.total.HasValue).Sum(d => d.total ?? 0),
                                        dataPointCount = dataPoints.Count
                                    };
                                    _logger.LogDebug("Collected {Count} data points for metric {MetricName}", dataPoints.Count, metricName);
                                }
                                else
                                {
                                    _logger.LogDebug("Metric {MetricName} returned but has no valid data points", metricName);
                                }
                            }
                        }
                        else
                        {
                            _logger.LogInformation("No metric data returned for {MetricName} on {Resource}. Response: {Response}",
                                metricName, displayName, content.Length > 500 ? content.Substring(0, 500) : content);
                        }
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogWarning("Failed to fetch metric {MetricName} for {Resource}. Status: {Status}, Error: {Error}",
                            metricName, displayName, (int)response.StatusCode, errorContent.Length > 500 ? errorContent.Substring(0, 500) : errorContent);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to collect metric {MetricName} for {Resource}", metricName, displayName);
                }
            }

            if (!hasAnyData) return (false, null, null);
            return (true, oldestDataDays > 0 ? oldestDataDays : 30, JsonSerializer.Serialize(metricsData));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting metrics for {Resource}", displayName);
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
        public double? maximum { get; set; }
        public double? minimum { get; set; }

        public bool HasAnyValue()
        {
            return average.HasValue || total.HasValue || maximum.HasValue || minimum.HasValue;
        }

        public bool IsAllZero()
        {
            return (average ?? 0) == 0 && (total ?? 0) == 0 && (maximum ?? 0) == 0 && (minimum ?? 0) == 0;
        }

        public double? GetPrimaryValue()
        {
            return average ?? total ?? maximum ?? minimum;
        }
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

    public async Task<string> CollectStorageAccountMetricsRawAsync(string storageAccountResourceId, string storageAccountName, int days)
    {
        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddDays(-Math.Max(1, Math.Min(93, days)));

        // Metrics at fileServices scope
        var fileServicesResourceId = $"{storageAccountResourceId}/fileServices/default";
        using var httpClient = new HttpClient();
        var token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { "https://management.azure.com/.default" }), default);
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

        var supported = await GetSupportedMetricNamesAsync(fileServicesResourceId);
        var preferred = new[] { "Transactions", "Ingress", "Egress", "SuccessServerLatency", "SuccessE2ELatency", "Availability", "FileCapacity" };
        var metrics = preferred.Where(m => supported.Contains(m, StringComparer.OrdinalIgnoreCase)).ToArray();

        var result = new Dictionary<string, object?>();
        result["resourceId"] = storageAccountResourceId;
        result["storageAccount"] = storageAccountName;
        result["timespan"] = new { start = startTime, end = endTime };
        result["interval"] = "PT1H";
        var metricsObj = new Dictionary<string, object?>();
        result["metrics"] = metricsObj;

        foreach (var metricName in metrics)
        {
            var timespan = $"{startTime:yyyy-MM-ddTHH:mm:ssZ}/{endTime:yyyy-MM-ddTHH:mm:ssZ}";
            var apiUrl = $"https://management.azure.com{fileServicesResourceId}/providers/Microsoft.Insights/metrics" +
                         $"?api-version={MetricsApiVersion}&timespan={Uri.EscapeDataString(timespan)}" +
                         $"&interval=PT1H&metricNamespace=microsoft.storage%2Fstorageaccounts%2Ffileservices&metricnames={metricName}&aggregation=Average,Total,Maximum,Minimum";
            var response = await httpClient.GetAsync(apiUrl);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }
            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var parsed = JsonSerializer.Deserialize<MetricsApiResponseRaw>(content, options);
            if (parsed?.value == null || parsed.value.Length == 0) continue;

            var first = parsed.value[0];
            var unit = first.unit;
            var points = new List<Dictionary<string, object?>>();
            foreach (var ts in first.timeseries ?? Array.Empty<TimeseriesRaw>())
            {
                foreach (var dp in (ts.data ?? Array.Empty<DataPointRaw>()))
                {
                    if (dp.average.HasValue || dp.total.HasValue || dp.maximum.HasValue || dp.minimum.HasValue)
                    {
                        points.Add(new Dictionary<string, object?>
                        {
                            ["timeStamp"] = dp.timeStamp,
                            ["average"] = dp.average,
                            ["total"] = dp.total,
                            ["maximum"] = dp.maximum,
                            ["minimum"] = dp.minimum
                        });
                    }
                }
            }
            metricsObj[metricName] = new { unit, points };
        }

        return JsonSerializer.Serialize(result);
    }

    private class MetricsApiResponseRaw
    {
        public MetricRaw[]? value { get; set; }
    }

    private class MetricRaw
    {
        public string? unit { get; set; }
        public TimeseriesRaw[]? timeseries { get; set; }
    }

    private class TimeseriesRaw
    {
        public DataPointRaw[]? data { get; set; }
    }

    private class DataPointRaw
    {
        public string timeStamp { get; set; } = "";
        public double? average { get; set; }
        public double? total { get; set; }
        public double? maximum { get; set; }
        public double? minimum { get; set; }
    }
}
