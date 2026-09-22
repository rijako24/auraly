using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Auraly.Pos.Edge.Host;

public sealed class PosOrdersServerClient(HttpClient http, PosDeviceCredentials credentials,
    PosEdgeRuntimeContext runtime)
{
    public Task<JsonElement> SendAsync(HttpMethod method, string path, JsonElement? body,
        PosLocalUserSession user, CancellationToken token, string? idempotencyKey = null) =>
        SendCoreAsync(method, path, body, user, token, idempotencyKey);

    private async Task<JsonElement> SendCoreAsync(HttpMethod method, string path, JsonElement? body,
        PosLocalUserSession user, CancellationToken token, string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body.HasValue) request.Content = JsonContent.Create(body.Value);
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        request.Headers.Add("X-Auraly-User-Id", user.UserId.ToString("D"));
        request.Headers.Add("X-Auraly-Business-Id", runtime.BusinessId.Value.ToString("D"));
        request.Headers.Add("X-Auraly-Work-Session-Id", user.WorkSessionId.ToString("D"));
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, token);
        }
        catch (Exception error) when (
            error is HttpRequestException ||
            error is TaskCanceledException && !token.IsCancellationRequested)
        {
            throw new PosOrdersServerException(503, "OrdersUnavailable",
                "No hay conexión con Auraly. Pedidos requiere conexión con el servidor.");
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var problem = await ReadProblemAsync(response, token);
                throw new PosOrdersServerException((int)response.StatusCode,
                    problem?.Title ?? "OrdersUnavailable",
                    problem?.Detail ?? "No fue posible procesar los pedidos.");
            }
            return await response.Content.ReadFromJsonAsync<JsonElement>(token);
        }
    }

    private static async Task<ProblemDetails?> ReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken token)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync(token);
            return JsonSerializer.Deserialize<ProblemDetails>(json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class PosOrdersServerException(int statusCode, string code, string message)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed record EdgePreparedOrderRecovery(
    Auraly.Contracts.Orders.OrderDetail Order,
    bool ClaimAcquiredByThisAttempt);
