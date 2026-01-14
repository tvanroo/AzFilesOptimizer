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
                PromptTemplate = @"Determine if this Azure Files share is used for Azure CloudShell and should be excluded from migration analysis.

Share Details:
- Name: {VolumeName}
- Resource Group: {ResourceGroup}
- Location: {Location}
- Size: {SizeGB} GB
- Usage: {UsedCapacity}
- Access Tier: {AccessTier}
- Storage Account: {StorageAccount} ({StorageAccountKind})
- Storage Account SKU: {StorageAccountSku}
- Protocols: {Protocols}
- Tags: {Tags}
- Metadata: {Metadata}

Typical Azure CloudShell Characteristics:
- Naming: Usually contains 'cloudshell', 'cs-', 'cloud-shell-storage'
- Size: Typically 5-6 GB quota (CloudShell default)
- Usage: Usually low (<500 MB)
- Resource Group: Often named like 'cloud-shell-storage-*' or contains region codes
- Tags: May have 'ms-resource-usage' = 'azure-cloud-shell'
- Storage Account Kind: Usually StorageV2 (not FileStorage)
- Tier: Usually Transaction Optimized or Hot (NOT Premium)

You MUST respond with ONLY the JSON structure below. Do not include any other text before or after the JSON.

```json
{
  ""match"": ""YES"" or ""NO"",
  ""classification"": ""cloudshell-profile"" or null,
  ""confidence"": 0-100,
  ""reasoning"": ""brief explanation""
}
```

Rules:
- match: ""YES"" means this IS CloudShell storage (exclude it). ""NO"" means this is NOT CloudShell (continue processing).
- classification: Must be ""cloudshell-profile"" if match=YES, otherwise null
- confidence: Integer 0-100 indicating certainty of your determination
- reasoning: One sentence explaining why this is or is not CloudShell storage based on the indicators above

IMPORTANT: Respond ONLY with the JSON block. No additional text.",
                Enabled = true,
                StopConditionsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    StopOnMatch = true,
                    ActionOnMatch = "SetWorkload",
                    TargetWorkloadId = "cloudshell-profile"
                })
            },
            new AnalysisPrompt
            {
                Name = "FSLogix Profile Detection",
                Priority = 20,
                Category = PromptCategory.WorkloadDetection.ToString(),
                PromptTemplate = @"Analyze if this Azure Files share is used for FSLogix profiles or VDI user profiles.

Share Configuration:
- Name: {VolumeName}
- Storage Account: {StorageAccount} ({StorageAccountKind}, {StorageAccountSku})
- Resource Group: {ResourceGroup}
- Location: {Location}
- Size: {SizeGB} GB (Usage: {UsedCapacity})
- Access Tier: {AccessTier}
- Protocols: {Protocols}
- Provisioned Performance: {ProvisionedIOPS} IOPS, {ProvisionedBandwidth} MiB/s
- Estimated Performance: {EstimatedIOPS} IOPS, {EstimatedThroughputMiBps} MiB/s
- Tags: {Tags}
- Metadata: {Metadata}

Snapshot/Backup Info:
- Snapshot Count: {SnapshotCount}
- Total Snapshot Size: {TotalSnapshotSizeBytes} bytes
- Data Churn Rate: {ChurnRateBytesPerDay} bytes/day
- Backup Policy: {BackupPolicyConfigured}

Monitoring:
- Monitoring Enabled: {MonitoringEnabled}
- Historical Data: {MonitoringDataAvailableDays} days
- Metrics Summary: {MetricsSummary}

FSLogix Profile Indicators:
- Naming: Contains 'fslogix', 'profile', 'profiles', 'vdi', 'wvd', 'avd', 'citrix', 'vda'
- Size: 100GB-2TB typical (depends on user count)
- Performance: Premium tier common; high IOPS for user logins
- Usage Pattern: Many small files, high churn rate during business hours
- Protocols: SMB (NFS not typical for FSLogix)
- Tags: May include 'workload=vdi', 'fslogix=true', 'avd-*'
- Snapshots: Often configured for profile backup/recovery

Provide:
1. MATCH or NO_MATCH
2. Confidence score (0-100)
3. Key evidence from the data above
4. If MATCH, specify if this is FSLogix Profile Container, Office Container, or general VDI profiles",
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
                PromptTemplate = @"Analyze if this share hosts database files (SQL Server, Oracle, SAP HANA, PostgreSQL, MySQL, etc.).

Share Configuration:
- Name: {VolumeName}
- Storage Account: {StorageAccount} ({StorageAccountKind}, {StorageAccountSku})
- Resource Group: {ResourceGroup}
- Location: {Location}
- Size: {SizeGB} GB (Usage: {UsedCapacity})
- Access Tier: {AccessTier}
- Protocols: {Protocols}
- Provisioned Performance: {ProvisionedIOPS} IOPS, {ProvisionedBandwidth} MiB/s
- Estimated Performance: {EstimatedIOPS} IOPS, {EstimatedThroughputMiBps} MiB/s
- Tags: {Tags}
- Metadata: {Metadata}

Snapshot/Backup Info:
- Snapshot Count: {SnapshotCount}
- Data Churn Rate: {ChurnRateBytesPerDay} bytes/day
- Backup Policy: {BackupPolicyConfigured}

Monitoring:
- Monitoring Enabled: {MonitoringEnabled}
- Historical Data: {MonitoringDataAvailableDays} days
- Metrics Summary: {MetricsSummary}

Database Workload Indicators:
- Naming: Contains 'db', 'database', 'sql', 'oracle', 'hana', 'postgres', 'mysql', 'data', 'mdf', 'ldf'
- Size: Typically 500GB+ (can be smaller for dev/test)
- Performance: Premium or Ultra tier; high IOPS (10K+); high throughput
- Protocol: SMB for SQL Server, NFS for Oracle/SAP HANA
- Usage: High and consistent utilization
- Churn: Variable - high for transaction logs, moderate for data files
- Snapshots: Critical - often configured for point-in-time recovery
- Tags: May include 'workload=database', 'app=sql', 'tier=production'
- Lease State: May be leased if actively mounted by database server

Provide:
1. MATCH or NO_MATCH
2. Confidence score (0-100)
3. Evidence from above indicators
4. If MATCH, identify likely database type (SQL Server, Oracle, SAP HANA, PostgreSQL, MySQL, other)
5. If MATCH, assess if this is production, dev/test, or backup/archive based on indicators",
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
                PromptTemplate = @"Determine if this share is used for Kubernetes persistent volumes or container orchestration storage.

Share Configuration:
- Name: {VolumeName}
- Storage Account: {StorageAccount} ({StorageAccountKind}, {StorageAccountSku})
- Resource Group: {ResourceGroup}
- Location: {Location}
- Size: {SizeGB} GB (Usage: {UsedCapacity})
- Access Tier: {AccessTier}
- Protocols: {Protocols}
- Provisioned Performance: {ProvisionedIOPS} IOPS, {ProvisionedBandwidth} MiB/s
- Estimated Performance: {EstimatedIOPS} IOPS, {EstimatedThroughputMiBps} MiB/s
- Tags: {Tags}
- Metadata: {Metadata}

Snapshot/Backup Info:
- Snapshot Count: {SnapshotCount}
- Data Churn Rate: {ChurnRateBytesPerDay} bytes/day

Monitoring:
- Monitoring Enabled: {MonitoringEnabled}
- Metrics Summary: {MetricsSummary}

Kubernetes/Container Storage Indicators:
- Naming: Contains 'k8s', 'aks', 'kubernetes', 'pv', 'pvc', 'container', 'docker', 'pods'
- Size: Often smaller volumes (10-500GB) unless for databases/stateful sets
- Performance: Variable - depends on workload (Standard to Premium)
- Protocol: NFS common for multi-pod access; SMB for Windows containers
- Resource Group: Often named with 'aks', 'kubernetes', 'k8s', or 'MC_' prefix (managed cluster RG)
- Tags: May include 'aks-managed=true', 'kubernetes.io/*', 'orchestrator=kubernetes'
- Pattern: Multiple volumes with similar naming in same RG suggests PV provisioning
- Usage: Varies by application - can have high churn for logging/temp storage

Provide:
1. MATCH or NO_MATCH
2. Confidence score (0-100)
3. Key evidence supporting the classification
4. If MATCH, specify likely use case (stateful application storage, shared config/data, logging, or general PV)",
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
                PromptTemplate = @"Assess if this share is used for High Performance Computing (HPC), batch processing, or scientific computing workloads.

Share Configuration:
- Name: {VolumeName}
- Storage Account: {StorageAccount} ({StorageAccountKind}, {StorageAccountSku})
- Resource Group: {ResourceGroup}
- Location: {Location}
- Size: {SizeGB} GB (Usage: {UsedCapacity})
- Access Tier: {AccessTier}
- Protocols: {Protocols}
- Provisioned Performance: {ProvisionedIOPS} IOPS, {ProvisionedBandwidth} MiB/s
- Estimated Performance: {EstimatedIOPS} IOPS, {EstimatedThroughputMiBps} MiB/s
- Tags: {Tags}
- Metadata: {Metadata}

Snapshot/Backup Info:
- Snapshot Count: {SnapshotCount}
- Data Churn Rate: {ChurnRateBytesPerDay} bytes/day

Monitoring:
- Monitoring Enabled: {MonitoringEnabled}
- Metrics Summary: {MetricsSummary}

HPC/Scientific Computing Indicators:
- Naming: Contains 'hpc', 'batch', 'compute', 'research', 'simulation', 'render', 'scratch', 'genomics', 'ai', 'ml', 'training'
- Size: Often very large (multi-TB) for datasets, simulations, or scratch space
- Performance: Premium or Ultra tier; very high throughput (hundreds of MiB/s)
- Protocol: NFS common for Linux-based HPC clusters; SMB for Windows HPC
- Location: May be in regions with HPC-specific capabilities or near compute clusters
- Usage: Can be high with large datasets, or sporadic for burst compute jobs
- Churn: High during active jobs; low between runs
- Tags: May include 'workload=hpc', 'batch=true', 'project=research'
- Pattern: Often paired with Azure Batch, CycleCloud, or HPC Pack resources in same RG

Provide:
1. MATCH or NO_MATCH
2. Confidence score (0-100)
3. Evidence supporting classification
4. If MATCH, identify likely use case (scratch storage, dataset repository, simulation output, ML training data, rendering, genomics, other)",
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
                PromptTemplate = @"This share hasn't matched specific specialized workload patterns. Classify it as a general-purpose file share and provide insights.

Share Configuration:
- Name: {VolumeName}
- Storage Account: {StorageAccount} ({StorageAccountKind}, {StorageAccountSku})
- Resource Group: {ResourceGroup}
- Location: {Location}
- Size: {SizeGB} GB (Usage: {UsedCapacity})
- Access Tier: {AccessTier}
- Protocols: {Protocols}
- Provisioned Performance: {ProvisionedIOPS} IOPS, {ProvisionedBandwidth} MiB/s
- Estimated Performance: {EstimatedIOPS} IOPS, {EstimatedThroughputMiBps} MiB/s
- Lease Status: {LeaseStatus} / {LeaseState}
- Tags: {Tags}
- Metadata: {Metadata}

Snapshot/Backup Info:
- Snapshot Count: {SnapshotCount}
- Data Churn Rate: {ChurnRateBytesPerDay} bytes/day
- Backup Policy: {BackupPolicyConfigured}

Monitoring:
- Monitoring Enabled: {MonitoringEnabled}
- Historical Data: {MonitoringDataAvailableDays} days
- Metrics Summary: {MetricsSummary}

Timestamps:
- Created: {CreationTime}
- Last Modified: {LastModifiedTime}

Common General File Share Types:
- Departmental/Team Shares: Shared folders for collaboration (HR, Finance, Engineering, etc.)
- Home Directories: User home folders
- Application Data: Non-specialized application file storage
- Backup/Archive: Long-term file storage, backups
- Media Files: Videos, images, documents
- Software Distribution: Installation files, updates, patches

Based on all available data, provide:
1. Most likely general file share category from list above
2. Confidence score (0-100)
3. Key characteristics observed (naming, size, usage patterns, tags, metrics)
4. Preliminary ANF migration suitability (Yes/No/Maybe) with brief rationale
5. Any special considerations (e.g., active lease, high churn, snapshots, protocols)",
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
                PromptTemplate = @"Provide comprehensive Azure NetApp Files (ANF) migration assessment for this share.

Complete Share Profile:
- Name: {VolumeName}
- Storage Account: {StorageAccount} ({StorageAccountKind}, {StorageAccountSku})
- Resource Group: {ResourceGroup}
- Location: {Location}
- Size: {SizeGB} GB (Current Usage: {UsedCapacity})
- Access Tier: {AccessTier}
- Protocols: {Protocols}
- Root Squash: {RootSquash}
- TLS Version: {MinimumTlsVersion}
- HTTPS Only: {EnableHttpsTrafficOnly}

Performance Profile:
- Provisioned: {ProvisionedIOPS} IOPS, {ProvisionedBandwidth} MiB/s
- Estimated: {EstimatedIOPS} IOPS, {EstimatedThroughputMiBps} MiB/s
- Storage Account SKU: {StorageAccountSku}

Data Management:
- Snapshot Count: {SnapshotCount}
- Total Snapshot Size: {TotalSnapshotSizeBytes} bytes
- Data Churn Rate: {ChurnRateBytesPerDay} bytes/day
- Backup Policy: {BackupPolicyConfigured}
- Lease Status: {LeaseStatus} / {LeaseState}

Monitoring & Metrics:
- Monitoring Enabled: {MonitoringEnabled}
- Historical Data Available: {MonitoringDataAvailableDays} days
- Metrics Summary: {MetricsSummary}

Resource Context:
- Tags: {Tags}
- Metadata: {Metadata}
- Created: {CreationTime}
- Last Modified: {LastModifiedTime}
- Soft Deleted: {IsDeleted}

Provide comprehensive migration assessment:

1. **ANF Migration Suitability** (High/Medium/Low/Not Recommended)
   - Overall readiness score (0-100)
   - Primary justification

2. **Recommended ANF Service Level** (Standard/Premium/Ultra/Not Applicable)
   - Rationale based on performance requirements and workload

3. **Performance Benefits**
   - Expected performance improvements
   - Latency considerations
   - Throughput advantages

4. **Cost Analysis**
   - Estimated cost impact (Higher/Similar/Lower)
   - Cost optimization opportunities
   - Capacity efficiency considerations

5. **Feature Advantages**
   - Snapshot benefits (frequency, retention, performance)
   - Cross-region replication opportunities
   - Backup and DR improvements
   - Protocol support benefits

6. **Migration Considerations**
   - Any blockers or challenges
   - Downtime requirements
   - Application compatibility
   - Protocol considerations (SMB/NFS)

7. **Specific Recommendations**
   - Pre-migration steps
   - Optimal ANF volume size
   - QoS type (Auto/Manual)
   - Cool access tier opportunities (if applicable)
   - Snapshot policy recommendations

Provide detailed, actionable guidance based on ALL available data above.",
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
