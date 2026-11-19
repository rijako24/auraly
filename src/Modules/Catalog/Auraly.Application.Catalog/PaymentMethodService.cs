using Auraly.Contracts.Catalog;
using Auraly.Domain.Catalog;

namespace Auraly.Application.Catalog;

public sealed class PaymentMethodService(IPaymentMethodStore store)
{
    public Task<PaymentMethodCatalogSnapshot> GetSnapshotAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new CatalogForbiddenException("Falta el tenant autorizado.");
        return store.GetSnapshotAsync(tenantId, cancellationToken);
    }
    public Task<PaymentMethodPage> ListAsync(CatalogUserIdentity user, PaymentMethodQuery query,
        CancellationToken cancellationToken = default)
        => store.ListAsync(user, ValidateQuery(user,query), cancellationToken);

    public PaymentMethodQuery ValidateQuery(CatalogUserIdentity user, PaymentMethodQuery query)
    {
        var permission = query.Usage switch
        {
            "Sales" => "sales.create", "OrderReceipts" => "orders.invoice", "ServiceInvoices" => "service-invoices.create", "TipPayments" => "cash.close", "Receipts" => "receivables.read",
            "SupplierPayments" => "payables.read", "Payroll" => "payroll.read",
            "VoucherReceipts" or "VoucherPayments" => "accounting.read", _ => CatalogPermissionCodes.Read
        };
        var accountingCollection = (query.Usage is "Receipts" or "SupplierPayments") && user.Permissions.Contains("accounting.read");
        var dispatchCollection = query.Usage == "Receipts" && (user.Permissions.Contains("dispatches.delivery.execute") || user.Permissions.Contains("dispatches.settle"));
        var dispatchExpenseCash = query.Usage == "SupplierPayments" && query.MethodCode == "Cash"
            && user.Permissions.Contains("dispatches.settle") && user.Permissions.Contains("expenses.create");
        var closureCorrection = user.Permissions.Contains("work-sessions.closures.reconcile")
            && query.Usage is "Sales" or "Receipts" or "SupplierPayments" or "VoucherReceipts" or "VoucherPayments"
            && query.MethodCode is "Cash" or "DebitCard" or "CreditCard" or "Transfer";
        if (!accountingCollection && !dispatchCollection && !dispatchExpenseCash && !closureCorrection && !user.Permissions.Contains(permission) && !user.Permissions.Contains(CatalogPermissionCodes.Update))
            throw new CatalogForbiddenException("No tiene permiso para consultar formas de pago.");
        if (query.Page < 1 || query.PageSize is < 1 or > 100 ||
            ((long)query.Page - 1) * query.PageSize > int.MaxValue || query.Search?.Length > 160 ||
            query.MethodCode?.Length > 32 || query.ExcludeMethodCode?.Length > 32 ||
            query.Usage is not (null or "Sales" or "OrderReceipts" or "ServiceInvoices" or "TipPayments" or "Receipts" or "SupplierPayments" or "Payroll" or "VoucherReceipts" or "VoucherPayments"))
            throw new CatalogValidationException("Los filtros de formas de pago no son válidos.");
        return query with { Search = query.Search?.Trim() };
    }

    public Task<PaymentMethodView> SaveAsync(CatalogUserIdentity user, SavePaymentMethodRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!user.Permissions.Contains(CatalogPermissionCodes.Update))
            throw new CatalogForbiddenException("No tiene permiso para configurar formas de pago.");
        var normalized = request with { Code = request.Code?.Trim() ?? "", Name = request.Name?.Trim() ?? "" };
        try
        {
            PaymentMethodDefinition.Validate(normalized.PaymentMethodId, normalized.Code, normalized.Name,
                normalized.PaymentTypeId, normalized.AccountId, normalized.SortOrder, normalized.RowVersion);
        }
        catch (ArgumentException error) { throw new CatalogValidationException(error.Message); }
        return store.SaveAsync(user, normalized, cancellationToken);
    }
}
