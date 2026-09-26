using Auraly.Contracts.Purchasing;
using Auraly.Domain.Purchasing;

namespace Auraly.Application.Purchasing;

public interface IGoodsReceiptWorkspaceStore
{
    Task<GoodsReceiptWorkspaceOptions> GetOptionsAsync(PurchasingUserIdentity user, CancellationToken cancellationToken);
    Task<GoodsReceiptProductPage> FindProductsAsync(PurchasingUserIdentity user, Guid supplierId, string? search, bool includeUnassociated, int page, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, decimal?>> GetUnitGrossWeightsAsync(PurchasingUserIdentity user, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);
    Task<GoodsReceiptProductOption> AssociateProductAsync(PurchasingUserIdentity user, AssociateGoodsReceiptProductRequest request, CancellationToken cancellationToken);
    Task<GoodsReceiptPage> ListAsync(PurchasingUserIdentity user, string? search, string? status, int page, int pageSize, CancellationToken cancellationToken);
    Task<GoodsReceiptDraft?> GetDraftAsync(PurchasingUserIdentity user, Guid draftId, CancellationToken cancellationToken);
    Task<GoodsReceiptDetail?> GetDetailAsync(PurchasingUserIdentity user, Guid documentId, CancellationToken cancellationToken);
    Task<GoodsReceiptDraft> SaveDraftAsync(PurchasingUserIdentity user, SaveGoodsReceiptDraftRequest request, GoodsReceiptCalculation? calculation, CancellationToken cancellationToken);
    Task DeleteDraftAsync(PurchasingUserIdentity user, Guid draftId, string concurrencyToken, CancellationToken cancellationToken);
}

public sealed class GoodsReceiptWorkspaceService(IGoodsReceiptWorkspaceStore store)
{
    public Task<GoodsReceiptWorkspaceOptions> GetOptionsAsync(
        PurchasingUserIdentity user, CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.ReadGoodsReceipts);
        return store.GetOptionsAsync(user, cancellationToken);
    }

    public Task<GoodsReceiptProductPage> FindProductsAsync(
        PurchasingUserIdentity user, Guid supplierId, string? search,
        bool includeUnassociated, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.ReadGoodsReceipts);
        if (supplierId == Guid.Empty) throw new PurchasingValidationException("Selecciona un proveedor.");
        ValidatePage(page, pageSize);
        return store.FindProductsAsync(user, supplierId, Normalize(search, 160), includeUnassociated, page, pageSize, cancellationToken);
    }

    public Task<GoodsReceiptProductOption> AssociateProductAsync(
        PurchasingUserIdentity user, AssociateGoodsReceiptProductRequest request,
        CancellationToken cancellationToken = default)
    {
        Require(user, "catalog.costs.manage");
        if (request.SupplierId == Guid.Empty) throw new PurchasingValidationException("Selecciona un proveedor.");
        if (request.ProductId == Guid.Empty) throw new PurchasingValidationException("Selecciona un producto.");
        var presentation = Normalize(request.PurchasePresentationName, 80)
            ?? throw new PurchasingValidationException("Indica la presentación de compra.");
        if (request.UnitsPerPresentation <= 0)
            throw new PurchasingValidationException("Las unidades por presentación deben ser mayores que cero.");
        return store.AssociateProductAsync(user, request with
        {
            SupplierProductCode = Normalize(request.SupplierProductCode, 80),
            PurchasePresentationName = presentation
        }, cancellationToken);
    }

    public Task<GoodsReceiptPage> ListAsync(
        PurchasingUserIdentity user, string? search, string? status,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.ReadGoodsReceipts);
        ValidatePage(page, pageSize);
        var normalizedStatus = Normalize(status, 24);
        if (normalizedStatus is not null &&
            normalizedStatus is not ("Draft" or "Accepted" or "Processed"))
            throw new PurchasingValidationException("El estado solicitado es inválido.");
        return store.ListAsync(user, Normalize(search, 160), normalizedStatus, page, pageSize, cancellationToken);
    }

    public Task<GoodsReceiptDraft?> GetDraftAsync(
        PurchasingUserIdentity user, Guid draftId, CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.ReadGoodsReceipts);
        if (draftId == Guid.Empty) throw new PurchasingValidationException("Falta el identificador del borrador.");
        return store.GetDraftAsync(user, draftId, cancellationToken);
    }
    public Task<GoodsReceiptDetail?> GetDetailAsync(
        PurchasingUserIdentity user, Guid documentId, CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.ReadGoodsReceipts);
        if (documentId == Guid.Empty)
            throw new PurchasingValidationException("Falta el identificador de la recepción.");
        return store.GetDetailAsync(user, documentId, cancellationToken);
    }


    public async Task<GoodsReceiptDraft> SaveDraftAsync(
        PurchasingUserIdentity user, SaveGoodsReceiptDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.CreateGoodsReceipts);
        if (user.BusinessId != request.BusinessId)
            throw new PurchasingForbiddenException("El borrador pertenece a otra sede.");
        if (request.DraftId == Guid.Empty) throw new PurchasingValidationException("Falta el identificador del borrador.");
        if (request.ReceivedAt == default) throw new PurchasingValidationException("Indica la fecha de recepción.");
        if (request.SupplierId is not null && request.SupplierInvoiceDate is null)
            throw new PurchasingValidationException("Indica la fecha de emisión del documento de compra.");
        if (request.CreatesPayable && request.DueDate is null)
            throw new PurchasingValidationException("Indica el vencimiento de la compra a crédito.");
        if (request.DueDate < request.SupplierInvoiceDate)
            throw new PurchasingValidationException("El vencimiento no puede ser anterior a la emisión.");
        if (request.PurchaseEvidenceType is not null &&
            !PurchaseEvidenceTypes.IsValid(request.PurchaseEvidenceType))
            throw new PurchasingValidationException("El tipo de soporte de compra es inválido.");
        var currency = request.CurrencyCode.Trim().ToUpperInvariant();
        if (currency.Length != 3) throw new PurchasingValidationException("El código de moneda debe tener tres caracteres.");

        GoodsReceiptCalculation? calculation = null;
        var productWeights = await store.GetUnitGrossWeightsAsync(
            user, request.Lines.Select(line => line.ProductId).Distinct().ToArray(), cancellationToken);
        var normalizedLines = GoodsReceiptLineNormalizer.Normalize(request.Lines.Select(line => line with
        {
            UnitGrossWeightKg = productWeights.GetValueOrDefault(line.ProductId)
        }).ToArray());
        if (request.PurchaseOrderId is null && normalizedLines.Any(line => line.PurchaseOrderLineId is not null))
            throw new PurchasingValidationException("Selecciona la orden de compra referida por las líneas.");
        if (request.PurchaseOrderId is not null && normalizedLines.Any(line => line.PurchaseOrderLineId is null))
            throw new PurchasingValidationException("Cada línea recuperada de una orden debe conservar su referencia.");
        if (request.PurchaseEvidenceType == PurchaseEvidenceTypes.InternalReceiptVoucher &&
            normalizedLines.Any(line => line.TaxRate > 0 &&
                line.TaxTreatment == PurchasingTaxTreatments.DeductibleInputVat))
            throw new PurchasingValidationException(
                "Un comprobante interno no puede registrar IVA descontable; inclúyelo en el costo.");
        if (request.PurchaseEvidenceType == PurchaseEvidenceTypes.ForeignCommercialInvoice &&
            normalizedLines.Any(line => line.TaxRate > 0))
            throw new PurchasingValidationException(
                "Una factura del exterior no puede registrar IVA colombiano descontable; regístralo en la declaración de importación.");
        if (normalizedLines.Length > 0)
        {
            if (request.SupplierId is null)
                throw new PurchasingValidationException("Selecciona un proveedor antes de agregar productos.");
            try
            {
                calculation = GoodsReceiptCalculator.Calculate(normalizedLines.Select(line => (
                    line.LineNumber, line.ProductId, line.Description, line.Quantity,
                    line.UnitCost, line.DiscountAmount, line.TaxCode, line.TaxRate,
                    ParseTaxTreatment(line.TaxTreatment))));
            }
            catch (ArgumentException exception)
            {
                throw new PurchasingValidationException(exception.Message, exception);
            }
        }

        var normalizedDocuments = request.AdditionalCostDocuments?.Select(document => document with
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
        if (normalizedDocuments is { Length: > 0 })
        {
            if (calculation is null || request.SupplierId is null)
                throw new PurchasingValidationException("Agrega productos antes de distribuir costos adicionales.");
            GoodsReceiptCostCalculator.Calculate(new ConfirmGoodsReceiptRequest(
                request.DraftId, request.BusinessId, request.WarehouseId ?? Guid.Empty,
                request.SupplierId.Value, request.SupplierInvoiceNumber, request.SupplierInvoiceDate,
                request.ReceivedAt, request.CreatesPayable, request.DueDate, currency, request.Notes,
                normalizedLines, PurchaseEvidenceType: request.PurchaseEvidenceType ??
                    PurchaseEvidenceTypes.SupplierElectronicInvoice,
                PurchaseOrderId: request.PurchaseOrderId, ExchangeRate: request.ExchangeRate,
                ExchangeRateDate: request.ExchangeRateDate,
                ExchangeRateSource: request.ExchangeRateSource,
                AdditionalCostDocuments: normalizedDocuments), calculation);
        }

        return await store.SaveDraftAsync(user, request with
        {
            CurrencyCode = currency,
            SupplierInvoiceNumber = Normalize(request.SupplierInvoiceNumber, 80),
            Notes = Normalize(request.Notes, 1000),
            ExchangeRateSource = Normalize(request.ExchangeRateSource, 64) ?? "FunctionalCurrency",
            AdditionalCostDocuments = normalizedDocuments,
            Lines = normalizedLines.Select(line => line with
            { OverReceiptReason = Normalize(line.OverReceiptReason, 500) }).ToArray()
        }, calculation, cancellationToken);
    }

    public Task DeleteDraftAsync(
        PurchasingUserIdentity user, Guid draftId, string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.CreateGoodsReceipts);
        if (draftId == Guid.Empty) throw new PurchasingValidationException("Falta el identificador del borrador.");
        if (string.IsNullOrWhiteSpace(concurrencyToken))
            throw new PurchasingValidationException("Falta la versión del borrador.");
        return store.DeleteDraftAsync(user, draftId, concurrencyToken, cancellationToken);
    }

    private static PurchaseTaxTreatment ParseTaxTreatment(string value) =>
        Enum.TryParse<PurchaseTaxTreatment>(value, false, out var treatment) && Enum.IsDefined(treatment)
            ? treatment
            : throw new PurchasingValidationException("El tratamiento del IVA es inválido.");

    private static string? Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new PurchasingValidationException($"El valor supera {maximumLength} caracteres.");
        return normalized;
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1) throw new PurchasingValidationException("La página debe ser mayor que cero.");
        if (pageSize is < 1 or > 100)
            throw new PurchasingValidationException("El tamaño de página debe estar entre 1 y 100.");
    }

    private static void Require(PurchasingUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission))
            throw new PurchasingForbiddenException("No tienes permiso para esta operación de compras.");
    }
}
