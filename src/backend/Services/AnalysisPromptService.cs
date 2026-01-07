using Azure;
using Azure.Data.Tables;
using AzFilesOptimizer.Backend.Models;
using Microsoft.Extensions.Logging;

namespace AzFilesOptimizer.Backend.Services;

public class AnalysisPromptService
{
    private readonly TableClient _tableClient;
    private readonly ILogger _logger;

    public AnalysisPromptService(string connectionString, ILogger logger)
    {
        _logger = logger;
        var serviceClient = new TableServiceClient(connectionString);
        _tableClient = serviceClient.GetTableClient("AnalysisPrompts");
        _tableClient.CreateIfNotExists();
    }

    public async Task<List<AnalysisPrompt>> GetAllPromptsAsync()
    {
        var prompts = new List<AnalysisPrompt>();
        
        await foreach (var entity in _tableClient.QueryAsync<TableEntity>(filter: $"PartitionKey eq 'AnalysisPrompt'"))
        {
            prompts.Add(ConvertFromTableEntity(entity));
        }
        
        return prompts.OrderBy(p => p.Priority).ToList();
    }

    public async Task<List<AnalysisPrompt>> GetEnabledPromptsAsync()
    {
        var prompts = new List<AnalysisPrompt>();
        
        await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
            filter: $"PartitionKey eq 'AnalysisPrompt' and Enabled eq true"))
        {
            prompts.Add(ConvertFromTableEntity(entity));
        }
        
        return prompts.OrderBy(p => p.Priority).ToList();
    }

    public async Task<AnalysisPrompt?> GetPromptAsync(string promptId)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>("AnalysisPrompt", promptId);
            return ConvertFromTableEntity(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<AnalysisPrompt> CreatePromptAsync(AnalysisPrompt prompt)
    {
        prompt.RowKey = Guid.NewGuid().ToString();
        prompt.CreatedAt = DateTime.UtcNow;
        
        // Auto-assign priority if not set
        if (prompt.Priority == 0)
        {
            var allPrompts = await GetAllPromptsAsync();
            prompt.Priority = allPrompts.Any() ? allPrompts.Max(p => p.Priority) + 10 : 10;
        }
        
        var entity = ConvertToTableEntity(prompt);
        await _tableClient.AddEntityAsync(entity);
        
        _logger.LogInformation("Created analysis prompt: {PromptId} - {Name} (Priority: {Priority})", 
            prompt.PromptId, prompt.Name, prompt.Priority);
        return prompt;
    }

    public async Task<AnalysisPrompt> UpdatePromptAsync(AnalysisPrompt prompt)
    {
        var existing = await GetPromptAsync(prompt.PromptId);
        if (existing == null)
            throw new InvalidOperationException($"Prompt {prompt.PromptId} not found");
        
        prompt.UpdatedAt = DateTime.UtcNow;
        prompt.CreatedAt = existing.CreatedAt; // Preserve creation date
        
        var entity = ConvertToTableEntity(prompt);
        await _tableClient.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Replace);
        
        _logger.LogInformation("Updated analysis prompt: {PromptId} - {Name}", prompt.PromptId, prompt.Name);
        return prompt;
    }

    public async Task DeletePromptAsync(string promptId)
    {
        var existing = await GetPromptAsync(promptId);
        if (existing == null)
            return;
        
        await _tableClient.DeleteEntityAsync("AnalysisPrompt", promptId);
        _logger.LogInformation("Deleted analysis prompt: {PromptId}", promptId);
    }

    public async Task ReorderPromptsAsync(Dictionary<string, int> priorities)
    {
        foreach (var (promptId, newPriority) in priorities)
        {
            var prompt = await GetPromptAsync(promptId);
            if (prompt != null)
            {
                prompt.Priority = newPriority;
                prompt.UpdatedAt = DateTime.UtcNow;
                
                var entity = ConvertToTableEntity(prompt);
                await _tableClient.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Replace);
            }
        }
        
        _logger.LogInformation("Reordered {Count} prompts", priorities.Count);
    }

    public async Task SeedDefaultPromptsAsync()
    {
        var existingPrompts = await GetAllPromptsAsync();
        if (existingPrompts.Any())
        {
            _logger.LogInformation("Analysis prompts already exist. Skipping seed.");
            return;
        }

        var prompts = new List<AnalysisPrompt>
        {
            new AnalysisPrompt
            {
                Name = "CloudShell Exclusion",
                Priority = 10,
                Category = PromptCategory.Exclusion.ToString(),
                PromptTemplate = "Is this Azure Files share named '{VolumeName}' in resource group '{ResourceGroup}' used for Azure CloudShell? CloudShell shares typically have 'cloudshell' or 'cs-' in the name and are small (usually 5-6 GB). Answer with YES or NO and provide reasoning.",
                Enabled = true,
                StopConditionsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    StopOnMatch = true,
                    ActionOnMatch = "ExcludeVolume",
                    TargetWorkloadId = (string?)null
                })
            },
            new AnalysisPrompt
            {
                Name = "FSLogix Profile Detection",
                Priority = 20,
                Category = PromptCategory.WorkloadDetection.ToString(),
                PromptTemplate = "Analyze this Azure Files share: Name='{VolumeName}', Size={SizeGB}GB, Tags={Tags}. Does this appear to be an FSLogix profile container or VDI user profile share? Look for indicators like: share names containing 'fslogix', 'profile', 'vdi', 'wvd', 'avd'; appropriate size (100GB-2TB typical); or relevant tags. Provide a confidence score (0-1) and evidence.",
                Enabled = true,
                StopConditionsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    StopOnMatch = false,
                    ActionOnMatch = "None",
                    TargetWorkloadId = (string?)null
                })
            },
            new AnalysisPrompt
            {
                Name = "Database Workload Detection",
                Priority = 30,
                Category = PromptCategory.WorkloadDetection.ToString(),
                PromptTemplate = "Examine this storage: Name='{VolumeName}', Size={SizeGB}GB, Performance Tier={PerformanceTier}, IOPS={ProvisionedIOPS}, Tags={Tags}. Could this be hosting database files (SQL Server, Oracle, SAP HANA)? Indicators: names with 'db', 'database', 'sql', 'oracle', 'hana'; large size (500GB+); high IOPS provisioning; premium tiers. Rate confidence (0-1) and identify which database type if applicable.",
                Enabled = true,
                StopConditionsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    StopOnMatch = false,
                    ActionOnMatch = "None",
                    TargetWorkloadId = (string?)null
                })
            },
            new AnalysisPrompt
            {
                Name = "Kubernetes/Container Storage Detection",
                Priority = 40,
                Category = PromptCategory.WorkloadDetection.ToString(),
                PromptTemplate = "Review this share: Name='{VolumeName}', Size={SizeGB}GB, Location={Location}, Tags={Tags}. Is this likely used for Kubernetes persistent volumes or container storage? Look for: names with 'k8s', 'aks', 'kubernetes', 'pv', 'pvc'; tags indicating AKS or container use; multiple smaller volumes in the same resource group. Confidence (0-1) and reasoning.",
                Enabled = true,
                StopConditionsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    StopOnMatch = false,
                    ActionOnMatch = "None",
                    TargetWorkloadId = (string?)null
                })
            },
            new AnalysisPrompt
            {
                Name = "HPC/Scientific Workload Detection",
                Priority = 50,
                Category = PromptCategory.WorkloadDetection.ToString(),
                PromptTemplate = "Analyze this volume: Name='{VolumeName}', Size={SizeGB}GB, Performance={PerformanceTier}, Location={Location}, Tags={Tags}. Does this appear to be High Performance Computing (HPC) or scientific computing storage? Indicators: names with 'hpc', 'batch', 'compute', 'research'; very large capacity (multi-TB); high-performance tiers; specific Azure regions known for HPC. Confidence score and evidence.",
                Enabled = true,
                StopConditionsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    StopOnMatch = false,
                    ActionOnMatch = "None",
                    TargetWorkloadId = (string?)null
                })
            },
            new AnalysisPrompt
            {
                Name = "General File Share Classification",
                Priority = 60,
                Category = PromptCategory.WorkloadDetection.ToString(),
                PromptTemplate = "This share hasn't matched specific workload patterns: Name='{VolumeName}', Size={SizeGB}GB, Tier={PerformanceTier}. Classify as general-purpose file share and assess ANF migration suitability based on: size, performance requirements, access patterns. Is this a good candidate for Azure NetApp Files migration? Confidence (0-1) and recommendation.",
                Enabled = true,
                StopConditionsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    StopOnMatch = false,
                    ActionOnMatch = "None",
                    TargetWorkloadId = (string?)null
                })
            },
            new AnalysisPrompt
            {
                Name = "ANF Migration Assessment",
                Priority = 70,
                Category = PromptCategory.MigrationAssessment.ToString(),
                PromptTemplate = "Final migration assessment for: Name='{VolumeName}', Size={SizeGB}GB, Tier={PerformanceTier}, IOPS={ProvisionedIOPS}, Workload={Tags}. Evaluate: 1) Performance benefits of ANF, 2) Cost implications, 3) Feature advantages (snapshots, replication), 4) Any migration blockers, 5) Recommended ANF service level (Standard/Premium/Ultra). Provide detailed migration readiness score (0-1) and specific recommendations.",
                Enabled = true,
                StopConditionsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    StopOnMatch = false,
                    ActionOnMatch = "None",
                    TargetWorkloadId = (string?)null
                })
            }
        };

        foreach (var prompt in prompts)
        {
            await CreatePromptAsync(prompt);
        }

        _logger.LogInformation("Seeded {Count} default analysis prompts", prompts.Count);
    }

    private TableEntity ConvertToTableEntity(AnalysisPrompt prompt)
    {
        return new TableEntity(prompt.PartitionKey, prompt.RowKey)
        {
            ["Name"] = prompt.Name,
            ["Priority"] = prompt.Priority,
            ["Category"] = prompt.Category,
            ["PromptTemplate"] = prompt.PromptTemplate,
            ["Enabled"] = prompt.Enabled,
            ["CreatedAt"] = prompt.CreatedAt,
            ["UpdatedAt"] = prompt.UpdatedAt,
            ["StopConditionsJson"] = prompt.StopConditionsJson
        };
    }

    private AnalysisPrompt ConvertFromTableEntity(TableEntity entity)
    {
        return new AnalysisPrompt
        {
            PartitionKey = entity.PartitionKey,
            RowKey = entity.RowKey,
            Timestamp = entity.Timestamp,
            ETag = entity.ETag,
            Name = entity.GetString("Name") ?? "",
            Priority = entity.GetInt32("Priority") ?? 0,
            Category = entity.GetString("Category") ?? PromptCategory.WorkloadDetection.ToString(),
            PromptTemplate = entity.GetString("PromptTemplate") ?? "",
            Enabled = entity.GetBoolean("Enabled") ?? true,
            CreatedAt = entity.GetDateTimeOffset("CreatedAt")?.UtcDateTime ?? DateTime.UtcNow,
            UpdatedAt = entity.GetDateTimeOffset("UpdatedAt")?.UtcDateTime,
            StopConditionsJson = entity.GetString("StopConditionsJson")
        };
    }
}
