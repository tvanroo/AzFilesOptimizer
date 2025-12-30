using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AzFilesOptimizer.Backend.Functions;

public class JobProcessorFunction
{
    private readonly ILogger _logger;

    public JobProcessorFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<JobProcessorFunction>();
    }

    [Function("JobProcessor")]
    public void Run(
        [QueueTrigger("azfo-jobs", Connection = "AzureWebJobsStorage")] string message,
        FunctionContext context)
    {
        _logger.LogInformation("JobProcessor triggered. Message: {Message}", message);
        // TODO: implement job processing logic.
    }
}
