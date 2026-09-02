using Auraly.Commerce.Taxation.Application;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Application.DocumentProcessing;
using Auraly.Contracts.Purchasing;
using Auraly.Domain.Purchasing;

namespace Auraly.Application.Purchasing;

public interface IGoodsReceiptStore
{
    Task<GoodsReceiptAcceptance> AcceptAsync(
        PurchasingUserIdentity user,
        string idempotencyKey,
        ConfirmGoodsReceiptRequest request,
        GoodsReceiptCalculation calculation,
        WithholdingCalculationSnapshot withholding,
        CancellationToken cancellationToken);
}

public sealed class GoodsReceiptService(
    IGoodsReceiptStore store,
    IDocumentProcessingSignalPublisher signalPublisher,
    WithholdingService withholdingService)
{
    public async Task<WithholdingCalculationSnapshot> PreviewWithholdingAsync(
        PurchasingUserIdentity user,
        PreviewGoodsReceiptWithholdingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        if (user.BusinessId != request.BusinessId)
            throw new PurchasingForbiddenException("The goods receipt belongs to another business.");
        Require(user, PurchasingPermissionCodes.CreateGoodsReceipts);
        if (request.SupplierId == Guid.Empty)
            throw new PurchasingValidationException("SupplierId is required.");
        if (request.SupplierInvoiceDate == default)
            throw new PurchasingValidationException("SupplierInvoiceDate is required.");
        if (!PurchaseEvidenceTypes.IsValid(request.PurchaseEvidenceType))
            throw new PurchasingValidationException("PurchaseEvidenceType is invalid.");
        var normalizedLines = GoodsReceiptLineNormalizer.Normalize(request.Lines);
        ValidateEvidenceTaxTreatment(request.PurchaseEvidenceType, normalizedLines);
        var calculation = Calculate(normalizedLines);
        return await CalculateWithholdingAsync(
            user, request.SupplierId, request.WithholdingConceptCode,
            request.WithholdingJurisdictionCode, calculation,
            request.SupplierInvoiceDate, cancellationToken);
    }

    public async Task<GoodsReceiptAcceptance> ConfirmAsync(
        PurchasingUserIdentity user,
        string idempotencyKey,
        ConfirmGoodsReceiptRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        if (user.BusinessId != request.BusinessId)
            throw new PurchasingForbiddenException("The goods receipt belongs to another business.");
        Require(user, PurchasingPermissionCodes.CreateGoodsReceipts);
        Require(user, PurchasingPermissionCodes.ConfirmGoodsReceipts);
        if (request.DocumentId == Guid.Empty) throw new PurchasingValidationException("DocumentId is required.");
        if (request.WarehouseId == Guid.Empty) throw new PurchasingValidationException("WarehouseId is required.");
        if (request.SupplierId == Guid.Empty) throw new PurchasingValidationException("SupplierId is required.");
        if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new PurchasingValidationException("Idempotency-Key is required.");
        if (idempotencyKey.Length > 160) throw new PurchasingValidationException("Idempotency-Key is too long.");
        if (request.ReceivedAt == default) throw new PurchasingValidationException("ReceivedAt is required.");
        if (!PurchaseEvidenceTypes.IsValid(request.PurchaseEvidenceType))
            throw new PurchasingValidationException("PurchaseEvidenceType is invalid.");
        if (request.SupplierInvoiceDate is null)
            throw new PurchasingValidationException("SupplierInvoiceDate is required as the purchase document issue date.");
        if (request.PurchaseEvidenceType == PurchaseEvidenceTypes.SupplierElectronicInvoice &&
            string.IsNullOrWhiteSpace(request.SupplierInvoiceNumber))
            throw new PurchasingValidationException(
                "Supplier invoice number and date are required for an electronic supplier invoice.");
        if (request.PurchaseEvidenceType != PurchaseEvidenceTypes.SupplierElectronicInvoice &&
            !string.IsNullOrWhiteSpace(request.SupplierInvoiceNumber))
            throw new PurchasingValidationException(
                "Supplier invoice number is only valid for an electronic supplier invoice.");
        if (request.CreatesPayable && request.DueDate is null)
            throw new PurchasingValidationException("DueDate is required when the receipt creates a payable.");
        if (request.DueDate < request.SupplierInvoiceDate)
            throw new PurchasingValidationException("DueDate cannot be earlier than SupplierInvoiceDate.");
        var currency = request.CurrencyCode.Trim().ToUpperInvariant();
        if (currency.Length != 3) throw new PurchasingValidationException("CurrencyCode must contain three characters.");

        var normalizedLines = GoodsReceiptLineNormalizer.Normalize(request.Lines);
        if (request.PurchaseOrderId is null && normalizedLines.Any(line => line.PurchaseOrderLineId is not null))
            throw new PurchasingValidationException("PurchaseOrderId is required when receipt lines reference an order.");
        if (request.PurchaseOrderId is not null && normalizedLines.Any(line => line.PurchaseOrderLineId is null))
            throw new PurchasingValidationException("Every line recovered from a purchase order must retain its order-line reference.");
        if (normalizedLines.Any(line => line.OverReceiptReason?.Trim().Length > 500))
            throw new PurchasingValidationException("OverReceiptReason cannot exceed 500 characters.");
        ValidateEvidenceTaxTreatment(request.PurchaseEvidenceType, normalizedLines);
        var calculation = Calculate(normalizedLines);
        var withholding = await CalculateWithholdingAsync(
            user, request.SupplierId, request.WithholdingConceptCode,
            request.WithholdingJurisdictionCode, calculation,
            request.SupplierInvoiceDate.Value, cancellationToken);


        var acceptance = await store.AcceptAsync(user, idempotencyKey.Trim(), request with
        {
            CurrencyCode = currency,
            SupplierInvoiceNumber = Normalize(request.SupplierInvoiceNumber, 80),
            Notes = Normalize(request.Notes, 1000),
            Lines = normalizedLines.Select(line => line with
            { OverReceiptReason = Normalize(line.OverReceiptReason, 500) }).ToArray()
        }, calculation, withholding, cancellationToken);
        await signalPublisher.PublishAsync(
            new DocumentProcessingSignal(
                acceptance.MovementId,
                request.BusinessId,
                request.DocumentId,
                "GoodsReceipt"),
            cancellationToken);
        return acceptance;
    }

    private Task<WithholdingCalculationSnapshot> CalculateWithholdingAsync(
        PurchasingUserIdentity user,
        Guid supplierId,
        string? conceptCode,
        string? jurisdictionCode,
        GoodsReceiptCalculation calculation,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        withholdingService.CalculateAsync(user.TenantId, user.BusinessId,
            new WithholdingPreviewRequest(user.BusinessId, WithholdingDirections.Purchase,
                WithholdingRecognitionMoments.Accrual, supplierId, conceptCode,
                jurisdictionCode, calculation.NetAmount, calculation.TaxAmount, occurredAt),
            cancellationToken);

    private static GoodsReceiptCalculation Calculate(
        IReadOnlyCollection<GoodsReceiptLineRequest> normalizedLines)
    {
        try
        {
            return GoodsReceiptCalculator.Calculate(normalizedLines.Select(line => (
                line.LineNumber, line.ProductId, line.Description, line.Quantity,
                line.UnitCost, line.DiscountAmount, line.TaxCode, line.TaxRate,
                ParseTaxTreatment(line.TaxTreatment))));
        }
        catch (ArgumentException exception)
        {
            throw new PurchasingValidationException(exception.Message, exception);
        }
    }

    private static void ValidateEvidenceTaxTreatment(
        string purchaseEvidenceType,
        IReadOnlyCollection<GoodsReceiptLineRequest> normalizedLines)
    {
        if (purchaseEvidenceType == PurchaseEvidenceTypes.InternalReceiptVoucher &&
            normalizedLines.Any(line => line.TaxRate > 0 &&
                line.TaxTreatment == PurchasingTaxTreatments.DeductibleInputVat))
            throw new PurchasingValidationException(
                "An internal receipt voucher cannot recognize deductible input VAT; use CapitalizedCost.");
    }

    private static string? Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new PurchasingValidationException($"The value exceeds {maximumLength} characters.");
        return normalized;
    }

    private static PurchaseTaxTreatment ParseTaxTreatment(string value)
    {
        if (!Enum.TryParse<PurchaseTaxTreatment>(value, false, out var treatment) ||
            !Enum.IsDefined(treatment))
        {
            throw new PurchasingValidationException(
                $"TaxTreatment must be {PurchasingTaxTreatments.DeductibleInputVat}, " +
                $"{PurchasingTaxTreatments.CapitalizedCost} or {PurchasingTaxTreatments.NotApplicable}.");
        }

        return treatment;
    }

    private static void Require(PurchasingUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission))
            throw new PurchasingForbiddenException($"Permission '{permission}' is required.");
    }
}

public sealed class PurchasingForbiddenException(string message) : Exception(message);
public sealed class PurchasingValidationException : Exception
{
    public PurchasingValidationException(string message) : base(message) { }
    public PurchasingValidationException(string message, Exception innerException) : base(message, innerException) { }
}
public sealed class PurchasingConflictException(string message) : Exception(message);
