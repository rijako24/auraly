using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Auraly.Platform.Worker.Functions;

public sealed class HealthFunction
{
    [Function("Health")]
    public static async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")]
        HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        var payload = new
        {
            status = "healthy",
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
