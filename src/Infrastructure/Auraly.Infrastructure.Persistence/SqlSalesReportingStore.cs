using System.Data;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlSalesReportingStore(
    SqlServerConnectionFactory connections,
    TimeProvider timeProvider)
    : ISalesReportingStore
{
    public async Task<SellerPerformanceOverview> GetSellerPerformanceAsync(SalesReportingUserIdentity user,
        DateOnly from,DateOnly to,CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);
        var access=await ResolveAccessAsync(connection,user,ct);
        if(access.SupplierId is not null)throw new SalesReportingForbiddenException("A supplier cannot access seller performance data.");
        await using var command=new SqlCommand("""
          ;WITH Dates AS(SELECT @From d UNION ALL SELECT DATEADD(day,1,d) FROM Dates WHERE d<@To),
          Planned AS(SELECT c.SellerId,COUNT_BIG(*) Planned FROM reporting.CommercialCoverageAssignmentFacts c
            INNER JOIN Dates d ON ((DATEDIFF(day,'19000101',d.d)%7)+1)=c.DayOfWeek
              AND d.d>=c.ValidFromBusinessDate AND (c.ValidToBusinessDateExclusive IS NULL OR d.d<c.ValidToBusinessDateExclusive)
            WHERE c.TenantId=@TenantId AND c.BusinessId=@BusinessId GROUP BY c.SellerId),
          Visits AS(SELECT SellerId,SUM(CONVERT(bigint,CASE WHEN Status=N'Visited' THEN 1 ELSE 0 END)) Visited,
            SUM(CONVERT(bigint,CASE WHEN Status=N'Skipped' THEN 1 ELSE 0 END)) Skipped
            FROM reporting.CommercialReportVisitFacts WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND VisitDate BETWEEN @From AND @To GROUP BY SellerId),
          Orders AS(SELECT SellerId,COUNT_BIG(*) Orders,COUNT_BIG(DISTINCT CustomerId) Customers,SUM(TotalAmount) Amount,
            SUM(CONVERT(bigint,CASE WHEN InvoiceDocumentId IS NOT NULL THEN 1 ELSE 0 END)) Invoiced
            FROM reporting.CommercialReportOrderFacts WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND CreatedDate BETWEEN @From AND @To GROUP BY SellerId),
          Sales AS(SELECT SellerId,SUM(TotalAmount) NetSales,SUM(UntaxedAmount-RecognizedCostAmount) Profit
            FROM reporting.SalesReportLineFacts WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND BusinessLocalDate BETWEEN @From AND @To GROUP BY SellerId)
          SELECT seller.SellerId,COALESCE(NULLIF(p.DisplayName,N''),NULLIF(p.LegalName,N''),seller.Code),
            COALESCE(pl.Planned,0),COALESCE(v.Visited,0),COALESCE(v.Skipped,0),COALESCE(o.Orders,0),COALESCE(o.Invoiced,0),
            COALESCE(o.Customers,0),COALESCE(o.Amount,0),COALESCE(s.NetSales,0),COALESCE(s.Profit,0)
          FROM dbo.CommerceSellers seller INNER JOIN dbo.Parties p ON p.PartyId=seller.PartyId
          LEFT JOIN Planned pl ON pl.SellerId=seller.SellerId LEFT JOIN Visits v ON v.SellerId=seller.SellerId
          LEFT JOIN Orders o ON o.SellerId=seller.SellerId LEFT JOIN Sales s ON s.SellerId=seller.SellerId
          WHERE seller.BusinessId=@BusinessId AND seller.IsActive=1 AND (@SellerId IS NULL OR seller.SellerId=@SellerId)
          ORDER BY COALESCE(s.NetSales,0) DESC,p.DisplayName OPTION(MAXRECURSION 0);
          """,connection);
        Scope(command,user);Date(command,"@From",from);Date(command,"@To",to);
        command.Parameters.AddWithValue("@SellerId",(object?)access.SellerId??DBNull.Value);
        var rows=new List<SellerPerformanceRow>();await using var reader=await command.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
        {
            var planned=reader.GetInt64(2);var visited=reader.GetInt64(3);var orders=reader.GetInt64(5);var invoiced=reader.GetInt64(6);
            rows.Add(new(reader.GetGuid(0),reader.GetString(1),planned,visited,reader.GetInt64(4),orders,invoiced,reader.GetInt64(7),
                reader.GetDecimal(8),reader.GetDecimal(9),reader.GetDecimal(10),Percent(visited,planned),Percent(orders,visited),Percent(invoiced,orders)));
        }
        await reader.CloseAsync();
        return new(rows.Sum(x=>x.PlannedVisits),rows.Sum(x=>x.VisitedCount),rows.Sum(x=>x.SkippedCount),rows.Sum(x=>x.OrderCount),
            rows.Sum(x=>x.InvoicedCount),rows.Sum(x=>x.NetSales),rows.Sum(x=>x.GrossProfit),Percent(rows.Sum(x=>x.VisitedCount),rows.Sum(x=>x.PlannedVisits)),
            Percent(rows.Sum(x=>x.OrderCount),rows.Sum(x=>x.VisitedCount)),Percent(rows.Sum(x=>x.InvoicedCount),rows.Sum(x=>x.OrderCount)),
            rows,await ReadProjectedThroughAsync(connection,user,ct));
    }

    public async Task<CommercialCoverageOverview> GetCoverageAsync(SalesReportingUserIdentity user,
        DateOnly from,DateOnly to,CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);
        var access=await ResolveAccessAsync(connection,user,ct);
        if(access.SupplierId is not null)throw new SalesReportingForbiddenException("A supplier cannot access commercial coverage data.");
        await using var command=new SqlCommand("""
          ;WITH Dates AS(SELECT @From d UNION ALL SELECT DATEADD(day,1,d) FROM Dates WHERE d<@To),
          Planned AS(SELECT c.SellerId,MAX(c.SellerName) SellerName,c.RouteId,MAX(c.RouteName) RouteName,c.ZoneId,MAX(c.ZoneName) ZoneName,COUNT_BIG(*) Planned
            FROM reporting.CommercialCoverageAssignmentFacts c INNER JOIN Dates d ON ((DATEDIFF(day,'19000101',d.d)%7)+1)=c.DayOfWeek
              AND d.d>=c.ValidFromBusinessDate AND (c.ValidToBusinessDateExclusive IS NULL OR d.d<c.ValidToBusinessDateExclusive)
            WHERE c.TenantId=@TenantId AND c.BusinessId=@BusinessId AND (@SellerId IS NULL OR c.SellerId=@SellerId)
            GROUP BY c.SellerId,c.RouteId,c.ZoneId),
          Visits AS(SELECT SellerId,RouteId,SUM(CONVERT(bigint,CASE WHEN Status=N'Visited' THEN 1 ELSE 0 END)) Visited,
            SUM(CONVERT(bigint,CASE WHEN Status=N'Skipped' THEN 1 ELSE 0 END)) Skipped,
            SUM(CONVERT(bigint,CASE WHEN HasOrder=1 THEN 1 ELSE 0 END)) Ordered
            FROM reporting.CommercialReportVisitFacts WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND VisitDate BETWEEN @From AND @To
              AND (@SellerId IS NULL OR SellerId=@SellerId) GROUP BY SellerId,RouteId)
          SELECT p.SellerId,p.SellerName,p.RouteId,p.RouteName,p.ZoneId,p.ZoneName,p.Planned,
            COALESCE(v.Visited,0),COALESCE(v.Skipped,0),CASE WHEN p.Planned>COALESCE(v.Visited,0)+COALESCE(v.Skipped,0)
              THEN p.Planned-COALESCE(v.Visited,0)-COALESCE(v.Skipped,0) ELSE 0 END,COALESCE(v.Ordered,0)
          FROM Planned p LEFT JOIN Visits v ON v.SellerId=p.SellerId AND v.RouteId=p.RouteId
          ORDER BY p.SellerName,p.RouteName OPTION(MAXRECURSION 0);
          """,connection);
        Scope(command,user);Date(command,"@From",from);Date(command,"@To",to);command.Parameters.AddWithValue("@SellerId",(object?)access.SellerId??DBNull.Value);
        var rows=new List<CommercialCoverageRow>();await using var reader=await command.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
        {var planned=reader.GetInt64(6);var visited=reader.GetInt64(7);var skipped=reader.GetInt64(8);var ordered=reader.GetInt64(10);
            rows.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetGuid(2),reader.GetString(3),reader.IsDBNull(4)?null:reader.GetGuid(4),
                reader.IsDBNull(5)?null:reader.GetString(5),planned,visited,skipped,reader.GetInt64(9),ordered,
                Percent(visited+skipped,planned),Percent(visited,planned),Percent(ordered,planned)));}
        await reader.CloseAsync();
        var totalPlanned=rows.Sum(x=>x.PlannedVisits);var totalVisited=rows.Sum(x=>x.VisitedCount);var totalSkipped=rows.Sum(x=>x.SkippedCount);var totalOrdered=rows.Sum(x=>x.OrderedCount);
        DateOnly? available=null;await using(var first=new SqlCommand("SELECT MIN(ValidFromBusinessDate) FROM reporting.CommercialCoverageAssignmentFacts WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND (@SellerId IS NULL OR SellerId=@SellerId);",connection))
        {Scope(first,user);first.Parameters.AddWithValue("@SellerId",(object?)access.SellerId??DBNull.Value);var result=await first.ExecuteScalarAsync(ct);if(result is DateTime date)available=DateOnly.FromDateTime(date);}
        return new(totalPlanned,totalVisited,totalSkipped,rows.Sum(x=>x.MissingCount),totalOrdered,Percent(totalVisited+totalSkipped,totalPlanned),
            Percent(totalVisited,totalPlanned),Percent(totalOrdered,totalPlanned),available,rows,await ReadProjectedThroughAsync(connection,user,ct));
    }

    public async Task<SupplierImpactOverview> GetSupplierImpactAsync(SalesReportingUserIdentity user,
        DateOnly from,DateOnly to,CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);
        var access=await ResolveAccessAsync(connection,user,ct);if(access.SellerId is not null)
            throw new SalesReportingForbiddenException("A seller cannot access supplier impact data.");
        var days=to.DayNumber-from.DayNumber+1;var comparisonTo=from.AddDays(-1);var comparisonFrom=comparisonTo.AddDays(1-days);
        await using var command=new SqlCommand("""
          ;WITH Covered AS(SELECT CONVERT(bigint,COUNT(DISTINCT CustomerId)) Customers FROM reporting.CommercialCoverageAssignmentFacts
              WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND ValidFromBusinessDate<=@To
                AND (ValidToBusinessDateExclusive IS NULL OR ValidToBusinessDateExclusive>@From)),
          CurrentSales AS(SELECT SupplierId,MAX(SupplierName) SupplierName,CONVERT(bigint,COUNT(DISTINCT CustomerId)) Impacted,
              SUM(TotalAmount) Sales,SUM(UntaxedAmount-RecognizedCostAmount) Profit FROM reporting.SalesReportLineFacts
              WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND BusinessLocalDate BETWEEN @From AND @To AND SupplierId IS NOT NULL GROUP BY SupplierId),
          PreviousSales AS(SELECT SupplierId,SUM(TotalAmount) Sales FROM reporting.SalesReportLineFacts
              WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND BusinessLocalDate BETWEEN @ComparisonFrom AND @ComparisonTo AND SupplierId IS NOT NULL GROUP BY SupplierId),
          CurrentPurchases AS(SELECT SupplierId,MAX(SupplierName) SupplierName,SUM(TotalAmount) Purchases FROM reporting.PurchaseReportDocuments
              WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND BusinessLocalDate BETWEEN @From AND @To GROUP BY SupplierId),
          PreviousPurchases AS(SELECT SupplierId,SUM(TotalAmount) Purchases FROM reporting.PurchaseReportDocuments
              WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND BusinessLocalDate BETWEEN @ComparisonFrom AND @ComparisonTo GROUP BY SupplierId),
          Keys AS(SELECT SupplierId FROM CurrentSales UNION SELECT SupplierId FROM CurrentPurchases)
          SELECT k.SupplierId,COALESCE(cs.SupplierName,cp.SupplierName,s.Name),covered.Customers,COALESCE(cs.Impacted,0),
            COALESCE(cs.Sales,0),COALESCE(cs.Profit,0),COALESCE(cp.Purchases,0),COALESCE(ps.Sales,0),COALESCE(pp.Purchases,0)
          FROM Keys k INNER JOIN dbo.Suppliers s ON s.SupplierId=k.SupplierId CROSS JOIN Covered covered
          LEFT JOIN CurrentSales cs ON cs.SupplierId=k.SupplierId LEFT JOIN PreviousSales ps ON ps.SupplierId=k.SupplierId
          LEFT JOIN CurrentPurchases cp ON cp.SupplierId=k.SupplierId LEFT JOIN PreviousPurchases pp ON pp.SupplierId=k.SupplierId
          WHERE (@SupplierId IS NULL OR k.SupplierId=@SupplierId) ORDER BY COALESCE(cs.Sales,0) DESC,s.Name;
          """,connection);
        Scope(command,user);Date(command,"@From",from);Date(command,"@To",to);Date(command,"@ComparisonFrom",comparisonFrom);Date(command,"@ComparisonTo",comparisonTo);
        command.Parameters.AddWithValue("@SupplierId",(object?)access.SupplierId??DBNull.Value);
        var raw=new List<(Guid Id,string Name,long Covered,long Impacted,decimal Sales,decimal Profit,decimal Purchases,decimal PreviousSales,decimal PreviousPurchases)>();
        await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))raw.Add((reader.GetGuid(0),reader.GetString(1),reader.GetInt64(2),reader.GetInt64(3),
            reader.GetDecimal(4),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetDecimal(7),reader.GetDecimal(8)));
        await reader.CloseAsync();
        var totalSales=raw.Sum(x=>x.Sales);var rows=raw.Select(x=>new SupplierImpactRow(x.Id,x.Name,x.Covered,x.Impacted,Percent(x.Impacted,x.Covered),x.Sales,x.Profit,
            totalSales==0?0:decimal.Round(x.Sales*100m/totalSales,2),x.Purchases,x.PreviousSales,x.PreviousPurchases,Growth(x.Sales,x.PreviousSales),Growth(x.Purchases,x.PreviousPurchases))).ToArray();
        return new(rows.Select(x=>x.CoveredCustomers).DefaultIfEmpty().Max(),rows.Sum(x=>x.ImpactedCustomers),rows.Sum(x=>x.NetSales),rows.Sum(x=>x.NetPurchases),rows,
            await ReadProjectedThroughAsync(connection,user,ct));
    }
    public async Task<IReadOnlyList<SellerOrderReportRow>> ListSellerOrdersAsync(SalesReportingUserIdentity user,DateOnly from,DateOnly to,CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);
        var access=await ResolveAccessAsync(connection,user,ct);
        if(access.SupplierId is not null)throw new SalesReportingForbiddenException("A supplier cannot access seller performance data.");
        await using var command=new SqlCommand("""
          SELECT SellerId,SellerName,COUNT_BIG(*),COUNT_BIG(DISTINCT CustomerId),SUM(TotalAmount),
            SUM(CONVERT(bigint,CASE WHEN Status IN(2,3,4) THEN 1 ELSE 0 END)),SUM(CONVERT(bigint,CASE WHEN RequiresStockReview=1 OR Status=5 THEN 1 ELSE 0 END)),
            SUM(CONVERT(bigint,CASE WHEN InvoiceDocumentId IS NOT NULL THEN 1 ELSE 0 END))
          FROM reporting.CommercialReportOrderFacts WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND CreatedDate BETWEEN @From AND @To
            AND (@AccessSellerId IS NULL OR SellerId=@AccessSellerId)
          GROUP BY SellerId,SellerName ORDER BY SUM(TotalAmount) DESC,SellerName;
          """,connection);
        command.Parameters.AddWithValue("@TenantId",user.TenantId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        command.Parameters.AddWithValue("@AccessSellerId",(object?)access.SellerId??DBNull.Value);
        command.Parameters.Add("@From",SqlDbType.Date).Value=from.ToDateTime(TimeOnly.MinValue);command.Parameters.Add("@To",SqlDbType.Date).Value=to.ToDateTime(TimeOnly.MinValue);
        var rows=new List<SellerOrderReportRow>();await using var reader=await command.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))rows.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetInt64(2),reader.GetInt64(3),reader.GetDecimal(4),reader.GetInt64(5),reader.GetInt64(6),reader.GetInt64(7)));
        return rows;
    }

    public async Task<CommercialVisitReportPage> ListVisitsAsync(SalesReportingUserIdentity user,
        DateOnly from,DateOnly to,Guid? sellerId,Guid? routeId,string? status,bool? hasOrder,
        int page,int pageSize,CancellationToken cancellationToken)
    {
        await using var connection=connections.Create();await connection.OpenAsync(cancellationToken);
        var access=await ResolveAccessAsync(connection,user,cancellationToken);
        if(access.SupplierId is not null)throw new SalesReportingForbiddenException("A supplier cannot access commercial visit data.");
        sellerId=Constrain(sellerId,access.SellerId,"seller");
        await using var command=new SqlCommand("""
            SELECT RouteVisitId,VisitDate,OccurredAt,SellerId,SellerName,RouteId,RouteName,ZoneName,
              CustomerId,CustomerName,Status,HasOrder,OrderId,SkipReason,VisitObservation,
              COUNT(*) OVER(),SUM(CONVERT(bigint,CASE WHEN Status=N'Visited' THEN 1 ELSE 0 END)) OVER(),
              SUM(CONVERT(bigint,CASE WHEN HasOrder=1 THEN 1 ELSE 0 END)) OVER()
            FROM reporting.CommercialReportVisitFacts
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND VisitDate BETWEEN @From AND @To
              AND (@SellerId IS NULL OR SellerId=@SellerId) AND (@RouteId IS NULL OR RouteId=@RouteId)
              AND (@Status IS NULL OR Status=@Status) AND (@HasOrder IS NULL OR HasOrder=@HasOrder)
            ORDER BY VisitDate DESC,OccurredAt DESC,RouteVisitId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """,connection);
        Scope(command,user);Date(command,"@From",from);Date(command,"@To",to);
        command.Parameters.AddWithValue("@SellerId",(object?)sellerId??DBNull.Value);
        command.Parameters.AddWithValue("@RouteId",(object?)routeId??DBNull.Value);
        command.Parameters.AddWithValue("@Status",(object?)status??DBNull.Value);
        command.Parameters.AddWithValue("@HasOrder",(object?)hasOrder??DBNull.Value);
        command.Parameters.AddWithValue("@Offset",(page-1)*pageSize);command.Parameters.AddWithValue("@PageSize",pageSize);
        var rows=new List<CommercialVisitReportRow>();var total=0;long visited=0,ordered=0;
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(reader.GetGuid(0),DateOnly.FromDateTime(reader.GetDateTime(1)),reader.GetDateTimeOffset(2),
                reader.GetGuid(3),reader.GetString(4),reader.GetGuid(5),reader.GetString(6),reader.IsDBNull(7)?null:reader.GetString(7),
                reader.GetGuid(8),reader.GetString(9),reader.GetString(10),reader.GetBoolean(11),reader.IsDBNull(12)?null:reader.GetGuid(12),
                reader.IsDBNull(13)?null:reader.GetString(13),reader.IsDBNull(14)?null:reader.GetString(14)));
            total=reader.GetInt32(15);visited=reader.GetInt64(16);ordered=reader.GetInt64(17);
        }
        return new(rows,page,pageSize,total,visited,ordered,visited==0?0:decimal.Round(ordered*100m/visited,2));
    }

    public async Task<SalesTodayOverview> GetTodayAsync(
        SalesReportingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var access=await ResolveAccessAsync(connection,user,cancellationToken);

        await using var business = new SqlCommand("""
            SELECT TimeZone FROM dbo.Businesses
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
            """, connection);
        Scope(business, user);
        var timeZoneId = (string?)await business.ExecuteScalarAsync(cancellationToken)
            ?? throw new SalesReportingForbiddenException(
                "The reporting business is outside the authenticated tenant.");
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new InvalidOperationException(
                $"Business time zone '{timeZoneId}' is not available on this host.", exception);
        }

        var businessDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone).Date);
        if(access.SellerId is not null||access.SupplierId is not null)
        {
            var scopedFilter=new SalesReportFilter(businessDate,businessDate,SellerId:access.SellerId,SupplierId:access.SupplierId);
            var scopedTotals=await ReadFilteredTotalsAsync(connection,user,scopedFilter,cancellationToken);
            await using var scopedDetail=new SqlCommand("""
              SELECT COUNT_BIG(DISTINCT CustomerId),MAX(ProjectedAt) FROM reporting.SalesReportLineFacts
              WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND BusinessLocalDate=@BusinessDate
                AND (@SellerId IS NULL OR SellerId=@SellerId) AND (@SupplierId IS NULL OR SupplierId=@SupplierId);
              """,connection);
            Scope(scopedDetail,user);Date(scopedDetail,"@BusinessDate",businessDate);
            scopedDetail.Parameters.AddWithValue("@SellerId",(object?)access.SellerId??DBNull.Value);
            scopedDetail.Parameters.AddWithValue("@SupplierId",(object?)access.SupplierId??DBNull.Value);
            await using var scopedReader=await scopedDetail.ExecuteReaderAsync(cancellationToken);await scopedReader.ReadAsync(cancellationToken);
            var scopedCustomers=scopedReader.GetInt64(0);DateTimeOffset? scopedProjected=scopedReader.IsDBNull(1)?null:scopedReader.GetDateTimeOffset(1);
            var scopedAverage=scopedTotals.DocumentCount==0?0:decimal.Round(scopedTotals.NetTotalSales/scopedTotals.DocumentCount,2);
            var scopedBase=scopedTotals.NetTotalSales+scopedTotals.Returns;
            return new(businessDate,scopedTotals,scopedCustomers,scopedAverage,
                scopedBase==0?0:decimal.Round(scopedTotals.Returns/scopedBase*100m,2),scopedProjected);
        }
        var totals = await ReadTotalsAsync(
            connection, user, businessDate, businessDate, cancellationToken);

        await using var detail = new SqlCommand("""
            SELECT COUNT_BIG(DISTINCT CustomerId),MAX(ProjectedAt)
            FROM reporting.SalesReportDocuments
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId
              AND BusinessLocalDate=@BusinessDate;
            """, connection);
        Scope(detail, user);
        Date(detail, "@BusinessDate", businessDate);
        long customers;
        DateTimeOffset? projectedThrough;
        await using (var reader = await detail.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            customers = reader.GetInt64(0);
            projectedThrough = reader.IsDBNull(1) ? null : reader.GetDateTimeOffset(1);
        }

        var averageTicket = totals.DocumentCount == 0
            ? 0
            : decimal.Round(totals.NetTotalSales / totals.DocumentCount, 2);
        var returnBase = totals.NetTotalSales + totals.Returns;
        var returnRate = returnBase == 0
            ? 0
            : decimal.Round(totals.Returns / returnBase * 100m, 2);
        return new(businessDate, totals, customers, averageTicket, returnRate,
            projectedThrough);
    }

    public async Task<SalesReportSummary> GetSummaryAsync(
        SalesReportingUserIdentity user, SalesReportFilter filter,
        DateOnly? comparisonFrom, DateOnly? comparisonTo, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        filter=await ConstrainAsync(connection,user,filter,cancellationToken);
        var filtered = HasDimensionFilter(filter);
        var current = filtered
            ? await ReadFilteredTotalsAsync(connection, user, filter, cancellationToken)
            : await ReadTotalsAsync(connection, user, filter.From, filter.To, cancellationToken);
        SalesReportTotals? comparison = null;
        if (comparisonFrom is { } compareFrom && comparisonTo is { } compareTo)
            comparison = filtered
                ? await ReadFilteredTotalsAsync(connection, user,
                    filter with { From=compareFrom, To=compareTo }, cancellationToken)
                : await ReadTotalsAsync(connection, user, compareFrom, compareTo, cancellationToken);
        var trend = filtered
            ? await ReadFilteredTrendAsync(connection, user, filter, cancellationToken)
            : await ReadTrendAsync(connection, user, filter.From, filter.To, cancellationToken);
        await using var checkpoint = new SqlCommand("""
            SELECT MAX(c.LastProjectedAt)
            FROM reporting.SalesReportingCheckpoints c
            INNER JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId
            WHERE c.BusinessId=@BusinessId AND b.TenantId=@TenantId;
            """, connection);
        Scope(checkpoint, user);
        var projected = await checkpoint.ExecuteScalarAsync(cancellationToken);
        decimal? change = comparison is null ? null : comparison.NetTotalSales == 0
            ? current.NetTotalSales == 0 ? 0 : null
            : decimal.Round((current.NetTotalSales-comparison.NetTotalSales)/
                decimal.Abs(comparison.NetTotalSales)*100m, 2);
        return new(current, comparison, change, trend,
            projected is null or DBNull ? null : (DateTimeOffset)projected);
    }

    public async Task<IReadOnlyList<SalesReportBreakdownRow>> GetBreakdownAsync(
        SalesReportingUserIdentity user, SalesReportFilter filter, string dimension,
        int limit, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        filter=await ConstrainAsync(connection,user,filter,cancellationToken);
        if ((dimension is SalesReportingDimensions.Customer or SalesReportingDimensions.Seller or
            SalesReportingDimensions.Supplier or SalesReportingDimensions.Product or
            SalesReportingDimensions.Category or SalesReportingDimensions.Warehouse) &&
            filter.CustomerId is null && filter.SellerId is null && filter.SupplierId is null &&
            filter.ProductId is null && filter.CategoryId is null && filter.WarehouseId is null &&
            filter.DocumentType is null)
            return await ReadDailyDimensionBreakdownAsync(
                connection, user, filter, dimension, limit, cancellationToken);
        return dimension switch
        {
            SalesReportingDimensions.PaymentMethod => await ReadPaymentBreakdownAsync(
                connection, user, filter, limit, cancellationToken),
            SalesReportingDimensions.Tax => await ReadTaxBreakdownAsync(
                connection, user, filter, limit, cancellationToken),
            _ => await ReadLineBreakdownAsync(
                connection, user, filter, dimension, limit, cancellationToken)
        };
    }

    private static async Task<IReadOnlyList<SalesReportBreakdownRow>> ReadDailyDimensionBreakdownAsync(
        SqlConnection connection, SalesReportingUserIdentity user, SalesReportFilter filter,
        string dimension, int limit, CancellationToken token)
    {
        var dimensionType = char.ToUpperInvariant(dimension[0]) + dimension[1..];
        const string sql = """
            WITH grouped AS
            (
              SELECT DimensionKey [Key],MAX(DimensionLabel) Label,SUM(DocumentCount) Documents,
                SUM(Quantity) Quantity,SUM(GrossSales) Gross,SUM(Discounts) Discounts,
                SUM(Returns) Returns,SUM(NetUntaxedSales) Untaxed,SUM(NetTax) Tax,
                SUM(NetTotalSales) Net,SUM(NetRecognizedCost) Cost,SUM(GrossProfit) Profit
              FROM reporting.SalesReportDailyDimensionTotals t
              INNER JOIN dbo.Businesses b ON b.BusinessId=t.BusinessId
              WHERE t.BusinessId=@BusinessId AND b.TenantId=@TenantId
                AND t.BusinessLocalDate BETWEEN @From AND @To AND t.DimensionType=@DimensionType
              GROUP BY DimensionKey
            )
            SELECT TOP(@Limit) [Key],Label,Documents,Quantity,Gross,Discounts,Returns,Untaxed,
              Tax,Net,Cost,Profit,CASE WHEN Untaxed=0 THEN 0 ELSE Profit/Untaxed*100 END,
              CASE WHEN SUM(Net) OVER()=0 THEN 0 ELSE Net/SUM(Net) OVER()*100 END
            FROM grouped ORDER BY Net DESC,Label;
            """;
        await using var command = new SqlCommand(sql, connection);
        Scope(command, user); Date(command, "@From", filter.From); Date(command, "@To", filter.To);
        command.Parameters.AddWithValue("@DimensionType", dimensionType);
        command.Parameters.AddWithValue("@Limit", limit);
        return await ReadBreakdownRowsAsync(command, token);
    }

    public async Task<SalesReportDocumentPage> ListDocumentsAsync(
        SalesReportingUserIdentity user, SalesReportFilter filter, int page, int pageSize,
        string? search, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT d.DocumentId,d.DocumentType,d.DocumentNumber,d.FiscalNumber,d.IssuedAt,
                   d.CustomerName,d.SellerName,d.WarehouseName,d.GrossAmount,d.DiscountAmount,
                   d.UntaxedAmount,d.TaxAmount,d.TotalAmount,d.ReturnedTotalAmount,
                   d.TotalAmount-d.ReturnedTotalAmount,
                   (d.UntaxedAmount-d.ReturnedUntaxedAmount)-
                     (d.RecognizedCostAmount-d.ReturnedCostAmount),d.FiscalStatus,
                   COUNT(*) OVER()
            FROM reporting.SalesReportDocuments d
            WHERE d.TenantId=@TenantId AND d.BusinessId=@BusinessId
              AND d.BusinessLocalDate BETWEEN @From AND @To
              AND (@CustomerId IS NULL OR d.CustomerId=@CustomerId)
              AND (@SellerId IS NULL OR d.SellerId=@SellerId)
              AND (@WarehouseId IS NULL OR d.WarehouseId=@WarehouseId)
              AND (@DocumentType IS NULL OR d.DocumentType=@DocumentType)
              AND ((@ProductId IS NULL AND @SupplierId IS NULL AND @CategoryId IS NULL) OR EXISTS(
                    SELECT 1 FROM reporting.SalesReportLineFacts f
                    WHERE f.OriginalSaleDocumentId=d.DocumentId
                      AND (@ProductId IS NULL OR f.ProductId=@ProductId)
                      AND (@SupplierId IS NULL OR f.SupplierId=@SupplierId)
                      AND (@CategoryId IS NULL OR f.CategoryId=@CategoryId)))
              AND (@Search IS NULL OR d.DocumentNumber LIKE N'%'+@Search+N'%'
                    OR d.FiscalNumber LIKE N'%'+@Search+N'%'
                    OR d.CustomerName LIKE N'%'+@Search+N'%'
                    OR d.CustomerIdentification LIKE N'%'+@Search+N'%'
                    OR d.SellerName LIKE N'%'+@Search+N'%'
                    OR EXISTS(SELECT 1 FROM reporting.SalesReportLineFacts f
                      WHERE f.OriginalSaleDocumentId=d.DocumentId AND
                        (f.ProductCode LIKE N'%'+@Search+N'%' OR f.ProductName LIKE N'%'+@Search+N'%')))
            ORDER BY d.BusinessLocalDate DESC,d.DocumentId DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        filter=await ConstrainAsync(connection,user,filter,cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddFilter(command, user, filter);
        command.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("@Offset", (page-1)*pageSize);
        command.Parameters.AddWithValue("@PageSize", pageSize);
        var rows = new List<SalesReportDocumentRow>(); var total = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(ReadDocument(reader)); total = reader.GetInt32(17);
        }
        return new(rows, page, pageSize, total);
    }

    public async Task<SalesReportDocumentDetail?> GetDocumentAsync(
        SalesReportingUserIdentity user, Guid documentId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var access=await ResolveAccessAsync(connection,user,cancellationToken);
        await using var header = new SqlCommand("""
            SELECT d.DocumentId,d.DocumentType,d.DocumentNumber,d.FiscalNumber,d.IssuedAt,
                   d.CustomerName,d.SellerName,d.WarehouseName,d.GrossAmount,d.DiscountAmount,
                   d.UntaxedAmount,d.TaxAmount,d.TotalAmount,d.ReturnedTotalAmount,
                   d.TotalAmount-d.ReturnedTotalAmount,
                   (d.UntaxedAmount-d.ReturnedUntaxedAmount)-
                     (d.RecognizedCostAmount-d.ReturnedCostAmount),d.FiscalStatus
            FROM reporting.SalesReportDocuments d
            WHERE d.DocumentId=@DocumentId AND d.TenantId=@TenantId AND d.BusinessId=@BusinessId
              AND (@AccessSellerId IS NULL OR d.SellerId=@AccessSellerId)
              AND (@AccessSupplierId IS NULL OR EXISTS(SELECT 1 FROM reporting.SalesReportLineFacts scoped
                    WHERE scoped.OriginalSaleDocumentId=d.DocumentId AND scoped.SupplierId=@AccessSupplierId));
            """, connection);
        Scope(header, user); header.Parameters.AddWithValue("@DocumentId", documentId);
        header.Parameters.AddWithValue("@AccessSellerId",(object?)access.SellerId??DBNull.Value);
        header.Parameters.AddWithValue("@AccessSupplierId",(object?)access.SupplierId??DBNull.Value);
        SalesReportDocumentRow document;
        await using (var reader = await header.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            document = ReadDocument(reader);
        }
        await using var lines = new SqlCommand("""
            SELECT FactId,MovementType,OccurredAt,ProductCode,ProductName,CategoryName,
                   Quantity,GrossAmount,DiscountAmount,UntaxedAmount,TaxAmount,TotalAmount,
                   RecognizedCostAmount,ReturnReasonCode,ReturnDisposition
            FROM reporting.SalesReportLineFacts
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId
              AND OriginalSaleDocumentId=@DocumentId
              AND (@AccessSellerId IS NULL OR SellerId=@AccessSellerId)
              AND (@AccessSupplierId IS NULL OR SupplierId=@AccessSupplierId)
            ORDER BY OccurredAt,MovementType,SourceLineNumber;
            """, connection);
        Scope(lines, user); lines.Parameters.AddWithValue("@DocumentId", documentId);
        lines.Parameters.AddWithValue("@AccessSellerId",(object?)access.SellerId??DBNull.Value);
        lines.Parameters.AddWithValue("@AccessSupplierId",(object?)access.SupplierId??DBNull.Value);
        var detail = new List<SalesReportLineRow>();
        await using var lineReader = await lines.ExecuteReaderAsync(cancellationToken);
        while (await lineReader.ReadAsync(cancellationToken))
            detail.Add(new(lineReader.GetGuid(0),lineReader.GetString(1),lineReader.GetDateTimeOffset(2),
                lineReader.GetString(3),lineReader.GetString(4),lineReader.IsDBNull(5)?null:lineReader.GetString(5),
                lineReader.GetDecimal(6),lineReader.GetDecimal(7),lineReader.GetDecimal(8),lineReader.GetDecimal(9),
                lineReader.GetDecimal(10),lineReader.GetDecimal(11),lineReader.GetDecimal(12),
                lineReader.IsDBNull(13)?null:lineReader.GetString(13),lineReader.IsDBNull(14)?null:lineReader.GetString(14)));
        return new(document, detail);
    }

    private static async Task<SalesReportTotals> ReadTotalsAsync(SqlConnection connection,
        SalesReportingUserIdentity user, DateOnly from, DateOnly to, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            SELECT COALESCE(SUM(DocumentCount),0),COALESCE(SUM(UnitsSold),0),
              COALESCE(SUM(UnitsReturned),0),COALESCE(SUM(GrossSales),0),
              COALESCE(SUM(Discounts),0),COALESCE(SUM(Returns),0),
              COALESCE(SUM(NetUntaxedSales),0),COALESCE(SUM(NetTax),0),
              COALESCE(SUM(NetTotalSales),0),COALESCE(SUM(NetRecognizedCost),0),
              COALESCE(SUM(GrossProfit),0),COALESCE(SUM(CreditSales),0),
              COALESCE(SUM(Collected),0),COALESCE(SUM(Refunded),0)
            FROM reporting.SalesReportDailyTotals t
            INNER JOIN dbo.Businesses b ON b.BusinessId=t.BusinessId
            WHERE t.BusinessId=@BusinessId AND b.TenantId=@TenantId
              AND t.BusinessLocalDate BETWEEN @From AND @To;
            """, connection);
        Scope(command,user); Date(command,"@From",from); Date(command,"@To",to);
        await using var r=await command.ExecuteReaderAsync(token); await r.ReadAsync(token);
        var profit=r.GetDecimal(10);var net=r.GetDecimal(6);
        return new(r.GetInt64(0),r.GetDecimal(1),r.GetDecimal(2),r.GetDecimal(3),r.GetDecimal(4),
            r.GetDecimal(5),net,r.GetDecimal(7),r.GetDecimal(8),r.GetDecimal(9),profit,
            net==0?0:decimal.Round(profit/net*100m,2),r.GetDecimal(11),r.GetDecimal(12),r.GetDecimal(13));
    }

    private static async Task<SalesReportTotals> ReadFilteredTotalsAsync(SqlConnection connection,
        SalesReportingUserIdentity user, SalesReportFilter filter, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            SELECT COUNT(DISTINCT f.OriginalSaleDocumentId),
              COALESCE(SUM(CASE WHEN f.MovementType=N'Sale' THEN f.Quantity ELSE 0 END),0),
              COALESCE(-SUM(CASE WHEN f.MovementType=N'Return' THEN f.Quantity ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN f.MovementType=N'Sale' THEN f.GrossAmount ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN f.MovementType=N'Sale' THEN f.DiscountAmount ELSE 0 END),0),
              COALESCE(-SUM(CASE WHEN f.MovementType=N'Return' THEN f.TotalAmount ELSE 0 END),0),
              COALESCE(SUM(f.UntaxedAmount),0),COALESCE(SUM(f.TaxAmount),0),
              COALESCE(SUM(f.TotalAmount),0),COALESCE(SUM(f.RecognizedCostAmount),0),
              COALESCE(SUM(f.UntaxedAmount-f.RecognizedCostAmount),0)
            FROM reporting.SalesReportLineFacts f
            INNER JOIN reporting.SalesReportDocuments d ON d.DocumentId=f.OriginalSaleDocumentId
            WHERE f.TenantId=@TenantId AND f.BusinessId=@BusinessId
              AND f.BusinessLocalDate BETWEEN @From AND @To
              AND (@CustomerId IS NULL OR f.CustomerId=@CustomerId)
              AND (@SellerId IS NULL OR f.SellerId=@SellerId)
              AND (@SupplierId IS NULL OR f.SupplierId=@SupplierId)
              AND (@ProductId IS NULL OR f.ProductId=@ProductId)
              AND (@CategoryId IS NULL OR f.CategoryId=@CategoryId)
              AND (@WarehouseId IS NULL OR f.WarehouseId=@WarehouseId)
              AND (@DocumentType IS NULL OR d.DocumentType=@DocumentType);
            """, connection);
        AddFilter(command,user,filter);
        await using var r=await command.ExecuteReaderAsync(token);await r.ReadAsync(token);
        var untaxed=r.GetDecimal(6);var profit=r.GetDecimal(10);
        return new(r.GetInt32(0),r.GetDecimal(1),r.GetDecimal(2),r.GetDecimal(3),r.GetDecimal(4),
            r.GetDecimal(5),untaxed,r.GetDecimal(7),r.GetDecimal(8),r.GetDecimal(9),profit,
            untaxed==0?0:decimal.Round(profit/untaxed*100m,2),0,0,0);
    }

    private static async Task<IReadOnlyList<SalesReportTrendPoint>> ReadFilteredTrendAsync(
        SqlConnection connection,SalesReportingUserIdentity user,SalesReportFilter filter,CancellationToken token)
    {
        await using var command=new SqlCommand("""
          SELECT f.BusinessLocalDate,COUNT(DISTINCT f.OriginalSaleDocumentId),
            SUM(CASE WHEN f.MovementType=N'Sale' THEN f.GrossAmount ELSE 0 END),
            -SUM(CASE WHEN f.MovementType=N'Return' THEN f.TotalAmount ELSE 0 END),
            SUM(f.TotalAmount),SUM(f.UntaxedAmount-f.RecognizedCostAmount)
          FROM reporting.SalesReportLineFacts f INNER JOIN reporting.SalesReportDocuments d ON d.DocumentId=f.OriginalSaleDocumentId
          WHERE f.TenantId=@TenantId AND f.BusinessId=@BusinessId AND f.BusinessLocalDate BETWEEN @From AND @To
            AND (@CustomerId IS NULL OR f.CustomerId=@CustomerId) AND (@SellerId IS NULL OR f.SellerId=@SellerId)
            AND (@SupplierId IS NULL OR f.SupplierId=@SupplierId) AND (@ProductId IS NULL OR f.ProductId=@ProductId)
            AND (@CategoryId IS NULL OR f.CategoryId=@CategoryId) AND (@WarehouseId IS NULL OR f.WarehouseId=@WarehouseId)
            AND (@DocumentType IS NULL OR d.DocumentType=@DocumentType)
          GROUP BY f.BusinessLocalDate ORDER BY f.BusinessLocalDate;
          """,connection);AddFilter(command,user,filter);var rows=new List<SalesReportTrendPoint>();
        await using var r=await command.ExecuteReaderAsync(token);while(await r.ReadAsync(token))rows.Add(new(DateOnly.FromDateTime(r.GetDateTime(0)),r.GetInt32(1),r.GetDecimal(2),r.GetDecimal(3),r.GetDecimal(4),r.GetDecimal(5)));return rows;
    }

    private static async Task<IReadOnlyList<SalesReportTrendPoint>> ReadTrendAsync(
        SqlConnection connection,SalesReportingUserIdentity user,DateOnly from,DateOnly to,CancellationToken token)
    {
        await using var command=new SqlCommand("""
          SELECT BusinessLocalDate,SUM(DocumentCount),SUM(GrossSales),SUM(Returns),
                 SUM(NetTotalSales),SUM(GrossProfit)
          FROM reporting.SalesReportDailyTotals t INNER JOIN dbo.Businesses b ON b.BusinessId=t.BusinessId
          WHERE t.BusinessId=@BusinessId AND b.TenantId=@TenantId AND BusinessLocalDate BETWEEN @From AND @To
          GROUP BY BusinessLocalDate ORDER BY BusinessLocalDate;
          """,connection);Scope(command,user);Date(command,"@From",from);Date(command,"@To",to);
        var rows=new List<SalesReportTrendPoint>();await using var r=await command.ExecuteReaderAsync(token);
        while(await r.ReadAsync(token))rows.Add(new(DateOnly.FromDateTime(r.GetDateTime(0)),r.GetInt64(1),r.GetDecimal(2),r.GetDecimal(3),r.GetDecimal(4),r.GetDecimal(5)));
        return rows;
    }

    private static async Task<IReadOnlyList<SalesReportBreakdownRow>> ReadLineBreakdownAsync(
        SqlConnection connection,SalesReportingUserIdentity user,SalesReportFilter filter,
        string dimension,int limit,CancellationToken token)
    {
        var (key,label)=dimension switch
        {
            "customer" => ("COALESCE(CONVERT(nvarchar(36),d.CustomerId),N'final-consumer')","d.CustomerName"),
            "seller" => ("COALESCE(CONVERT(nvarchar(36),d.SellerId),N'no-seller')","d.SellerName"),
            "supplier" => ("COALESCE(CONVERT(nvarchar(36),f.SupplierId),N'no-supplier')","COALESCE(f.SupplierName,N'Sin proveedor asociado')"),
            "product" => ("CONVERT(nvarchar(36),f.ProductId)","f.ProductName"),
            "category" => ("COALESCE(CONVERT(nvarchar(36),f.CategoryId),N'no-category')","COALESCE(f.CategoryName,N'Sin categoría')"),
            "warehouse" => ("CONVERT(nvarchar(36),d.WarehouseId)","d.WarehouseName"),
            "day" => ("CONVERT(nvarchar(10),f.BusinessLocalDate,23)","CONVERT(nvarchar(10),f.BusinessLocalDate,23)"),
            "hour" => ("RIGHT(N'0'+CONVERT(nvarchar(2),DATEPART(HOUR,f.OccurredAt)),2)+N':00'","RIGHT(N'0'+CONVERT(nvarchar(2),DATEPART(HOUR,f.OccurredAt)),2)+N':00'"),
            "month" => ("CONVERT(nvarchar(7),f.BusinessLocalDate,126)","CONVERT(nvarchar(7),f.BusinessLocalDate,126)"),
            _ => throw new InvalidOperationException("Unsupported line dimension.")
        };
        var sql=$"""
          WITH grouped AS(SELECT {key} [Key],{label} Label,CONVERT(bigint,COUNT(DISTINCT f.OriginalSaleDocumentId)) Documents,
            SUM(f.Quantity) Quantity,SUM(CASE WHEN f.MovementType=N'Sale' THEN f.GrossAmount ELSE 0 END) Gross,
            SUM(CASE WHEN f.MovementType=N'Sale' THEN f.DiscountAmount ELSE 0 END) Discounts,
            -SUM(CASE WHEN f.MovementType=N'Return' THEN f.TotalAmount ELSE 0 END) Returns,
            SUM(f.UntaxedAmount) Untaxed,SUM(f.TaxAmount) Tax,SUM(f.TotalAmount) Net,
            SUM(f.RecognizedCostAmount) Cost,SUM(f.UntaxedAmount-f.RecognizedCostAmount) Profit
          FROM reporting.SalesReportLineFacts f INNER JOIN reporting.SalesReportDocuments d ON d.DocumentId=f.OriginalSaleDocumentId
          WHERE f.TenantId=@TenantId AND f.BusinessId=@BusinessId AND f.BusinessLocalDate BETWEEN @From AND @To
            AND (@CustomerId IS NULL OR f.CustomerId=@CustomerId) AND (@SellerId IS NULL OR f.SellerId=@SellerId)
            AND (@SupplierId IS NULL OR f.SupplierId=@SupplierId)
            AND (@ProductId IS NULL OR f.ProductId=@ProductId)
            AND (@CategoryId IS NULL OR f.CategoryId=@CategoryId)
            AND (@WarehouseId IS NULL OR f.WarehouseId=@WarehouseId)
            AND (@DocumentType IS NULL OR d.DocumentType=@DocumentType) GROUP BY {key},{label})
          SELECT TOP(@Limit) [Key],Label,Documents,Quantity,Gross,Discounts,Returns,Untaxed,Tax,Net,Cost,Profit,
            CASE WHEN Untaxed=0 THEN 0 ELSE Profit/Untaxed*100 END,
            CASE WHEN SUM(Net) OVER()=0 THEN 0 ELSE Net/SUM(Net) OVER()*100 END
          FROM grouped ORDER BY Net DESC,Label;
          """;
        await using var command=new SqlCommand(sql,connection);AddFilter(command,user,filter);command.Parameters.AddWithValue("@Limit",limit);
        return await ReadBreakdownRowsAsync(command,token);
    }

    private static async Task<IReadOnlyList<SalesReportBreakdownRow>> ReadPaymentBreakdownAsync(
        SqlConnection connection,SalesReportingUserIdentity user,SalesReportFilter filter,int limit,CancellationToken token)
    {
        await using var command=new SqlCommand("""
          WITH grouped AS(SELECT p.MethodCode [Key],p.MethodCode Label,CONVERT(bigint,COUNT(DISTINCT p.SourceDocumentId)) Documents,
            SUM(p.Amount) Net FROM reporting.SalesReportPaymentFacts p
            WHERE p.TenantId=@TenantId AND p.BusinessId=@BusinessId AND p.BusinessLocalDate BETWEEN @From AND @To
              AND EXISTS(SELECT 1 FROM reporting.SalesReportLineFacts f
                INNER JOIN reporting.SalesReportDocuments d ON d.DocumentId=f.OriginalSaleDocumentId
                WHERE f.TenantId=p.TenantId AND f.BusinessId=p.BusinessId AND f.SourceDocumentId=p.SourceDocumentId
                  AND (@CustomerId IS NULL OR f.CustomerId=@CustomerId) AND (@SellerId IS NULL OR f.SellerId=@SellerId)
                  AND (@SupplierId IS NULL OR f.SupplierId=@SupplierId) AND (@ProductId IS NULL OR f.ProductId=@ProductId)
                  AND (@CategoryId IS NULL OR f.CategoryId=@CategoryId) AND (@WarehouseId IS NULL OR f.WarehouseId=@WarehouseId)
                  AND (@DocumentType IS NULL OR d.DocumentType=@DocumentType))
            GROUP BY p.MethodCode)
          SELECT TOP(@Limit) g.[Key],g.Label,g.Documents,CAST(0 AS decimal(19,6)),g.Net,
            CAST(0 AS decimal(19,4)),CAST(0 AS decimal(19,4)),g.Net,CAST(0 AS decimal(19,4)),g.Net,
            CAST(0 AS decimal(19,4)),g.Net,CAST(100 AS decimal(19,4)),
            CASE WHEN SUM(g.Net) OVER()=0 THEN 0 ELSE g.Net/SUM(g.Net) OVER()*100 END
          FROM grouped g ORDER BY g.Net DESC;
          """,connection);AddFilter(command,user,filter);command.Parameters.AddWithValue("@Limit",limit);
        return await ReadBreakdownRowsAsync(command,token);
    }

    private static async Task<IReadOnlyList<SalesReportBreakdownRow>> ReadTaxBreakdownAsync(
        SqlConnection connection,SalesReportingUserIdentity user,SalesReportFilter filter,int limit,CancellationToken token)
    {
        await using var command=new SqlCommand("""
          WITH grouped AS(SELECT CONCAT(t.TaxCode,N' · ',FORMAT(t.TaxRate*100,N'0.##'),N'%') [Key],
            CONCAT(t.TaxCode,N' · ',FORMAT(t.TaxRate*100,N'0.##'),N'%') Label,
            CONVERT(bigint,COUNT(DISTINCT t.SourceDocumentId)) Documents,SUM(t.TaxableAmount) Base,SUM(t.TaxAmount) Tax,SUM(t.TotalAmount) Net
            FROM reporting.SalesReportTaxFacts t WHERE t.TenantId=@TenantId AND t.BusinessId=@BusinessId
              AND t.BusinessLocalDate BETWEEN @From AND @To
              AND EXISTS(SELECT 1 FROM reporting.SalesReportLineFacts f
                INNER JOIN reporting.SalesReportDocuments d ON d.DocumentId=f.OriginalSaleDocumentId
                WHERE f.TenantId=t.TenantId AND f.BusinessId=t.BusinessId AND f.SourceDocumentId=t.SourceDocumentId
                  AND (@CustomerId IS NULL OR f.CustomerId=@CustomerId) AND (@SellerId IS NULL OR f.SellerId=@SellerId)
                  AND (@SupplierId IS NULL OR f.SupplierId=@SupplierId) AND (@ProductId IS NULL OR f.ProductId=@ProductId)
                  AND (@CategoryId IS NULL OR f.CategoryId=@CategoryId) AND (@WarehouseId IS NULL OR f.WarehouseId=@WarehouseId)
                  AND (@DocumentType IS NULL OR d.DocumentType=@DocumentType))
              GROUP BY t.TaxCode,t.TaxRate)
          SELECT TOP(@Limit) g.[Key],g.Label,g.Documents,CAST(0 AS decimal(19,6)),g.Base,
            CAST(0 AS decimal(19,4)),CAST(0 AS decimal(19,4)),g.Base,g.Tax,g.Net,
            CAST(0 AS decimal(19,4)),g.Base,CAST(100 AS decimal(19,4)),
            CASE WHEN SUM(g.Net) OVER()=0 THEN 0 ELSE g.Net/SUM(g.Net) OVER()*100 END
          FROM grouped g ORDER BY g.Net DESC;
          """,connection);AddFilter(command,user,filter);command.Parameters.AddWithValue("@Limit",limit);
        return await ReadBreakdownRowsAsync(command,token);
    }

    private static async Task<IReadOnlyList<SalesReportBreakdownRow>> ReadBreakdownRowsAsync(SqlCommand command,CancellationToken token)
    {var rows=new List<SalesReportBreakdownRow>();await using var r=await command.ExecuteReaderAsync(token);while(await r.ReadAsync(token))rows.Add(new(r.GetString(0),r.GetString(1),r.GetInt64(2),r.GetDecimal(3),r.GetDecimal(4),r.GetDecimal(5),r.GetDecimal(6),r.GetDecimal(7),r.GetDecimal(8),r.GetDecimal(9),r.GetDecimal(10),r.GetDecimal(11),decimal.Round(r.GetDecimal(12),2),decimal.Round(r.GetDecimal(13),2)));return rows;}

    private static SalesReportDocumentRow ReadDocument(SqlDataReader r)=>new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.GetDateTimeOffset(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetDecimal(8),r.GetDecimal(9),r.GetDecimal(10),r.GetDecimal(11),r.GetDecimal(12),r.GetDecimal(13),r.GetDecimal(14),r.GetDecimal(15),r.IsDBNull(16)?null:r.GetString(16));
    private async Task<SalesReportFilter> ConstrainAsync(SqlConnection connection,SalesReportingUserIdentity user,
        SalesReportFilter filter,CancellationToken token)
    {
        var access=await ResolveAccessAsync(connection,user,token);
        return filter with { SellerId=Constrain(filter.SellerId,access.SellerId,"seller"),
            SupplierId=Constrain(filter.SupplierId,access.SupplierId,"supplier") };
    }
    private static Guid? Constrain(Guid? requested,Guid? required,string dimension)
    {
        if(required is null)return requested;
        if(requested is not null&&requested!=required)
            throw new SalesReportingForbiddenException($"The authenticated {dimension} cannot widen the report scope.");
        return required;
    }
    private static async Task<ReportingAccess> ResolveAccessAsync(SqlConnection connection,
        SalesReportingUserIdentity user,CancellationToken token)
    {
        // Execution-context authorization has already validated tenant/business access.
        // Platform administrators can legitimately inspect a tenant without being
        // provisioned as a local AppUser in it; ReadAll is the explicit boundary.
        if(user.Permissions.Contains(SalesReportingPermissionCodes.ReadAll))
            return new ReportingAccess(null,null);

        await using var command=new SqlCommand("""
          SELECT seller.SellerId,supplier.SupplierId
          FROM dbo.AppUsers app
          LEFT JOIN dbo.CommerceSellers seller ON seller.PartyId=app.PartyId AND seller.BusinessId=@BusinessId AND seller.IsActive=1
          LEFT JOIN dbo.Suppliers supplier ON supplier.PartyId=app.PartyId AND supplier.BusinessId=@BusinessId AND supplier.IsActive=1
          WHERE app.UserId=@UserId AND app.TenantId=@TenantId AND app.IsActive=1;
          """,connection);
        command.Parameters.AddWithValue("@UserId",user.UserId);command.Parameters.AddWithValue("@TenantId",user.TenantId);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        await using var reader=await command.ExecuteReaderAsync(token);
        if(!await reader.ReadAsync(token))throw new SalesReportingForbiddenException("The reporting identity is not active in this tenant.");
        Guid? seller=reader.IsDBNull(0)?null:reader.GetGuid(0);Guid? supplier=reader.IsDBNull(1)?null:reader.GetGuid(1);
        if(seller is not null&&supplier is not null)
            throw new SalesReportingForbiddenException("The reporting identity maps ambiguously to both seller and supplier.");
        return new(seller,supplier);
    }
    private sealed record ReportingAccess(Guid? SellerId,Guid? SupplierId);
    private static decimal Percent(long numerator,long denominator)=>denominator==0?0:decimal.Round(numerator*100m/denominator,2);
    private static decimal? Growth(decimal current,decimal previous)=>previous==0?(current==0?0:null):
        decimal.Round((current-previous)*100m/decimal.Abs(previous),2);
    private static async Task<DateTimeOffset?> ReadProjectedThroughAsync(SqlConnection connection,
        SalesReportingUserIdentity user,CancellationToken token)
    {
        await using var command=new SqlCommand("""
          SELECT MAX(LastProjectedAt) FROM reporting.SalesReportingCheckpoints c
          INNER JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId
          WHERE c.BusinessId=@BusinessId AND b.TenantId=@TenantId;
          """,connection);Scope(command,user);var value=await command.ExecuteScalarAsync(token);
        return value is null or DBNull?null:(DateTimeOffset)value;
    }
    private static void AddFilter(SqlCommand c,SalesReportingUserIdentity u,SalesReportFilter f){Scope(c,u);Date(c,"@From",f.From);Date(c,"@To",f.To);c.Parameters.AddWithValue("@CustomerId",(object?)f.CustomerId??DBNull.Value);c.Parameters.AddWithValue("@SellerId",(object?)f.SellerId??DBNull.Value);c.Parameters.AddWithValue("@SupplierId",(object?)f.SupplierId??DBNull.Value);c.Parameters.AddWithValue("@ProductId",(object?)f.ProductId??DBNull.Value);c.Parameters.AddWithValue("@CategoryId",(object?)f.CategoryId??DBNull.Value);c.Parameters.AddWithValue("@WarehouseId",(object?)f.WarehouseId??DBNull.Value);c.Parameters.AddWithValue("@DocumentType",(object?)f.DocumentType??DBNull.Value);}
    private static bool HasDimensionFilter(SalesReportFilter f)=>f.CustomerId is not null||f.SellerId is not null||f.SupplierId is not null||f.ProductId is not null||f.CategoryId is not null||f.WarehouseId is not null||f.DocumentType is not null;
    private static void Scope(SqlCommand c,SalesReportingUserIdentity u){c.Parameters.AddWithValue("@TenantId",u.TenantId);c.Parameters.AddWithValue("@BusinessId",u.BusinessId);}
    private static void Date(SqlCommand c,string name,DateOnly value)=>c.Parameters.Add(new SqlParameter(name,SqlDbType.Date){Value=value.ToDateTime(TimeOnly.MinValue)});
}
