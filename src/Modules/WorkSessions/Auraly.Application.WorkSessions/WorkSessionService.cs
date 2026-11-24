using Auraly.Application.DocumentProcessing;
using Auraly.Contracts.Authorization;
using Auraly.Domain.WorkSessions;
using Auraly.Contracts.WorkSessions;
using Auraly.Contracts.Dispatching;
using Auraly.Pos.Printing;
using Auraly.Commerce.Accounting.Application;

namespace Auraly.Application.WorkSessions;

public interface IWorkSessionStore
{
    Task<IReadOnlyList<DeviceWorkSessionSnapshot>> ReadOpenForEnrollmentAsync(
        Guid tenantId, Guid businessId, Guid deviceId, CancellationToken cancellationToken);

    Task<WorkSessionView?> CurrentAsync(
        WorkSessionIdentity identity,
        CancellationToken cancellationToken);

    Task<WorkSessionView?> CurrentForDeviceAsync(
        WorkSessionIdentity identity,
        Guid businessId,
        Guid deviceId,
        CancellationToken cancellationToken);

    Task<WorkSessionView> OpenOrResumeAsync(
        WorkSessionIdentity identity,
        OpenWorkSessionRequest request,
        CancellationToken cancellationToken);

    Task<WorkSessionView> RegisterDeviceOpenAsync(
        WorkSessionIdentity identity,
        RegisterDeviceWorkSessionRequest request,
        Guid deviceId,
        CancellationToken cancellationToken);

    Task<WorkSessionClosureView> CloseAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        string idempotencyKey,
        CloseWorkSessionRequest request,
        CancellationToken cancellationToken);

    Task<WorkSessionClosureView?> GetClosureAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken);

    Task<WorkSessionClosurePreviewView> PreviewClosureAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken);
    Task<bool> HasPausedSalesAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkSessionCashDifferenceView>> ListCashDifferencesAsync(
        WorkSessionIdentity identity,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
    Task<WorkSessionClosurePage> ListClosuresAsync(
        WorkSessionIdentity identity, DateOnly from, DateOnly to, string? status,
        int page, int pageSize, CancellationToken cancellationToken, Guid? closureId = null);
    Task<CashClosureInboxPage> ListClosureInboxAsync(
        WorkSessionIdentity identity, DateOnly from, DateOnly to, string? status,
        int page, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkSessionPaymentVerificationItem>> ListClosurePaymentVerificationsAsync(
        WorkSessionIdentity identity, Guid closureId, CancellationToken cancellationToken);
    Task<WorkSessionPaymentVerificationPage> ListClosurePaymentVerificationPageAsync(
        WorkSessionIdentity identity, Guid closureId, string? paymentMethodCode,
        string? movementType, int page, int pageSize, CancellationToken cancellationToken);
    Task<WorkSessionInvoiceChargePage> ListClosureInvoiceChargesAsync(
        WorkSessionIdentity identity, Guid closureId, string? paymentMethodCode,
        int page, int pageSize, CancellationToken cancellationToken);
    Task<WorkSessionClosureReconciliationView> ReconcileClosureAsync(
        WorkSessionIdentity identity, Guid closureId, string idempotencyKey,
        ReconcileWorkSessionClosureRequest request, CancellationToken cancellationToken);
    Task ValidatePaymentCorrectionsAsync(WorkSessionIdentity identity, Guid closureId,
        IReadOnlyList<WorkSessionPaymentCorrection> corrections, CancellationToken cancellationToken);
    Task<SalePaymentCorrectionContext> GetSalePaymentCorrectionAsync(WorkSessionIdentity identity,
        Guid documentId, CancellationToken cancellationToken);
    Task<SalePaymentCorrectionAcceptance> CorrectSalePaymentAsync(WorkSessionIdentity identity,
        Guid documentId, CorrectSalePaymentRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<CashMovementReasonView>> ListCashReasonsAsync(
        WorkSessionIdentity identity,
        Guid businessId,
        string? direction,
        CancellationToken cancellationToken);

    Task<CashMovementReasonView> UpsertCashReasonAsync(
        WorkSessionIdentity identity,
        CashMovementReasonDefinition reason,
        CancellationToken cancellationToken);

    Task<CashMovementReasonDefinition?> FindCashReasonAsync(
        WorkSessionIdentity identity,
        Guid businessId,
        Guid reasonId,
        CancellationToken cancellationToken);

    Task<CashMovementAcceptance> AcceptCashMovementAsync(
        WorkSessionIdentity identity,
        string idempotencyKey,
        CashMovement movement,
        CancellationToken cancellationToken);
}

public sealed class WorkSessionService(
    IWorkSessionStore store,
    IDocumentProcessingSignalPublisher signalPublisher,
    AccountingProcessingCoordinator accountingProcessing)
{
    public Task<WorkSessionView?> CurrentAsync(
        WorkSessionIdentity identity,
        CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.Read);
        if (identity.BusinessId is not { } businessId || businessId == Guid.Empty)
            throw new WorkSessionForbiddenException(
                "El contexto de trabajo no tiene una sede válida. Vuelve a seleccionar la sede.");
        return store.CurrentAsync(identity, cancellationToken);
    }

    public async Task RequireActiveWebSessionAsync(
        WorkSessionIdentity identity, Guid? workSessionId,
        CancellationToken cancellationToken = default)
    {
        if (workSessionId is null || workSessionId == Guid.Empty)
            throw new WorkSessionForbiddenException("El pago de caja requiere una sesión abierta.");
        var current = await CurrentAsync(identity, cancellationToken);
        if (current?.WorkSessionId != workSessionId)
            throw new WorkSessionForbiddenException("El pago no corresponde a la sesión de caja activa.");
    }

    public async Task<WorkSessionView> RequireActiveDeviceSessionAsync(
        Guid userId, Guid tenantId, Guid businessId, Guid deviceId,
        Guid workSessionId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || tenantId == Guid.Empty || businessId == Guid.Empty ||
            deviceId == Guid.Empty || workSessionId == Guid.Empty)
            throw new WorkSessionForbiddenException("El dispositivo no identificó una sesión de trabajo válida.");
        var session = await store.CurrentForDeviceAsync(
            new WorkSessionIdentity(userId, tenantId, new HashSet<string>(), businessId),
            businessId, deviceId, cancellationToken);
        if (session is null || session.WorkSessionId != workSessionId)
            throw new WorkSessionForbiddenException("La sesión de trabajo del dispositivo no está abierta.");
        return session;
    }

    public async Task<WorkSessionView> OpenOrResumeAsync(
        WorkSessionIdentity identity,
        OpenWorkSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateUserIdentity(identity);
        ValidateOpen(request);
        if (identity.BusinessId != request.BusinessId)
            throw new WorkSessionForbiddenException(
                "La empresa solicitada no corresponde al contexto autorizado.");
        return await store.OpenOrResumeAsync(
            identity,
            request with { WarehouseId = null, DeviceId = null },
            cancellationToken);
    }

    public Task<WorkSessionView> RegisterDeviceOpenAsync(
        WorkSessionIdentity identity,
        RegisterDeviceWorkSessionRequest request,
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        ValidateDeviceIdentity(identity);
        if (request.UserId == Guid.Empty || request.UserId != identity.UserId)
            throw new WorkSessionForbiddenException(
                "El usuario local no corresponde a la sesión de caja solicitada.");
        if (request.WorkSessionId == Guid.Empty || request.BusinessId == Guid.Empty ||
            request.OpenedAt == default || deviceId == Guid.Empty)
            throw new WorkSessionValidationException(
                "Se requiere una sesión de caja, una sede, una fecha de apertura y un dispositivo válidos.");
        return store.RegisterDeviceOpenAsync(
            identity, request, deviceId, cancellationToken);
    }

    private static void ValidateOpen(OpenWorkSessionRequest request)
    {
        if (request.BusinessId == Guid.Empty)
            throw new WorkSessionValidationException(
                "Se requiere una sede válida.");
        if (request.DeviceId == Guid.Empty)
            throw new WorkSessionValidationException(
                "El dispositivo debe estar vacío o tener una identificación válida.");
        if (request.OpeningCash < 0)
            throw new WorkSessionValidationException(
                "El efectivo de apertura no puede ser negativo.");
    }

    public async Task<WorkSessionClosureView> CloseAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        string idempotencyKey,
        CloseWorkSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(identity, await RequiredClosePermissionAsync(
            identity, workSessionId, cancellationToken));
        return await CloseCoreAsync(
            identity, workSessionId, idempotencyKey, request, cancellationToken);
    }

    public async Task<string> RequiredClosePermissionAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken = default)
    {
        if (workSessionId == Guid.Empty)
            throw new WorkSessionValidationException("Se requiere una sesión de caja válida.");
        return await store.HasPausedSalesAsync(identity, workSessionId, cancellationToken)
            ? WorkSessionPermissionCodes.CloseWithPausedSales
            : WorkSessionPermissionCodes.Close;
    }

    public Task<WorkSessionClosureView> CloseFromDeviceAsync(
        WorkSessionIdentity deviceIdentity,
        Guid workSessionId,
        string idempotencyKey,
        CloseWorkSessionRequest request,
        CancellationToken cancellationToken = default) =>
        CloseCoreAsync(
            deviceIdentity, workSessionId, idempotencyKey, request, cancellationToken);

    private async Task<WorkSessionClosureView> CloseCoreAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        string idempotencyKey,
        CloseWorkSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (workSessionId == Guid.Empty)
            throw new WorkSessionValidationException("Se requiere una sesión de caja válida.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            throw new WorkSessionValidationException(
                "Se requiere una clave válida para identificar la operación.");
        if (request.CountedCash < 0)
            throw new WorkSessionValidationException(
                "El efectivo contado no puede ser negativo.");
        if (request.PaymentCounts is not null &&
            (request.PaymentCounts.Any(value =>
                string.IsNullOrWhiteSpace(value.PaymentMethodCode) ||
                value.PaymentMethodCode.Trim().Length > 32 ||
                value.CountedAmount < 0) ||
             request.PaymentCounts.Select(value => value.PaymentMethodCode.Trim())
                 .Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.PaymentCounts.Count))
            throw new WorkSessionValidationException(
                "Cada forma de pago requiere un único valor contado válido, mayor o igual que cero.");
        if (request.ClosedByUserId == Guid.Empty)
            throw new WorkSessionValidationException(
                "La identificación del supervisor no es válida.");
        if (request.Note?.Trim().Length > 500)
            throw new WorkSessionValidationException(
                "La observación de cierre admite máximo 500 caracteres.");
        var closure = await store.CloseAsync(
            identity,
            workSessionId,
            idempotencyKey.Trim(),
            request with
            {
                Note = NullIfWhiteSpace(request.Note),
                ReceiptTemplateVersion = PosPrintTemplateCatalog.WorkSessionClosure.Version
            },
            cancellationToken);
        if (closure.PaymentTotals.Any(value => value.Difference is not null and not 0))
            await accountingProcessing.RequestPostingAsync(
                closure.BusinessId,
                closure.WorkSessionClosureId,
                WorkSessionAccountingDocumentTypes.CashDifference,
                cancellationToken);
        return closure;
    }

    public Task<WorkSessionClosureView?> GetClosureAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken = default)
    {
        if (!identity.Permissions.Contains(WorkSessionPermissionCodes.ReadCashDifferences))
            Demand(identity, WorkSessionPermissionCodes.Read);
        if (workSessionId == Guid.Empty)
            throw new WorkSessionValidationException("Se requiere una sesión de caja válida.");
        return store.GetClosureAsync(identity, workSessionId, cancellationToken);
    }

    public Task<WorkSessionClosureView?> GetClosureFromDeviceAsync(
        WorkSessionIdentity deviceIdentity,
        Guid workSessionId,
        CancellationToken cancellationToken = default)
    {
        if (workSessionId == Guid.Empty)
            throw new WorkSessionValidationException("Se requiere una sesión de caja válida.");
        return store.GetClosureAsync(deviceIdentity, workSessionId, cancellationToken);
    }

    public Task<WorkSessionClosurePreviewView> PreviewClosureAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken = default)
    {
        // The count must remain blind, but opening the non-mutating preview is
        // part of the cashier workflow. Supervisor approval belongs to the
        // final close, where the sensitive state transition actually occurs.
        Demand(identity, WorkSessionPermissionCodes.Read);
        if (workSessionId == Guid.Empty)
            throw new WorkSessionValidationException("Se requiere una sesión de caja válida.");
        return store.PreviewClosureAsync(identity, workSessionId, cancellationToken);
    }

    public Task<WorkSessionClosurePreviewView> PreviewClosureFromDeviceAsync(
        WorkSessionIdentity deviceIdentity,
        Guid workSessionId,
        CancellationToken cancellationToken = default)
    {
        if (workSessionId == Guid.Empty)
            throw new WorkSessionValidationException("Se requiere una sesión de caja válida.");
        return store.PreviewClosureAsync(
            deviceIdentity, workSessionId, cancellationToken);
    }

    public Task<IReadOnlyList<WorkSessionCashDifferenceView>> ListCashDifferencesAsync(
        WorkSessionIdentity identity,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.ReadCashDifferences);
        if (from == default || to == default || from > to || to.DayNumber - from.DayNumber > 366)
            throw new WorkSessionValidationException(
                "El rango de diferencias de caja admite máximo 367 días.");
        return store.ListCashDifferencesAsync(identity, from, to, cancellationToken);
    }

    public Task<WorkSessionClosurePage> ListClosuresAsync(
        WorkSessionIdentity identity, DateOnly from, DateOnly to, string? status,
        int page, int pageSize, CancellationToken cancellationToken = default, Guid? closureId = null)
    {
        Demand(identity, WorkSessionPermissionCodes.ReadCashDifferences);
        ValidateClosureSearch(from, to, status, page, pageSize);
        return store.ListClosuresAsync(identity,from,to,status,page,pageSize,cancellationToken,closureId);
    }

    public Task<CashClosureInboxPage> ListClosureInboxAsync(
        WorkSessionIdentity identity, DateOnly from, DateOnly to, string? status,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!identity.Permissions.Contains(WorkSessionPermissionCodes.ReadCashDifferences)
            && !identity.Permissions.Contains(DispatchPermissionCodes.Settle))
            throw new WorkSessionForbiddenException("No tienes permiso para consultar cierres.");
        ValidateClosureSearch(from, to, status, page, pageSize);
        return store.ListClosureInboxAsync(identity, from, to, status, page, pageSize, cancellationToken);
    }

    private static void ValidateClosureSearch(DateOnly from, DateOnly to, string? status, int page, int pageSize)
    {
        if (from == default || to == default || from > to || to.DayNumber - from.DayNumber > 366
            || to == DateOnly.MaxValue)
            throw new WorkSessionValidationException("El rango de cierres debe contener máximo 367 días.");
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            throw new WorkSessionValidationException("La paginación de cierres no es válida.");
        if (status is not null && status is not ("Pending" or "Partial" or "Reconciled" or "ReconciledWithDifferences"))
            throw new WorkSessionValidationException("El estado de conciliación no es válido.");
    }

    public Task<IReadOnlyList<WorkSessionPaymentVerificationItem>> ListClosurePaymentVerificationsAsync(
        WorkSessionIdentity identity, Guid closureId, CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.ReadCashDifferences);
        if (closureId == Guid.Empty)
            throw new WorkSessionValidationException("El cierre es obligatorio.");
        return store.ListClosurePaymentVerificationsAsync(identity, closureId, cancellationToken);
    }

    public Task<WorkSessionPaymentVerificationPage> ListClosurePaymentVerificationPageAsync(
        WorkSessionIdentity identity, Guid closureId, string? paymentMethodCode,
        string? movementType, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.ReadCashDifferences);
        if (closureId == Guid.Empty || page < 1 || pageSize is < 1 or > 100 ||
            page > int.MaxValue / pageSize ||
            (paymentMethodCode is not null && (paymentMethodCode.Length is < 1 or > 32 ||
                paymentMethodCode.Any(character => !char.IsLetterOrDigit(character)))) ||
            (movementType is not null && (movementType.Length is < 1 or > 40 ||
                movementType.Any(character => !char.IsLetterOrDigit(character)))))
            throw new WorkSessionValidationException("El cierre, filtro o paginación de comprobantes no es válido.");
        return store.ListClosurePaymentVerificationPageAsync(identity, closureId,
            paymentMethodCode, movementType, page, pageSize, cancellationToken);
    }

    public Task<WorkSessionInvoiceChargePage> ListClosureInvoiceChargesAsync(
        WorkSessionIdentity identity, Guid closureId, string? paymentMethodCode,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.ReadCashDifferences);
        if (closureId == Guid.Empty || page < 1 || pageSize is < 1 or > 100 ||
            page > int.MaxValue / pageSize || paymentMethodCode is not null &&
            (paymentMethodCode.Length is < 1 or > 32 || paymentMethodCode.Any(character => !char.IsLetterOrDigit(character))))
            throw new WorkSessionValidationException("El cierre, medio o paginación de cargos no es válido.");
        return store.ListClosureInvoiceChargesAsync(identity, closureId, paymentMethodCode,
            page, pageSize, cancellationToken);
    }

    public async Task<WorkSessionClosureReconciliationView> ReconcileClosureAsync(
        WorkSessionIdentity identity, Guid closureId, string idempotencyKey,
        ReconcileWorkSessionClosureRequest request, CancellationToken cancellationToken = default)
    {
        Demand(identity,WorkSessionPermissionCodes.ReconcileClosures);
        if(request.PaymentCorrections is { Count: > 0 } || request.Reclassifications is { Count: > 0 })
            Demand(identity,CommercePermissionCodes.SalesCorrectPaymentMethods);
        if (closureId==Guid.Empty || string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length>128)
            throw new WorkSessionValidationException("El cierre y la clave idempotente son obligatorios.");
        if (request.Lines is null || request.Reclassifications is null || request.Lines.Count==0 || request.Lines.Any(line=>string.IsNullOrWhiteSpace(line.PaymentMethodCode) ||
                (line.PaymentMethodCode.Equals("Cash",StringComparison.OrdinalIgnoreCase) && line.VerifiedAmount<0)) ||
            request.Reclassifications.Any(line=>line.Amount<=0) || request.Note?.Trim().Length>500)
            throw new WorkSessionValidationException("Los valores de conciliación no son válidos.");
        if (request.ChargeCashShortageToEmployee && string.IsNullOrWhiteSpace(request.Note))
            throw new WorkSessionValidationException("Describe el faltante antes de registrarlo a cargo del empleado.");
        if (request.PaymentVerifications?.Any(item => string.IsNullOrWhiteSpace(item.VerificationKey) ||
                item.VerificationKey.Length > 200 ||
                item.Status is not ("Verified" or "Missing")) == true)
            throw new WorkSessionValidationException("La verificación individual de pagos no es válida.");
        var manual = request.ManualMovements ?? [];
        if (manual.Count > 100 || manual.Select(item => item.MovementId).Distinct().Count() != manual.Count ||
            manual.Any(item => item.MovementId == Guid.Empty ||
                string.IsNullOrWhiteSpace(item.PaymentMethodCode) || item.PaymentMethodCode.Length > 32 ||
                item.PaymentMethodCode.Any(character => !char.IsLetterOrDigit(character)) ||
                item.MovementType is not ("Sale" or "Refund" or "ReceivablePayment" or
                    "PayablePayment" or "CashIn" or "CashOut") ||
                item.Amount <= 0 || item.Amount >= 1_000_000_000_000_000m ||
                decimal.Round(item.Amount, 4) != item.Amount ||
                string.IsNullOrWhiteSpace(item.Observation) || item.Observation.Trim().Length > 500))
            throw new WorkSessionValidationException("Los movimientos manuales de conciliación no son válidos.");
        ValidatePaymentCorrections(request.PaymentCorrections ?? []);
        var result=await store.ReconcileClosureAsync(identity,closureId,idempotencyKey.Trim(),request with { Note=NullIfWhiteSpace(request.Note) },cancellationToken);
        if (!result.IdempotentReplay && result.AccountingStatus == "Pending")
            await accountingProcessing.RequestPostingAsync(result.BusinessId,result.ReconciliationId,
                WorkSessionAccountingDocumentTypes.ClosureReconciliation,cancellationToken);
        return result;
    }

    public Task ValidatePaymentCorrectionsAsync(WorkSessionIdentity identity, Guid closureId,
        IReadOnlyList<WorkSessionPaymentCorrection> corrections, CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.ReconcileClosures);
        Demand(identity, CommercePermissionCodes.SalesCorrectPaymentMethods);
        if (closureId == Guid.Empty || corrections.Count == 0)
            throw new WorkSessionValidationException("Selecciona un comprobante del cierre para corregir.");
        ValidatePaymentCorrections(corrections);
        return store.ValidatePaymentCorrectionsAsync(identity, closureId, corrections, cancellationToken);
    }

    public Task<SalePaymentCorrectionContext> GetSalePaymentCorrectionAsync(WorkSessionIdentity identity,
        Guid documentId, CancellationToken cancellationToken = default)
    {
        Demand(identity, CommercePermissionCodes.SalesCorrectPaymentMethods);
        if (documentId == Guid.Empty)
            throw new WorkSessionValidationException("Selecciona el comprobante y su sede para corregir el recaudo.");
        return store.GetSalePaymentCorrectionAsync(identity, documentId, cancellationToken);
    }

    public async Task<SalePaymentCorrectionAcceptance> CorrectSalePaymentAsync(WorkSessionIdentity identity,
        Guid documentId, CorrectSalePaymentRequest request, CancellationToken cancellationToken = default)
    {
        Demand(identity, CommercePermissionCodes.SalesCorrectPaymentMethods);
        if (documentId == Guid.Empty || request.CorrectionId == Guid.Empty || request.CorrectionId == documentId
            || request.Correction is null || request.Correction.PaymentMethodId is null)
            throw new WorkSessionValidationException("Selecciona el comprobante, su sede y una forma de pago configurada.");
        ValidatePaymentCorrections([request.Correction]);
        var result = await store.CorrectSalePaymentAsync(identity, documentId, request, cancellationToken);
        if (!result.IdempotentReplay && result.AccountingStatus == "Pending")
            await accountingProcessing.RequestPostingAsync(result.BusinessId, result.CorrectionId,
                WorkSessionAccountingDocumentTypes.ClosureReconciliation, cancellationToken);
        return result;
    }

    private static void ValidatePaymentCorrections(IReadOnlyList<WorkSessionPaymentCorrection> corrections)
    {
        if (corrections.Count(item => item.PaymentMethodId is not null) > 500 || corrections.Any(item =>
                string.IsNullOrWhiteSpace(item.VerificationKey) || item.VerificationKey.Length > 200 ||
                string.IsNullOrWhiteSpace(item.PaymentMethodCode) || item.PaymentMethodCode.Length > 32 ||
                item.PaymentMethodCode.Any(character => !char.IsLetterOrDigit(character)) ||
                item.Amount == 0 || string.IsNullOrWhiteSpace(item.Reason) ||
                item.Reason.Trim().Length > 500 ||
                item.TenderMethodCode?.Length > 32 ||
                item.CardFranchiseCode?.Length > 64 ||
                item.ApprovalNumber?.Length > 100 ||
                item.Reference?.Length > 160 ||
                item.PaymentMethodId == Guid.Empty ||
                (item.PaymentMethodId is null) != (item.PaymentMethodVersion is null) ||
                item.PaymentMethodVersion is <= 0 ||
                (item.PaymentMethodId is not null && string.IsNullOrWhiteSpace(item.TenderMethodCode))))
            throw new WorkSessionValidationException("La corrección del comprobante requiere medio, valor y motivo válidos.");
    }

    public Task<IReadOnlyList<CashMovementReasonView>> ListCashReasonsAsync(
        WorkSessionIdentity identity,
        Guid businessId,
        string? direction,
        CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.Read);
        return ListCashReasonsCoreAsync(
            identity, businessId, direction, cancellationToken);
    }

    public Task<IReadOnlyList<CashMovementReasonView>> ListCashReasonsFromDeviceAsync(
        WorkSessionIdentity deviceIdentity,
        Guid businessId,
        string? direction,
        CancellationToken cancellationToken = default)
    {
        ValidateDeviceIdentity(deviceIdentity);
        return ListCashReasonsCoreAsync(
            deviceIdentity, businessId, direction, cancellationToken);
    }

    private Task<IReadOnlyList<CashMovementReasonView>> ListCashReasonsCoreAsync(
        WorkSessionIdentity identity,
        Guid businessId,
        string? direction,
        CancellationToken cancellationToken)
    {
        if (businessId == Guid.Empty)
            throw new WorkSessionValidationException("Se requiere una sede válida.");
        var normalizedDirection = NullIfWhiteSpace(direction);
        if (normalizedDirection is not null &&
            !CashMovementDirections.IsSupported(normalizedDirection))
            throw new WorkSessionValidationException(
                "Selecciona una entrada o una salida de dinero.");
        return store.ListCashReasonsAsync(
            identity, businessId, normalizedDirection, cancellationToken);
    }

    public Task<CashMovementReasonView> UpsertCashReasonAsync(
        WorkSessionIdentity identity,
        UpsertCashMovementReasonRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.ConfigureCashReasons);
        if (!Enum.TryParse<CashMovementDirection>(
                request.Direction, ignoreCase: false, out var direction))
            throw new WorkSessionValidationException(
                "Selecciona una entrada o una salida de dinero.");
        try
        {
            var reason = CashMovementReasonDefinition.Create(
                request.ReasonId,
                request.BusinessId,
                request.Code,
                request.Name,
                direction,
                request.CounterpartAccountingCategory,
                request.DefaultCostCenterId,
                request.RequiresReference,
                request.IsActive);
            return store.UpsertCashReasonAsync(
                identity, reason, cancellationToken);
        }
        catch (CashMovementRuleException exception)
        {
            throw new WorkSessionValidationException(exception.Message);
        }
    }

    public async Task<CashMovementAcceptance> ConfirmCashMovementAsync(
        WorkSessionIdentity identity,
        string idempotencyKey,
        ConfirmCashMovementRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.ManageCash);
        return await ConfirmCashMovementCoreAsync(
            identity, idempotencyKey, request, cancellationToken);
    }

    public Task<CashMovementAcceptance> ConfirmCashMovementFromDeviceAsync(
        WorkSessionIdentity deviceIdentity,
        string idempotencyKey,
        ConfirmCashMovementRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateDeviceIdentity(deviceIdentity);
        return ConfirmCashMovementCoreAsync(
            deviceIdentity, idempotencyKey, request, cancellationToken);
    }

    private async Task<CashMovementAcceptance> ConfirmCashMovementCoreAsync(
        WorkSessionIdentity identity,
        string idempotencyKey,
        ConfirmCashMovementRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Trim().Length > 160)
            throw new WorkSessionValidationException(
                "Se requiere una clave válida para identificar la operación.");
        var reason = await store.FindCashReasonAsync(
            identity,
            request.BusinessId,
            request.ReasonId,
            cancellationToken)
            ?? throw new WorkSessionValidationException(
                "El motivo del movimiento de caja no existe o está inactivo.");
        CashMovement movement;
        try
        {
            movement = CashMovement.Create(
                request.DocumentId,
                request.BusinessId,
                request.WorkSessionId,
                reason,
                request.Amount,
                request.OccurredAt,
                request.Reference,
                request.Notes,
                request.CostCenterId);
        }
        catch (CashMovementRuleException exception)
        {
            throw new WorkSessionValidationException(exception.Message);
        }

        var acceptance = await store.AcceptCashMovementAsync(
            identity,
            idempotencyKey.Trim(),
            movement,
            cancellationToken);
        await signalPublisher.PublishAsync(
            new DocumentProcessingSignal(
                acceptance.MovementId,
                request.BusinessId,
                request.DocumentId,
                acceptance.DocumentType),
            cancellationToken);
        return acceptance;
    }

    private static void Demand(WorkSessionIdentity identity, string permission)
    {
        ValidateUserIdentity(identity);
        if (!identity.Permissions.Contains(permission))
            throw new WorkSessionForbiddenException(
                $"No tienes permiso para realizar esta acción.");
    }

    private static void ValidateUserIdentity(WorkSessionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.UserId == Guid.Empty || identity.TenantId == Guid.Empty)
            throw new WorkSessionForbiddenException(
                "The authenticated user context is incomplete.");
    }

    private static void ValidateDeviceIdentity(WorkSessionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.UserId == Guid.Empty || identity.TenantId == Guid.Empty)
            throw new WorkSessionForbiddenException(
                "The enrolled POS device context is incomplete.");
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class WorkSessionForbiddenException(string message) : Exception(message);
public sealed class WorkSessionValidationException(string message) : Exception(message);
public sealed class WorkSessionConflictException(string message) : Exception(message);
public sealed class WorkSessionNotFoundException(string message) : Exception(message);
