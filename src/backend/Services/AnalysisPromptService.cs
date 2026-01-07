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
