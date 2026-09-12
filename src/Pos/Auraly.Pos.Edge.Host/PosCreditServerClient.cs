using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Contracts.Sales;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed class PosCreditServerClient(
    HttpClient http,
    PosDeviceCredentials credentials,
    PosOperationalScope scope)
{
    public async Task<PosCreditValidationResult> ValidateAsync(
        Guid customerId,
        decimal amount,
        int? fiscalEnvironment,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/pos/v1/sales/credit-validation")
        {
            Content = JsonContent.Create(new PosCreditValidationRequest(
                scope.BusinessId, customerId, amount, fiscalEnvironment))
        };
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            throw new InvalidOperationException(
                "La venta a crédito requiere conexión para validar el cupo actual del cliente.",
                error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "La venta a crédito requiere conexión para validar el cupo actual del cliente.",
                error);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    await ReadDetailAsync(response, cancellationToken));
            return await response.Content.ReadFromJsonAsync<PosCreditValidationResult>(
                cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException(
                    "Auraly Server no devolvió la validación de cartera.");
        }
    }

    private static async Task<string> ReadDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("detail", out var detail) &&
                    detail.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(detail.GetString()))
                    return detail.GetString()!;
            }
            catch (JsonException)
            {
                // The upstream status still owns the localized fallback below.
            }
        }
        return "La venta a crédito requiere conexión para validar el cupo actual del cliente.";
    }
}
