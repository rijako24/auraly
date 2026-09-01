using System.Net.Http.Json;
using Auraly.Contracts.Orders;
using Auraly.Contracts.Sales;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed class PosOrderServerClient(
    HttpClient http,
    PosDeviceCredentials credentials,
    PosEdgeRuntimeContext runtime)
{
    public Task<OrderPage> PageAsync(
        PosLocalUserSession session,
        string query,
        CancellationToken cancellationToken) =>
        SendAsync<OrderPage>(
            HttpMethod.Get,
            $"/api/pos/v1/orders?{ContextQuery(session)}&{query}",
            null,
            null,
            cancellationToken);

    public Task<OrderDetail> GetAsync(
        PosLocalUserSession session,
        Guid orderId,
        CancellationToken cancellationToken) =>
        SendAsync<OrderDetail>(
            HttpMethod.Get,
            $"/api/pos/v1/orders/{orderId:D}?{ContextQuery(session)}",
            null,
            null,
            cancellationToken);

    public Task<OrderClaimSummary> ClaimAsync(
        PosLocalUserSession session,
        Guid orderId,
        CancellationToken cancellationToken) =>
        SendAsync<OrderClaimSummary>(
            HttpMethod.Post,
            $"/api/pos/v1/orders/{orderId:D}/claim",
            JsonContent.Create(ContextBody(session, leaseMinutes: 10)),
            null,
            cancellationToken);

    public async Task ReleaseAsync(
        PosLocalUserSession session,
        Guid orderId,
        CancellationToken cancellationToken) =>
        await SendAsync<object>(
            HttpMethod.Post,
            $"/api/pos/v1/orders/{orderId:D}/claim/release",
            JsonContent.Create(ContextBody(session)),
            null,
            cancellationToken);

    public Task<InvoiceOrdersResponse> InvoiceAsync(
        PosLocalUserSession session,
        IReadOnlyCollection<Guid> orderIds,
        string paymentMethodCode,
        string? paymentReference,
        Guid? bankAccountId,
        string? paymentNotes,
        string documentType,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        SendAsync<InvoiceOrdersResponse>(
            HttpMethod.Post,
            "/api/pos/v1/orders/invoice",
            JsonContent.Create(new
            {
                userId = session.UserId,
                businessId = runtime.BusinessId.Value,
                warehouseId = runtime.WarehouseId.Value,
                workSessionId = session.WorkSessionId,
                orderIds,
                paymentMethodCode,
                documentType,
                paymentReference,
                bankAccountId,
                paymentNotes
            }),
            idempotencyKey,
            cancellationToken);

    public Task<OnlineSalesReceipt> ReceiptAsync(
        PosLocalUserSession session,
        Guid documentId,
        CancellationToken cancellationToken) =>
        SendAsync<OnlineSalesReceipt>(
            HttpMethod.Get,
            $"/api/pos/v1/orders/documents/{documentId:D}/receipt?{ContextQuery(session)}",
            null,
            null,
            cancellationToken);

    private string ContextQuery(PosLocalUserSession session) =>
        $"userId={session.UserId:D}" +
        $"&businessId={runtime.BusinessId.Value:D}" +
        $"&warehouseId={runtime.WarehouseId.Value:D}" +
        $"&workSessionId={session.WorkSessionId:D}";

    private object ContextBody(PosLocalUserSession session, int leaseMinutes = 10) =>
        new
        {
            userId = session.UserId,
            businessId = runtime.BusinessId.Value,
            warehouseId = runtime.WarehouseId.Value,
            workSessionId = session.WorkSessionId,
            leaseMinutes
        };

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        HttpContent? content,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new PosOrderServerException((int)response.StatusCode, detail);
        }
        return await response.Content.ReadFromJsonAsync<T>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidDataException(
                "Auraly Server devolvió una respuesta vacía para pedidos.");
    }
}

public sealed class PosOrderServerException(int statusCode, string message)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
