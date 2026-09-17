using Auraly.Contracts.Authorization;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Commerce.Taxation.Contracts;

namespace Auraly.Application.Sales;

public sealed record OnlineSalesFiscalKeyContext(FiscalKeyReference Reference);

public sealed record PreparedOnlineSalesCheckout(
    PosSaleUploadRequest Request,
    OnlineSalesDraft NextDraft,
    bool IsReplay);

public sealed record PreparedOnlineOrderCheckout(
    PosSaleUploadRequest Request,
    bool IsReplay);

public sealed record OnlineSalesOrderCheckoutLine(
    Guid LineId,
    Guid ProductId,
    string ProductCode,
    string Description,
    string UnitCode,
    string TaxCode,
    decimal TaxRate,
    decimal Quantity,
    decimal PublicUnitPrice,
    decimal DiscountAmount,
    decimal PublicLineTotal,
    decimal DocumentUnitCost,
    string CurrencyCode,
    string PriceSource,
    bool IsGenericProductSnapshot = false);

public sealed record OnlineSalesOrderCheckoutSource(
    Guid OperationId,
    Guid OrderId,
    Guid BusinessId,
    Guid WarehouseId,
    Guid WorkSessionId,
    Guid? CustomerId,
    Guid? CustomerPartySiteId,
    byte[] SnapshotVersion,
    IReadOnlyList<OnlineSalesOrderCheckoutLine> Lines);

public static class OnlineSalesOrderCheckoutLineMapper
{
    public static OnlineSalesDraftLine[] Normalize(
        IReadOnlyList<OnlineSalesOrderCheckoutLine> source) =>
        source.Select(line =>
        {
            var unitPrice = decimal.Round(
                line.PublicUnitPrice / (1m + line.TaxRate / 100m),
                6, MidpointRounding.AwayFromZero);
            var net = decimal.Round(
                line.PublicLineTotal / (1m + line.TaxRate / 100m),
                2, MidpointRounding.ToEven);
            if (decimal.Round(line.Quantity * unitPrice, 2, MidpointRounding.ToEven) < net)
                unitPrice = decimal.Ceiling(net / line.Quantity * 100m) / 100m;
            var promotion = string.Equals(
                line.PriceSource, "Promotion", StringComparison.OrdinalIgnoreCase);
            return new OnlineSalesDraftLine(
                line.LineId, line.ProductId, line.ProductCode, line.Description,
                line.UnitCode, line.TaxCode, line.TaxRate, line.Quantity,
                unitPrice, unitPrice, line.CurrencyCode, line.PriceSource,
                promotion ? 0m : line.DiscountAmount,
                line.DocumentUnitCost, line.IsGenericProductSnapshot, true,
                net, line.PublicLineTotal - net, line.PublicLineTotal,
                promotion ? line.DiscountAmount : 0m,
                line.PublicUnitPrice, line.PublicLineTotal);
        }).ToArray();
}

public sealed record CompleteOnlineOrderSaleResponse(
    OnlineSalesReceipt Receipt,
    bool IsReplay);

public sealed record OnlineSaleSettlementContext(
    Guid BusinessId,
    Guid? CustomerId,
    decimal TaxExclusiveAmount,
    decimal VatAmount,
    DateTimeOffset OccurredAt,
    bool HasBelowCostLine = false);

public sealed record PreparedOnlineSaleSettlement(
    OnlineSaleSettlementContext Context,
    WithholdingCalculationSnapshot Withholding);

public sealed record OnlineOrderCreditValidationIssue(
    Guid? CustomerId,
    string CustomerName,
    string? CustomerIdentification,
    decimal RequestedAmount,
    decimal? AvailableCredit,
    string Reason);

public interface IOnlineSaleWithholdingCalculator
{
    Task WarmAsync(
        Guid tenantId,
        Guid businessId,
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken cancellationToken);

    Task<WithholdingCalculationSnapshot> CalculateAsync(
        Guid tenantId,
        OnlineSaleSettlementContext context,
        CancellationToken cancellationToken);
}

public interface IOnlineSalesCheckoutStore
{
    Task<OnlineSaleSettlementContext> ReadSettlementContextAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OnlineOrderCreditValidationIssue>> ValidateOrderCreditBatchAsync(
        OnlineSalesUserIdentity user,
        Guid businessId,
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken);

    Task<OnlineSalesFiscalKeyContext> ResolveFiscalKeyContextAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CancellationToken cancellationToken);

    Task<PreparedOnlineSalesCheckout> PrepareAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey,
        FiscalVerificationMaterial? fiscalMaterial,
        PreparedOnlineSaleSettlement settlement,
        CancellationToken cancellationToken);

    Task<OnlineSalesFiscalKeyContext> ResolveOrderFiscalKeyContextAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesOrderCheckoutSource source,
        CancellationToken cancellationToken);

    Task<PreparedOnlineOrderCheckout> PrepareOrderAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesOrderCheckoutSource source,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey,
        FiscalVerificationMaterial? fiscalMaterial,
        PreparedOnlineSaleSettlement settlement,
        CancellationToken cancellationToken);

    Task MarkResultAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid documentId,
        string status,
        CancellationToken cancellationToken);

    Task MarkOrderResultAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesOrderCheckoutSource source,
        Guid documentId,
        string status,
        CancellationToken cancellationToken);
}

public sealed class OnlineSalesCheckoutService(
    IOnlineSalesCheckoutStore checkouts,
    IFiscalTechnicalKeyProvider technicalKeys,
    ReceivePosSaleService receiver,
    IOnlineSaleWithholdingCalculator withholdings,
    TimeProvider time)
{
    // The service is request-scoped. A batch can issue several documents with
    // the same authorization, so resolve protected key material once per key
    // instead of performing a secret/database read for every document.
    private readonly Dictionary<FiscalKeyReference, FiscalVerificationMaterial?>
        _fiscalMaterialByReference = [];
    private readonly Dictionary<Guid, OnlineSalesFiscalKeyContext>
        _fiscalKeyContextByBusiness = [];

    private static readonly HashSet<string> PaymentMethods =
    [
        "Cash",
        "DebitCard",
        "CreditCard",
        "Transfer"
    ];

    public Task WarmSettlementBatchAsync(
        OnlineSalesUserIdentity user,
        Guid businessId,
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        return withholdings.WarmAsync(
            user.TenantId, businessId, customerIds, cancellationToken);
    }

    public Task<IReadOnlyList<OnlineOrderCreditValidationIssue>> ValidateOrderCreditBatchAsync(
        OnlineSalesUserIdentity user,
        Guid businessId,
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        ArgumentNullException.ThrowIfNull(orderIds);
        if (businessId == Guid.Empty || orderIds.Count is < 1 or > 50 ||
            orderIds.Any(orderId => orderId == Guid.Empty))
            throw new OnlineSalesDraftValidationException(
                "Selecciona entre 1 y 50 pedidos válidos para validar el crédito.");
        return checkouts.ValidateOrderCreditBatchAsync(
            user, businessId, orderIds, cancellationToken);
    }

    public async Task<CompleteOnlineSalesDraftResponse> CompleteAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        Validate(draftId, request, idempotencyKey);
        var settlement = await PrepareSettlementAsync(
            user, draftId, cancellationToken);
        return await CompletePreparedAsync(
            user, draftId, request, idempotencyKey, settlement, cancellationToken);
    }

    public async Task<CompleteOnlineSalesDraftResponse> CompleteKnownDraftAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesDraft draft,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        ArgumentNullException.ThrowIfNull(draft);
        Validate(draft.DraftId, request, idempotencyKey);
        if (draft.UserId != user.UserId ||
            draft.Version != request.ExpectedVersion ||
            !string.Equals(draft.Status, "Active", StringComparison.Ordinal))
            throw new OnlineSalesDraftConcurrencyException(
                "El borrador preparado ya no coincide con la venta que se intenta completar.");
        var context = new OnlineSaleSettlementContext(
            draft.BusinessId,
            draft.CustomerId,
            draft.UntaxedAmount,
            draft.TaxAmount,
            draft.UpdatedAt,
            draft.Lines.Any(line => SaleBelowCostPolicy.IsBelowCost(
                line.Quantity, line.Net, line.DocumentUnitCost)));
        var withholding = await withholdings.CalculateAsync(
            user.TenantId, context, cancellationToken);
        return await CompletePreparedAsync(
            user,
            draft.DraftId,
            request,
            idempotencyKey,
            new PreparedOnlineSaleSettlement(context, withholding),
            cancellationToken);
    }

    public async Task<CompleteOnlineOrderSaleResponse> CompleteOrderAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesOrderCheckoutSource source,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        ArgumentNullException.ThrowIfNull(source);
        ValidateOrderSource(source, request, idempotencyKey);
        var lines = OnlineSalesOrderCheckoutLineMapper.Normalize(source.Lines);
        var context = new OnlineSaleSettlementContext(
            source.BusinessId,
            source.CustomerId,
            lines.Sum(line => line.Net),
            lines.Sum(line => line.Tax),
            time.GetUtcNow(),
            lines.Any(line => SaleBelowCostPolicy.IsBelowCost(
                line.Quantity, line.Net, line.DocumentUnitCost)));
        var withholding = await withholdings.CalculateAsync(
            user.TenantId, context, cancellationToken);
        var settlement = new PreparedOnlineSaleSettlement(context, withholding);
        if (context.HasBelowCostLine &&
            !user.Permissions.Contains(CommercePermissionCodes.SalesBelowCost))
            throw new OnlineSalesDraftForbiddenException(
                $"Permission '{CommercePermissionCodes.SalesBelowCost}' is required.");

        FiscalVerificationMaterial? material = null;
        if (PosSaleDocumentTypes.IsFiscal(request.DocumentType))
        {
            var cacheKey = source.BusinessId;
            if (!_fiscalKeyContextByBusiness.TryGetValue(cacheKey, out var keyContext))
            {
                keyContext = await checkouts.ResolveOrderFiscalKeyContextAsync(
                    user, source, cancellationToken);
                _fiscalKeyContextByBusiness.Add(cacheKey, keyContext);
            }
            material = await ResolveFiscalMaterialAsync(
                keyContext.Reference, cancellationToken)
                ?? throw new OnlineSalesDraftValidationException(
                    "La clave técnica de la resolución fiscal activa no está disponible.");
        }

        var prepared = await checkouts.PrepareOrderAsync(
            user, source, request, idempotencyKey.Trim(), material,
            settlement, cancellationToken);
        var reception = await receiver.ReceivePreparedOnlineAsync(
            user,
            $"online:{prepared.Request.DocumentId:N}",
            prepared.Request,
            prepared.IsReplay,
            cancellationToken);
        var status = reception.Status == PosSaleRemoteStatuses.FiscalIntegrityConflict
            ? "FiscalConflict"
            : "Completed";
        await checkouts.MarkOrderResultAsync(
            user, source, prepared.Request.DocumentId, status, cancellationToken);
        if (status == "FiscalConflict")
            throw new OnlineSalesDraftValidationException(
                "La factura no superó la validación fiscal interna y no fue enviada a la DIAN. " +
                "Consulta el documento fiscal antes de volver a facturar el pedido.");
        return new(
            OnlineSalesReceiptMapper.From(prepared.Request, reception.Status),
            prepared.IsReplay || reception.IsDuplicate);
    }

    private async Task<CompleteOnlineSalesDraftResponse> CompletePreparedAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey,
        PreparedOnlineSaleSettlement settlement,
        CancellationToken cancellationToken)
    {
        if (settlement.Context.HasBelowCostLine &&
            !user.Permissions.Contains(CommercePermissionCodes.SalesBelowCost))
            throw new OnlineSalesDraftForbiddenException(
                $"Permission '{CommercePermissionCodes.SalesBelowCost}' is required.");
        FiscalVerificationMaterial? material = null;
        if (PosSaleDocumentTypes.IsFiscal(request.DocumentType))
        {
            var cacheKey = settlement.Context.BusinessId;
            if (!_fiscalKeyContextByBusiness.TryGetValue(cacheKey, out var keyContext))
            {
                keyContext = await checkouts.ResolveFiscalKeyContextAsync(
                    user, draftId, cancellationToken);
                _fiscalKeyContextByBusiness.Add(cacheKey, keyContext);
            }
            material = await ResolveFiscalMaterialAsync(
                keyContext.Reference, cancellationToken)
                ?? throw new OnlineSalesDraftValidationException(
                    "La clave técnica de la resolución fiscal activa no está disponible.");
        }
        var prepared = await checkouts.PrepareAsync(
            user, draftId, request, idempotencyKey.Trim(),
            material, settlement, cancellationToken);
        var reception = await receiver.ReceivePreparedOnlineAsync(
            user,
            $"online:{prepared.Request.DocumentId:N}",
            prepared.Request,
            prepared.IsReplay,
            cancellationToken);
        await checkouts.MarkResultAsync(
            user,
            draftId,
            prepared.Request.DocumentId,
            reception.Status == PosSaleRemoteStatuses.FiscalIntegrityConflict
                ? "FiscalConflict"
                : "Completed",
            cancellationToken);
        if (reception.Status == PosSaleRemoteStatuses.FiscalIntegrityConflict)
            throw new OnlineSalesDraftValidationException(
                "La factura no superó la validación fiscal interna y no fue enviada a la DIAN. " +
                "Consulta el documento fiscal antes de volver a facturar el pedido.");
        return new CompleteOnlineSalesDraftResponse(
            OnlineSalesReceiptMapper.From(prepared.Request, reception.Status),
            prepared.NextDraft,
            prepared.IsReplay || reception.IsDuplicate);
    }

    private async Task<FiscalVerificationMaterial?> ResolveFiscalMaterialAsync(
        FiscalKeyReference reference,
        CancellationToken cancellationToken)
    {
        if (_fiscalMaterialByReference.TryGetValue(reference, out var cached))
            return cached;
        var resolved = await technicalKeys.ResolveAsync(reference, cancellationToken);
        _fiscalMaterialByReference.Add(reference, resolved);
        return resolved;
    }

    public async Task<WithholdingCalculationSnapshot> PreviewSettlementAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        if (draftId == Guid.Empty)
            throw new OnlineSalesDraftValidationException("El borrador es obligatorio.");
        return (await PrepareSettlementAsync(user, draftId, cancellationToken))
            .Withholding;
    }

    private async Task<PreparedOnlineSaleSettlement> PrepareSettlementAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        var context = await checkouts.ReadSettlementContextAsync(
            user, draftId, cancellationToken);
        var withholding = await withholdings.CalculateAsync(
            user.TenantId, context, cancellationToken);
        return new PreparedOnlineSaleSettlement(context, withholding);
    }

    private static void DemandPermission(OnlineSalesUserIdentity user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.Permissions.Contains(CommercePermissionCodes.SalesCreate))
            throw new OnlineSalesDraftForbiddenException(
                $"Permission '{CommercePermissionCodes.SalesCreate}' is required.");
    }

    private static void Validate(
        Guid draftId,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!PosSaleDocumentTypes.IsSupported(request.DocumentType))
            throw new OnlineSalesDraftValidationException(
                "El tipo de documento de venta no es valido.");
        if (draftId == Guid.Empty || request.ExpectedVersion < 1)
            throw new OnlineSalesDraftValidationException(
                "Borrador y versión esperada son obligatorios.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new OnlineSalesDraftValidationException(
                "Idempotency-Key es obligatorio y admite máximo 100 caracteres.");
        if (request.Payments.Count > 10 ||
            (request.Payments.Count == 0 && request.Credit is null))
            throw new OnlineSalesDraftValidationException(
                "La venta requiere pagos reales, saldo a crédito o ambos.");
        if (request.Payments.Any(payment =>
                !PaymentMethods.Contains(payment.MethodCode) ||
                payment.Amount <= 0 ||
                payment.Reference?.Length > 160 ||
                payment.Notes?.Length > 500 ||
                payment.CardFranchiseCode?.Length > 64 ||
                payment.ApprovalNumber?.Length > 100 ||
                (payment.MethodCode is "Card" or "DebitCard" or "CreditCard") !=
                (!string.IsNullOrWhiteSpace(payment.CardFranchiseCode) && !string.IsNullOrWhiteSpace(payment.ApprovalNumber)) ||
                (payment.MethodCode == "Transfer" && string.IsNullOrWhiteSpace(payment.Reference)) ||
                (payment.MethodCode != "Transfer" && (payment.BankAccountId is not null || payment.Notes is not null))))
            throw new OnlineSalesDraftValidationException(
                "Uno de los medios de pago no es válido.");
        if (request.Payments.Any(payment => payment.TenderedAmount is { } tendered &&
                (payment.MethodCode != "Cash" || tendered < payment.Amount)))
            throw new OnlineSalesDraftValidationException(
                "El efectivo recibido debe corresponder al pago en efectivo y no puede ser menor al valor aplicado.");
        if (request.Payments.Count(payment => payment.MethodCode == "Cash") > 1)
            throw new OnlineSalesDraftValidationException(
                "La venta admite una sola línea de efectivo.");
        if (request.Credit is not null && request.Credit.Amount <= 0)
            throw new OnlineSalesDraftValidationException(
                "El valor del crédito no es válido.");

    }

    private static void ValidateOrderSource(
        OnlineSalesOrderCheckoutSource source,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey)
    {
        Validate(source.OrderId, request, idempotencyKey);
        if (source.OperationId == Guid.Empty || source.OrderId == Guid.Empty ||
            source.BusinessId == Guid.Empty || source.WarehouseId == Guid.Empty ||
            source.WorkSessionId == Guid.Empty || source.SnapshotVersion.Length == 0 ||
            source.Lines.Count is < 1 or > 500 ||
            source.Lines.Any(line => line.LineId == Guid.Empty ||
                line.ProductId == Guid.Empty || line.Quantity <= 0 ||
                line.PublicUnitPrice < 0 || line.DiscountAmount < 0 ||
                line.PublicLineTotal < 0 || line.DocumentUnitCost < 0 ||
                line.TaxRate is < 0 or > 100) ||
            string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new OnlineSalesDraftValidationException(
                "El pedido no contiene datos comerciales válidos para facturar.");
    }

}
