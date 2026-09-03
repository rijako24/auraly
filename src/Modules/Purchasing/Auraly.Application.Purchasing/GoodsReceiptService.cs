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
        GoodsReceiptCostCalculation costCalculation,
        WithholdingCalculationSnapshot withholding,
        IReadOnlyDictionary<Guid, WithholdingCalculationSnapshot> additionalWithholdings,
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
        if (request.PurchaseEvidenceType == PurchaseEvidenceTypes.ImportDeclaration)
            throw new PurchasingValidationException("An import declaration must be added as a nationalization cost document.");
        var normalizedLines = GoodsReceiptLineNormalizer.Normalize(request.Lines);
        ValidateEvidenceTaxTreatment(request.PurchaseEvidenceType, normalizedLines);
        var calculation = Calculate(normalizedLines);
        if (request.ExchangeRate <= 0)
            throw new PurchasingValidationException("ExchangeRate must be positive.");
        return await CalculateWithholdingAsync(
            user, request.SupplierId, request.WithholdingConceptCode,
            request.WithholdingJurisdictionCode,
            FunctionalCalculation(
                decimal.Round(calculation.NetAmount * request.ExchangeRate, 4,
                    MidpointRounding.AwayFromZero),
                decimal.Round(calculation.TaxAmount * request.ExchangeRate, 4,
                    MidpointRounding.AwayFromZero)),
            request.SupplierInvoiceDate, cancellationToken);
    }

    public async Task<WithholdingCalculationSnapshot> PreviewCostWithholdingAsync(
        PurchasingUserIdentity user,
        PreviewGoodsReceiptCostWithholdingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        if (user.BusinessId != request.BusinessId)
            throw new PurchasingForbiddenException("The cost document belongs to another business.");
        Require(user, PurchasingPermissionCodes.CreateGoodsReceipts);
        var document = request.Document ?? throw new PurchasingValidationException("Document is required.");
        if (document.SupplierId == Guid.Empty || document.IssuedAt == default ||
            document.ExchangeRate <= 0 || document.Lines is null || document.Lines.Count == 0)
            throw new PurchasingValidationException("Supplier, issue date, exchange rate and lines are required.");
        if (!PurchaseEvidenceTypes.IsValid(document.PurchaseEvidenceType))
            throw new PurchasingValidationException("The cost document evidence type is invalid.");
        if (document.Lines.Any(line => line.Amount < 0 || line.TaxAmount < 0 ||
            line.TaxableBaseAmount < 0 || line.TaxRate is < 0 or > 100))
            throw new PurchasingValidationException("The cost document contains invalid amounts.");
        if (document.PurchaseEvidenceType == PurchaseEvidenceTypes.ForeignCommercialInvoice &&
            document.Lines.Any(line => line.TaxAmount != 0))
            throw new PurchasingValidationException("A foreign invoice cannot recognize Colombian input VAT.");
        var calculation = FunctionalCalculation(
            decimal.Round(document.Lines.Sum(line => line.Amount) * document.ExchangeRate, 4,
                MidpointRounding.AwayFromZero),
            decimal.Round(document.Lines.Sum(line => line.TaxAmount) * document.ExchangeRate, 4,
                MidpointRounding.AwayFromZero));
        return await CalculateWithholdingAsync(
            user, document.SupplierId, document.WithholdingConceptCode,
            document.WithholdingJurisdictionCode, calculation,
            document.IssuedAt, cancellationToken);
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
        if (request.PurchaseEvidenceType is PurchaseEvidenceTypes.SupplierElectronicInvoice or
                PurchaseEvidenceTypes.ForeignCommercialInvoice &&
            string.IsNullOrWhiteSpace(request.SupplierInvoiceNumber))
            throw new PurchasingValidationException(
                "Supplier invoice number and date are required for an electronic supplier invoice.");
        if (request.PurchaseEvidenceType is not (PurchaseEvidenceTypes.SupplierElectronicInvoice or
                PurchaseEvidenceTypes.ForeignCommercialInvoice) &&
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
        var normalizedRequest = request with
        {
            CurrencyCode = currency,
            SupplierInvoiceNumber = Normalize(request.SupplierInvoiceNumber, 80),
            Notes = Normalize(request.Notes, 1000),
            ExchangeRateSource = Normalize(request.ExchangeRateSource, 64) ?? "FunctionalCurrency",
            Lines = normalizedLines.Select(line => line with
            { OverReceiptReason = Normalize(line.OverReceiptReason, 500) }).ToArray(),
            AdditionalCostDocuments = NormalizeAdditionalDocuments(request.AdditionalCostDocuments)
        };
        if ((normalizedRequest.AdditionalCostDocuments ?? []).Select(value => value.CostDocumentId)
            .Append(request.DocumentId).Distinct().Count() !=
            (normalizedRequest.AdditionalCostDocuments?.Count ?? 0) + 1)
            throw new PurchasingValidationException("Document ids must be unique within the receipt.");
        if ((normalizedRequest.AdditionalCostDocuments ?? []).Any(value =>
            value.SupplierId == request.SupplierId &&
            string.Equals(value.DocumentNumber, normalizedRequest.SupplierInvoiceNumber,
                StringComparison.OrdinalIgnoreCase)))
            throw new PurchasingValidationException(
                "The primary supplier invoice cannot be repeated as an additional cost document.");
        var costCalculation = GoodsReceiptCostCalculator.Calculate(normalizedRequest, calculation);
        var withholding = await CalculateWithholdingAsync(
            user, request.SupplierId, request.WithholdingConceptCode,
            request.WithholdingJurisdictionCode, FunctionalCalculation(
                costCalculation.FunctionalNetAmount, costCalculation.FunctionalTaxAmount),
            request.SupplierInvoiceDate.Value, cancellationToken);
        var additionalWithholdings = new Dictionary<Guid, WithholdingCalculationSnapshot>();
        foreach (var document in costCalculation.AdditionalDocuments)
        {
            additionalWithholdings[document.Request.CostDocumentId] = await CalculateWithholdingAsync(
                user, document.Request.SupplierId, document.Request.WithholdingConceptCode,
                document.Request.WithholdingJurisdictionCode,
                FunctionalCalculation(document.FunctionalNetAmount, document.FunctionalTaxAmount),
                document.Request.IssuedAt, cancellationToken);
        }

        var acceptance = await store.AcceptAsync(user, idempotencyKey.Trim(), normalizedRequest,
            calculation, costCalculation, withholding, additionalWithholdings, cancellationToken);
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

    private static GoodsReceiptCalculation FunctionalCalculation(decimal net, decimal tax) =>
        new([], net, tax, decimal.Round(net + tax, 4, MidpointRounding.AwayFromZero));

    private static IReadOnlyCollection<GoodsReceiptCostDocumentRequest>? NormalizeAdditionalDocuments(
        IReadOnlyCollection<GoodsReceiptCostDocumentRequest>? documents) =>
        documents?.Select(document => document with
        {
            DocumentNumber = Normalize(document.DocumentNumber, 80)!,
            CurrencyCode = document.CurrencyCode.Trim().ToUpperInvariant(),
            ExchangeRateSource = Normalize(document.ExchangeRateSource, 64) ?? "FunctionalCurrency",
            Lines = document.Lines.Select(line => line with
            {
                Description = Normalize(line.Description, 250)!,
                TaxCode = Normalize(line.TaxCode, 32)!.ToUpperInvariant()
            }).ToArray()
        }).ToArray();

    private static void ValidateEvidenceTaxTreatment(
        string purchaseEvidenceType,
        IReadOnlyCollection<GoodsReceiptLineRequest> normalizedLines)
    {
        if (purchaseEvidenceType == PurchaseEvidenceTypes.InternalReceiptVoucher &&
            normalizedLines.Any(line => line.TaxRate > 0 &&
                line.TaxTreatment == PurchasingTaxTreatments.DeductibleInputVat))
            throw new PurchasingValidationException(
                "An internal receipt voucher cannot recognize deductible input VAT; use CapitalizedCost.");
        if (purchaseEvidenceType == PurchaseEvidenceTypes.ForeignCommercialInvoice &&
            normalizedLines.Any(line => line.TaxRate > 0))
            throw new PurchasingValidationException(
                "A foreign commercial invoice cannot recognize Colombian input VAT; record import VAT on the import declaration.");
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
