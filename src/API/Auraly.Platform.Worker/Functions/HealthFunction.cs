using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Auraly.Infrastructure.Persistence;

namespace Auraly.Platform.Worker.Functions;

public sealed class HealthFunction(
    SqlDatabaseConnectivityProbe database,
    ILogger<HealthFunction> logger)
{
    [Function("Health")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var databaseStatus = "reachable";
        var statusCode = HttpStatusCode.OK;
        try
        {
            await database.CheckAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            databaseStatus = "unreachable";
            statusCode = HttpStatusCode.ServiceUnavailable;
            logger.LogError(exception, "The worker cannot reach its canonical database.");
        }
        var response = request.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        var payload = new
        {
            status = statusCode == HttpStatusCode.OK ? "healthy" : "unhealthy",
            database = databaseStatus,
            service = "auraly-function",
            environment = Environment.GetEnvironmentVariable("AURALY_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT")
                ?? "unknown",
            version = Environment.GetEnvironmentVariable("Release__Version") ?? "unknown"
        };
        await response.WriteStringAsync(JsonSerializer.Serialize(payload));
        return response;
    }
}
