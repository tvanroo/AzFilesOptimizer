using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using AzFilesOptimizer.Backend.Models;
using AzFilesOptimizer.Backend.Services;

namespace AzFilesOptimizer.Backend.Functions;

public class AnalysisProcessorFunction
{
    private readonly ILogger _logger;
    private readonly string _connectionString;
    private readonly AnalysisJobRunner _jobRunner;

    public AnalysisProcessorFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<AnalysisProcessorFunction>();
        _connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            _logger.LogError("AzureWebJobsStorage connection string is missing or empty. AnalysisProcessor will fail to access storage.");
        }

        _jobRunner = new AnalysisJobRunner(_connectionString, _logger);
    }

    [Function("AnalysisProcessor")]
    public async Task Run(
        [QueueTrigger("analysis-queue", Connection = "AzureWebJobsStorage")] string message,
        FunctionContext context)
    {
        _logger.LogInformation("AnalysisProcessor triggered. Message: {Message}", message);

        try
        {
            var jobMessage = JsonSerializer.Deserialize<AnalysisQueueMessage>(message);
            if (jobMessage == null || string.IsNullOrEmpty(jobMessage.AnalysisJobId))
            {
                _logger.LogError("Invalid analysis message format");
                return;
            }

            await ProcessAnalysisJobAsync(jobMessage.AnalysisJobId, jobMessage.DiscoveryJobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing analysis message");
        }
    }

    private async Task ProcessAnalysisJobAsync(string analysisJobId, string discoveryJobId)
    {
        _logger.LogInformation("Processing analysis job: {AnalysisJobId}", analysisJobId);

        await _jobRunner.RunAsync(analysisJobId, discoveryJobId);
    }


    private class AnalysisQueueMessage
    {
        public string AnalysisJobId { get; set; } = string.Empty;
        public string DiscoveryJobId { get; set; } = string.Empty;
    }
}
