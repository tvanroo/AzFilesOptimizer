using Azure.Storage.Blobs;
using AzFilesOptimizer.Backend.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AzFilesOptimizer.Backend.Services;

public class VolumeAnalysisService
{
    private readonly ILogger _logger;
    private readonly BlobContainerClient _blobContainer;
    private readonly WorkloadProfileService _profileService;
    private readonly AnalysisPromptService _promptService;

    public VolumeAnalysisService(
        string connectionString,
        WorkloadProfileService profileService,
        AnalysisPromptService promptService,
        ILogger logger)
    {
        _logger = logger;
        _profileService = profileService;
        _promptService = promptService;
        
        var blobServiceClient = new BlobServiceClient(connectionString);
        _blobContainer = blobServiceClient.GetBlobContainerClient("discovery-data");
        _blobContainer.CreateIfNotExists();
    }

    public async Task AnalyzeVolumesAsync(string discoveryJobId, string userId, string apiKey, string provider, string? endpoint)
    {
        _logger.LogInformation("Starting analysis for discovery job: {JobId}", discoveryJobId);

        // Load discovery data
        var discoveryData = await LoadDiscoveryDataAsync(discoveryJobId);
        if (discoveryData == null || discoveryData.Volumes.Count == 0)
        {
            _logger.LogWarning("No volumes found for discovery job: {JobId}", discoveryJobId);
            return;
        }

        // Load enabled prompts and workload profiles
        var prompts = await _promptService.GetEnabledPromptsAsync();
        var profiles = await _profileService.GetAllProfilesAsync();

        _logger.LogInformation("Loaded {PromptCount} prompts and {ProfileCount} profiles", prompts.Count, profiles.Count);

        int processedCount = 0;
        int failedCount = 0;

        // Analyze each volume
        foreach (var volumeWrapper in discoveryData.Volumes)
        {
            try
            {
                _logger.LogInformation("Analyzing volume: {VolumeName}", volumeWrapper.Volume.ShareName);
                
                var analysis = await AnalyzeSingleVolumeAsync(
                    volumeWrapper.Volume,
                    prompts.ToArray(),
                    profiles.ToArray(),
                    apiKey,
                    provider,
                    endpoint);
                
                volumeWrapper.AiAnalysis = analysis;
                volumeWrapper.UserAnnotations ??= new UserAnnotations();
                
                processedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze volume: {VolumeName}", volumeWrapper.Volume.ShareName);
                volumeWrapper.AiAnalysis = new AiAnalysisResult
                {
                    LastAnalyzed = DateTime.UtcNow,
                    ErrorMessage = ex.Message
                };
                failedCount++;
            }
        }

        // Save updated discovery data
        discoveryData.LastAnalyzed = DateTime.UtcNow;
        await SaveDiscoveryDataAsync(discoveryData);

        _logger.LogInformation("Analysis complete. Processed: {Processed}, Failed: {Failed}", processedCount, failedCount);
    }

    public async Task<AiAnalysisResult> AnalyzeSingleVolumeAsync(
        DiscoveredAzureFileShare volume,
        AnalysisPrompt[] prompts,
        WorkloadProfile[] profiles,
        string apiKey,
        string provider,
        string? endpoint)
    {
        var result = new AiAnalysisResult
        {
            LastAnalyzed = DateTime.UtcNow,
            AppliedPrompts = new List<PromptExecutionResult>().ToArray()
        };

        var appliedPrompts = new List<PromptExecutionResult>();
        bool shouldStop = false;

        foreach (var prompt in prompts.OrderBy(p => p.Priority))
        {
            if (shouldStop)
            {
                _logger.LogInformation("Stopping prompt processing for volume {VolumeName} due to stop condition", volume.ShareName);
                break;
            }

            try
            {
                // Substitute variables in prompt template
                var processedPrompt = SubstitutePromptVariables(prompt.PromptTemplate, volume);
                
                // Add workload profile context
                var fullPrompt = BuildFullPrompt(processedPrompt, profiles);
                
                // Call AI
                var aiResponse = await CallAIForAnalysis(fullPrompt, apiKey, provider, endpoint);
                
                // Parse response
                var promptResult = new PromptExecutionResult
                {
                    PromptId = prompt.PromptId,
                    PromptName = prompt.Name,
                    Result = aiResponse,
                    Evidence = ExtractEvidence(aiResponse)
                };

                // Check stop conditions
                if (prompt.StopCondition.StopOnMatch && CheckIfMatches(aiResponse, prompt.Category))
                {
                    promptResult.StoppedProcessing = true;
                    shouldStop = true;

                    // Apply stop action
                    ApplyStopAction(prompt.StopCondition, result, profiles);
                }

                appliedPrompts.Add(promptResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing prompt {PromptName} on volume {VolumeName}", 
                    prompt.Name, volume.ShareName);
                
                appliedPrompts.Add(new PromptExecutionResult
                {
                    PromptId = prompt.PromptId,
                    PromptName = prompt.Name,
                    Result = $"Error: {ex.Message}",
                    StoppedProcessing = false
                });
            }
        }

        result.AppliedPrompts = appliedPrompts.ToArray();
        
        // Calculate overall confidence if workload was determined
        if (!string.IsNullOrEmpty(result.SuggestedWorkloadId))
        {
            result.ConfidenceScore = CalculateConfidence(appliedPrompts);
        }

        return result;
    }

    public string SubstitutePromptVariables(string template, DiscoveredAzureFileShare volume)
    {
        var substituted = template;
        
        substituted = substituted.Replace("{VolumeName}", volume.ShareName ?? "Unknown");
        substituted = substituted.Replace("{Size}", $"{volume.ShareQuotaGiB ?? 0} GiB");
        substituted = substituted.Replace("{SizeGB}", $"{volume.ShareQuotaGiB ?? 0}");
        substituted = substituted.Replace("{UsedCapacity}", FormatBytes(volume.ShareUsageBytes ?? 0));
        substituted = substituted.Replace("{Tags}", FormatDictionary(volume.Tags));
        substituted = substituted.Replace("{Metadata}", FormatDictionary(volume.Metadata));
        substituted = substituted.Replace("{StorageAccount}", volume.StorageAccountName ?? "Unknown");
        substituted = substituted.Replace("{ResourceGroup}", volume.ResourceGroup ?? "Unknown");
        substituted = substituted.Replace("{PerformanceTier}", volume.AccessTier ?? "Unknown");
        substituted = substituted.Replace("{StorageAccountSku}", volume.StorageAccountSku ?? "Unknown");
        substituted = substituted.Replace("{ProvisionedIOPS}", volume.ProvisionedIops?.ToString() ?? "N/A");
        substituted = substituted.Replace("{ProvisionedBandwidth}", volume.ProvisionedBandwidthMiBps?.ToString() ?? "N/A");
        substituted = substituted.Replace("{Protocols}", volume.EnabledProtocols != null ? string.Join(", ", volume.EnabledProtocols) : "Unknown");
        substituted = substituted.Replace("{Location}", volume.Location ?? "Unknown");
        substituted = substituted.Replace("{LeaseStatus}", volume.LeaseStatus ?? "Unknown");
        substituted = substituted.Replace("{AccessTier}", volume.AccessTier ?? "Unknown");
        
        return substituted;
    }

    private string BuildFullPrompt(string userPrompt, WorkloadProfile[] profiles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an Azure storage workload classification expert. Your task is to analyze Azure Files volumes and classify them based on their characteristics.");
        sb.AppendLine();
        sb.AppendLine("Available Workload Classifications:");
        
        foreach (var profile in profiles.Where(p => !p.IsExclusionProfile))
        {
            sb.AppendLine($"- {profile.Name}: {profile.Description.Substring(0, Math.Min(200, profile.Description.Length))}...");
        }
        
        sb.AppendLine();
        sb.AppendLine("Volume to analyze:");
        sb.AppendLine(userPrompt);
        sb.AppendLine();
        sb.AppendLine("Provide your analysis. If this is a workload detection prompt, respond with MATCH or NO_MATCH followed by confidence (0-100) and brief reasoning.");
        
        return sb.ToString();
    }

    private async Task<string> CallAIForAnalysis(string prompt, string apiKey, string provider, string? endpoint)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            string apiUrl;
            string requestBody;

            if (provider == "AzureOpenAI" && !string.IsNullOrEmpty(endpoint))
            {
                // Azure OpenAI format
                apiUrl = $"{endpoint.TrimEnd('/')}/openai/deployments/gpt-4/chat/completions?api-version=2024-02-15-preview";
                httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
            }
            else
            {
                // OpenAI format
                apiUrl = "https://api.openai.com/v1/chat/completions";
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            }

            var requestPayload = new
            {
                model = provider == "AzureOpenAI" ? "" : "gpt-4",
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                temperature = 0.3,
                max_tokens = 500
            };

            requestBody = JsonSerializer.Serialize(requestPayload);
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(apiUrl, content);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
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
            _logger.LogError(ex, "Error calling AI API");
            throw new InvalidOperationException($"AI API call failed: {ex.Message}", ex);
        }
    }

    private bool CheckIfMatches(string aiResponse, string promptCategory)
    {
        // Simple match detection - look for keywords
        var response = aiResponse.ToUpperInvariant();
        
        if (response.Contains("MATCH") && !response.Contains("NO_MATCH") && !response.Contains("NO MATCH"))
            return true;
            
        if (response.Contains("YES") && promptCategory == PromptCategory.Exclusion.ToString())
            return true;
            
        return false;
    }

    private void ApplyStopAction(StopConditions stopCondition, AiAnalysisResult result, WorkloadProfile[] profiles)
    {
        switch (stopCondition.ActionOnMatch)
        {
            case StopAction.SetWorkload:
                if (!string.IsNullOrEmpty(stopCondition.TargetWorkloadId))
                {
                    var workload = profiles.FirstOrDefault(p => p.ProfileId == stopCondition.TargetWorkloadId);
                    if (workload != null)
                    {
                        result.SuggestedWorkloadId = workload.ProfileId;
                        result.SuggestedWorkloadName = workload.Name;
                    }
                }
                break;
                
            case StopAction.ExcludeVolume:
                result.SuggestedWorkloadId = "excluded";
                result.SuggestedWorkloadName = "Excluded from Migration";
                break;
        }
    }

    private string[] ExtractEvidence(string aiResponse)
    {
        var evidence = new List<string>();
        
        // Simple extraction - split by sentences or bullet points
        var lines = aiResponse.Split(new[] { '\n', '.', '•', '-' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines.Take(3))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 10 && trimmed.Length < 200)
            {
                evidence.Add(trimmed);
            }
        }
        
        return evidence.ToArray();
    }

    private double CalculateConfidence(List<PromptExecutionResult> prompts)
    {
        // Extract confidence scores from responses
        var confidenceScores = new List<int>();
        
        foreach (var prompt in prompts)
        {
            var match = Regex.Match(prompt.Result, @"confidence[:\s]+(\d+)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int score))
            {
                confidenceScores.Add(score);
            }
        }
        
        if (confidenceScores.Any())
        {
            return confidenceScores.Average() / 100.0;
        }
        
        return 0.5; // Default moderate confidence
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private string FormatDictionary(Dictionary<string, string>? dict)
    {
        if (dict == null || dict.Count == 0)
            return "None";
            
        return string.Join(", ", dict.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
    }

    private async Task<DiscoveryData?> LoadDiscoveryDataAsync(string jobId)
    {
        try
        {
            var blobClient = _blobContainer.GetBlobClient($"jobs/{jobId}/discovered-volumes.json");
            
            if (!await blobClient.ExistsAsync())
            {
                _logger.LogWarning("Discovery data not found for job: {JobId}", jobId);
                return null;
            }
            
            var response = await blobClient.DownloadContentAsync();
            var json = response.Value.Content.ToString();
            return JsonSerializer.Deserialize<DiscoveryData>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading discovery data for job: {JobId}", jobId);
            throw;
        }
    }

    private async Task SaveDiscoveryDataAsync(DiscoveryData data)
    {
        try
        {
            var blobClient = _blobContainer.GetBlobClient($"jobs/{data.JobId}/discovered-volumes.json");
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await blobClient.UploadAsync(BinaryData.FromString(json), overwrite: true);
            
            _logger.LogInformation("Saved discovery data for job: {JobId}", data.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving discovery data for job: {JobId}", data.JobId);
            throw;
        }
    }
}
