using System.Net;
using Auraly.Infrastructure.Persistence;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Auraly.Commerce.Worker;

public sealed class HealthFunction(
    SqlDatabaseConnectivityProbe database,
    IConfiguration configuration,
    ILogger<HealthFunction> logger)
{
    [Function("Health")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        try
        {
            await database.CheckAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The commerce worker cannot reach its canonical database.");
            var unavailable = request.CreateResponse(HttpStatusCode.ServiceUnavailable);
            await unavailable.WriteAsJsonAsync(new { status = "unhealthy" }, cancellationToken);
            return unavailable;
        }

        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            status = "healthy",
            service = "auraly-commerce-worker",
            version = configuration["Release:Version"] ?? "unknown"
        }, cancellationToken);
        return response;
    }
}
