using Auraly.Contracts.Dispatching;
using Auraly.BuildingBlocks.Domain.Payments;

namespace Auraly.Application.Dispatching;

public interface IDispatchDeliveryStore
{
    Task<IReadOnlyList<DispatchReasonOption>> ReasonsAsync(DispatchActorIdentity actor, string reasonType, CancellationToken ct);
    Task<DispatchExecutionDetail?> GetAsync(DispatchActorIdentity actor, Guid dispatchId, CancellationToken ct);
    Task<DispatchExecutionDetail> RecordAsync(DispatchActorIdentity actor, Guid dispatchId, RecordDispatchDeliveryRequest request, CancellationToken ct);
    Task<DispatchExecutionDetail> ReorderAsync(DispatchActorIdentity actor, Guid dispatchId, ReorderDispatchDocumentsRequest request, byte[] rowVersion, CancellationToken ct);
    Task<DispatchExecutionDetail> RecordExpenseAsync(DispatchActorIdentity actor, Guid dispatchId, DispatchExpenseInput request, CancellationToken ct);
    Task<DispatchExecutionDetail> ReviewExpenseAsync(DispatchActorIdentity actor, Guid dispatchId, Guid expenseId, ReviewDispatchExpenseRequest request, CancellationToken ct);
    Task<DispatchExecutionDetail> CloseRouteAsync(DispatchActorIdentity actor, Guid dispatchId, CloseDispatchRouteRequest request, CancellationToken ct);
    Task<DispatchExecutionDetail> SettleAsync(DispatchActorIdentity actor, Guid dispatchId, SettleDispatchRequest request, CancellationToken ct);
    Task<DispatchPaymentReviewResult> VerifyPaymentAsync(DispatchActorIdentity actor, Guid dispatchId, Guid paymentId, VerifyDispatchPaymentRequest request, CancellationToken ct);
    Task<DispatchPaymentReviewResult> CorrectPaymentAsync(DispatchActorIdentity actor, Guid dispatchId, Guid paymentId, CorrectDispatchPaymentRequest request, CancellationToken ct);
}

public sealed class DispatchDeliveryService(IDispatchDeliveryStore store)
{
    public Task<IReadOnlyList<DispatchReasonOption>> ReasonsAsync(DispatchActorIdentity actor, string reasonType, CancellationToken ct)
    {
        RequireAny(actor, DispatchPermissionCodes.Read, DispatchPermissionCodes.ExecuteDeliveries, DispatchPermissionCodes.Settle);
        if (reasonType is not ("NotDelivered" or "DeliveryReturn")) throw new DispatchValidationException("El tipo de motivo no es válido.");
        return store.ReasonsAsync(actor, reasonType, ct);
    }

    public async Task<DispatchExecutionDetail> GetAsync(DispatchActorIdentity actor, Guid dispatchId, CancellationToken ct)
    {
        RequireAny(actor, DispatchPermissionCodes.Read, DispatchPermissionCodes.ExecuteDeliveries, DispatchPermissionCodes.Settle);
        Required(dispatchId, "DispatchId");
        return await store.GetAsync(actor, dispatchId, ct)
            ?? throw new DispatchNotFoundException("El despacho no está asignado o no existe.");
    }

    public Task<DispatchExecutionDetail> RecordAsync(DispatchActorIdentity actor, Guid dispatchId, RecordDispatchDeliveryRequest request, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.ExecuteDeliveries);
        Required(dispatchId, "DispatchId"); Required(request.DispatchSourceDocumentId, "DispatchSourceDocumentId");
        Idempotency(request.IdempotencyKey);
        if (request.DeliveryStatus is not (DeliveryStatuses.Delivered or DeliveryStatuses.PartiallyDelivered or DeliveryStatuses.NotDelivered))
            throw new DispatchValidationException("El estado de la entrega no es válido.");
        if (request.OccurredAt == default) throw new DispatchValidationException("Se requiere la fecha de la operación.");
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
            throw new DispatchValidationException("Las coordenadas de la entrega no son válidas.");
        var reason = Text(request.Reason, 160);
        if (request.DeliveryStatus != DeliveryStatuses.Delivered && reason is null)
            throw new DispatchValidationException("Indica un motivo para una entrega parcial o fallida.");
        if (request.DeliveryStatus == DeliveryStatuses.NotDelivered && (request.Payments.Count > 0 || request.Returns.Count > 0))
            throw new DispatchValidationException("Una entrega no realizada no puede incluir pagos ni mercancía devuelta.");
        if (request.Payments.Count > DispatchLimits.MaximumPaymentsPerDocument || request.Returns.Count > 500)
            throw new DispatchValidationException("El resultado de entrega supera la cantidad de detalles permitida.");
        var payments = request.Payments.Select(payment => payment with {
            Reference = Text(payment.Reference, PaymentTenderBreakdown.MaximumReferenceLength),
            EvidenceUrl = Text(payment.EvidenceUrl, 1000),
            CardFranchiseCode = Text(payment.CardFranchiseCode, PaymentTenderBreakdown.MaximumCardFranchiseLength),
            ApprovalNumber = Text(payment.ApprovalNumber, PaymentTenderBreakdown.MaximumApprovalLength) }).ToArray();
        foreach (var payment in payments) ValidatePayment(payment);
        var money = payments.Where(value => value.ApplicationType != DeliveryPaymentApplications.CreditDocument).ToArray();
        if (money.Any(value => value.PaymentMethodId.HasValue))
        {
            try { PaymentTenderBreakdown.Create(money.Select(value => new PaymentTender(value.PaymentMethod ?? "", value.Amount,
                Reference: value.Reference, CardFranchiseCode: value.CardFranchiseCode, ApprovalNumber: value.ApprovalNumber,
                PaymentMethodId: value.PaymentMethodId, PaymentMethodVersion: value.PaymentMethodVersion)),
                money.Sum(value => value.Amount), 1, PortfolioPaymentMethods.Supported); }
            catch (ArgumentException ex) { throw new DispatchValidationException(ex.Message); }
        }
        if (request.Returns.Select(value => value.OriginalLineNumber).Distinct().Count() != request.Returns.Count)
            throw new DispatchValidationException("Una línea devuelta no puede repetirse.");
        foreach (var value in request.Returns)
        {
            if (value.OriginalLineNumber <= 0 || value.Quantity <= 0) throw new DispatchValidationException("La línea original y la cantidad devuelta deben ser mayores que cero.");
            if (value.InventoryDisposition is not ("Sellable" or "NotReturned")) throw new DispatchValidationException("El tratamiento de la devolución no es válido.");
            if (string.IsNullOrWhiteSpace(value.ReasonCode) || string.IsNullOrWhiteSpace(value.ReasonDescription) || value.ReasonDescription.Trim().Length > 300)
                throw new DispatchValidationException("Cada producto devuelto requiere un motivo.");
        }
        return store.RecordAsync(actor, dispatchId, request with { Payments = payments, Reason = reason, Notes = Text(request.Notes, 500), IdempotencyKey = request.IdempotencyKey.Trim() }, ct);
    }

    public Task<DispatchExecutionDetail> ReorderAsync(DispatchActorIdentity actor, Guid dispatchId, ReorderDispatchDocumentsRequest request, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.ExecuteDeliveries); Required(dispatchId, "DispatchId");
        if (request.OrderedDocumentIds.Count is 0 or >DispatchLimits.MaximumDocuments || request.OrderedDocumentIds.Any(id => id == Guid.Empty) || request.OrderedDocumentIds.Distinct().Count() != request.OrderedDocumentIds.Count)
            throw new DispatchValidationException("El orden del despacho debe contener documentos válidos, sin repetirlos.");
        return store.ReorderAsync(actor, dispatchId, request, RowVersion(request.RowVersion), ct);
    }

    public Task<DispatchExecutionDetail> CloseRouteAsync(DispatchActorIdentity actor, Guid dispatchId, CloseDispatchRouteRequest request, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.ExecuteDeliveries); Required(dispatchId, "DispatchId"); Idempotency(request.IdempotencyKey);
        if (request.DeclaredCash < 0 || request.DeclaredCard is null or < 0 || request.DeclaredTransfer is null or < 0)
            throw new DispatchValidationException("Declara el efectivo, las tarjetas y las transferencias con valores iguales o mayores que cero.");
        return store.CloseRouteAsync(actor, dispatchId, request with { DifferenceReason = null, IdempotencyKey = request.IdempotencyKey.Trim() }, ct);
    }

    public Task<DispatchExecutionDetail> RecordExpenseAsync(DispatchActorIdentity actor, Guid dispatchId, DispatchExpenseInput request, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.ExecuteDeliveries); Required(dispatchId, "DispatchId"); Idempotency(request.IdempotencyKey);
        if (request.Amount <= 0) throw new DispatchValidationException("El valor del gasto debe ser mayor que cero.");
        if (request.OccurredAt == default) throw new DispatchValidationException("Se requiere la fecha de la operación.");
        var category = Text(request.Category, 120) ?? throw new DispatchValidationException("Selecciona un concepto para el gasto.");
        var description = Text(request.Description, 300);
        var evidence = Text(request.EvidenceUrl, 1000);
        return store.RecordExpenseAsync(actor, dispatchId, request with { Category = category, Description = description, EvidenceUrl = evidence, IdempotencyKey = request.IdempotencyKey.Trim() }, ct);
    }

    public Task<DispatchExecutionDetail> ReviewExpenseAsync(DispatchActorIdentity actor, Guid dispatchId, Guid expenseId, ReviewDispatchExpenseRequest request, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.Settle); Required(dispatchId, "DispatchId"); Required(expenseId, "ExpenseId"); Idempotency(request.IdempotencyKey);
        if (request.Decision is not ("Approved" or "Rejected")) throw new DispatchValidationException("La decisión sobre el gasto no es válida.");
        if (request.Decision == "Approved") throw new DispatchValidationException("Aprueba el reporte desde el formulario de Gastos para registrar su tercero, concepto y soporte.");
        if (request.Decision == "Rejected" && request.ApprovedAmount is not null and not 0) throw new DispatchValidationException("Un gasto rechazado no puede tener un valor aprobado.");
        var notes=Text(request.Notes,500)??throw new DispatchValidationException("Indica el motivo del rechazo del gasto.");
        return store.ReviewExpenseAsync(actor, dispatchId, expenseId, request with { Notes = notes, IdempotencyKey = request.IdempotencyKey.Trim() }, ct);
    }

    public Task<DispatchExecutionDetail> SettleAsync(DispatchActorIdentity actor, Guid dispatchId, SettleDispatchRequest request, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.Settle); Required(dispatchId, "DispatchId"); Idempotency(request.IdempotencyKey);
        if(request.CashDifferencePaymentMethodId==Guid.Empty) throw new DispatchValidationException("Selecciona una forma de efectivo válida para la diferencia.");
        if (request.CashReceived < 0) throw new DispatchValidationException("El efectivo recibido no puede ser negativo.");
        var returnIds=request.ReturnIds ?? [];
        if(returnIds.Count>DispatchLimits.MaximumDocuments || returnIds.Any(id=>id==Guid.Empty) || returnIds.Distinct().Count()!=returnIds.Count)
            throw new DispatchValidationException("Selecciona devoluciones válidas, sin repetirlas, hasta un máximo de 500.");
        return store.SettleAsync(actor, dispatchId, request with { Notes = Text(request.Notes, 500), IdempotencyKey = request.IdempotencyKey.Trim(),
            ReturnIds=returnIds.Count==0?null:returnIds.Order().ToArray() }, ct);
    }

    public Task<DispatchPaymentReviewResult> VerifyPaymentAsync(DispatchActorIdentity actor, Guid dispatchId,
        Guid paymentId, VerifyDispatchPaymentRequest request, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.Settle); Required(dispatchId, "DispatchId"); Required(paymentId, "PaymentId");
        return store.VerifyPaymentAsync(actor, dispatchId, paymentId, request, ct);
    }

    public Task<DispatchPaymentReviewResult> CorrectPaymentAsync(DispatchActorIdentity actor, Guid dispatchId,
        Guid paymentId, CorrectDispatchPaymentRequest request, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.Settle); Required(dispatchId, "DispatchId"); Required(paymentId, "PaymentId");
        Required(request.PaymentMethodId, "PaymentMethodId");
        ValidateNativeTender(new(request.MethodCode, 1, Reference: request.Reference,
            CardFranchiseCode: request.CardFranchiseCode, ApprovalNumber: request.ApprovalNumber,
            PaymentMethodId: request.PaymentMethodId, PaymentMethodVersion: request.PaymentMethodVersion));
        var reason = Text(request.Reason, 500) ?? throw new DispatchValidationException("Explica el motivo de la corrección.");
        return store.CorrectPaymentAsync(actor, dispatchId, paymentId, request with {
            Reference = Text(request.Reference, 120), CardFranchiseCode = Text(request.CardFranchiseCode, 40),
            ApprovalNumber = Text(request.ApprovalNumber, 80), Reason = reason }, ct);
    }

    private static void ValidateNativeTender(PaymentTender tender)
    {
        try { PaymentTenderBreakdown.Create([tender], tender.Amount, 1, PortfolioPaymentMethods.Supported); }
        catch (ArgumentException ex) { throw new DispatchValidationException(ex.Message); }
    }

    private static void ValidatePayment(DispatchDeliveryPaymentInput value)
    {
        if (value.ApplicationType == DeliveryPaymentApplications.CreditDocument)
        {
            if (value.Amount != 0 || value.PaymentMethod is not null || value.PaymentMethodId.HasValue || value.PaymentMethodVersion.HasValue
                || value.CardFranchiseCode is not null || value.ApprovalNumber is not null || string.IsNullOrWhiteSpace(value.EvidenceUrl))
                throw new DispatchValidationException("Una entrega a crédito requiere la factura firmada; el documento a crédito no puede registrarse como dinero recibido.");
            return;
        }
        if (value.ApplicationType is not (DeliveryPaymentApplications.InvoicePayment or DeliveryPaymentApplications.CreditAdvance) || value.Amount <= 0)
            throw new DispatchValidationException("La aplicación del pago no es válida.");
        if (!value.PaymentMethodId.HasValue && value.PaymentMethod is not ("Cash" or "Deposit"))
            throw new DispatchValidationException("Selecciona una forma de pago configurada para recibir el dinero.");
        if (value.Reference?.Trim().Length > 120 || value.EvidenceUrl?.Trim().Length > 1000) throw new DispatchValidationException("Los datos del soporte de pago superan la longitud permitida.");
    }

    private static string? Text(string? value, int max) { var text=value?.Trim(); if(string.IsNullOrEmpty(text))return null; if(text.Length>max)throw new DispatchValidationException($"El texto admite máximo {max} caracteres."); return text; }
    private static void Required(Guid value,string field){if(value==Guid.Empty)throw new DispatchValidationException($"El campo {field} es obligatorio.");}
    private static void Idempotency(string value){if(string.IsNullOrWhiteSpace(value)||value.Trim().Length>128)throw new DispatchValidationException("No se pudo identificar la operación. Vuelve a intentarlo con la misma solicitud.");}
    private static byte[] RowVersion(string value){try{var bytes=Convert.FromBase64String(value);if(bytes.Length!=8)throw new FormatException();return bytes;}catch(FormatException){throw new DispatchValidationException("La versión del documento no es válida. Vuelve a abrirlo antes de continuar.");}}
    private static void Require(DispatchActorIdentity actor,string permission){if(!actor.Permissions.Contains(permission))throw new DispatchForbiddenException($"No tienes permiso para realizar esta acción.");}
    private static void RequireAny(DispatchActorIdentity actor,params string[] permissions){if(!permissions.Any(actor.Permissions.Contains))throw new DispatchForbiddenException("No tienes permiso para registrar entregas de despachos.");}
}
