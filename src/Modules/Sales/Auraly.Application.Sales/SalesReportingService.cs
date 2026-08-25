using Auraly.Contracts.Sales;

namespace Auraly.Application.Sales;

public interface ISalesReportingStore
{
    Task<SalesTodayOverview> GetTodayAsync(SalesReportingUserIdentity user,
        CancellationToken cancellationToken);
    Task<SalesReportSummary> GetSummaryAsync(SalesReportingUserIdentity user,
        SalesReportFilter filter, DateOnly? comparisonFrom, DateOnly? comparisonTo,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesReportBreakdownRow>> GetBreakdownAsync(
        SalesReportingUserIdentity user, SalesReportFilter filter, string dimension,
        int limit, CancellationToken cancellationToken);
    Task<SalesReportDocumentPage> ListDocumentsAsync(SalesReportingUserIdentity user,
        SalesReportFilter filter, int page, int pageSize, string? search,
        CancellationToken cancellationToken);
    Task<SalesReportDocumentDetail?> GetDocumentAsync(SalesReportingUserIdentity user,
        Guid documentId, CancellationToken cancellationToken);
    Task<CommercialVisitReportPage> ListVisitsAsync(SalesReportingUserIdentity user,
        DateOnly from,DateOnly to,Guid? sellerId,Guid? routeId,string? status,bool? hasOrder,
        int page,int pageSize,CancellationToken cancellationToken);
    Task<IReadOnlyList<SellerOrderReportRow>> ListSellerOrdersAsync(SalesReportingUserIdentity user,
        DateOnly from,DateOnly to,CancellationToken cancellationToken);
}

public sealed class SalesReportingService(ISalesReportingStore store)
{
    public Task<SalesTodayOverview> GetTodayAsync(SalesReportingUserIdentity user,
        CancellationToken cancellationToken = default)
    {
        Demand(user);
        return store.GetTodayAsync(user, cancellationToken);
    }

    public Task<SalesReportSummary> GetSummaryAsync(SalesReportingUserIdentity user,
        SalesReportFilter filter, DateOnly? comparisonFrom, DateOnly? comparisonTo,
        CancellationToken cancellationToken = default)
    {
        Demand(user); Validate(filter);
        if (comparisonFrom.HasValue != comparisonTo.HasValue || comparisonTo < comparisonFrom)
            throw new SalesReportingValidationException("The comparison range is invalid.");
        return store.GetSummaryAsync(user, filter, comparisonFrom, comparisonTo, cancellationToken);
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
        Guid documentId, CancellationToken cancellationToken = default)
    {
        Demand(user);
        if (documentId == Guid.Empty)
            throw new SalesReportingValidationException("DocumentId is required.");
        return store.GetDocumentAsync(user, documentId, cancellationToken);
    }

    public Task<CommercialVisitReportPage> ListVisitsAsync(SalesReportingUserIdentity user,
        DateOnly from,DateOnly to,Guid? sellerId,Guid? routeId,string? status,bool? hasOrder,
        int page,int pageSize,CancellationToken cancellationToken=default)
    {
        Demand(user);
        if(from==default||to<from||to.DayNumber-from.DayNumber>366||page<1||pageSize is <1 or >200)
            throw new SalesReportingValidationException("The visit report range or pagination is invalid.");
        if(status is not null && status is not ("Visited" or "Skipped"))
            throw new SalesReportingValidationException("The visit status is invalid.");
        return store.ListVisitsAsync(user,from,to,sellerId,routeId,status,hasOrder,page,pageSize,cancellationToken);
    }

    public Task<IReadOnlyList<SellerOrderReportRow>> ListSellerOrdersAsync(SalesReportingUserIdentity user,
        DateOnly from,DateOnly to,CancellationToken cancellationToken=default)
    {Demand(user);if(from==default||to<from||to.DayNumber-from.DayNumber>1827)throw new SalesReportingValidationException("The order report range is invalid.");return store.ListSellerOrdersAsync(user,from,to,cancellationToken);}

    private static void Demand(SalesReportingUserIdentity user)
    {
        if (!user.Permissions.Contains(SalesReportingPermissionCodes.Read))
            throw new SalesReportingForbiddenException(
                $"Permission '{SalesReportingPermissionCodes.Read}' is required.");
    }

    private static void Validate(SalesReportFilter filter)
    {
        if (filter.From == default || filter.To < filter.From || filter.To.DayNumber-filter.From.DayNumber > 1827)
            throw new SalesReportingValidationException("The date range is invalid or exceeds five years.");
        if (filter.DocumentType is not null && filter.DocumentType is not ("SalesInvoice" or "SalesReceipt"))
            throw new SalesReportingValidationException("The document type is invalid.");
    }
}
