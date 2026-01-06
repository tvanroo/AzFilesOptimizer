using Azure.Core;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AzFilesOptimizer.Backend.Services;

public class MetricsCollectionService
{
    private readonly ILogger _logger;
    private readonly TokenCredential _credential;

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
            
            var metrics = new[] { "Transactions", "Ingress", "Egress", "SuccessServerLatency", "Availability" };
            var metricsData = new Dictionary<string, object>();
            bool hasAnyData = false;
            int oldestDataDays = 0;

            using var httpClient = new HttpClient();
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://management.azure.com/.default" }), default);
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

            foreach (var metricName in metrics)
            {
                try
                {
                    var timespan = $"{startTime:yyyy-MM-ddTHH:mm:ssZ}/{endTime:yyyy-MM-ddTHH:mm:ssZ}";
                    var apiUrl = $"https://management.azure.com{fileServicesResourceId}/providers/Microsoft.Insights/metrics" +
                        $"?api-version=2023-10-01&timespan={Uri.EscapeDataString(timespan)}" +
                        $"&interval=P1D&metricnames={metricName}&aggregation=Average,Total";
                    
                    var response = await httpClient.GetAsync(apiUrl);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var result = JsonSerializer.Deserialize<MetricsApiResponse>(content, options);
                        
                        if (result?.value?.Any() == true)
                        {
                            foreach (var metric in result.value)
                            {
                                var timeseries = metric.timeseries?.FirstOrDefault();
                                if (timeseries?.data?.Any() == true)
                                {
                                    hasAnyData = true;
                                    var dataPoints = timeseries.data.Where(d => d.total.HasValue || d.average.HasValue).ToList();
                                    
                                    if (dataPoints.Any())
                                    {
                                        var oldestPoint = dataPoints.Min(d => DateTime.Parse(d.timeStamp));
                                        oldestDataDays = Math.Max(oldestDataDays, (endTime - oldestPoint).Days);
                                        
                                        metricsData[metricName] = new
                                        {
                                            average = dataPoints.Average(d => d.average ?? d.total ?? 0),
                                            max = dataPoints.Max(d => d.average ?? d.total ?? 0),
                                            dataPointCount = dataPoints.Count
                                        };
                                    }
                                }
                            }
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
            
            var metrics = new[] { "VolumeLogicalSize", "ReadIops", "WriteIops", "VolumeThroughputReadBytes", "VolumeThroughputWriteBytes" };
            var metricsData = new Dictionary<string, object>();
            bool hasAnyData = false;
            int oldestDataDays = 0;

            using var httpClient = new HttpClient();
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://management.azure.com/.default" }), default);
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

            foreach (var metricName in metrics)
            {
                try
                {
                    var timespan = $"{startTime:yyyy-MM-ddTHH:mm:ssZ}/{endTime:yyyy-MM-ddTHH:mm:ssZ}";
                    var apiUrl = $"https://management.azure.com{resourceId}/providers/Microsoft.Insights/metrics" +
                        $"?api-version=2023-10-01&timespan={Uri.EscapeDataString(timespan)}" +
                        $"&interval=P1D&metricnames={metricName}&aggregation=Average,Total";
                    
                    var response = await httpClient.GetAsync(apiUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var result = JsonSerializer.Deserialize<MetricsApiResponse>(content, options);
                        
                        if (result?.value?.Any() == true)
                        {
                            foreach (var metric in result.value)
                            {
                                var timeseries = metric.timeseries?.FirstOrDefault();
                                if (timeseries?.data?.Any() == true)
                                {
                                    hasAnyData = true;
                                    var dataPoints = timeseries.data.Where(d => d.total.HasValue || d.average.HasValue).ToList();
                                    
                                    if (dataPoints.Any())
                                    {
                                        var oldestPoint = dataPoints.Min(d => DateTime.Parse(d.timeStamp));
                                        oldestDataDays = Math.Max(oldestDataDays, (endTime - oldestPoint).Days);
                                        
                                        metricsData[metricName] = new
                                        {
                                            average = dataPoints.Average(d => d.average ?? d.total ?? 0),
                                            max = dataPoints.Max(d => d.average ?? d.total ?? 0),
                                            dataPointCount = dataPoints.Count
                                        };
                                    }
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
}
