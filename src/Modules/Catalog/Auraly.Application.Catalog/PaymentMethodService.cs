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
            "Sales" => "sales.create", "Receipts" => "receivables.read",
            "SupplierPayments" => "payables.read", "Payroll" => "payroll.read",
            "VoucherReceipts" or "VoucherPayments" => "accounting.read", _ => CatalogPermissionCodes.Read
        };
        var accountingCollection = (query.Usage is "Receipts" or "SupplierPayments") && user.Permissions.Contains("accounting.read");
        if (!accountingCollection && !user.Permissions.Contains(permission) && !user.Permissions.Contains(CatalogPermissionCodes.Update))
            throw new CatalogForbiddenException("No tiene permiso para consultar formas de pago.");
        if (query.Page < 1 || query.PageSize is < 1 or > 100 ||
            ((long)query.Page - 1) * query.PageSize > int.MaxValue || query.Search?.Length > 160 ||
            query.Usage is not (null or "Sales" or "Receipts" or "SupplierPayments" or "Payroll" or "VoucherReceipts" or "VoucherPayments"))
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
