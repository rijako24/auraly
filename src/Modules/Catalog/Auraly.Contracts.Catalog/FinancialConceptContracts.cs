namespace Auraly.Contracts.Catalog;

public sealed record FinancialConceptQuery(int Page=1,int PageSize=50,string? Search=null,
    bool? IsActive=null,string? Usage=null,Guid? FinancialConceptId=null);

public sealed record FinancialConceptView(Guid FinancialConceptId,string Code,string Name,Guid AccountId,
    string AccountCode,string AccountName,string? FinancialFunction,Guid SideOptionId,string SideCode,string SideLabel,
    bool UseForReceipts,bool UseForSupplierPayments,bool UseForVouchers,bool IsActive,int SortOrder,byte[] RowVersion);

public sealed record FinancialConceptPage(IReadOnlyList<FinancialConceptView> Items,int Page,int PageSize,int TotalCount);
public sealed record FinancialConceptSaveResult(FinancialConceptView Concept,FinancialConceptPage Page);
public sealed record SaveFinancialConceptRequest(Guid FinancialConceptId,string Code,string Name,Guid AccountId,
    Guid SideOptionId,bool UseForReceipts,bool UseForSupplierPayments,bool UseForVouchers,bool IsActive,int SortOrder,byte[]? RowVersion=null);

public interface IFinancialConceptStore
{
    Task<FinancialConceptPage> ListAsync(CatalogUserIdentity user,FinancialConceptQuery query,CancellationToken token);
    Task<FinancialConceptSaveResult> SaveAsync(CatalogUserIdentity user,SaveFinancialConceptRequest request,FinancialConceptQuery query,CancellationToken token);
}
