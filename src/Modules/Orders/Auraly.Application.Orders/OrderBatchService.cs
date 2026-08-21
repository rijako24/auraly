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
    OrderRecoveryService recovery,
    OnlineSalesDraftService drafts,
    OnlineSalesHistoryService history,
    OnlineSalesCheckoutService checkout)
{
    private static readonly HashSet<string> PaymentMethods =
    [
        "Cash",
        "DebitCard",
        "CreditCard",
        "Transfer"
    ];

    public async Task<InvoiceOrdersResponse> InvoiceAsync(
        OrderActor actor,
        InvoiceOrdersRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Validate(actor, request, idempotencyKey);
        var normalizedOrders = request.OrderIds.Distinct().ToArray();
        var hash = RequestHash(request, normalizedOrders);
        var lease = await batches.BeginAsync(
            actor,
            request with { OrderIds = normalizedOrders },
            idempotencyKey.Trim(),
            hash,
            cancellationToken);
        if (lease.Replay is not null)
            return lease.Replay with { IsReplay = true };

        var identity = new OnlineSalesUserIdentity(
            actor.UserId,
            actor.TenantId,
            actor.Permissions);
        var results = new List<InvoiceOrderResult>(normalizedOrders.Length);
        var completed = 0;
        var failed = 0;

        foreach (var orderId in normalizedOrders)
        {
            try
            {
                var order = await orders.GetAsync(actor, orderId, cancellationToken);
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

                var draft = await drafts.OpenAsync(
                    identity,
                    new OpenOnlineSalesDraftRequest(
                        new OnlineSalesDraftContext(
                            actor.BusinessId,
                            request.WarehouseId,
                            request.WorkSessionId)),
                    cancellationToken);
                if (draft.SourceOrderId is null)
                {
                    if (draft.Lines.Count != 0)
                        throw new OrderConflictException(
                            "La venta activa tiene productos. Páusala o reiníciala antes de facturar pedidos seleccionados.");
                    var recovered = await recovery.RecoverAsync(
                        actor,
                        orderId,
                        new RecoverOrderIntoSaleRequest(
                            request.WorkSessionId,
                            request.UserId,
                            draft.DraftId,
                            draft.Version),
                        OperationKey(lease.OperationId, orderId, "recover"),
                        cancellationToken);
                    draft = draft with
                    {
                        Version = recovered.DraftVersion,
                        PayableAmount = recovered.PayableAmount,
                        SourceOrderId = orderId
                    };
                }
                else if (draft.SourceOrderId != orderId)
                {
                    throw new OrderConflictException(
                        "La venta activa contiene otro pedido pendiente de completar.");
                }

                var paymentMethod = order.PaymentStatus == "Confirmed"
                    ? "Transfer"
                    : request.PaymentMethodCode;
                var paymentReference = order.PaymentStatus == "Confirmed"
                    ? $"Pago confirmado del pedido {order.OrderNumber}"
                    : request.PaymentReference;
                var documentType = request.DocumentType;
                if (order.CustomerId is not null &&
                    request.DocumentType == PosSaleDocumentTypes.Receipt)
                {
                    var customer = await history.GetCustomerAsync(
                        identity,
                        new GetOnlineSalesCustomerRequest(
                            new OnlineSalesDraftContext(
                                actor.BusinessId,
                                request.WarehouseId,
                                request.WorkSessionId),
                            order.CustomerId.Value),
                        cancellationToken);
                    if (customer?.RequiresElectronicInvoice == true)
                        documentType = PosSaleDocumentTypes.Invoice;
                }
                var issued = await checkout.CompleteAsync(
                    identity,
                    draft.DraftId,
                    new CompleteOnlineSalesDraftRequest(
                        draft.Version,
                        [
                            new OnlineSalesPayment(
                                paymentMethod,
                                draft.PayableAmount,
                                paymentReference)
                        ],
                        DocumentType: documentType),
                    OperationKey(lease.OperationId, orderId, "invoice"),
                    cancellationToken);
                results.Add(new(
                    order.OrderId,
                    order.OrderNumber,
                    "Invoiced",
                    issued.Receipt.DocumentId,
                    issued.Receipt.DocumentNumber,
                    null));
                completed++;
            }
            catch (Exception exception) when (
                exception is OrderConflictException or
                OrderValidationException or
                OrderNotFoundException or
                OnlineSalesDraftValidationException or
                OnlineSalesDraftConcurrencyException)
            {
                results.Add(new(
                    orderId,
                    orderId.ToString("D"),
                    "Failed",
                    null,
                    null,
                    exception.Message));
                failed++;

                // A failed recovered draft must remain visible and recoverable.
                // Continuing could incorrectly mix the following order into it.
                break;
            }

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
    }

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
            request.PaymentReference?.Length > 160)
            throw new OrderValidationException(
                "Sesión, usuario, pedidos y medio de pago válidos son obligatorios.");
        if (actor.WorkSessionId is not null && actor.WorkSessionId != request.WorkSessionId)
            throw new OrderForbiddenException(
                "La sesión solicitada no coincide con el dispositivo autenticado.");
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
            request.DocumentType,
            string.Join(",", orderIds.Select(id => id.ToString("D"))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string OperationKey(
        Guid operationId,
        Guid orderId,
        string suffix) =>
        $"ord:{operationId:N}:{orderId:N}:{suffix}";
}
