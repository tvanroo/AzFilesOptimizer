using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AzFilesOptimizer.Backend.Functions;

public class HealthFunction
{
    private readonly ILogger _logger;

    public HealthFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<HealthFunction>();
    }

    [Function("Health")]
    public HttpResponseData Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        _logger.LogInformation("Health endpoint called.");

        var response = req.CreateResponse(HttpStatusCode.OK);

        var payload = new
        {
            serviceName = "AzFilesOptimizer",
            version = "1.0.0",
            environment = "Development",
            status = "healthy",
            storageConfigured = false,
            timestampUtc = DateTime.UtcNow
        };

        response.WriteAsJsonAsync(payload).GetAwaiter().GetResult();

        return response;
    }
}
