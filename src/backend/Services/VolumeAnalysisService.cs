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
    private AnalysisLogService? _logService;
    private Azure.Data.Tables.TableClient? _analysisJobsTable;
    private string? _currentAnalysisJobId;

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

    public async Task AnalyzeVolumesAsync(string discoveryJobId, string userId, string apiKey, string provider, string? endpoint, string? analysisJobId = null, string? preferredModel = null)
    {
        _logger.LogInformation("Starting analysis for discovery job: {JobId}", discoveryJobId);
        
        // Initialize log service and job tracking if analysisJobId provided
        if (!string.IsNullOrEmpty(analysisJobId))
        {
            _currentAnalysisJobId = analysisJobId;
            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
            _logService = new AnalysisLogService(connectionString, _logger);
            await _logService.LogProgressAsync(analysisJobId, $"Starting analysis for discovery job: {discoveryJobId}");
            
            // Initialize table client for progress updates
            var tableServiceClient = new Azure.Data.Tables.TableServiceClient(connectionString);
            _analysisJobsTable = tableServiceClient.GetTableClient("AnalysisJobs");
        }

        // Load discovery data
        var discoveryData = await LoadDiscoveryDataAsync(discoveryJobId);
        if (discoveryData == null || discoveryData.Volumes.Count == 0)
        {
            _logger.LogWarning("No volumes found for discovery job: {JobId}", discoveryJobId);
            if (_logService != null && !string.IsNullOrEmpty(analysisJobId))
                await _logService.LogProgressAsync(analysisJobId, "No volumes found for analysis", "WARNING");
            return;
        }

        // Load enabled prompts and workload profiles
        var prompts = await _promptService.GetEnabledPromptsAsync();
        var profiles = await _profileService.GetAllProfilesAsync();

        _logger.LogInformation("Loaded {PromptCount} prompts and {ProfileCount} profiles", prompts.Count, profiles.Count);
        if (_logService != null && !string.IsNullOrEmpty(analysisJobId))
            await _logService.LogProgressAsync(analysisJobId, $"Found {discoveryData.Volumes.Count} volumes to analyze using {prompts.Count} prompts");

        int processedCount = 0;
        int failedCount = 0;
        int totalCount = discoveryData.Volumes.Count;

        // Analyze each volume
        foreach (var volumeWrapper in discoveryData.Volumes)
        {
            processedCount++;
            try
            {
                _logger.LogInformation("Analyzing volume: {VolumeName}", volumeWrapper.Volume.ShareName);
                if (_logService != null && !string.IsNullOrEmpty(analysisJobId))
                    await _logService.LogVolumeStartAsync(analysisJobId, volumeWrapper.Volume.ShareName ?? "Unknown", processedCount, totalCount);
                
                var analysis = await AnalyzeSingleVolumeAsync(
                    volumeWrapper.Volume,
                    prompts.ToArray(),
                    profiles.ToArray(),
                    apiKey,
                    provider,
                    endpoint,
                    analysisJobId,
                    volumeWrapper.Volume.ShareName ?? "Unknown",
                    preferredModel);
                
                volumeWrapper.AiAnalysis = analysis;
                volumeWrapper.UserAnnotations ??= new UserAnnotations();
                
                if (_logService != null && !string.IsNullOrEmpty(analysisJobId))
                {
                    var workloadName = analysis.SuggestedWorkloadName ?? "Unclassified";
                    await _logService.LogVolumeCompleteAsync(analysisJobId, volumeWrapper.Volume.ShareName ?? "Unknown", workloadName, analysis.ConfidenceScore);
                }
                
                // Update job progress
                await UpdateJobProgressAsync(processedCount, failedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze volume: {VolumeName}", volumeWrapper.Volume.ShareName);
                if (_logService != null && !string.IsNullOrEmpty(analysisJobId))
                    await _logService.LogVolumeErrorAsync(analysisJobId, volumeWrapper.Volume.ShareName ?? "Unknown", ex.Message);
                    
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
        if (_logService != null && !string.IsNullOrEmpty(analysisJobId))
            await _logService.LogProgressAsync(analysisJobId, $"✓ Analysis complete! Processed: {processedCount}, Failed: {failedCount}");
    }

    public async Task<AiAnalysisResult> AnalyzeSingleVolumeAsync(
        DiscoveredAzureFileShare volume,
        AnalysisPrompt[] prompts,
        WorkloadProfile[] profiles,
        string apiKey,
        string provider,
        string? endpoint,
        string? analysisJobId = null,
        string? volumeName = null,
        string? preferredModel = null)
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
                if (_logService != null && !string.IsNullOrEmpty(analysisJobId) && !string.IsNullOrEmpty(volumeName))
                {
                    await _logService.LogProgressAsync(
                        analysisJobId,
                        $"  [{volumeName}] → Executing prompt '{prompt.Name}' (priority {prompt.Priority})");
                }
                // Substitute volume and workload profile variables in prompt template
                var processedPrompt = SubstitutePromptVariables(prompt.PromptTemplate, volume);
                processedPrompt = SubstituteWorkloadProfileVariables(processedPrompt, profiles);

                // Add workload profile context and instructions
                var fullPrompt = BuildFullPrompt(processedPrompt, profiles);

                // Call AI
                var aiResponse = await CallAIForAnalysis(fullPrompt, apiKey, provider, endpoint, preferredModel);
                
                // Log prompt execution (both prompt and response, truncated for readability)
                if (_logService != null && !string.IsNullOrEmpty(analysisJobId) && !string.IsNullOrEmpty(volumeName))
                {
                    await _logService.LogPromptExecutionAsync(analysisJobId, volumeName, prompt.Name, fullPrompt, aiResponse);
                }

                // Parse structured response
                var (classification, confidence, reasoning) = ParseStructuredResponse(aiResponse);
                
                var promptResult = new PromptExecutionResult
                {
                    PromptId = prompt.PromptId,
                    PromptName = prompt.Name,
                    Result = aiResponse,
                    Evidence = ExtractEvidence(aiResponse)
                };

                // Check stop conditions using structured response
                bool matched = CheckIfMatches(aiResponse, prompt.Category);
                
                if (prompt.StopCondition.StopOnMatch && matched)
                {
                    promptResult.StoppedProcessing = true;
                    shouldStop = true;

                    // If structured parsing found a classification, use it
                    if (!string.IsNullOrEmpty(classification))
                    {
                        var workload = profiles.FirstOrDefault(p => p.ProfileId == classification);
                        if (workload != null)
                        {
                            result.SuggestedWorkloadId = workload.ProfileId;
                            result.SuggestedWorkloadName = workload.Name;
                            result.ConfidenceScore = confidence / 100.0;
                            
                            _logger.LogInformation("Structured classification: {WorkloadName} with {Confidence}% confidence", 
                                workload.Name, confidence);
                        }
                    }
                    else
                    {
                        // Fallback to old logic
                        ApplyStopAction(prompt.StopCondition, result, profiles);
                    }
                }
                else if (!string.IsNullOrEmpty(classification) && matched)
                {
                    // Workload detected but not stopping - store for later
                    var workload = profiles.FirstOrDefault(p => p.ProfileId == classification);
                    if (workload != null && string.IsNullOrEmpty(result.SuggestedWorkloadId))
                    {
                        result.SuggestedWorkloadId = workload.ProfileId;
                        result.SuggestedWorkloadName = workload.Name;
                        result.ConfidenceScore = confidence / 100.0;
                    }
                }

                appliedPrompts.Add(promptResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing prompt {PromptName} on volume {VolumeName}",
                    prompt.Name, volume.ShareName);

                if (_logService != null && !string.IsNullOrEmpty(analysisJobId) && !string.IsNullOrEmpty(volumeName))
                {
                    await _logService.LogProgressAsync(
                        analysisJobId,
                        $"  [{volumeName}] ✗ Prompt '{prompt.Name}' failed: {ex.Message}",
                        "ERROR");
                }

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
        if (string.IsNullOrEmpty(template))
            return template;

        var substituted = template;

        // Identity / hierarchy
        substituted = substituted.Replace("{TenantId}", volume.TenantId ?? "Unknown");
        substituted = substituted.Replace("{SubscriptionId}", volume.SubscriptionId ?? "Unknown");
        substituted = substituted.Replace("{ResourceGroup}", volume.ResourceGroup ?? "Unknown");
        substituted = substituted.Replace("{StorageAccount}", volume.StorageAccountName ?? "Unknown");
        substituted = substituted.Replace("{VolumeName}", volume.ShareName ?? "Unknown");
        substituted = substituted.Replace("{ResourceId}", volume.ResourceId ?? "Unknown");
        substituted = substituted.Replace("{Location}", volume.Location ?? "Unknown");

        // Storage account configuration / security
        substituted = substituted.Replace("{StorageAccountSku}", volume.StorageAccountSku ?? "Unknown");
        substituted = substituted.Replace("{StorageAccountKind}", volume.StorageAccountKind ?? "Unknown");
        substituted = substituted.Replace("{EnableHttpsTrafficOnly}", volume.EnableHttpsTrafficOnly?.ToString() ?? "Unknown");
        substituted = substituted.Replace("{MinimumTlsVersion}", volume.MinimumTlsVersion ?? "Unknown");
        substituted = substituted.Replace("{AllowBlobPublicAccess}", volume.AllowBlobPublicAccess?.ToString() ?? "Unknown");
        substituted = substituted.Replace("{AllowSharedKeyAccess}", volume.AllowSharedKeyAccess?.ToString() ?? "Unknown");

        // Capacity & usage
        var quotaGiB = volume.ShareQuotaGiB ?? 0;
        var usageBytes = volume.ShareUsageBytes ?? 0;
        substituted = substituted.Replace("{Size}", $"{quotaGiB} GiB");
        substituted = substituted.Replace("{SizeGB}", quotaGiB.ToString());
        substituted = substituted.Replace("{ShareQuotaGiB}", quotaGiB.ToString());
        substituted = substituted.Replace("{ShareUsageBytes}", usageBytes.ToString());
        substituted = substituted.Replace("{UsedCapacity}", FormatBytes(usageBytes));
        substituted = substituted.Replace("{Protocols}", volume.EnabledProtocols != null ? string.Join(", ", volume.EnabledProtocols) : "Unknown");
        substituted = substituted.Replace("{RootSquash}", volume.RootSquash ?? "Unknown");

        // Performance
        substituted = substituted.Replace("{PerformanceTier}", volume.AccessTier ?? "Unknown");
        substituted = substituted.Replace("{AccessTier}", volume.AccessTier ?? "Unknown");
        substituted = substituted.Replace("{ProvisionedIOPS}", volume.ProvisionedIops?.ToString() ?? "N/A");
        substituted = substituted.Replace("{ProvisionedBandwidth}", volume.ProvisionedBandwidthMiBps?.ToString() ?? "N/A");
        substituted = substituted.Replace("{EstimatedIOPS}", volume.EstimatedIops?.ToString() ?? "N/A");
        substituted = substituted.Replace("{EstimatedThroughputMiBps}", volume.EstimatedThroughputMiBps?.ToString("0.##") ?? "N/A");

        // Lease / lifecycle
        substituted = substituted.Replace("{LeaseStatus}", volume.LeaseStatus ?? "Unknown");
        substituted = substituted.Replace("{LeaseState}", volume.LeaseState ?? "Unknown");
        substituted = substituted.Replace("{LeaseDuration}", volume.LeaseDuration ?? "Unknown");
        substituted = substituted.Replace("{IsDeleted}", (volume.IsDeleted ?? false).ToString());
        substituted = substituted.Replace("{DeletedTime}", volume.DeletedTime?.ToString("o") ?? "N/A");
        substituted = substituted.Replace("{RemainingRetentionDays}", volume.RemainingRetentionDays?.ToString() ?? "N/A");
        substituted = substituted.Replace("{Version}", volume.Version ?? "Unknown");

        // Snapshots / churn / backup
        substituted = substituted.Replace("{IsSnapshot}", volume.IsSnapshot.ToString());
        substituted = substituted.Replace("{SnapshotTime}", volume.SnapshotTime?.ToString("o") ?? "N/A");
        substituted = substituted.Replace("{SnapshotId}", volume.SnapshotId ?? "N/A");
        substituted = substituted.Replace("{SnapshotCount}", volume.SnapshotCount?.ToString() ?? "0");
        substituted = substituted.Replace("{TotalSnapshotSizeBytes}", volume.TotalSnapshotSizeBytes?.ToString() ?? "0");
        substituted = substituted.Replace("{ChurnRateBytesPerDay}", volume.ChurnRateBytesPerDay?.ToString("0.##") ?? "N/A");
        substituted = substituted.Replace("{BackupPolicyConfigured}", volume.BackupPolicyConfigured?.ToString() ?? "Unknown");

        // Timestamps
        substituted = substituted.Replace("{CreationTime}", volume.CreationTime?.ToString("o") ?? "N/A");
        substituted = substituted.Replace("{LastModifiedTime}", volume.LastModifiedTime?.ToString("o") ?? "N/A");
        substituted = substituted.Replace("{DiscoveredAt}", volume.DiscoveredAt.ToString("o"));

        // Metadata & tags
        substituted = substituted.Replace("{Tags}", FormatDictionary(volume.Tags));
        substituted = substituted.Replace("{Metadata}", FormatDictionary(volume.Metadata));

        // Monitoring / metrics
        substituted = substituted.Replace("{MonitoringEnabled}", volume.MonitoringEnabled ? "true" : "false");
        substituted = substituted.Replace("{MonitoringDataAvailableDays}", volume.MonitoringDataAvailableDays?.ToString() ?? "N/A");
        substituted = substituted.Replace("{HistoricalMetricsSummary}", volume.HistoricalMetricsSummary ?? "None");
        substituted = substituted.Replace("{MetricsSummary}", FormatMetricsSummary(volume.HistoricalMetricsSummary));

        return substituted;
    }

    private string SubstituteWorkloadProfileVariables(string template, WorkloadProfile[] profiles)
    {
        if (string.IsNullOrEmpty(template) || profiles == null || profiles.Length == 0)
            return template;

        var regex = new Regex(@"\{WorkloadProfile:([^}]+)\}", RegexOptions.IgnoreCase);
        var result = regex.Replace(template, match =>
        {
            var profileId = match.Groups[1].Value;
            var profile = profiles.FirstOrDefault(p =>
                string.Equals(p.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));

            if (profile == null)
            {
                return $"[Unknown workload profile: {profileId}]";
            }

            return FormatWorkloadProfileForPrompt(profile);
        });

        return result;
    }

    private string FormatWorkloadProfileForPrompt(WorkloadProfile profile)
    {
        var perf = profile.PerformanceRequirements;
        var anf = profile.AnfSuitabilityInfo;
        var hints = profile.Hints;

        var sb = new StringBuilder();
        sb.AppendLine($"Workload profile: {profile.Name} (Id: {profile.ProfileId})");
        sb.AppendLine($"Description: {profile.Description}");
        sb.AppendLine("Performance requirements:");
        sb.AppendLine($"  - Size range: {perf.MinSizeGB?.ToString() ?? "?"}–{perf.MaxSizeGB?.ToString() ?? "?"} GB");
        sb.AppendLine($"  - IOPS range: {perf.MinIops?.ToString() ?? "?"}–{perf.MaxIops?.ToString() ?? "?"}");
        sb.AppendLine($"  - Latency sensitivity: {perf.LatencySensitivity}");
        sb.AppendLine($"  - Throughput range: {perf.MinThroughputMBps?.ToString() ?? "?"}–{perf.MaxThroughputMBps?.ToString() ?? "?"} MB/s");
        if (!string.IsNullOrEmpty(perf.IoPattern))
            sb.AppendLine($"  - I/O pattern: {perf.IoPattern}");

        sb.AppendLine("ANF suitability:");
        sb.AppendLine($"  - Compatible with ANF: {anf.Compatible}");
        if (!string.IsNullOrEmpty(anf.RecommendedServiceLevel))
            sb.AppendLine($"  - Recommended ANF service level: {anf.RecommendedServiceLevel}");
        if (!string.IsNullOrEmpty(anf.Notes))
            sb.AppendLine($"  - Notes: {anf.Notes}");
        if (anf.Caveats != null && anf.Caveats.Length > 0)
            sb.AppendLine($"  - Caveats: {string.Join("; ", anf.Caveats)}");

        sb.AppendLine("Detection hints (for matching volumes to this workload):");
        if (hints.NamingPatterns != null && hints.NamingPatterns.Length > 0)
            sb.AppendLine($"  - Naming patterns: {string.Join(", ", hints.NamingPatterns)}");
        if (hints.CommonTags != null && hints.CommonTags.Length > 0)
            sb.AppendLine($"  - Common tags: {string.Join(", ", hints.CommonTags)}");
        if (hints.FileTypeIndicators != null && hints.FileTypeIndicators.Length > 0)
            sb.AppendLine($"  - File types: {string.Join(", ", hints.FileTypeIndicators)}");
        if (hints.PathPatterns != null && hints.PathPatterns.Length > 0)
            sb.AppendLine($"  - Path patterns: {string.Join(", ", hints.PathPatterns)}");

        return sb.ToString();
    }

    private string BuildFullPrompt(string userPrompt, WorkloadProfile[] profiles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an Azure storage workload classification expert. Your task is to analyze Azure Files volumes and classify them based on their characteristics.");
        sb.AppendLine();
        sb.AppendLine("Available Workload Classifications (summary):");

        foreach (var profile in profiles.Where(p => !p.IsExclusionProfile))
        {
            var desc = profile.Description ?? string.Empty;
            if (desc.Length > 220)
            {
                desc = desc.Substring(0, 220) + "...";
            }
            sb.AppendLine($"- {profile.Name} (ID: {profile.ProfileId}): {desc}");
        }

        sb.AppendLine();
        sb.AppendLine("Volume to analyze and instructions:");
        sb.AppendLine(userPrompt);
        sb.AppendLine();
        sb.AppendLine("When workload profile details are inlined (via {WorkloadProfile:<id>} markers already expanded above), explicitly compare the volume against those profiles.");
        sb.AppendLine();
        
        // Add structured output requirements (cannot be edited by users - applies to ALL prompts)
        sb.AppendLine("========================================");
        sb.AppendLine("MANDATORY OUTPUT FORMAT (NON-NEGOTIABLE)");
        sb.AppendLine("========================================");
        sb.AppendLine();
        sb.AppendLine("You MUST respond with EXACTLY this JSON structure and NOTHING else:");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"match\": \"YES\" or \"NO\",");
        sb.AppendLine("  \"classification\": \"workload-profile-id\" or null,");
        sb.AppendLine("  \"confidence\": 0-100,");
        sb.AppendLine("  \"reasoning\": \"brief explanation\"");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("FIELD DEFINITIONS:");
        sb.AppendLine("- match: String \"YES\" or \"NO\" ONLY (case-sensitive)");
        sb.AppendLine("  * For EXCLUSION prompts: \"YES\" = exclude this volume (stop processing), \"NO\" = not excluded (continue)");
        sb.AppendLine("  * For WORKLOAD DETECTION prompts: \"YES\" = this workload matches, \"NO\" = does not match");
        sb.AppendLine("- classification: String containing valid ProfileId from list above, or null");
        sb.AppendLine("  * Set to ProfileId if match=YES and you can classify to a specific workload");
        sb.AppendLine("  * Set to null if match=NO or cannot classify");
        sb.AppendLine("- confidence: Integer between 0 and 100 (inclusive) representing certainty percentage");
        sb.AppendLine("- reasoning: String with one brief sentence explaining your decision");
        sb.AppendLine();
        sb.AppendLine("CRITICAL RULES:");
        sb.AppendLine("1. Output ONLY the JSON block above - no additional text before or after");
        sb.AppendLine("2. Use exact field names and value formats as specified");
        sb.AppendLine("3. match field must be exactly \"YES\" or \"NO\" - no other values accepted");
        sb.AppendLine("4. confidence must be a numeric integer, not a string");
        sb.AppendLine("5. All string values must use double quotes");
        sb.AppendLine("6. Do not include any analysis or explanation outside the JSON structure");

        return sb.ToString();
    }

    private async Task<string> CallAIForAnalysis(string prompt, string apiKey, string provider, string? endpoint, string? preferredModel)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            string apiUrl;

            var modelToUse = string.IsNullOrWhiteSpace(preferredModel) ? "gpt-4" : preferredModel.Trim();

            if (provider == "AzureOpenAI" && !string.IsNullOrEmpty(endpoint))
            {
                // Azure OpenAI: deployment is in the URL, model name comes from the selected preference
                apiUrl = $"{endpoint.TrimEnd('/')}/openai/deployments/{modelToUse}/chat/completions?api-version=2024-02-15-preview";
                httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
            }
            else
            {
                // Public OpenAI
                apiUrl = "https://api.openai.com/v1/chat/completions";
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            }

            object requestPayload;

            if (provider == "AzureOpenAI")
            {
                // For Azure, the deployment is already encoded in the URL; request body does not need a model field
                requestPayload = new
                {
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.3,
                    max_tokens = 500
                };
            }
            else
            {
                // For OpenAI, send the selected model in the body
                // Newer GPT-5 / O-series models require max_completion_tokens instead of max_tokens
                var useMaxCompletionTokens = ModelRequiresMaxCompletionTokens(modelToUse);

                if (useMaxCompletionTokens)
                {
                    requestPayload = new
                    {
                        model = modelToUse,
                        messages = new[]
                        {
                            new { role = "user", content = prompt }
                        },
                        temperature = 0.3,
                        max_completion_tokens = 500
                    };
                }
                else
                {
                    requestPayload = new
                    {
                        model = modelToUse,
                        messages = new[]
                        {
                            new { role = "user", content = prompt }
                        },
                        temperature = 0.3,
                        max_tokens = 500
                    };
                }
            }

            var requestBody = JsonSerializer.Serialize(requestPayload);

            // Log request details (excluding API key)
            var truncatedRequestBody = requestBody.Length > 2000
                ? requestBody.Substring(0, 2000) + "... (truncated)"
                : requestBody;
            _logger.LogInformation(
                "Calling AI provider {Provider} at {Url} with model {Model}",
                provider,
                apiUrl,
                modelToUse);
            _logger.LogDebug("AI request payload: {Payload}", truncatedRequestBody);

            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(apiUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var truncatedResponseBody = responseBody.Length > 2000
                    ? responseBody.Substring(0, 2000) + "... (truncated)"
                    : responseBody;

                _logger.LogError(
                    "AI API call failed. Provider={Provider}, Model={Model}, Url={Url}, StatusCode={StatusCode}, Reason={Reason}, Body={Body}",
                    provider,
                    modelToUse,
                    apiUrl,
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    truncatedResponseBody);

                throw new InvalidOperationException(
                    $"AI API call failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {truncatedResponseBody}");
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
            _logger.LogError(ex, "Error calling AI API");

            // Preserve detailed InvalidOperationException messages we threw above
            if (ex is InvalidOperationException)
            {
                throw;
            }

            throw new InvalidOperationException($"AI API call failed: {ex.Message}", ex);
        }
    }

    private bool CheckIfMatches(string aiResponse, string promptCategory)
    {
        // First, try to parse structured JSON response
        try
        {
            var jsonMatch = Regex.Match(aiResponse, @"```json\s*({[^`]+})\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!jsonMatch.Success)
            {
                // Try without markdown code blocks
                jsonMatch = Regex.Match(aiResponse, @"({\s*""match""[^}]+})", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            }
            
            if (jsonMatch.Success)
            {
                var jsonStr = jsonMatch.Groups[1].Value;
                var jsonDoc = JsonDocument.Parse(jsonStr);
                var root = jsonDoc.RootElement;
                
                if (root.TryGetProperty("match", out var matchEl))
                {
                    var matchValue = matchEl.GetString()?.ToUpperInvariant().Trim();
                    
                    // For exclusion prompts: YES = match (exclude), NO = no match (continue)
                    if (promptCategory == PromptCategory.Exclusion.ToString())
                    {
                        return matchValue == "YES";
                    }
                    
                    // For workload detection: MATCH = match, NO_MATCH/NO = no match
                    return matchValue == "MATCH" || matchValue == "YES";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse structured match field, falling back to text parsing");
        }
        
        // Fallback: Simple match detection - look for keywords in raw text
        var response = aiResponse.ToUpperInvariant().Trim();
        
        // For workload detection prompts
        if (response.Contains("MATCH") && !response.Contains("NO_MATCH") && !response.Contains("NO MATCH"))
            return true;
        
        // For exclusion prompts - must start with YES
        if (promptCategory == PromptCategory.Exclusion.ToString())
        {
            // Check if response starts with YES (ignoring whitespace)
            if (response.StartsWith("YES"))
                return true;
            
            // If it starts with NO, definitely not a match
            if (response.StartsWith("NO"))
                return false;
        }
            
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

    private (string? classification, int confidence, string reasoning) ParseStructuredResponse(string aiResponse)
    {
        try
        {
            // Extract JSON block from response (between ```json and ```)
            var jsonMatch = Regex.Match(aiResponse, @"```json\s*({[^`]+})\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!jsonMatch.Success)
            {
                // Try without markdown code blocks
                jsonMatch = Regex.Match(aiResponse, @"({\s*""match""[^}]+})", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            }
            
            if (jsonMatch.Success)
            {
                var jsonStr = jsonMatch.Groups[1].Value;
                var jsonDoc = JsonDocument.Parse(jsonStr);
                var root = jsonDoc.RootElement;
                
                var classification = root.TryGetProperty("classification", out var classEl) && classEl.ValueKind != JsonValueKind.Null
                    ? classEl.GetString()
                    : null;
                    
                var confidence = root.TryGetProperty("confidence", out var confEl) && confEl.TryGetInt32(out var conf)
                    ? conf
                    : 50; // Default moderate confidence
                    
                var reasoning = root.TryGetProperty("reasoning", out var reasonEl)
                    ? reasonEl.GetString() ?? "No reasoning provided"
                    : "No reasoning provided";
                    
                return (classification, confidence, reasoning);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse structured JSON response from AI");
        }
        
        // Fallback: try to extract confidence from text
        var match = Regex.Match(aiResponse, @"confidence[:\s]+(\d+)", RegexOptions.IgnoreCase);
        var fallbackConfidence = match.Success && int.TryParse(match.Groups[1].Value, out var c) ? c : 50;
        
        return (null, fallbackConfidence, "Failed to parse structured response");
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

    private static bool ModelRequiresMaxCompletionTokens(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            return false;

        modelName = modelName.Trim().ToLowerInvariant();

        // All GPT-5 family models and O-series reasoning models use max_completion_tokens
        return modelName.StartsWith("gpt-5")
               || modelName.StartsWith("o3")
               || modelName.StartsWith("o4");
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

    private string FormatMetricsSummary(string? metricsJson)
    {
        if (string.IsNullOrWhiteSpace(metricsJson))
            return "No historical metrics available.";

        try
        {
            using var doc = JsonDocument.Parse(metricsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return metricsJson; // fallback to raw JSON

            var parts = new List<string>();
            foreach (var metricProp in root.EnumerateObject())
            {
                var name = metricProp.Name;
                var obj = metricProp.Value;
                double avg = obj.TryGetProperty("average", out var avgEl) && avgEl.TryGetDouble(out var a) ? a : 0;
                double max = obj.TryGetProperty("max", out var maxEl) && maxEl.TryGetDouble(out var m) ? m : 0;
                double total = obj.TryGetProperty("total", out var totEl) && totEl.TryGetDouble(out var t) ? t : 0;
                int count = obj.TryGetProperty("dataPointCount", out var cEl) && cEl.TryGetInt32(out var c) ? c : 0;

                parts.Add($"{name}: avg={avg:0.##}, max={max:0.##}, total={total:0.##}, points={count}");
            }

            return parts.Count == 0
                ? "No historical metrics available."
                : "Historical metrics summary: " + string.Join("; ", parts);
        }
        catch
        {
            // If parsing fails, return the raw JSON
            return metricsJson;
        }
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
    
    private async Task UpdateJobProgressAsync(int processedCount, int failedCount)
    {
        if (_analysisJobsTable == null || string.IsNullOrEmpty(_currentAnalysisJobId))
            return;
            
        try
        {
            var job = await _analysisJobsTable.GetEntityAsync<AnalysisJob>("AnalysisJob", _currentAnalysisJobId);
            var analysisJob = job.Value;
            
            analysisJob.ProcessedVolumes = processedCount;
            analysisJob.FailedVolumes = failedCount;
            
            await _analysisJobsTable.UpdateEntityAsync(analysisJob, Azure.ETag.All, Azure.Data.Tables.TableUpdateMode.Replace);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update job progress for {JobId}", _currentAnalysisJobId);
        }
    }
}
