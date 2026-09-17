using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Auraly.Pos.Edge.Host;

public sealed class PosSalesReturnServerClient(HttpClient http, PosDeviceCredentials credentials)
{
    public Task<JsonElement> SearchAsync(JsonElement body, PosLocalUserSession user, CancellationToken token) =>
        SendAsync("api/pos/v1/sales-returns/search", body, user, null, token);
    public Task<JsonElement> GetAsync(Guid id, JsonElement body, PosLocalUserSession user, CancellationToken token) =>
        SendAsync($"api/pos/v1/sales-returns/sales/{id:D}", body, user, null, token);
    public Task<JsonElement> BootstrapAsync(JsonElement body, PosLocalUserSession user, CancellationToken token) =>
        SendAsync("api/pos/v1/sales-returns/bootstrap", body, user, null, token);
    public Task<JsonElement> ConfirmAsync(JsonElement body, PosLocalUserSession user, CancellationToken token)
    {
        if (!body.TryGetProperty("returnId", out var value) ||
            !Guid.TryParse(value.GetString(), out var id) || id == Guid.Empty)
            throw new PosSalesReturnServerException(400, "InvalidReturnId",
                "La devolución requiere un identificador válido.");
        return SendAsync("api/pos/v1/sales-returns/confirm", body, user, id.ToString("D"), token);
    }

    private async Task<JsonElement> SendAsync(string path, JsonElement body,
        PosLocalUserSession user, string? idempotencyKey, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        request.Headers.Add("X-Auraly-User-Id", user.UserId.ToString("D"));
        request.Headers.Add("X-Auraly-Work-Session-Id", user.WorkSessionId.ToString("D"));
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(token);
            throw new PosSalesReturnServerException((int)response.StatusCode,
                problem?.Title ?? "SalesReturnUnavailable",
                problem?.Detail ?? "No fue posible procesar la devolución.");
        }
        return await response.Content.ReadFromJsonAsync<JsonElement>(token);
    }
}

public sealed class PosSalesReturnServerException(int statusCode, string code, string message)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
