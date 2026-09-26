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
    IGoodsReceiptWorkspaceStore workspaceStore,
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
            throw new PurchasingForbiddenException("La recepción pertenece a otra sede.");
        Require(user, PurchasingPermissionCodes.CreateGoodsReceipts);
        if (request.SupplierId == Guid.Empty)
            throw new PurchasingValidationException("Selecciona un proveedor.");
        if (request.SupplierInvoiceDate == default)
            throw new PurchasingValidationException("Indica la fecha de emisión.");
        if (!PurchaseEvidenceTypes.IsValid(request.PurchaseEvidenceType))
            throw new PurchasingValidationException("El tipo de soporte de compra es inválido.");
        if (request.PurchaseEvidenceType == PurchaseEvidenceTypes.ImportDeclaration)
            throw new PurchasingValidationException("Agrega la declaración de importación como documento de costo de nacionalización.");
        var productWeights = await workspaceStore.GetUnitGrossWeightsAsync(
            user, request.Lines.Select(line => line.ProductId).Distinct().ToArray(), cancellationToken);
        var normalizedLines = GoodsReceiptLineNormalizer.Normalize(request.Lines.Select(line => line with
        {
            UnitGrossWeightKg = productWeights.GetValueOrDefault(line.ProductId)
        }).ToArray());
        ValidateEvidenceTaxTreatment(request.PurchaseEvidenceType, normalizedLines);
        var calculation = Calculate(normalizedLines);
        if (request.ExchangeRate <= 0)
            throw new PurchasingValidationException("La tasa de cambio debe ser positiva.");
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
            throw new PurchasingForbiddenException("El documento de costo pertenece a otra sede.");
        Require(user, PurchasingPermissionCodes.CreateGoodsReceipts);
        var document = request.Document ?? throw new PurchasingValidationException("Selecciona un documento de costo.");
        if (document.SupplierId == Guid.Empty || document.IssuedAt == default ||
            document.ExchangeRate <= 0 || document.Lines is null || document.Lines.Count == 0)
            throw new PurchasingValidationException("Completa proveedor, emisión, tasa de cambio y conceptos.");
        if (!PurchaseEvidenceTypes.IsValid(document.PurchaseEvidenceType))
            throw new PurchasingValidationException("El tipo de soporte del costo es inválido.");
        if (document.Lines.Any(line => line.Amount < 0 || line.TaxAmount < 0 ||
            line.TaxableBaseAmount < 0 || line.TaxRate is < 0 or > 100))
            throw new PurchasingValidationException("El documento de costo contiene valores inválidos.");
        if (document.PurchaseEvidenceType == PurchaseEvidenceTypes.ForeignCommercialInvoice &&
            document.Lines.Any(line => line.TaxAmount != 0))
            throw new PurchasingValidationException("Una factura del exterior no puede registrar IVA colombiano descontable.");
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
            throw new PurchasingForbiddenException("La recepción pertenece a otra sede.");
        Require(user, PurchasingPermissionCodes.CreateGoodsReceipts);
        Require(user, PurchasingPermissionCodes.ConfirmGoodsReceipts);
        if (request.DocumentId == Guid.Empty) throw new PurchasingValidationException("Falta el identificador de la recepción.");
        if (request.WarehouseId == Guid.Empty) throw new PurchasingValidationException("Selecciona una bodega.");
        if (request.SupplierId == Guid.Empty) throw new PurchasingValidationException("Selecciona un proveedor.");
        if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new PurchasingValidationException("Falta la clave de confirmación.");
        if (idempotencyKey.Length > 160) throw new PurchasingValidationException("La clave de confirmación es demasiado larga.");
        if (request.ReceivedAt == default) throw new PurchasingValidationException("Indica la fecha de recepción.");
        if (!PurchaseEvidenceTypes.IsValid(request.PurchaseEvidenceType))
            throw new PurchasingValidationException("El tipo de soporte de compra es inválido.");
        if (request.SupplierInvoiceDate is null)
            throw new PurchasingValidationException("Indica la fecha de emisión del documento de compra.");
        if (request.PurchaseEvidenceType is PurchaseEvidenceTypes.SupplierElectronicInvoice or
                PurchaseEvidenceTypes.ForeignCommercialInvoice &&
            string.IsNullOrWhiteSpace(request.SupplierInvoiceNumber))
            throw new PurchasingValidationException(
                "La factura del proveedor requiere número y fecha de emisión.");
        if (request.PurchaseEvidenceType is not (PurchaseEvidenceTypes.SupplierElectronicInvoice or
                PurchaseEvidenceTypes.ForeignCommercialInvoice) &&
            !string.IsNullOrWhiteSpace(request.SupplierInvoiceNumber))
            throw new PurchasingValidationException(
                "El número de factura del proveedor solo aplica cuando ese es el tipo de soporte.");
        if (request.CreatesPayable && request.DueDate is null)
            throw new PurchasingValidationException("Indica el vencimiento de la cuenta por pagar.");
        if (request.DueDate < request.SupplierInvoiceDate)
            throw new PurchasingValidationException("El vencimiento no puede ser anterior a la emisión.");
        if ((request.AdditionalCostDocuments ?? []).Any(document =>
            document.PurchaseEvidenceType == PurchaseEvidenceTypes.BuyerElectronicSupportDocument &&
            !string.IsNullOrWhiteSpace(document.DocumentNumber)))
            throw new PurchasingValidationException(
                "Deja vacío el número del documento soporte adicional; Auraly lo asigna al confirmar.");
        var currency = request.CurrencyCode.Trim().ToUpperInvariant();
        if (currency.Length != 3) throw new PurchasingValidationException("El código de moneda debe tener tres caracteres.");

        var productWeights = await workspaceStore.GetUnitGrossWeightsAsync(
            user, request.Lines.Select(line => line.ProductId).Distinct().ToArray(), cancellationToken);
        var normalizedLines = GoodsReceiptLineNormalizer.Normalize(request.Lines.Select(line => line with
        {
            UnitGrossWeightKg = productWeights.GetValueOrDefault(line.ProductId)
        }).ToArray());
        if (request.PurchaseOrderId is null && normalizedLines.Any(line => line.PurchaseOrderLineId is not null))
            throw new PurchasingValidationException("Selecciona la orden de compra referida por las líneas.");
        if (request.PurchaseOrderId is not null && normalizedLines.Any(line => line.PurchaseOrderLineId is null))
            throw new PurchasingValidationException("Cada línea recuperada de una orden debe conservar su referencia.");
        if (normalizedLines.Any(line => line.OverReceiptReason?.Trim().Length > 500))
            throw new PurchasingValidationException("El motivo del exceso recibido no puede superar 500 caracteres.");
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
            throw new PurchasingValidationException("Cada documento de la recepción debe tener un identificador único.");
        if ((normalizedRequest.AdditionalCostDocuments ?? []).Any(value =>
            value.SupplierId == request.SupplierId &&
            string.Equals(value.DocumentNumber, normalizedRequest.SupplierInvoiceNumber,
                StringComparison.OrdinalIgnoreCase)))
            throw new PurchasingValidationException(
                "La factura principal del proveedor no se puede repetir como documento de costo adicional.");
        var costCalculation = GoodsReceiptCostCalculator.Calculate(normalizedRequest, calculation);
        var withholdingPlan = await withholdingService.PrepareCalculationPlanAsync(
            user.TenantId, user.BusinessId,
            costCalculation.AdditionalDocuments.Select(item => item.Request.SupplierId)
                .Append(request.SupplierId).Distinct().ToArray(), cancellationToken);
        var withholding = CalculateWithholding(withholdingPlan,
            user, request.SupplierId, request.WithholdingConceptCode,
            request.WithholdingJurisdictionCode, FunctionalCalculation(
                costCalculation.FunctionalNetAmount, costCalculation.FunctionalTaxAmount),
            request.SupplierInvoiceDate.Value);
        var additionalWithholdings = new Dictionary<Guid, WithholdingCalculationSnapshot>();
        foreach (var document in costCalculation.AdditionalDocuments)
        {
            additionalWithholdings[document.Request.CostDocumentId] = CalculateWithholding(withholdingPlan,
                user, document.Request.SupplierId, document.Request.WithholdingConceptCode,
                document.Request.WithholdingJurisdictionCode,
                FunctionalCalculation(document.FunctionalNetAmount, document.FunctionalTaxAmount),
                document.Request.IssuedAt);
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

    private WithholdingCalculationSnapshot CalculateWithholding(
        WithholdingCalculationPlan plan,
        PurchasingUserIdentity user,
        Guid supplierId,
        string? conceptCode,
        string? jurisdictionCode,
        GoodsReceiptCalculation calculation,
        DateTimeOffset occurredAt) =>
        withholdingService.Calculate(plan,
            new WithholdingPreviewRequest(user.BusinessId, WithholdingDirections.Purchase,
                WithholdingRecognitionMoments.Accrual, supplierId, conceptCode,
                jurisdictionCode, calculation.NetAmount, calculation.TaxAmount, occurredAt));

    private Task<WithholdingCalculationSnapshot> CalculateWithholdingAsync(
        PurchasingUserIdentity user, Guid supplierId, string? conceptCode,
        string? jurisdictionCode, GoodsReceiptCalculation calculation,
        DateTimeOffset occurredAt, CancellationToken cancellationToken) =>
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
            DocumentNumber = document.PurchaseEvidenceType == PurchaseEvidenceTypes.BuyerElectronicSupportDocument
                ? $"DS-{document.CostDocumentId:N}"
                : Normalize(document.DocumentNumber, 80)!,
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
                "Un comprobante interno no puede registrar IVA descontable; inclúyelo en el costo.");
        if (purchaseEvidenceType == PurchaseEvidenceTypes.ForeignCommercialInvoice &&
            normalizedLines.Any(line => line.TaxRate > 0))
            throw new PurchasingValidationException(
                "Una factura del exterior no puede registrar IVA colombiano descontable; regístralo en la declaración de importación.");
    }

    private static string? Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new PurchasingValidationException($"El valor supera {maximumLength} caracteres.");
        return normalized;
    }

    private static PurchaseTaxTreatment ParseTaxTreatment(string value)
    {
        if (!Enum.TryParse<PurchaseTaxTreatment>(value, false, out var treatment) ||
            !Enum.IsDefined(treatment))
        {
            throw new PurchasingValidationException(
                "Selecciona un tratamiento del IVA válido.");
        }

        return treatment;
    }

    private static void Require(PurchasingUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission))
            throw new PurchasingForbiddenException("No tienes permiso para esta operación de compras.");
    }
}

public sealed class PurchasingForbiddenException(string message) : Exception(message);
public sealed class PurchasingValidationException : Exception
{
    public PurchasingValidationException(string message) : base(message) { }
    public PurchasingValidationException(string message, Exception innerException) : base(message, innerException) { }
}
public sealed class PurchasingConflictException(string message) : Exception(message);
