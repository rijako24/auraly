using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Sales;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Orders;
using Auraly.Contracts.Sales;

namespace Auraly.Application.Orders;

public sealed record OrderBatchLease(
    Guid OperationId,
    Guid LeaseToken,
    InvoiceOrdersResponse? Replay);

public interface IOrderBatchStore
{
    Task<OrderBatchLease> BeginAsync(
        OrderActor actor,
        InvoiceOrdersRequest request,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken);

    Task SaveProgressAsync(
        OrderActor actor,
        Guid operationId,
        Guid leaseToken,
        InvoiceOrdersResponse response,
        bool completed,
        CancellationToken cancellationToken);
}

public sealed class OrderBatchService(
    IOrderBatchStore batches,
    OrderService orders,
    OnlineSalesCheckoutService checkout,
    InvoiceChargeService invoiceCharges)
{
    private static readonly HashSet<string> PaymentMethods =
    [
        "Cash",
        "Credit",
        "DebitCard",
        "CreditCard",
        "Transfer"
    ];
    private const int ProgressCheckpointSize = 5;

    public async Task<IReadOnlyList<OrderCreditValidationIssue>> ValidateCreditAsync(
        OrderActor actor,
        InvoiceOrdersRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(actor, request with { PaymentMethodCode = "Credit" }, "credit-preflight");
        var identity = new OnlineSalesUserIdentity(
            actor.UserId,
            actor.TenantId,
            actor.Permissions);
        var normalizedOrders = request.OrderIds.Distinct().ToArray();
        var batchOrders = await orders.GetBatchAsync(actor, normalizedOrders, cancellationToken);
        var chargeDefinitions = await ResolveChargeDefinitionsAsync(actor, request, cancellationToken);
        var issues = await checkout.ValidateOrderCreditBatchAsync(
            identity,
            actor.BusinessId,
            CreditAmounts(normalizedOrders, batchOrders, ChargeSelections(request), chargeDefinitions),
            cancellationToken);
        return issues.Select(MapCreditIssue).ToArray();
    }

    public async Task<InvoiceOrdersResponse> InvoiceAsync(
        OrderActor actor,
        InvoiceOrdersRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Validate(actor, request, idempotencyKey);
        var normalizedOrders = request.OrderIds.Distinct().ToArray();
        var identity = new OnlineSalesUserIdentity(
            actor.UserId,
            actor.TenantId,
            actor.Permissions);
        var hash = RequestHash(request, normalizedOrders);
        var lease = await batches.BeginAsync(
            actor,
            request with { OrderIds = normalizedOrders },
            idempotencyKey.Trim(),
            hash,
            cancellationToken);
        if (lease.Replay is not null)
            return lease.Replay with { IsReplay = true };

        var batchOrders = await orders.GetBatchAsync(
            actor, normalizedOrders, cancellationToken);
        var chargeDefinitions = await ResolveChargeDefinitionsAsync(
            actor, request, cancellationToken);

        if (request.PaymentMethodCode == "Credit")
        {
            var creditIssues = await checkout.ValidateOrderCreditBatchAsync(
                identity,
                actor.BusinessId,
                CreditAmounts(normalizedOrders, batchOrders, ChargeSelections(request), chargeDefinitions),
                cancellationToken);
            if (creditIssues.Count > 0)
            {
                var rejected = new InvoiceOrdersResponse(
                    Guid.Empty,
                    "CreditRejected",
                    normalizedOrders.Length,
                    0,
                    0,
                    false,
                    [],
                    CreditValidationIssues: creditIssues.Select(MapCreditIssue).ToArray());
                await batches.SaveProgressAsync(
                    actor,
                    lease.OperationId,
                    lease.LeaseToken,
                    rejected,
                    completed: true,
                    cancellationToken);
                return rejected;
            }
        }

        var settlementWarmup = checkout.WarmSettlementBatchAsync(
            identity,
            actor.BusinessId,
            batchOrders.Values
                .Where(order => order.CustomerId.HasValue)
                .Select(order => order.CustomerId!.Value)
                .Distinct()
                .ToArray(),
            cancellationToken);
        var preparedOrders = new Dictionary<Guid, OrderDetail>();
        var preparationFailures = new Dictionary<Guid, string>();
        foreach (var orderId in normalizedOrders)
        {
            try
            {
                if (!batchOrders.TryGetValue(orderId, out var order))
                    throw new OrderNotFoundException("El pedido no existe en esta sede.");
                if (order.WarehouseId is null)
                    throw new OrderConflictException(
                        "El pedido no tiene una bodega de venta asignada y no puede emitirse.");
                if (order.WarehouseId != request.WarehouseId)
                    throw new OrderConflictException(
                        "El pedido pertenece a otra bodega de venta. Ábrelo desde la bodega asignada.");
                preparedOrders.Add(orderId, order);
            }
            catch (Exception exception) when (IsRecoverableOrderFailure(exception))
            {
                preparationFailures.Add(orderId, exception.Message);
            }
        }
        await settlementWarmup;

        var results = new List<InvoiceOrderResult>(normalizedOrders.Length);
        var completed = 0;
        var failed = 0;
        foreach (var orderId in normalizedOrders)
        {
            if (preparationFailures.TryGetValue(orderId, out var preparationError))
            {
                results.Add(new(
                    orderId,
                    ResultOrderNumber(orderId),
                    "Failed",
                    null,
                    null,
                    preparationError));
                failed++;
                if (ShouldCheckpointProgress())
                    await SaveProgressAsync(false);
                continue;
            }

            try
            {
                var order = preparedOrders[orderId];
                if (order.InvoiceDocumentId is not null)
                {
                    results.Add(new(
                        order.OrderId,
                        order.OrderNumber,
                        "AlreadyInvoiced",
                        order.InvoiceDocumentId,
                        null,
                        null));
                    completed++;
                    continue;
                }

                var paidOrder = order.PaymentStatus == "Confirmed";
                var paymentMethod = paidOrder
                    ? "Transfer"
                    : request.PaymentMethodCode;
                var paymentReference = paidOrder
                    ? $"Pago confirmado del pedido {order.OrderNumber}"
                    : request.PaymentReference;
                var creditSale = paymentMethod == "Credit";
                var documentType = request.DocumentType;
                if (request.DocumentType == PosSaleDocumentTypes.Receipt &&
                    order.CustomerRequiresElectronicInvoice)
                    documentType = PosSaleDocumentTypes.Invoice;
                var warehouseId = order.WarehouseId ?? throw new OrderConflictException(
                    "El pedido no tiene una bodega de venta asignada y no puede emitirse.");
                var source = new OnlineSalesOrderCheckoutSource(
                    lease.OperationId,
                    order.OrderId,
                    order.BusinessId,
                    warehouseId,
                    request.WorkSessionId,
                    order.CustomerId,
                    order.PartySiteId,
                    order.SnapshotVersion ?? throw new OrderConflictException(
                        "El pedido no conserva una versión transaccional válida."),
                    order.Lines.Select(line => new OnlineSalesOrderCheckoutLine(
                        line.OrderItemId,
                        line.ProductId!.Value,
                        line.ProductCode ?? line.Sku ?? string.Empty,
                        line.ProductName,
                        line.UnitCode,
                        line.TaxCode,
                        line.TaxRate,
                        line.Quantity,
                        line.UnitPrice,
                        line.DiscountAmount,
                        line.LineTotal,
                        line.DocumentUnitCost,
                        order.Currency,
                        line.PriceSource,
                        line.IsGenericProductSnapshot)).ToArray());
                var productTotal = source.Lines.Sum(line => line.PublicLineTotal);
                var requestedCharges = ChargeSelections(request);
                var charges = requestedCharges.Count == 0
                    ? []
                    : InvoiceChargeApplication.Calculate(productTotal, requestedCharges.Select((selection, index) =>
                        new InvoiceChargeSelection(
                            DeterministicGuid($"order-charge:{lease.OperationId:N}:{orderId:N}:{selection.ChargeId:N}"),
                            chargeDefinitions[index], selection.SupplierId, selection.ManualAmount)).ToArray());
                source = source with { Charges = charges.Count == 0 ? null : charges };
                var payableAmount = productTotal + charges.Sum(charge => charge.InvoicedAmount);
                var issued = await checkout.CompleteOrderAsync(
                    identity,
                    source,
                    new CompleteOnlineSalesDraftRequest(
                        1,
                        creditSale
                            ? []
                            : [
                                new OnlineSalesPayment(
                                    paymentMethod,
                                    payableAmount,
                                    paymentReference,
                                    BankAccountId: request.BankAccountId,
                                    Notes: request.PaymentNotes,
                                    RoundingAdjustment: PosPaymentRoundingPolicy.Adjustment(payableAmount))
                            ],
                        Credit: creditSale
                            ? new OnlineSalesCreditTerms(
                                PosPaymentRoundingPolicy.RoundedTotal(payableAmount))
                            : null,
                        DocumentType: documentType),
                    OperationKey(lease.OperationId, orderId, "invoice"),
                    cancellationToken);
                results.Add(new(
                    order.OrderId,
                    order.OrderNumber,
                    "Invoiced",
                    issued.Receipt.DocumentId,
                    issued.Receipt.DocumentNumber,
                    null,
                    issued.Receipt));
                completed++;
            }
            catch (Exception exception) when (IsRecoverableOrderFailure(exception))
            {
                results.Add(new(
                    orderId,
                    ResultOrderNumber(orderId),
                    "Failed",
                    null,
                    null,
                    exception.Message));
                failed++;

            }

            if (ShouldCheckpointProgress())
                await SaveProgressAsync(false);
        }

        var final = BuildResponse(false);
        await batches.SaveProgressAsync(
            actor,
            lease.OperationId,
            lease.LeaseToken,
            final,
            completed: true,
            cancellationToken);
        return final;

        InvoiceOrdersResponse BuildResponse(bool replay) =>
            new(
                lease.OperationId,
                failed == 0 && results.Count == normalizedOrders.Length
                    ? "Completed"
                    : completed == 0
                        ? "Failed"
                        : "PartiallyCompleted",
                normalizedOrders.Length,
                completed,
                failed,
                replay,
                results.ToArray());

        Task SaveProgressAsync(bool done) =>
            batches.SaveProgressAsync(
                actor,
                lease.OperationId,
                lease.LeaseToken,
                BuildResponse(false),
                done,
                cancellationToken);

        bool ShouldCheckpointProgress() =>
            results.Count < normalizedOrders.Length &&
            (failed > 0 || results.Count % ProgressCheckpointSize == 0);

        string ResultOrderNumber(Guid orderId) =>
            batchOrders.TryGetValue(orderId, out var order)
                ? order.OrderNumber
                : orderId.ToString("D");
    }

    private static OrderCreditValidationIssue MapCreditIssue(
        OnlineOrderCreditValidationIssue issue) =>
        new(
            issue.CustomerId,
            issue.CustomerName,
            issue.CustomerIdentification,
            issue.RequestedAmount,
            issue.AvailableCredit,
            issue.Reason);

    private async Task<IReadOnlyList<InvoiceChargeDefinition>> ResolveChargeDefinitionsAsync(
        OrderActor actor,
        InvoiceOrdersRequest request,
        CancellationToken cancellationToken)
    {
        var selections = ChargeSelections(request);
        if (selections.Count == 0) return [];
        return await invoiceCharges.ResolveDefinitionsForSaleAsync(
            new(actor.TenantId, actor.BusinessId, actor.UserId, actor.Permissions),
            selections.Select(value => (value.ChargeId, value.ChargeVersion)).ToArray(),
            cancellationToken);
    }

    private static IReadOnlyDictionary<Guid, decimal> CreditAmounts(
        IReadOnlyCollection<Guid> orderIds,
        IReadOnlyDictionary<Guid, OrderDetail> ordersById,
        IReadOnlyList<OrderInvoiceChargeSelection> selections,
        IReadOnlyList<InvoiceChargeDefinition> definitions) =>
        orderIds.ToDictionary(orderId => orderId, orderId =>
        {
            if (selections.Count == 0 ||
                !ordersById.TryGetValue(orderId, out var order)) return 0m;
            var productTotal = order.Lines.Sum(line => line.LineTotal);
            return InvoiceChargeApplication.Calculate(productTotal, selections.Select((selection, index) =>
                new InvoiceChargeSelection(
                    DeterministicGuid($"credit-charge:{orderId:N}:{selection.ChargeId:N}"),
                    definitions[index], selection.SupplierId, selection.ManualAmount)).ToArray())
                .Sum(charge => charge.InvoicedAmount);
        });

    private static IReadOnlyList<OrderInvoiceChargeSelection> ChargeSelections(InvoiceOrdersRequest request) =>
        request.Charges ?? (request.Charge is null ? [] : [request.Charge]);

    private static void Validate(
        OrderActor actor,
        InvoiceOrdersRequest request,
        string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        if (!actor.Permissions.Contains(OrderPermissionCodes.Invoice))
            throw new OrderForbiddenException(
                $"Permission '{OrderPermissionCodes.Invoice}' is required.");
        if (!actor.Permissions.Contains(CommercePermissionCodes.SalesCreate))
            throw new OrderForbiddenException(
                $"Permission '{CommercePermissionCodes.SalesCreate}' is required.");
        if (request.WorkSessionId == Guid.Empty ||
            request.WarehouseId == Guid.Empty ||
            request.UserId != actor.UserId ||
            request.OrderIds.Count is < 1 or > 50 ||
            request.OrderIds.Any(id => id == Guid.Empty) ||
            !PaymentMethods.Contains(request.PaymentMethodCode) ||
            request.DocumentType is not (
                PosSaleDocumentTypes.Invoice or PosSaleDocumentTypes.Receipt) ||
            request.PaymentReference?.Length > 160 ||
            request.PaymentNotes?.Length > 500)
            throw new OrderValidationException(
                "Sesión, usuario, pedidos y medio de pago válidos son obligatorios.");
        if (request.PaymentMethodCode == "Transfer" &&
            string.IsNullOrWhiteSpace(request.PaymentReference))
            throw new OrderValidationException(
                "La referencia de la transferencia es obligatoria.");
        if (request.PaymentMethodCode != "Transfer" &&
            (request.BankAccountId is not null ||
             !string.IsNullOrWhiteSpace(request.PaymentNotes)))
            throw new OrderValidationException(
                "La cuenta bancaria y la nota solo aplican a transferencias.");
        if (request.PaymentMethodCode == "Credit" &&
            !string.IsNullOrWhiteSpace(request.PaymentReference))
            throw new OrderValidationException(
                "La venta a crédito no admite una referencia de pago.");
        if (actor.WorkSessionId is not null && actor.WorkSessionId != request.WorkSessionId)
            throw new OrderForbiddenException(
                "La sesión solicitada no coincide con el dispositivo autenticado.");
        var charges = ChargeSelections(request);
        if (request.Charge is not null && request.Charges is not null ||
            charges.Count > InvoiceChargeApplication.MaximumChargesPerInvoice ||
            charges.Any(value => value.ChargeId == Guid.Empty || value.ChargeVersion < 1 || value.SupplierId == Guid.Empty) ||
            charges.Select(value => (value.ChargeId, value.ChargeVersion)).Distinct().Count() != charges.Count)
            throw new OrderValidationException("Selecciona hasta diez cargos distintos con proveedor válido.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new OrderValidationException(
                "Idempotency-Key es obligatorio y admite máximo 100 caracteres.");
    }

    private static string RequestHash(
        InvoiceOrdersRequest request,
        IReadOnlyList<Guid> orderIds)
    {
        var value = string.Join(
            "|",
            request.WorkSessionId.ToString("D"),
            request.WarehouseId.ToString("D"),
            request.UserId.ToString("D"),
            request.PaymentMethodCode,
            request.PaymentReference ?? string.Empty,
            request.BankAccountId?.ToString("D") ?? string.Empty,
            request.PaymentNotes ?? string.Empty,
            request.DocumentType,
            string.Join(",", ChargeSelections(request).Select(charge => string.Join(":",
                charge.ChargeId.ToString("D"), charge.ChargeVersion.ToString(),
                charge.SupplierId.ToString("D"),
                charge.ManualAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty))),
            string.Join(",", orderIds.Select(id => id.ToString("D"))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string OperationKey(
        Guid operationId,
        Guid orderId,
        string suffix) =>
        $"ord:{operationId:N}:{orderId:N}:{suffix}";

    private static Guid DeterministicGuid(string value) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));

    private static bool IsRecoverableOrderFailure(Exception exception) =>
        exception is OrderConflictException or
            OrderValidationException or
            OrderNotFoundException or
            OnlineSalesDraftValidationException or
            OnlineSalesDraftConcurrencyException or
            PosSaleInvalidException;
}
