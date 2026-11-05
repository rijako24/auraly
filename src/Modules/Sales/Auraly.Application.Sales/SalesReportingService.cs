using Auraly.Contracts.Sales;

namespace Auraly.Application.Sales;

public interface ISalesReportingStore
{
    Task<SalesReportFilterOptionPage> GetFilterOptionsAsync(SalesReportingUserIdentity user,SalesReportFilterOptionsRequest request,CancellationToken cancellationToken);
    Task<SalesReportContext> GetContextAsync(SalesReportingUserIdentity user,CancellationToken cancellationToken);
    Task<SalesReportExplorerPage> ExploreAsync(SalesReportingUserIdentity user,SalesReportExplorerRequest request,CancellationToken cancellationToken);
    Task<SalesTodayOverview> GetTodayAsync(SalesReportingUserIdentity user,
        CancellationToken cancellationToken);
    Task<SalesReportSummary> GetSummaryAsync(SalesReportingUserIdentity user,
        SalesReportFilter filter, DateOnly? comparisonFrom, DateOnly? comparisonTo,
        CancellationToken cancellationToken,bool includeBreakdowns=false);
    Task<IReadOnlyList<SalesReportBreakdownRow>> GetBreakdownAsync(
        SalesReportingUserIdentity user, SalesReportFilter filter, string dimension,
        int limit, CancellationToken cancellationToken);
    Task<SalesReportDocumentPage> ListDocumentsAsync(SalesReportingUserIdentity user,
        SalesReportFilter filter, int page, int pageSize, string? search,
        CancellationToken cancellationToken);
    Task<SalesReportDocumentDetail?> GetDocumentAsync(SalesReportingUserIdentity user,
        Guid documentId, CancellationToken cancellationToken,int page=1,int pageSize=200);
    Task<SupplierImpactOverview> GetSupplierImpactAsync(SalesReportingUserIdentity user,
        DateOnly from,DateOnly to,CancellationToken cancellationToken,int page=1,int pageSize=200,string comparison="none");
}

public sealed class SalesReportingService(ISalesReportingStore store)
{
    public Task<SalesReportFilterOptionPage> GetFilterOptionsAsync(SalesReportingUserIdentity user,SalesReportFilterOptionsRequest request,CancellationToken cancellationToken=default)
    {
        Demand(user);Validate(request.Filter);
        if(request.Dimension is not ("product" or "category" or "warehouse" or "area" or "category-line" or "category-group" or "subgroup" or "table") ||
            request.Page is <1 or >1000000 || request.PageSize is <1 or >100 || request.Search?.Length>160)
            throw new SalesReportingValidationException("La búsqueda del filtro no es válida.");
        return store.GetFilterOptionsAsync(user,request,cancellationToken);
    }
    public Task<SalesReportContext> GetContextAsync(SalesReportingUserIdentity user,CancellationToken cancellationToken=default)
    {
        Demand(user);
        return store.GetContextAsync(user,cancellationToken);
    }

    public Task<SalesReportExplorerPage> ExploreAsync(SalesReportingUserIdentity user,
        SalesReportExplorerRequest request,CancellationToken cancellationToken=default)
    {
        Demand(user); Validate(request.Filter);
        if(request.Page<1||request.Page>1000000||request.PageSize is <1 or >200||request.Search?.Length>160)
            throw new SalesReportingValidationException("La página, tamaño o búsqueda no es válido.");
        if(request.DocumentId==Guid.Empty||request.BilledByUserId==Guid.Empty||request.WaiterPartyId==Guid.Empty)
            throw new SalesReportingValidationException("La identidad del filtro no es válida.");
        if((request.DrillDimension is null) != (request.DrillKey is null)||request.DrillKey?.Length>80)
            throw new SalesReportingValidationException("El grupo seleccionado no es válido.");
        if(request.DrillPath is {Count:>8} || request.DrillPath?.Any(step=>string.IsNullOrWhiteSpace(step.Key)||step.Key.Length>80)==true)
            throw new SalesReportingValidationException("La ruta del detalle no es válida.");
        if(request.Products is { } products && (request.Module!="products" ||
            products.ReturnActivity is not ("all" or "with-returns" or "without-returns") ||
            products.CompositionKind is not (null or "None" or "Ingredients" or "Combo" or "Unknown") ||
            products.MarginFrom>products.MarginTo || products.MarginFrom is <-1000000 or >1000000 ||
            products.MarginTo is <-1000000 or >1000000 ||
            (products.MarginFrom is {} minimum&&decimal.Round(minimum,2)!=minimum) ||
            (products.MarginTo is {} maximum&&decimal.Round(maximum,2)!=maximum)))
            throw new SalesReportingValidationException("Los filtros de rentabilidad no son válidos.");
        return store.ExploreAsync(user,request,cancellationToken);
    }
    public Task<SalesTodayOverview> GetTodayAsync(SalesReportingUserIdentity user,
        CancellationToken cancellationToken = default)
    {
        Demand(user);
        return store.GetTodayAsync(user, cancellationToken);
    }

    public Task<SalesReportSummary> GetSummaryAsync(SalesReportingUserIdentity user,
        SalesReportFilter filter, DateOnly? comparisonFrom, DateOnly? comparisonTo,
        CancellationToken cancellationToken = default,bool includeBreakdowns=false)
    {
        Demand(user); Validate(filter);
        if (comparisonFrom.HasValue != comparisonTo.HasValue || comparisonTo < comparisonFrom)
            throw new SalesReportingValidationException("The comparison range is invalid.");
        return store.GetSummaryAsync(user, filter, comparisonFrom, comparisonTo, cancellationToken,includeBreakdowns);
    }

    public Task<IReadOnlyList<SalesReportBreakdownRow>> GetBreakdownAsync(
        SalesReportingUserIdentity user, SalesReportFilter filter, string dimension,
        int limit, CancellationToken cancellationToken = default)
    {
        Demand(user); Validate(filter);
        if (!SalesReportingDimensions.IsSupported(dimension))
            throw new SalesReportingValidationException("The reporting dimension is invalid.");
        if (limit is < 1 or > 500)
            throw new SalesReportingValidationException("Limit must be between 1 and 500.");
        return store.GetBreakdownAsync(user, filter, dimension, limit, cancellationToken);
    }

    public Task<SalesReportDocumentPage> ListDocumentsAsync(SalesReportingUserIdentity user,
        SalesReportFilter filter, int page, int pageSize, string? search,
        CancellationToken cancellationToken = default)
    {
        Demand(user); Validate(filter);
        if (page < 1 || pageSize is < 1 or > 200)
            throw new SalesReportingValidationException("Page and pageSize are invalid.");
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (search?.Length > 160)
            throw new SalesReportingValidationException("Search is limited to 160 characters.");
        return store.ListDocumentsAsync(user, filter, page, pageSize, search, cancellationToken);
    }

    public Task<SalesReportDocumentDetail?> GetDocumentAsync(SalesReportingUserIdentity user,
        Guid documentId, CancellationToken cancellationToken = default,int page=1,int pageSize=200)
    {
        Demand(user);
        if (documentId == Guid.Empty)
            throw new SalesReportingValidationException("DocumentId is required.");
        if(page<1||pageSize is <1 or >200)throw new SalesReportingValidationException("La página del detalle no es válida.");
        return store.GetDocumentAsync(user, documentId, cancellationToken,page,pageSize);
    }

    public Task<SupplierImpactOverview> GetSupplierImpactAsync(SalesReportingUserIdentity user,
        DateOnly from,DateOnly to,CancellationToken cancellationToken=default,int page=1,int pageSize=200,string comparison="none")
    {
        Demand(user);ValidateRange(from,to);
        if(page<1||pageSize is <1 or >200)throw new SalesReportingValidationException("La página de proveedores no es válida.");
        return store.GetSupplierImpactAsync(user,from,to,cancellationToken,page,pageSize,comparison);
    }

    private static void Demand(SalesReportingUserIdentity user)
    {
        if (!user.Permissions.Contains(SalesReportingPermissionCodes.Read))
            throw new SalesReportingForbiddenException(
                $"Permission '{SalesReportingPermissionCodes.Read}' is required.");
    }

    private static void Validate(SalesReportFilter filter)
    {
        if (filter.Selections().Sum(selection => selection.Values?.Count ?? 0) > 100)
            throw new SalesReportingValidationException("Los filtros admiten hasta 100 selecciones en total.");
        foreach (var selection in filter.Selections().Select(selection => selection.Values))
            if (selection is { Count: > 100 } || selection?.Any(id => id == Guid.Empty) == true)
                throw new SalesReportingValidationException("Cada filtro admite hasta 100 selecciones válidas.");
        if (filter.From == default || filter.To < filter.From || filter.To.DayNumber-filter.From.DayNumber > 1827)
            throw new SalesReportingValidationException("The date range is invalid or exceeds five years.");
        if (filter.DocumentType is not null && filter.DocumentType is not ("SalesInvoice" or "SalesReceipt" or "ServiceInvoice"))
            throw new SalesReportingValidationException("The document type is invalid.");
    }

    private static void ValidateRange(DateOnly from,DateOnly to)
    {
        if(from==default||to<from||to.DayNumber-from.DayNumber>1827)
            throw new SalesReportingValidationException("The report range is invalid or exceeds five years.");
    }
}
