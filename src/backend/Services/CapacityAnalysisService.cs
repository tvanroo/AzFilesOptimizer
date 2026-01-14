using AzFilesOptimizer.Backend.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace AzFilesOptimizer.Backend.Services;

/// <summary>
/// Service for analyzing historical metrics and generating capacity and throughput
/// sizing recommendations for ANF volumes with AI enhancement.
/// </summary>
public class CapacityAnalysisService
{
    private readonly ILogger _logger;
    
    public CapacityAnalysisService(ILogger logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Analyzes metrics and generates capacity/throughput sizing recommendations
    /// </summary>
    /// <param name="metricsJson">JSON string containing historical metrics summary</param>
    /// <param name="bufferPercent">Buffer percentage to apply above peak (default 30%)</param>
    /// <param name="daysAnalyzed">Number of days of metrics analyzed</param>
    /// <param name="volumeType">Type of volume: AzureFiles or ANF</param>
    /// <returns>CapacitySizingResult with recommendations</returns>
    public CapacitySizingResult AnalyzeMetrics(
        string? metricsJson, 
        double bufferPercent = 30.0, 
        int daysAnalyzed = 30,
        string volumeType = "AzureFiles")
    {
        var result = new CapacitySizingResult
        {
            BufferPercent = bufferPercent,
            DaysAnalyzed = daysAnalyzed,
            AnalyzedAt = DateTime.UtcNow
        };
        
        if (string.IsNullOrWhiteSpace(metricsJson))
        {
            result.HasSufficientData = false;
            result.Warnings = "No historical metrics data available for analysis.";
            result.DataQualityScore = 0.0;
            return result;
        }
        
        try
        {
            using var doc = JsonDocument.Parse(metricsJson);
            var root = doc.RootElement;
            
            int totalDataPoints = 0;
            int metricsFound = 0;
            
            // Capacity Analysis
            if (volumeType == "AzureFiles")
            {
                result.PeakCapacityGiB = ExtractPeakCapacityFromAzureFiles(root, ref totalDataPoints, ref metricsFound);
                var (peakRead, peakWrite) = ExtractThroughputFromAzureFiles(root, ref totalDataPoints, ref metricsFound);
                result.PeakReadThroughputMiBps = peakRead;
                result.PeakWriteThroughputMiBps = peakWrite;
                result.PeakThroughputMiBps = peakRead + peakWrite;
            }
            else if (volumeType == "ANF")
            {
                result.PeakCapacityGiB = ExtractPeakCapacityFromAnf(root, ref totalDataPoints, ref metricsFound);
                var (peakRead, peakWrite, readIOPS, writeIOPS) = ExtractThroughputFromAnf(root, ref totalDataPoints, ref metricsFound);
                result.PeakReadThroughputMiBps = peakRead;
                result.PeakWriteThroughputMiBps = peakWrite;
                result.PeakThroughputMiBps = peakRead + peakWrite;
                result.PeakReadIOPS = readIOPS;
                result.PeakWriteIOPS = writeIOPS;
                result.PeakTotalIOPS = readIOPS + writeIOPS;
            }
            
            result.MetricDataPoints = totalDataPoints;
            
            // Calculate recommendations with buffer
            double bufferMultiplier = 1.0 + (bufferPercent / 100.0);
            result.RecommendedCapacityGiB = result.PeakCapacityGiB * bufferMultiplier;
            result.RecommendedThroughputMiBps = result.PeakThroughputMiBps * bufferMultiplier;
            
            // Format capacity in appropriate unit
            FormatCapacity(result);
            
            // Calculate data quality score
            result.DataQualityScore = CalculateDataQualityScore(totalDataPoints, metricsFound, daysAnalyzed);
            result.HasSufficientData = result.DataQualityScore >= 0.5;
            
            // Generate warnings based on data quality and patterns
            result.Warnings = GenerateWarnings(result);
            
            _logger.LogInformation(
                "Capacity analysis complete: Peak={PeakGiB} GiB, Recommended={RecGiB} GiB ({RecUnit}), " +
                "Peak Throughput={PeakThroughput} MiB/s, Recommended={RecThroughput} MiB/s, Quality={Quality:0.##}",
                result.PeakCapacityGiB, result.RecommendedCapacityGiB, 
                result.RecommendedCapacityInUnit + " " + result.CapacityUnit,
                result.PeakThroughputMiBps, result.RecommendedThroughputMiBps, result.DataQualityScore);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing metrics JSON");
            result.HasSufficientData = false;
            result.Warnings = $"Error parsing metrics data: {ex.Message}";
            result.DataQualityScore = 0.0;
        }
        
        return result;
    }
    
    /// <summary>
    /// Enhances sizing result with AI-generated reasoning and recommendations
    /// </summary>
    public async Task<CapacitySizingResult> EnhanceWithAiAnalysisAsync(
        CapacitySizingResult sizing,
        string? workloadType,
        string apiKey,
        string provider,
        string? endpoint,
        string? preferredModel = null)
    {
        try
        {
            var prompt = BuildSizingAnalysisPrompt(sizing, workloadType);
            var aiResponse = await CallAIForSizingAnalysis(prompt, apiKey, provider, endpoint, preferredModel);
            
            // Parse AI response for reasoning and service level suggestion
            var (reasoning, serviceLevel, warnings) = ParseSizingAiResponse(aiResponse);
            
            sizing.AiReasoning = reasoning;
            sizing.SuggestedServiceLevel = serviceLevel;
            
            // Append AI warnings to existing warnings
            if (!string.IsNullOrEmpty(warnings))
            {
                sizing.Warnings = string.IsNullOrEmpty(sizing.Warnings)
                    ? warnings
                    : $"{sizing.Warnings}; {warnings}";
            }
            
            _logger.LogInformation("AI enhancement complete. Service level: {ServiceLevel}", serviceLevel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enhance sizing with AI analysis");
            // Don't fail the entire analysis if AI enhancement fails
        }
        
        return sizing;
    }
    
    private double ExtractPeakCapacityFromAzureFiles(JsonElement root, ref int totalPoints, ref int metricsFound)
    {
        // FileCapacity metric contains capacity data
        if (root.TryGetProperty("FileCapacity", out var capacityMetric))
        {
            metricsFound++;
            var max = capacityMetric.TryGetProperty("max", out var maxEl) && maxEl.TryGetDouble(out var m) ? m : 0;
            var count = capacityMetric.TryGetProperty("dataPointCount", out var cEl) && cEl.TryGetInt32(out var c) ? c : 0;
            totalPoints += count;
            
            // FileCapacity is in bytes, convert to GiB
            return max / (1024.0 * 1024.0 * 1024.0);
        }
        
        return 0;
    }
    
    private (double readMiBps, double writeMiBps) ExtractThroughputFromAzureFiles(
        JsonElement root, ref int totalPoints, ref int metricsFound)
    {
        double readMiBps = 0;
        double writeMiBps = 0;
        
        // Egress = read throughput (bytes per second over the period)
        if (root.TryGetProperty("Egress", out var egressMetric))
        {
            metricsFound++;
            var maxEgress = egressMetric.TryGetProperty("max", out var maxEl) && maxEl.TryGetDouble(out var m) ? m : 0;
            var count = egressMetric.TryGetProperty("dataPointCount", out var cEl) && cEl.TryGetInt32(out var c) ? c : 0;
            totalPoints += count;
            
            // Egress is total bytes in the time interval (1 hour), convert to MiB/s
            // Max value over 1-hour intervals
            readMiBps = (maxEgress / 3600.0) / (1024.0 * 1024.0); // bytes/hour -> MiB/s
        }
        
        // Ingress = write throughput
        if (root.TryGetProperty("Ingress", out var ingressMetric))
        {
            metricsFound++;
            var maxIngress = ingressMetric.TryGetProperty("max", out var maxEl) && maxEl.TryGetDouble(out var m) ? m : 0;
            var count = ingressMetric.TryGetProperty("dataPointCount", out var cEl) && cEl.TryGetInt32(out var c) ? c : 0;
            totalPoints += count;
            
            writeMiBps = (maxIngress / 3600.0) / (1024.0 * 1024.0);
        }
        
        return (readMiBps, writeMiBps);
    }
    
    private double ExtractPeakCapacityFromAnf(JsonElement root, ref int totalPoints, ref int metricsFound)
    {
        // VolumeLogicalSize metric
        if (root.TryGetProperty("VolumeLogicalSize", out var capacityMetric))
        {
            metricsFound++;
            var max = capacityMetric.TryGetProperty("max", out var maxEl) && maxEl.TryGetDouble(out var m) ? m : 0;
            var count = capacityMetric.TryGetProperty("dataPointCount", out var cEl) && cEl.TryGetInt32(out var c) ? c : 0;
            totalPoints += count;
            
            // VolumeLogicalSize is in bytes, convert to GiB
            return max / (1024.0 * 1024.0 * 1024.0);
        }
        
        return 0;
    }
    
    private (double readMiBps, double writeMiBps, double readIOPS, double writeIOPS) ExtractThroughputFromAnf(
        JsonElement root, ref int totalPoints, ref int metricsFound)
    {
        double readMiBps = 0;
        double writeMiBps = 0;
        double readIOPS = 0;
        double writeIOPS = 0;
        
        // ReadThroughput or VolumeThroughputReadBytes
        if (root.TryGetProperty("ReadThroughput", out var readMetric))
        {
            metricsFound++;
            readMiBps = readMetric.TryGetProperty("max", out var maxEl) && maxEl.TryGetDouble(out var m) ? m : 0;
            var count = readMetric.TryGetProperty("dataPointCount", out var cEl) && cEl.TryGetInt32(out var c) ? c : 0;
            totalPoints += count;
            
            // ReadThroughput is already in MiB/s
        }
        else if (root.TryGetProperty("VolumeThroughputReadBytes", out var readBytesMetric))
        {
            metricsFound++;
            var maxBytes = readBytesMetric.TryGetProperty("max", out var maxEl) && maxEl.TryGetDouble(out var m) ? m : 0;
            var count = readBytesMetric.TryGetProperty("dataPointCount", out var cEl) && cEl.TryGetInt32(out var c) ? c : 0;
            totalPoints += count;
            
            // Convert bytes/second to MiB/s
            readMiBps = maxBytes / (1024.0 * 1024.0);
        }
        
        // WriteThroughput or VolumeThroughputWriteBytes
        if (root.TryGetProperty("WriteThroughput", out var writeMetric))
        {
            metricsFound++;
            writeMiBps = writeMetric.TryGetProperty("max", out var maxEl) && maxEl.TryGetDouble(out var m) ? m : 0;
            var count = writeMetric.TryGetProperty("dataPointCount", out var cEl) && cEl.TryGetInt32(out var c) ? c : 0;
            totalPoints += count;
        }
        else if (root.TryGetProperty("VolumeThroughputWriteBytes", out var writeBytesMetric))
        {
            metricsFound++;
            var maxBytes = writeBytesMetric.TryGetProperty("max", out var maxEl) && maxEl.TryGetDouble(out var m) ? m : 0;
            var count = writeBytesMetric.TryGetProperty("dataPointCount", out var cEl) && cEl.TryGetInt32(out var c) ? c : 0;
            totalPoints += count;
            
            writeMiBps = maxBytes / (1024.0 * 1024.0);
        }
        
        // IOPS metrics
        if (root.TryGetProperty("ReadIOPS", out var readIopsMetric))
        {
            metricsFound++;
            readIOPS = readIopsMetric.TryGetProperty("max", out var maxEl) && maxEl.TryGetDouble(out var m) ? m : 0;
            var count = readIopsMetric.TryGetProperty("dataPointCount", out var cEl) && cEl.TryGetInt32(out var c) ? c : 0;
            totalPoints += count;
        }
        
        if (root.TryGetProperty("WriteIOPS", out var writeIopsMetric))
        {
            metricsFound++;
            writeIOPS = writeIopsMetric.TryGetProperty("max", out var maxEl) && maxEl.TryGetDouble(out var m) ? m : 0;
            var count = writeIopsMetric.TryGetProperty("dataPointCount", out var cEl) && cEl.TryGetInt32(out var c) ? c : 0;
            totalPoints += count;
        }
        
        return (readMiBps, writeMiBps, readIOPS, writeIOPS);
    }
    
    private void FormatCapacity(CapacitySizingResult result)
    {
        // Format capacity in most appropriate unit
        if (result.RecommendedCapacityGiB >= 1024 * 1024) // >= 1 PiB
        {
            result.CapacityUnit = "PiB";
            result.RecommendedCapacityInUnit = result.RecommendedCapacityGiB / (1024.0 * 1024.0);
        }
        else if (result.RecommendedCapacityGiB >= 1024) // >= 1 TiB
        {
            result.CapacityUnit = "TiB";
            result.RecommendedCapacityInUnit = result.RecommendedCapacityGiB / 1024.0;
        }
        else
        {
            result.CapacityUnit = "GiB";
            result.RecommendedCapacityInUnit = result.RecommendedCapacityGiB;
        }
    }
    
    private double CalculateDataQualityScore(int totalDataPoints, int metricsFound, int daysAnalyzed)
    {
        // Expected: at least 3 metrics (capacity, read throughput, write throughput)
        // Expected: ~24 data points per day per metric for hourly data
        double metricsScore = Math.Min(1.0, metricsFound / 3.0);
        
        int expectedPoints = daysAnalyzed * 24 * metricsFound;
        double pointsScore = expectedPoints > 0 ? Math.Min(1.0, totalDataPoints / (double)expectedPoints) : 0.0;
        
        // Weighted average: 50% metrics availability, 50% data completeness
        return (metricsScore * 0.5) + (pointsScore * 0.5);
    }
    
    private string? GenerateWarnings(CapacitySizingResult result)
    {
        var warnings = new List<string>();
        
        if (result.DataQualityScore < 0.5)
        {
            warnings.Add("Insufficient historical data - recommendations may be unreliable");
        }
        else if (result.DataQualityScore < 0.7)
        {
            warnings.Add("Limited historical data - consider monitoring for longer period");
        }
        
        if (result.DaysAnalyzed < 30)
        {
            warnings.Add($"Only {result.DaysAnalyzed} days of data analyzed - 30+ days recommended");
        }
        
        if (result.PeakCapacityGiB == 0)
        {
            warnings.Add("No capacity metrics found");
        }
        
        if (result.PeakThroughputMiBps == 0)
        {
            warnings.Add("No throughput metrics found");
        }
        
        if (result.BufferPercent < 0)
        {
            warnings.Add($"Using aggressive sizing ({result.BufferPercent}% buffer) - ensure peak capacity is sufficient");
        }
        else if (result.BufferPercent < 20)
        {
            warnings.Add("Low buffer percentage - may not handle traffic spikes well");
        }
        
        return warnings.Count > 0 ? string.Join("; ", warnings) : null;
    }
    
    private string BuildSizingAnalysisPrompt(CapacitySizingResult sizing, string? workloadType)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an Azure NetApp Files sizing expert. Analyze the following capacity and throughput metrics:");
        sb.AppendLine();
        sb.AppendLine($"Workload Type: {workloadType ?? "Unknown"}");
        sb.AppendLine($"Historical Data Period: {sizing.DaysAnalyzed} days");
        sb.AppendLine($"Data Quality Score: {sizing.DataQualityScore:P1}");
        sb.AppendLine();
        sb.AppendLine("Capacity Metrics:");
        sb.AppendLine($"- Peak Capacity: {sizing.PeakCapacityGiB:0.##} GiB");
        sb.AppendLine($"- Recommended Capacity: {sizing.RecommendedCapacityInUnit:0.##} {sizing.CapacityUnit} (with {sizing.BufferPercent}% buffer)");
        sb.AppendLine();
        sb.AppendLine("Throughput Metrics:");
        sb.AppendLine($"- Peak Read Throughput: {sizing.PeakReadThroughputMiBps:0.##} MiB/s");
        sb.AppendLine($"- Peak Write Throughput: {sizing.PeakWriteThroughputMiBps:0.##} MiB/s");
        sb.AppendLine($"- Peak Total Throughput: {sizing.PeakThroughputMiBps:0.##} MiB/s");
        sb.AppendLine($"- Recommended Throughput: {sizing.RecommendedThroughputMiBps:0.##} MiB/s");
        
        if (sizing.PeakTotalIOPS > 0)
        {
            sb.AppendLine();
            sb.AppendLine("IOPS Metrics:");
            sb.AppendLine($"- Peak Read IOPS: {sizing.PeakReadIOPS:0.##}");
            sb.AppendLine($"- Peak Write IOPS: {sizing.PeakWriteIOPS:0.##}");
            sb.AppendLine($"- Peak Total IOPS: {sizing.PeakTotalIOPS:0.##}");
        }
        
        sb.AppendLine();
        sb.AppendLine("Based on these metrics, provide:");
        sb.AppendLine("1. Brief reasoning (2-3 sentences) for the sizing recommendation considering workload type and performance patterns");
        sb.AppendLine("2. Recommended ANF service level (Standard, Premium, or Ultra) based on throughput and IOPS requirements");
        sb.AppendLine("3. Any additional warnings or considerations");
        sb.AppendLine();
        sb.AppendLine("Format your response as JSON:");
        sb.AppendLine("{");
        sb.AppendLine("  \"reasoning\": \"your reasoning here\",");
        sb.AppendLine("  \"serviceLevel\": \"Standard|Premium|Ultra\",");
        sb.AppendLine("  \"warnings\": \"any warnings or null\"");
        sb.AppendLine("}");
        
        return sb.ToString();
    }
    
    private async Task<string> CallAIForSizingAnalysis(
        string prompt, string apiKey, string provider, string? endpoint, string? preferredModel)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            string apiUrl;
            var modelToUse = string.IsNullOrWhiteSpace(preferredModel) ? "gpt-4" : preferredModel.Trim();
            
            if (provider == "AzureOpenAI" && !string.IsNullOrEmpty(endpoint))
            {
                apiUrl = $"{endpoint.TrimEnd('/')}/openai/deployments/{modelToUse}/chat/completions?api-version=2024-02-15-preview";
                httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
            }
            else
            {
                apiUrl = "https://api.openai.com/v1/chat/completions";
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            }
            
            object requestPayload;
            if (provider == "AzureOpenAI")
            {
                requestPayload = new
                {
                    messages = new[] { new { role = "user", content = prompt } },
                    temperature = 0.3,
                    max_tokens = 400
                };
            }
            else
            {
                var useMaxCompletionTokens = modelToUse.StartsWith("gpt-5") || 
                                             modelToUse.StartsWith("o3") || 
                                             modelToUse.StartsWith("o4");
                
                if (useMaxCompletionTokens)
                {
                    requestPayload = new
                    {
                        model = modelToUse,
                        messages = new[] { new { role = "user", content = prompt } },
                        temperature = 0.3,
                        max_completion_tokens = 400
                    };
                }
                else
                {
                    requestPayload = new
                    {
                        model = modelToUse,
                        messages = new[] { new { role = "user", content = prompt } },
                        temperature = 0.3,
                        max_tokens = 400
                    };
                }
            }
            
            var requestBody = JsonSerializer.Serialize(requestPayload);
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            
            var response = await httpClient.PostAsync(apiUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"AI API call failed: {(int)response.StatusCode} {response.ReasonPhrase}");
            }
            
            var jsonResponse = JsonDocument.Parse(responseBody);
            var messageContent = jsonResponse.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            
            return messageContent ?? "No response";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling AI API for sizing analysis");
            throw;
        }
    }
    
    private (string reasoning, string serviceLevel, string? warnings) ParseSizingAiResponse(string aiResponse)
    {
        try
        {
            // Extract JSON from response (may be wrapped in markdown)
            var jsonStart = aiResponse.IndexOf('{');
            var jsonEnd = aiResponse.LastIndexOf('}');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = aiResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var jsonDoc = JsonDocument.Parse(jsonStr);
                var root = jsonDoc.RootElement;
                
                var reasoning = root.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";
                var serviceLevel = root.TryGetProperty("serviceLevel", out var sl) ? sl.GetString() ?? "Standard" : "Standard";
                var warnings = root.TryGetProperty("warnings", out var w) && w.ValueKind != JsonValueKind.Null 
                    ? w.GetString() 
                    : null;
                
                return (reasoning, serviceLevel, warnings);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI sizing response");
        }
        
        // Fallback - extract service level from text
        var text = aiResponse.ToUpperInvariant();
        string fallbackServiceLevel = "Standard";
        if (text.Contains("ULTRA")) fallbackServiceLevel = "Ultra";
        else if (text.Contains("PREMIUM")) fallbackServiceLevel = "Premium";
        
        return (aiResponse, fallbackServiceLevel, null);
    }
}
