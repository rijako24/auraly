using Auraly.Contracts.Catalog;

namespace Auraly.Application.Catalog;

public sealed class FinancialConceptService(IFinancialConceptStore store)
{
    public Task<FinancialConceptPage> ListAsync(CatalogUserIdentity user,FinancialConceptQuery query,CancellationToken token=default)
        =>store.ListAsync(user,ValidateQuery(user,query),token);

    public Task<FinancialConceptSaveResult> SaveAsync(CatalogUserIdentity user,SaveFinancialConceptRequest request,
        FinancialConceptQuery query,CancellationToken token=default)
    {
        if(!user.Permissions.Contains(CatalogPermissionCodes.Update))
            throw new CatalogForbiddenException("No tiene permiso para configurar conceptos financieros.");
        query=ValidateQuery(user,query);
        var value=request with { Code=request.Code?.Trim()??"",Name=request.Name?.Trim()??"" };
        if(value.FinancialConceptId==Guid.Empty||value.AccountId==Guid.Empty||value.SideOptionId==Guid.Empty||
            value.Code.Length is <1 or >64||value.Name.Length is <1 or >160||value.SortOrder is <0 or >9999||
            value.RowVersion is {Length:not 8})
            throw new CatalogValidationException("El concepto requiere identidad, código, nombre, auxiliar, naturaleza y versión válidos.");
        return store.SaveAsync(user,value,query,token);
    }

    private static FinancialConceptQuery ValidateQuery(CatalogUserIdentity user,FinancialConceptQuery query)
    {
        if(!user.Permissions.Contains(CatalogPermissionCodes.Update)&&!user.Permissions.Contains(CatalogPermissionCodes.Read)&&
            !user.Permissions.Contains("accounting.read"))
            throw new CatalogForbiddenException("No tiene permiso para consultar conceptos financieros.");
        if(query.Page<1||query.PageSize is <1 or >100||((long)query.Page-1)*query.PageSize>int.MaxValue||
            query.Search?.Length>160||query.FinancialConceptId==Guid.Empty||
            query.Usage is not(null or "Receipts" or "SupplierPayments" or "Vouchers" or "Filter"))
            throw new CatalogValidationException("Los filtros de conceptos financieros no son válidos.");
        return query with {Search=query.Search?.Trim()};
    }
}
