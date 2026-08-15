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
        if (request.CreatesPayable && request.DueDate is null)
            throw new PurchasingValidationException("DueDate is required when the receipt creates a payable.");
        if (request.DueDate < request.ReceivedAt)
            throw new PurchasingValidationException("DueDate cannot be earlier than ReceivedAt.");
        var currency = request.CurrencyCode.Trim().ToUpperInvariant();
        if (currency.Length != 3) throw new PurchasingValidationException("CurrencyCode must contain three characters.");

        var normalizedLines = GoodsReceiptLineNormalizer.Normalize(request.Lines);
        GoodsReceiptCalculation calculation;
        try
        {
            calculation = GoodsReceiptCalculator.Calculate(normalizedLines.Select(line => (
                line.LineNumber,
                line.ProductId,
                line.Description,
                line.Quantity,
                line.UnitCost,
                line.DiscountAmount,
                line.TaxCode,
                line.TaxRate,
                ParseTaxTreatment(line.TaxTreatment))));
        }
        catch (ArgumentException exception)
        {
            throw new PurchasingValidationException(exception.Message, exception);
        }
        var withholding = await withholdingService.CalculateAsync(user.TenantId, user.BusinessId,
            new WithholdingPreviewRequest(user.BusinessId, WithholdingDirections.Purchase,
                "Accrual", request.SupplierId, request.WithholdingConceptCode,
                request.WithholdingJurisdictionCode, calculation.NetAmount,
                calculation.TaxAmount, request.ReceivedAt),
            cancellationToken);


        var acceptance = await store.AcceptAsync(user, idempotencyKey.Trim(), request with
        {
            CurrencyCode = currency,
            SupplierInvoiceNumber = Normalize(request.SupplierInvoiceNumber, 80),
            Notes = Normalize(request.Notes, 1000),
            Lines = normalizedLines
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
