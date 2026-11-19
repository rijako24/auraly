using Auraly.BuildingBlocks.Domain.Payments;
using Auraly.BuildingBlocks.Domain.Pagination;
using Auraly.Commerce.Accounting.Application;
using Auraly.Contracts.Payables;
using Auraly.Domain.Payables;

namespace Auraly.Application.Payables;

public interface IPayablesStore
{
    Task<PayablesReportPage> ReportAsync(PayablesUserIdentity user,
        PayablesReportQuery query, CancellationToken cancellationToken);
    Task<PayablesReportPage> PrintReportAsync(PayablesUserIdentity user,
        PayablesReportQuery query, CancellationToken cancellationToken);
    Task<PayableExpenseConceptPage> ListExpenseConceptsAsync(
        PayablesUserIdentity user, string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<PayablePage> ListAsync(
        PayablesUserIdentity user,
        PayableQuery query,
        CancellationToken cancellationToken);

    Task<PayableDetail?> GetAsync(
        PayablesUserIdentity user,
        Guid payableId,
        CancellationToken cancellationToken);

    Task<SupplierPortfolioPage> ListSuppliersAsync(
        PayablesUserIdentity user, SupplierPortfolioQuery query, CancellationToken cancellationToken);

    Task<SupplierPaymentHistoryPage> PaymentHistoryAsync(
        PayablesUserIdentity user, Guid supplierId, int page, int pageSize,
        CancellationToken cancellationToken);
    Task<SupplierPaymentHistoryPage> ListPaymentsAsync(PayablesUserIdentity user,
        SupplierPaymentHistoryQuery query,CancellationToken cancellationToken);

    Task<SupplierPaymentAcceptance> AcceptPaymentAsync(
        PayablesUserIdentity user,
        string idempotencyKey,
        ConfirmSupplierPaymentRequest request,
        PayableSettlement settlement,
        PaymentTenderBreakdown tenders,
        CancellationToken cancellationToken);
}

public sealed class PayablesService(
    IPayablesStore store,
    AccountingProcessingCoordinator accounting)
{
    public Task<PayablesReportPage> ReportAsync(PayablesUserIdentity user,
        PayablesReportQuery query, CancellationToken cancellationToken = default)
    {
        ValidateReport(user, query);
        return store.ReportAsync(user, query, cancellationToken);
    }
    public async Task<PayablesReportPage> PrintReportAsync(PayablesUserIdentity user,
        PayablesReportQuery query, CancellationToken cancellationToken = default)
    {
        ValidateReport(user, query);
        var result = await store.PrintReportAsync(user, query, cancellationToken);
        PortfolioReportLimits.ValidateExport(result.TotalCount,
            result.Items.Sum(item => (long)(item.Applications?.Count ?? 0)));
        return result;
    }
    private static void ValidateReport(PayablesUserIdentity user, PayablesReportQuery query)
    {
        Require(user, PayablesPermissionCodes.Read);
        if (query.Page < 1 || query.PageSize is < 1 or > 100 ||
            (long)(query.Page - 1) * query.PageSize > int.MaxValue ||
            query.Cutoff == DateOnly.MaxValue || query.To == DateOnly.MaxValue ||
            query.SupplierId == Guid.Empty || query.PartySiteId == Guid.Empty || query.ConceptId == Guid.Empty ||
            query.From > query.To || query.From > query.Cutoff || query.To > query.Cutoff)
            throw new PayablesValidationException("Los filtros del informe no son válidos.");
        ValidateLedgerFilters(query.Status, query.From, query.To, query.DateRangeVersion);
        if (!PagedSort.IsValid(query.SortBy, query.SortDirection,
            "name", "issuedAt", "dueDate", "documentNumber", "originalAmount",
            "paidAmount", "outstandingAmount", "overdueAmount", "invoiceCount", "currency"))
            throw new PayablesValidationException("El orden del informe no es válido.");
    }
    public Task<PayableExpenseConceptPage> ListExpenseConceptsAsync(
        PayablesUserIdentity user, string? search, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        Require(user, PayablesPermissionCodes.Read);
        if (page < 1 || pageSize is < 1 or > 100)
            throw new PayablesValidationException("La paginación de conceptos no es válida.");
        return store.ListExpenseConceptsAsync(user, Normalize(search, 120), page, pageSize, cancellationToken);
    }
    public Task<SupplierPortfolioPage> ListSuppliersAsync(PayablesUserIdentity user,
        SupplierPortfolioQuery query, CancellationToken cancellationToken = default)
    {
        Require(user, PayablesPermissionCodes.Read);
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            throw new PayablesValidationException("La paginación no es válida.");
        ValidateLedgerFilters(query.Status,query.From,query.To,query.DateRangeVersion);
        if (!PagedSort.IsValid(query.SortBy, query.SortDirection, "name", "invoiceCount", "originalAmount", "paidAmount", "outstandingAmount", "overdueAmount", "supplierCreditAmount"))
            throw new PayablesValidationException("El ordenamiento de proveedores no es válido.");
        return store.ListSuppliersAsync(user, query with { Search = Normalize(query.Search, 120) }, cancellationToken);
    }

    public Task<PayablePage> ListAsync(
        PayablesUserIdentity user,
        PayableQuery query,
        CancellationToken cancellationToken = default)
        => ListCoreAsync(user, query, PayablesPermissionCodes.Read, cancellationToken);

    public Task<PayablePage> ListForPosAsync(
        PayablesUserIdentity user, PayableQuery query,
        CancellationToken cancellationToken = default)
    {
        Require(user, PayablesPermissionCodes.RegisterPosPayment);
        if (query.SupplierId is null || query.SupplierId == Guid.Empty)
            throw new PayablesValidationException("Selecciona un proveedor para consultar sus facturas pendientes.");
        return ListCoreAsync(user, query with { Search = null, Status = null, PayableId = null,
            Overdue = null, OutstandingOnly = true }, PayablesPermissionCodes.RegisterPosPayment, cancellationToken);
    }

    private Task<PayablePage> ListCoreAsync(
        PayablesUserIdentity user, PayableQuery query, string permission,
        CancellationToken cancellationToken)
    {
        Require(user, permission);
        if (query.Page < 1) throw new PayablesValidationException("La página debe ser mayor que cero.");
        if (query.PayableId == Guid.Empty) throw new PayablesValidationException("La obligación seleccionada no es válida.");
        if (query.PageSize is < 1 or > 100)
            throw new PayablesValidationException("La cantidad de registros por página debe estar entre 1 y 100.");
        ValidateLedgerFilters(query.Status,query.From,query.To,query.DateRangeVersion);
        if (!PagedSort.IsValid(query.SortBy, query.SortDirection, "documentNumber", "dueDate", "originalAmount", "outstandingAmount", "status"))
            throw new PayablesValidationException("El ordenamiento de facturas no es válido.");
        if (query.ConceptId == Guid.Empty)
            throw new PayablesValidationException("El concepto de gasto no es válido.");
        return store.ListAsync(user, query with { Search = Normalize(query.Search, 120) }, cancellationToken);
    }

    public Task<PayableDetail?> GetAsync(
        PayablesUserIdentity user,
        Guid payableId,
        CancellationToken cancellationToken = default)
    {
        Require(user, PayablesPermissionCodes.Read);
        if (payableId == Guid.Empty) throw new PayablesValidationException("Se requiere una cuenta por pagar válida.");
        return store.GetAsync(user, payableId, cancellationToken);
    }

    public Task<SupplierPaymentHistoryPage> PaymentHistoryAsync(PayablesUserIdentity user,
        Guid supplierId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        Require(user, PayablesPermissionCodes.Read);
        if (supplierId == Guid.Empty || page < 1 || pageSize is < 1 or > 100)
            throw new PayablesValidationException("Los filtros del historial de abonos no son válidos.");
        return store.PaymentHistoryAsync(user, supplierId, page, pageSize, cancellationToken);
    }

    public Task<SupplierPaymentHistoryPage> ListPaymentsAsync(PayablesUserIdentity user,
        SupplierPaymentHistoryQuery query,CancellationToken cancellationToken=default)
    {
        Require(user,PayablesPermissionCodes.Read);
        if(query.Page<1||query.PageSize is <1 or >100)throw new PayablesValidationException("La paginación no es válida.");
        ValidateLedgerFilters(query.Status,query.From,query.To,query.DateRangeVersion);
        if (!PagedSort.IsValid(query.SortBy, query.SortDirection, "paidAt", "partyName", "documentNumber", "totalAmount"))
            throw new PayablesValidationException("El ordenamiento de pagos no es válido.");
        return store.ListPaymentsAsync(user,query with { Search=Normalize(query.Search,120) },cancellationToken);
    }

    private static void ValidateLedgerFilters(string? status,DateOnly? from,DateOnly? to,int dateRangeVersion)
    {
        if(status is not null and not ("Open" or "PartiallyPaid" or "Paid" or "Cancelled"))
            throw new PayablesValidationException("El estado de la cuenta por pagar no es válido.");
        if(dateRangeVersion is not (1 or 2)||from>to||to==DateOnly.MaxValue)throw new PayablesValidationException("El rango de fechas no es válido.");
    }

    public Task<SupplierPaymentAcceptance> ConfirmPaymentAsync(
        PayablesUserIdentity user,
        string idempotencyKey,
        ConfirmSupplierPaymentRequest request,
        CancellationToken cancellationToken = default)
        => ConfirmPaymentCoreAsync(user, idempotencyKey, request,
            PayablesPermissionCodes.RegisterPayment, cancellationToken);

    public Task<SupplierPaymentAcceptance> ConfirmPosPaymentAsync(
        PayablesUserIdentity user, string idempotencyKey,
        ConfirmSupplierPaymentRequest request, CancellationToken cancellationToken = default)
        => ConfirmPaymentCoreAsync(user, idempotencyKey, request,
            PayablesPermissionCodes.RegisterPosPayment, cancellationToken);

    private async Task<SupplierPaymentAcceptance> ConfirmPaymentCoreAsync(
        PayablesUserIdentity user, string idempotencyKey,
        ConfirmSupplierPaymentRequest request, string permission,
        CancellationToken cancellationToken)
    {
        Require(user, permission);
        if (permission == PayablesPermissionCodes.RegisterPosPayment &&
            (request.WorkSessionId is null || request.WorkSessionId == Guid.Empty))
            throw new PayablesValidationException("Los pagos desde el punto de venta requieren una sesión de caja abierta.");
        if (request.BusinessId != user.BusinessId)
            throw new PayablesForbiddenException("El pago pertenece a otra sede.");
        if (request.PaymentId == Guid.Empty) throw new PayablesValidationException("Se requiere un pago válido.");
        if (request.WorkSessionId == Guid.Empty)
            throw new PayablesValidationException("La sesión de caja debe estar vacía o tener una identificación válida.");
        if (request.SupplierId == Guid.Empty) throw new PayablesValidationException("Se requiere un proveedor válido.");
        if (request.PaidAt == default) throw new PayablesValidationException("Indica la fecha del pago.");
        if (string.IsNullOrWhiteSpace(request.CurrencyCode))
            throw new PayablesValidationException("Selecciona una moneda.");
        if (request.Allocations is null || request.Payments is null)
            throw new PayablesValidationException("Se requieren las aplicaciones a documentos y las formas de pago.");
        if (request.Allocations.Any(item => item is null) || request.Payments.Any(item => item is null))
            throw new PayablesValidationException("Las aplicaciones y las formas de pago no pueden contener filas vacías.");
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new PayablesValidationException("No se pudo identificar la operación. Vuelve a intentarlo con la misma solicitud.");
        if (idempotencyKey.Length > 160)
            throw new PayablesValidationException("La clave de identificación de la operación supera la longitud permitida.");
        var currency = request.CurrencyCode.Trim().ToUpperInvariant();
        if (currency != "COP")
            throw new PayablesValidationException("Los pagos a proveedores deben registrarse en pesos colombianos.");
        PayableSettlement settlement;
        try
        {
            settlement = PayableSettlement.Create(
                request.Allocations.Select(item => new PayableAllocation(item.PayableId, item.Amount)));
        }
        catch (ArgumentException exception)
        {
            throw new PayablesValidationException(exception.Message, exception);
        }

        var payments = request.Payments.Select(NormalizeTender).ToArray();
        PaymentTenderBreakdown breakdown;
        try
        {
            breakdown = PaymentTenderBreakdown.Create(payments.Select(ToDomain), settlement.TotalAmount,
                settlement.Allocations.Count, PortfolioPaymentMethods.Supported);
        }
        catch (ArgumentException exception)
        {
            throw new PayablesValidationException(exception.Message, exception);
        }
        var normalized = request with { CurrencyCode = currency,
            Notes = Normalize(request.Notes, 1000), Payments = payments };
        var acceptance = await store.AcceptPaymentAsync(
            user, idempotencyKey.Trim(), normalized, settlement, breakdown, cancellationToken);
        if (!acceptance.IdempotentReplay)
            await accounting.RequestPostingAsync(request.BusinessId, request.PaymentId,
                PayablesDocumentTypes.Payment, cancellationToken);
        return acceptance;
    }



    private static SupplierPaymentTenderRequest NormalizeTender(SupplierPaymentTenderRequest value) => value with
    {
        MethodCode = value.MethodCode?.Trim() ?? string.Empty, Reference = Normalize(value.Reference, PaymentTenderBreakdown.MaximumReferenceLength),
        Notes = Normalize(value.Notes, PaymentTenderBreakdown.MaximumNotesLength), CardFranchiseCode = Normalize(value.CardFranchiseCode, PaymentTenderBreakdown.MaximumCardFranchiseLength),
        ApprovalNumber = Normalize(value.ApprovalNumber, PaymentTenderBreakdown.MaximumApprovalLength)
    };

    private static PaymentTender ToDomain(SupplierPaymentTenderRequest value) => new(
        value.MethodCode, value.Amount, value.TenderedAmount, value.BankAccountId,
        value.Reference, value.Notes, value.CardFranchiseCode, value.ApprovalNumber,
        value.PaymentMethodId, value.PaymentMethodVersion);

    private static string? Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var result = value.Trim();
        if (result.Length > maximumLength)
            throw new PayablesValidationException($"El valor admite máximo {maximumLength} caracteres.");
        return result;
    }

    private static void Require(PayablesUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission))
            throw new PayablesForbiddenException($"No tienes permiso para realizar esta acción.");
    }
}

public sealed class PayablesForbiddenException(string message) : Exception(message);
public sealed class PayablesConflictException : Exception
{
    public PayablesConflictException(string message) : base(message) { }
    public PayablesConflictException(string message, Exception inner) : base(message, inner) { }
}
public sealed class PayablesValidationException : Exception
{
    public PayablesValidationException(string message) : base(message) { }
    public PayablesValidationException(string message, Exception inner) : base(message, inner) { }
}
