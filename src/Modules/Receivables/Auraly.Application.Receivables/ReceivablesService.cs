using Auraly.BuildingBlocks.Domain.Payments;
using Auraly.BuildingBlocks.Domain.Pagination;
using Auraly.Commerce.Accounting.Application;
using Auraly.Contracts.Receivables;
using Auraly.Domain.Receivables;

namespace Auraly.Application.Receivables;

public interface IReceivablesStore
{
    Task<ReceivablesReportPage> ReportAsync(ReceivablesUserIdentity user,
        ReceivablesReportQuery query, CancellationToken token);
    Task<ReceivablesReportPage> PrintReportAsync(ReceivablesUserIdentity user,
        ReceivablesReportQuery query, CancellationToken token);
    Task<ReceivablePage> ListAsync(ReceivablesUserIdentity user, ReceivableQuery query, CancellationToken token);
    Task<CustomerPortfolioPage> ListCustomersAsync(ReceivablesUserIdentity user, CustomerPortfolioQuery query,
        CancellationToken token);
    Task<ReceivableDetail?> GetAsync(ReceivablesUserIdentity user, Guid receivableId, CancellationToken token);
    Task<CustomerCreditProfile?> GetCreditProfileAsync(ReceivablesUserIdentity user, Guid customerId, CancellationToken token);
    Task<CustomerPaymentHistoryPage> PaymentHistoryAsync(ReceivablesUserIdentity user, Guid customerId,
        int page, int pageSize, CancellationToken token);
    Task<CustomerPaymentHistoryPage> ListPaymentsAsync(ReceivablesUserIdentity user,
        CustomerPaymentHistoryQuery query,CancellationToken token);
    Task<CustomerCreditProfile> UpdateCreditProfileAsync(ReceivablesUserIdentity user, Guid customerId,
        UpdateCustomerCreditProfileRequest request, CancellationToken token);
    Task<CustomerPaymentAcceptance> AcceptPaymentAsync(ReceivablesUserIdentity user, string idempotencyKey,
        ConfirmCustomerPaymentRequest request, ReceivableSettlement settlement,
        PaymentTenderBreakdown tenders, CancellationToken token);
    Task<ImportPreexistingReceivablesAcceptance> ImportPreexistingAsync(ReceivablesUserIdentity user,
        ImportPreexistingReceivablesRequest request,CancellationToken token);
}

public sealed class ReceivablesService(
    IReceivablesStore store,
    AccountingProcessingCoordinator accounting,
    Auraly.BuildingBlocks.Application.Synchronization.IPosSynchronizationOutboxDispatcher synchronization)
{
    public Task<ReceivablesReportPage> ReportAsync(ReceivablesUserIdentity user,
        ReceivablesReportQuery query, CancellationToken token = default)
    {
        ValidateReport(user, query);
        return store.ReportAsync(user, NormalizeReport(query), token);
    }
    public async Task<ReceivablesReportPage> PrintReportAsync(ReceivablesUserIdentity user,
        ReceivablesReportQuery query, CancellationToken token = default)
    {
        ValidateReport(user, query);
        var result = await store.PrintReportAsync(user, NormalizeReport(query), token);
        PortfolioReportLimits.ValidateExport(result.TotalCount,
            result.Items.Sum(item => (long)(item.Applications?.Count ?? 0)));
        return result;
    }
    private static void ValidateReport(ReceivablesUserIdentity user, ReceivablesReportQuery query)
    {
        Require(user, ReceivablesPermissionCodes.Read);
        if (query.Page < 1 || query.PageSize is < 1 or > 100 ||
            (long)(query.Page - 1) * query.PageSize > int.MaxValue ||
            query.Cutoff == DateOnly.MaxValue || query.To == DateOnly.MaxValue || query.TemplateVersion is not (2 or 3 or 4) ||
            query.CustomerId == Guid.Empty || query.PartySiteId == Guid.Empty ||
            query.From > query.To || query.From > query.Cutoff ||
            query.To > query.Cutoff || query.IssuedFrom > query.IssuedTo ||
            query.IssuedFrom > query.Cutoff || query.IssuedTo > query.Cutoff ||
            query.IssuedTo == DateOnly.MaxValue ||
            query.MinimumAgeDays is < 0 || query.MaximumAgeDays is < 0 ||
            query.MinimumAgeDays > query.MaximumAgeDays ||
            query.MinimumDaysPastDue > query.MaximumDaysPastDue ||
            query.MinimumAgeDays > query.Cutoff.DayNumber ||
            query.MaximumAgeDays > query.Cutoff.DayNumber ||
            query.MinimumDaysPastDue < query.Cutoff.DayNumber - DateOnly.MaxValue.DayNumber + 1 ||
            query.MinimumDaysPastDue > query.Cutoff.DayNumber ||
            query.MaximumDaysPastDue < query.Cutoff.DayNumber - DateOnly.MaxValue.DayNumber ||
            query.MaximumDaysPastDue > query.Cutoff.DayNumber)
            throw new ReceivablesValidationException("Los filtros del informe no son válidos.");
        ValidateLedgerFilters(query.Status, query.From, query.To, query.DateRangeVersion);
        if (!PagedSort.IsValid(query.SortBy, query.SortDirection,
            "name", "issuedAt", "dueDate", "ageDays", "daysPastDue", "documentNumber", "originalAmount",
            "paidAmount", "outstandingAmount", "netAmount", "overdueAmount", "invoiceCount"))
            throw new ReceivablesValidationException("El orden del informe no es válido.");
    }

    private static ReceivablesReportQuery NormalizeReport(ReceivablesReportQuery query) => query with
    {
        Search = Normalize(query.Search, 120), DocumentNumber = Normalize(query.DocumentNumber, 64)
    };
    public Task<CustomerPortfolioPage> ListCustomersAsync(ReceivablesUserIdentity user,
        CustomerPortfolioQuery query, CancellationToken token = default)
    {
        Require(user, ReceivablesPermissionCodes.Read);
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            throw new ReceivablesValidationException("La paginación no es válida.");
        ValidateLedgerFilters(query.Status, query.From, query.To, query.DateRangeVersion);
        if (!PagedSort.IsValid(query.SortBy, query.SortDirection, "name", "invoiceCount", "originalAmount", "paidAmount", "outstandingAmount", "overdueAmount"))
            throw new ReceivablesValidationException("El ordenamiento de clientes no es válido.");
        return store.ListCustomersAsync(user, query with { Search = Normalize(query.Search, 120) }, token);
    }

    public Task<ReceivablePage> ListAsync(ReceivablesUserIdentity user, ReceivableQuery query, CancellationToken token = default)
        => ListCoreAsync(user, query, ReceivablesPermissionCodes.Read, token);

    public Task<ReceivablePage> ListForPosAsync(ReceivablesUserIdentity user, ReceivableQuery query, CancellationToken token = default)
    {
        Require(user, ReceivablesPermissionCodes.RegisterPosPayment);
        if (query.CustomerId is null || query.CustomerId == Guid.Empty)
            throw new ReceivablesValidationException("Selecciona un cliente para consultar sus facturas pendientes.");
        return ListCoreAsync(user, query with { Search = null, Status = null, ReceivableId = null,
            Overdue = null, OutstandingOnly = true }, ReceivablesPermissionCodes.RegisterPosPayment, token);
    }

    private Task<ReceivablePage> ListCoreAsync(ReceivablesUserIdentity user, ReceivableQuery query, string permission, CancellationToken token)
    {
        Require(user, permission);
        if (query.Page < 1 || query.PageSize is < 1 or > 100) throw new ReceivablesValidationException("La paginación no es válida.");
        if (query.ReceivableId == Guid.Empty) throw new ReceivablesValidationException("La factura seleccionada no es válida.");
        ValidateLedgerFilters(query.Status, query.From, query.To, query.DateRangeVersion);
        if (!PagedSort.IsValid(query.SortBy, query.SortDirection, "documentNumber", "dueDate", "originalAmount", "outstandingAmount", "status"))
            throw new ReceivablesValidationException("El ordenamiento de facturas no es válido.");
        return store.ListAsync(user, query with { Search = Normalize(query.Search, 120) }, token);
    }

    public Task<ReceivableDetail?> GetAsync(ReceivablesUserIdentity user, Guid id, CancellationToken token = default)
    {
        Require(user, ReceivablesPermissionCodes.Read);
        if (id == Guid.Empty) throw new ReceivablesValidationException("Se requiere una cuenta por cobrar válida.");
        return store.GetAsync(user, id, token);
    }

    public Task<CustomerCreditProfile?> GetCreditProfileAsync(ReceivablesUserIdentity user, Guid customerId, CancellationToken token = default)
    {
        Require(user, ReceivablesPermissionCodes.Read);
        if (customerId == Guid.Empty) throw new ReceivablesValidationException("Se requiere un cliente válido.");
        return store.GetCreditProfileAsync(user, customerId, token);
    }

    public Task<CustomerPaymentHistoryPage> PaymentHistoryAsync(ReceivablesUserIdentity user,
        Guid customerId, int page, int pageSize, CancellationToken token = default)
    {
        Require(user, ReceivablesPermissionCodes.Read);
        if (customerId == Guid.Empty || page < 1 || pageSize is < 1 or > 100)
            throw new ReceivablesValidationException("Los filtros del historial de abonos no son válidos.");
        return store.PaymentHistoryAsync(user, customerId, page, pageSize, token);
    }

    public Task<CustomerPaymentHistoryPage> ListPaymentsAsync(ReceivablesUserIdentity user,
        CustomerPaymentHistoryQuery query,CancellationToken token=default)
    {
        Require(user,ReceivablesPermissionCodes.Read);
        if(query.Page<1||query.PageSize is <1 or >100)throw new ReceivablesValidationException("La paginación no es válida.");
        ValidateLedgerFilters(query.Status, query.From, query.To, query.DateRangeVersion);
        if (!PagedSort.IsValid(query.SortBy, query.SortDirection, "paidAt", "partyName", "documentNumber", "totalAmount"))
            throw new ReceivablesValidationException("El ordenamiento de recaudos no es válido.");
        return store.ListPaymentsAsync(user,query with { Search=Normalize(query.Search,120) },token);
    }

    private static void ValidateLedgerFilters(string? status, DateOnly? from, DateOnly? to, int dateRangeVersion)
    {
        if (status is not null and not ("Open" or "PartiallyPaid" or "Paid" or "Cancelled"))
            throw new ReceivablesValidationException("El estado de la cuenta por cobrar no es válido.");
        if (dateRangeVersion is not (1 or 2) || from > to || to == DateOnly.MaxValue)
            throw new ReceivablesValidationException("El rango de fechas no es válido.");
    }

    public async Task<CustomerCreditProfile> UpdateCreditProfileAsync(ReceivablesUserIdentity user, Guid customerId,
        UpdateCustomerCreditProfileRequest request, CancellationToken token = default)
    {
        Require(user, ReceivablesPermissionCodes.ManageCredit);
        if (request.BusinessId != user.BusinessId) throw new ReceivablesForbiddenException("El perfil pertenece a otra sede.");
        if (customerId == Guid.Empty || request.DefaultDueDays is < 0 or > 3650 || request.CreditLimit < 0 ||
            request.MaximumOverdueInvoices < 0)
            throw new ReceivablesValidationException("El perfil de crédito no es válido.");
        var result = await store.UpdateCreditProfileAsync(user, customerId, request, token);
        await synchronization.DispatchPendingAsync(user.TenantId, user.BusinessId, CancellationToken.None);
        return result;
    }

    public Task<CustomerPaymentAcceptance> ConfirmPaymentAsync(ReceivablesUserIdentity user,
        string idempotencyKey, ConfirmCustomerPaymentRequest request, CancellationToken token = default)
        => ConfirmPaymentCoreAsync(user, idempotencyKey, request, ReceivablesPermissionCodes.RegisterPayment, token);

    public Task<CustomerPaymentAcceptance> ConfirmPosPaymentAsync(ReceivablesUserIdentity user,
        string idempotencyKey, ConfirmCustomerPaymentRequest request, CancellationToken token = default)
        => ConfirmPaymentCoreAsync(user, idempotencyKey, request, ReceivablesPermissionCodes.RegisterPosPayment, token);

    private async Task<CustomerPaymentAcceptance> ConfirmPaymentCoreAsync(ReceivablesUserIdentity user,
        string idempotencyKey, ConfirmCustomerPaymentRequest request, string permission, CancellationToken token)
    {
        Require(user, permission);
        if (permission == ReceivablesPermissionCodes.RegisterPosPayment &&
            (request.WorkSessionId is null || request.WorkSessionId == Guid.Empty))
            throw new ReceivablesValidationException("Los pagos desde el punto de venta requieren una sesión de caja abierta.");
        if (request.BusinessId != user.BusinessId) throw new ReceivablesForbiddenException("El recibo pertenece a otra sede.");
        if (request.PaymentId == Guid.Empty || request.CustomerId == Guid.Empty || request.PaidAt == default)
            throw new ReceivablesValidationException("Se requiere un pago, un cliente y una fecha de pago válidos.");
        if (request.Allocations is null || request.Payments is null)
            throw new ReceivablesValidationException("Se requieren las aplicaciones a documentos y las formas de pago.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 160)
            throw new ReceivablesValidationException("No se pudo identificar la operación. Vuelve a intentarlo con la misma solicitud.");
        var currency = request.CurrencyCode?.Trim().ToUpperInvariant();
        if (currency != "COP") throw new ReceivablesValidationException("Solo se admiten valores en pesos colombianos (COP).");
        if (request.Allocations.Any(x => x is null) || request.Payments.Any(x => x is null))
            throw new ReceivablesValidationException("Las aplicaciones y las formas de pago no pueden contener filas vacías.");
        ReceivableSettlement settlement;
        try { settlement = ReceivableSettlement.Create(request.Allocations.Select(x => new ReceivableAllocation(x.ReceivableId, x.Amount))); }
        catch (ArgumentException ex) { throw new ReceivablesValidationException(ex.Message, ex); }
        var payments = request.Payments.Select(NormalizeTender).ToArray();
        PaymentTenderBreakdown breakdown;
        try
        {
            breakdown = PaymentTenderBreakdown.Create(payments.Select(ToDomain), settlement.TotalAmount,
                settlement.Allocations.Count, PortfolioPaymentMethods.Supported);
        }
        catch (ArgumentException ex) { throw new ReceivablesValidationException(ex.Message, ex); }
        var normalized = request with { CurrencyCode = currency,
            Notes = Normalize(request.Notes, 1000), Payments = payments };
        var acceptance = await store.AcceptPaymentAsync(
            user, idempotencyKey.Trim(), normalized, settlement, breakdown, token);
        if (!acceptance.IdempotentReplay)
            await accounting.RequestPostingAsync(request.BusinessId, request.PaymentId,
                ReceivablesDocumentTypes.Payment, token);
        return acceptance;
    }

    public async Task<ImportPreexistingReceivablesAcceptance> ImportPreexistingAsync(
        ReceivablesUserIdentity user,ImportPreexistingReceivablesRequest request,CancellationToken token=default)
    {
        Require(user,ReceivablesPermissionCodes.ManageCredit);
        if(request.BusinessId!=user.BusinessId)throw new ReceivablesForbiddenException("La cartera pertenece a otra sede.");
        if(request.Items is null||request.Items.Count is <1 or >100)throw new ReceivablesValidationException("Importa entre 1 y 100 cuentas por cobrar por lote.");
        if(request.Items.Any(x=>x is null))throw new ReceivablesValidationException("La cartera no puede contener filas vacías.");
        if(request.Items.Any(x=>x.ReceivableId==Guid.Empty||((x.CustomerId is null||x.CustomerId==Guid.Empty)&&string.IsNullOrWhiteSpace(x.CustomerIdentification))||x.CounterpartAccountId==Guid.Empty||x.Amount<=0||x.Amount>=1000000000000000m||x.Amount!=decimal.Round(x.Amount,4)||x.IssuedAt==default||x.DueDate==default||x.DueDate<x.IssuedAt||string.IsNullOrWhiteSpace(x.DocumentNumber)||x.DocumentNumber.Trim().Length>64))throw new ReceivablesValidationException("Cada fila de cartera requiere un cliente, una factura, fechas, un valor y una cuenta de contrapartida válidos.");
        var normalized=request with { Items=request.Items.Select(x=>x with { DocumentNumber=x.DocumentNumber.Trim(),CustomerIdentification=Normalize(x.CustomerIdentification,64),Notes=Normalize(x.Notes,500) }).ToArray() };
        var result=await store.ImportPreexistingAsync(user,normalized,token);
        foreach(var id in result.ReceivableIds)await accounting.RequestPostingAsync(user.BusinessId,id,ReceivablesDocumentTypes.PreexistingReceivable,token);
        return result;
    }



    private static CustomerPaymentTenderRequest NormalizeTender(CustomerPaymentTenderRequest value) => value with
    {
        MethodCode = value.MethodCode?.Trim() ?? string.Empty, Reference = Normalize(value.Reference, PaymentTenderBreakdown.MaximumReferenceLength),
        Notes = Normalize(value.Notes, PaymentTenderBreakdown.MaximumNotesLength), CardFranchiseCode = Normalize(value.CardFranchiseCode, PaymentTenderBreakdown.MaximumCardFranchiseLength),
        ApprovalNumber = Normalize(value.ApprovalNumber, PaymentTenderBreakdown.MaximumApprovalLength)
    };

    private static PaymentTender ToDomain(CustomerPaymentTenderRequest value) => new(
        value.MethodCode, value.Amount, value.TenderedAmount, value.BankAccountId,
        value.Reference, value.Notes, value.CardFranchiseCode, value.ApprovalNumber,
        value.PaymentMethodId, value.PaymentMethodVersion);

    private static string? Normalize(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var result = value.Trim();
        if (result.Length > max) throw new ReceivablesValidationException($"El valor admite máximo {max} caracteres.");
        return result;
    }
    private static void Require(ReceivablesUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission)) throw new ReceivablesForbiddenException($"No tienes permiso para realizar esta acción.");
    }
}

public sealed class ReceivablesForbiddenException(string message) : Exception(message);
public sealed class ReceivablesConflictException : Exception
{
    public ReceivablesConflictException(string message) : base(message) { }
    public ReceivablesConflictException(string message, Exception inner) : base(message, inner) { }
}
public sealed class ReceivablesValidationException : Exception
{
    public ReceivablesValidationException(string message) : base(message) { }
    public ReceivablesValidationException(string message, Exception inner) : base(message, inner) { }
}
