using System.Net.Http.Json;
using Auraly.Contracts.Sales;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Auraly.Pos.Edge.Host;

public sealed class PosSalesHistoryServerClient(
    HttpClient http,
    PosDeviceCredentials credentials)
{
    public Task<OnlineSalesCustomerPage> SearchCustomersAsync(
        SearchOnlineSalesHistoryOptionsRequest request,
        PosLocalUserSession user,
        CancellationToken cancellationToken) =>
        SendAsync<OnlineSalesCustomerPage>(
            HttpMethod.Post,
            "api/pos/v1/history/customers/search",
            request,
            user,
            cancellationToken);

    public Task<OnlineSalesProductPage> SearchProductsAsync(
        SearchOnlineSalesHistoryOptionsRequest request,
        PosLocalUserSession user,
        CancellationToken cancellationToken) =>
        SendAsync<OnlineSalesProductPage>(
            HttpMethod.Post,
            "api/pos/v1/history/products/search",
            request,
            user,
            cancellationToken);

    public Task<OnlineSalesIssuedSalePage> SearchSalesAsync(
        SearchOnlineSalesIssuedSalesRequest request,
        PosLocalUserSession user,
        CancellationToken cancellationToken) =>
        SendAsync<OnlineSalesIssuedSalePage>(
            HttpMethod.Post,
            "api/pos/v1/history/sales/search",
            request,
            user,
            cancellationToken);

    public Task<OnlineSalesReceipt> GetReceiptAsync(
        Guid documentId,
        OnlineSalesHistoryContext request,
        PosLocalUserSession user,
        CancellationToken cancellationToken) =>
        SendAsync<OnlineSalesReceipt>(
            HttpMethod.Post,
            $"api/pos/v1/history/sales/{documentId:D}/receipt",
            request,
            user,
            cancellationToken);

    public async Task RecordReprintAsync(
        Guid documentId,
        OnlineSalesHistoryContext requestBody,
        PosLocalUserSession user,
        CancellationToken cancellationToken)
    {
        _ = await SendAsync<object>(
            HttpMethod.Post,
            $"api/pos/v1/history/sales/{documentId:D}/reprint-audit",
            requestBody,
            user,
            cancellationToken,
            allowEmptyResponse: true);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object body,
        PosLocalUserSession user,
        CancellationToken cancellationToken,
        bool allowEmptyResponse = false)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        request.Headers.Add("X-Auraly-User-Id", user.UserId.ToString("D"));
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
            throw new PosSalesHistoryServerException(
                (int)response.StatusCode,
                problem?.Title ?? "SalesHistoryUnavailable",
                problem?.Detail ?? "No fue posible consultar el historial de ventas.");
        }
        if (allowEmptyResponse)
            return default!;
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new PosSalesHistoryServerException(
                502,
                "InvalidSalesHistoryResponse",
                "El servidor devolvió una respuesta de historial inválida.");
    }
}

public sealed class PosSalesHistoryServerException(
    int statusCode,
    string code,
    string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
