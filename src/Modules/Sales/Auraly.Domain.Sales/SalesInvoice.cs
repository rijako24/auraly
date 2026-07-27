using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Domain.Sales;

public enum SalesInvoiceStatus
{
    Draft,
    LocallyIssuedPendingSync,
    Uploaded,
    FiscalVerified,
    FiscalIntegrityConflict
}

public sealed record SalesInvoiceLine(
    ProductId ProductId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal Tax)
{
    public decimal Subtotal => decimal.Round((Quantity * UnitPrice) - Discount, 2, MidpointRounding.ToEven);
    public decimal Total => Subtotal + Tax;
}

public sealed record ImmutableFiscalSnapshot(
    string FiscalNumber,
    string Prefix,
    long Consecutive,
    string AuthorizationNumber,
    DateTimeOffset IssuedAt,
    string CustomerIdentification,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal PayableAmount,
    string Cufe,
    string QrPayload);

public sealed class SalesInvoice
{
    private readonly List<SalesInvoiceLine> _lines = [];

    public SalesInvoice(
        DocumentId id,
        TenantId tenantId,
        BusinessId businessId,
        WarehouseId warehouseId,
        RegisterId registerId)
    {
        if (id.Value == Guid.Empty) throw new ArgumentException("A document ID is required.", nameof(id));
        Id = id;
        TenantId = tenantId;
        BusinessId = businessId;
        WarehouseId = warehouseId;
        RegisterId = registerId;
    }

    public DocumentId Id { get; }
    public TenantId TenantId { get; }
    public BusinessId BusinessId { get; }
    public WarehouseId WarehouseId { get; }
    public RegisterId RegisterId { get; }
    public SalesInvoiceStatus Status { get; private set; } = SalesInvoiceStatus.Draft;
    public IReadOnlyCollection<SalesInvoiceLine> Lines => _lines;
    public ImmutableFiscalSnapshot? FiscalSnapshot { get; private set; }
    public decimal UntaxedAmount => _lines.Sum(line => line.Subtotal);
    public decimal TaxAmount => _lines.Sum(line => line.Tax);
    public decimal PayableAmount => UntaxedAmount + TaxAmount;

    public void AddLine(SalesInvoiceLine line)
    {
        EnsureDraft();
        if (line.ProductId.Value == Guid.Empty) throw new ArgumentException("A product ID is required.", nameof(line));
        if (line.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(line), "Quantity must be positive.");
        if (line.UnitPrice < 0 || line.Discount < 0 || line.Tax < 0) throw new ArgumentOutOfRangeException(nameof(line));
        if (line.Discount > line.Quantity * line.UnitPrice) throw new ArgumentOutOfRangeException(nameof(line), "Discount cannot exceed gross value.");
        _lines.Add(line);
    }

    public void ConfirmOffline(ImmutableFiscalSnapshot snapshot)
    {
        EnsureDraft();
        if (_lines.Count == 0) throw new InvalidOperationException("An empty invoice cannot be confirmed.");
        if (snapshot.PayableAmount != PayableAmount ||
            snapshot.UntaxedAmount != UntaxedAmount ||
            snapshot.TaxAmount != TaxAmount)
        {
            throw new InvalidOperationException("The fiscal snapshot totals do not match the invoice.");
        }

        FiscalSnapshot = snapshot;
        Status = SalesInvoiceStatus.LocallyIssuedPendingSync;
    }

    public void MarkUploaded() => Transition(SalesInvoiceStatus.LocallyIssuedPendingSync, SalesInvoiceStatus.Uploaded);

    public void MarkFiscalVerified() => Transition(SalesInvoiceStatus.Uploaded, SalesInvoiceStatus.FiscalVerified);

    public void MarkFiscalIntegrityConflict()
    {
        if (Status != SalesInvoiceStatus.Uploaded)
        {
            throw new InvalidOperationException("Only an uploaded invoice can have a fiscal integrity conflict.");
        }

        Status = SalesInvoiceStatus.FiscalIntegrityConflict;
    }

    private void EnsureDraft()
    {
        if (Status != SalesInvoiceStatus.Draft)
        {
            throw new InvalidOperationException("An issued invoice is immutable.");
        }
    }

    private void Transition(SalesInvoiceStatus expected, SalesInvoiceStatus next)
    {
        if (Status != expected) throw new InvalidOperationException($"Expected {expected}, but invoice is {Status}.");
        Status = next;
    }
}
