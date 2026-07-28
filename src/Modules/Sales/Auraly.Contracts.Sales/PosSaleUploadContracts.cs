using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Auraly.Contracts.Sales;

public static class PosSaleDocumentTypes
{
    public const string Invoice = "SalesInvoice";
}

public static class PosSaleRemoteStatuses
{
    public const string FiscalVerified = "FiscalVerified";
    public const string FiscalIntegrityConflict = "FiscalIntegrityConflict";
    public const string AlreadyProcessed = "AlreadyProcessed";
}

public sealed record PosSaleTaxContract(string Code, decimal Amount);

public sealed record PosSaleLineContract(
    int LineNumber,
    Guid ProductId,
    string Description,
    string TaxCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal UntaxedAmount,
    decimal LineTotal);

public sealed record PosSalePaymentContract(
    int PaymentNumber,
    string MethodCode,
    decimal Amount,
    string? Reference);

public sealed record PosSaleFiscalSnapshotContract(
    Guid SeriesId,
    Guid FiscalAuthorizationId,
    string AuthorizationNumber,
    string DocumentType,
    string FiscalNumber,
    string Prefix,
    long Consecutive,
    DateTimeOffset IssuedAt,
    string SupplierTaxId,
    string CustomerIdentification,
    int Environment,
    string TechnicalKeyVersion,
    IReadOnlyList<PosSaleTaxContract> Taxes,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal PayableAmount,
    string Cufe,
    string QrPayload);

public sealed record PosSaleUploadRequest(
    Guid TenantId,
    Guid BusinessId,
    Guid LocationId,
    Guid WarehouseId,
    Guid RegisterId,
    Guid DeviceId,
    Guid DocumentId,
    PosSaleFiscalSnapshotContract FiscalSnapshot,
    IReadOnlyList<PosSaleLineContract> Lines,
    IReadOnlyList<PosSalePaymentContract> Payments);

public sealed record PosSaleUploadResponse(
    Guid ReceiptId,
    Guid DocumentId,
    string Status,
    string CufeReceived,
    string? CufeCalculated,
    bool IsDuplicate,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    string? Detail);

public static class PosSaleContractSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(PosSaleUploadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonSerializer.Serialize(request, Options);
    }

    public static PosSaleUploadRequest Deserialize(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException("A POS sale payload is required.", nameof(payload));
        }

        return JsonSerializer.Deserialize<PosSaleUploadRequest>(payload, Options)
            ?? throw new JsonException("The POS sale payload is empty.");
    }

    public static byte[] Hash(PosSaleUploadRequest request) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(request)));

    public static string HashHex(PosSaleUploadRequest request) =>
        Convert.ToHexString(Hash(request)).ToLowerInvariant();

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

