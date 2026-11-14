namespace Auraly.Contracts.Catalog;

public sealed record PaymentMethodQuery(int Page = 1, int PageSize = 50,
    string? Search = null, bool? IsActive = null, string? Usage = null, Guid? PaymentMethodId = null);

public sealed record PaymentMethodView(Guid PaymentMethodId, string Code, string Name,
    Guid PaymentTypeId, string MethodCode, string TypeLabel, Guid AccountId,
    string AccountCode, string AccountName, Guid? BankAccountId,
    bool UseForSales, bool UseForReceipts, bool UseForSupplierPayments, bool UseForPayroll,
    bool IsActive, int SortOrder, int Version, byte[] RowVersion, bool UseForVouchers = false);

public sealed record PaymentMethodPage(IReadOnlyList<PaymentMethodView> Items,
    int Page, int PageSize, int TotalCount);

public sealed record PaymentMethodSaveResult(PaymentMethodView Method, PaymentMethodPage Page);

public sealed record PaymentMethodCatalogSnapshot(int ContractVersion, IReadOnlyList<PaymentMethodView> Forms);

public sealed record SavePaymentMethodRequest(Guid PaymentMethodId, string Code, string Name,
    Guid PaymentTypeId, Guid AccountId, bool UseForSales, bool UseForReceipts,
    bool UseForSupplierPayments, bool UseForPayroll, bool IsActive, int SortOrder,
    byte[]? RowVersion = null, bool? UseForVouchers = null);

public interface IPaymentMethodStore
{
    Task<PaymentMethodCatalogSnapshot> GetSnapshotAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<PaymentMethodPage> ListAsync(CatalogUserIdentity user, PaymentMethodQuery query,
        CancellationToken cancellationToken);
    Task<PaymentMethodView> SaveAsync(CatalogUserIdentity user, SavePaymentMethodRequest request,
        CancellationToken cancellationToken);
}
