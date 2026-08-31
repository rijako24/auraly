using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Auraly.BuildingBlocks.Domain.Documents;

namespace Auraly.Contracts.Sales;

public static class ServiceInvoiceDocumentTypes
{
    public const string ServiceInvoice = AuralyDocumentTypes.ServiceInvoice;
}

public sealed record ServiceInvoiceLineContract(
    int LineNumber,
    Guid BillableServiceId,
    string ServiceCode,
    string Description,
    string UnitCode,
    string TaxCode,
    string TaxName,
    decimal TaxRate,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal LineTotal);

/// <summary>
/// Immutable online-only source for a service invoice. It intentionally contains no
/// warehouse, device, work-session or product identity.
/// </summary>
public sealed record ServiceInvoiceSnapshot(
    Guid TenantId,
    Guid BusinessId,
    Guid CustomerId,
    Guid DocumentId,
    Guid? PaymentTransactionId,
    Guid? RenewalOrderId,
    Guid? SoldByUserId,
    PosSaleDocumentNumberContract DocumentNumber,
    PosSaleCommercialSnapshotContract CommercialSnapshot,
    PosSaleFiscalSnapshotContract FiscalSnapshot,
    PosSaleUblSnapshotContract UblSnapshot,
    IReadOnlyList<ServiceInvoiceLineContract> Lines,
    PosSalePaymentContract Payment);

public static class ServiceInvoiceSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(ServiceInvoiceSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.Serialize(value, Options);
    }

    public static ServiceInvoiceSnapshot Deserialize(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("A service invoice payload is required.", nameof(payload));
        return JsonSerializer.Deserialize<ServiceInvoiceSnapshot>(payload, Options)
            ?? throw new JsonException("The service invoice payload is empty.");
    }

    public static byte[] Hash(ServiceInvoiceSnapshot value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(value)));

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            NumberHandling = JsonNumberHandling.Strict
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
