using System.Data;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlSalesReportingStore(
    SqlServerConnectionFactory connections,
    TimeProvider timeProvider)
    : ISalesReportingStore
{
    public async Task<SupplierImpactOverview> GetSupplierImpactAsync(SalesReportingUserIdentity user,
        DateOnly from,DateOnly to,CancellationToken ct,int page=1,int pageSize=200,string comparison="none")
    {
        var result=await ExploreAsync(user,new(new(from,to),"supplier",page,pageSize,Module:"supplier",Comparison:comparison),ct);
        var known=result.Supplier!.KnownCustomers;
        var rows=result.Items.Select(row=>new SupplierImpactRow(Guid.TryParse(row.Key,out var supplierId)?supplierId:null,row.Label,
            known,row.Measures.Customers,row.Supplier!.PenetrationPercent,row.Measures.NetUntaxed,row.Measures.Profit,
            result.Totals.NetUntaxed==0?0:decimal.Round(row.Measures.NetUntaxed/result.Totals.NetUntaxed*100m,2),row.Supplier.NetPurchases,
            row.Supplier.PreviousNetSales,row.Supplier.PreviousNetPurchases,
            row.Supplier.PreviousNetSales is null?null:Growth(row.Measures.NetUntaxed,row.Supplier.PreviousNetSales.Value),
            row.Supplier.PreviousNetPurchases is null?null:Growth(row.Supplier.NetPurchases,row.Supplier.PreviousNetPurchases.Value))).ToArray();
        return new(known,result.Totals.Customers,result.Totals.NetUntaxed,result.Supplier.NetPurchases,rows,result.ProjectedThrough,page,pageSize,result.TotalCount);
    }

    public async Task<SalesTodayOverview> GetTodayAsync(
        SalesReportingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection=connections.Create();
        await connection.OpenAsync(cancellationToken);
        var access=await ResolveAccessAsync(connection,user,cancellationToken);
        var date=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(),
            TimeZoneInfo.FindSystemTimeZoneById(access.TimeZoneId)).Date);
        var filter=new SalesReportFilter(date,date,SellerId:access.SellerId,SupplierId:access.SupplierId);
        var summary=await ReadSummaryAsync(connection,user,filter,null,null,cancellationToken);
        var totals=summary.Report.Current;
        var salesBeforeReturns=totals.NetUntaxedSales+totals.Returns;
        return new(date,totals,summary.Customers,totals.DocumentCount==0?0:decimal.Round(totals.NetTotalSales/totals.DocumentCount,2),
            salesBeforeReturns==0?0:decimal.Round(totals.Returns/salesBeforeReturns*100m,2),summary.Report.ProjectedThrough,
            HasLineScope(filter),access.HasPendingSources,access.HasFailedSources,access.TimeZoneId);
    }

    public async Task<SalesReportSummary> GetSummaryAsync(
        SalesReportingUserIdentity user,SalesReportFilter filter,
        DateOnly? comparisonFrom,DateOnly? comparisonTo,CancellationToken cancellationToken)
    {
        await using var connection=connections.Create();
        await connection.OpenAsync(cancellationToken);
        filter=await ConstrainAsync(connection,user,filter,cancellationToken);
        return (await ReadSummaryAsync(connection,user,filter,comparisonFrom,comparisonTo,cancellationToken)).Report;
    }

    public async Task<IReadOnlyList<SalesReportBreakdownRow>> GetBreakdownAsync(
        SalesReportingUserIdentity user, SalesReportFilter filter, string dimension,
        int limit, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        filter=await ConstrainAsync(connection,user,filter,cancellationToken);
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

    public async Task<SalesReportDocumentPage> ListDocumentsAsync(
        SalesReportingUserIdentity user,SalesReportFilter filter,int page,int pageSize,string? search,CancellationToken cancellationToken)
    {
        var result=await ExploreAsync(user,new(filter,"document",page,pageSize,Search:search),cancellationToken);
        var rows=result.Items.Select(row=>LegacyDocumentRow(row.Document
            ??throw new InvalidOperationException("Falta el snapshot documental de la proyección."),row.Measures,
            row.OfficialTotal,row.RoundingAdjustment,row.RestaurantTip,filter)).ToArray();
        return new(rows,page,pageSize,checked((int)result.TotalCount));
    }

    public async Task<SalesReportDocumentDetail?> GetDocumentAsync(SalesReportingUserIdentity user,Guid documentId,
        CancellationToken cancellationToken,int page=1,int pageSize=200)
    {
        var filter=new SalesReportFilter(DateOnly.MinValue,DateOnly.MaxValue);
        var result=await ExploreAsync(user,new(filter,"document",page,pageSize,DocumentId:documentId),cancellationToken);
        if(result.Document is null)return null;
        var document=LegacyDocumentRow(result.Document,result.Totals,result.OfficialTotal,result.RoundingAdjustment,result.RestaurantTip,filter);
        var lines=result.Items.Select(row=>new SalesReportLineRow(row.FactId,row.MovementType??"Sale",row.OccurredAt,
            row.ItemCode??"",row.Label,null,row.Measures.Quantity,row.Measures.GrossSales,row.Measures.Discounts,
            row.Measures.NetUntaxed,row.Measures.Tax,row.Measures.NetTotal,row.Measures.RecognizedCost,
            row.ReturnReasonCode,row.ReturnDisposition,row.ItemKind,row.Key)).ToArray();
        return new(document,lines,page,pageSize,result.TotalCount);
    }

    private static SalesReportDocumentRow LegacyDocumentRow(SalesReportDocumentSnapshot snapshot,SalesReportMeasures measures,
        decimal? official,decimal? rounding,decimal? tip,SalesReportFilter filter)
    {
        var positiveTotal=snapshot.OriginalOfficialTotal is not null && snapshot.BusinessLocalDate>=filter.From && snapshot.BusinessLocalDate<=filter.To
            ?snapshot.OriginalOfficialTotal.Value:measures.SalesWithTax;
        var net=official??measures.NetTotal;
        return new(snapshot.DocumentId,snapshot.DocumentType,snapshot.DocumentNumber,snapshot.FiscalNumber,snapshot.IssuedAt,
            snapshot.CustomerName,snapshot.SellerName,snapshot.WarehouseName,measures.GrossSales,measures.Discounts,measures.SalesBeforeReturns,
            measures.SalesTax,positiveTotal,rounding,positiveTotal-net,net,measures.Profit,snapshot.FiscalStatus,tip);
    }

    private static async Task<IReadOnlyList<SalesReportBreakdownRow>> ReadLineBreakdownAsync(
        SqlConnection connection,SalesReportingUserIdentity user,SalesReportFilter filter,
        string dimension,int limit,CancellationToken token)
    {
        var (key,label)=ExplorerDimension(dimension);
        await using var command=new SqlCommand($"""
            WITH grouped AS(SELECT {key} [Key],MAX({label}) Label,{ExplorerMeasures}
              {ExplorerSource} GROUP BY {key})
            SELECT TOP(@Limit) [Key],Label,Documents,Quantity,GrossSales,Discounts,Returns,NetUntaxed,Tax,NetTotal,
              RecognizedCost,Profit,Profit/NULLIF(NetUntaxed,0)*100,
              COALESCE(NetUntaxed/NULLIF(SUM(NetUntaxed) OVER(),0)*100,0)
            FROM grouped ORDER BY NetUntaxed DESC,Label,[Key];
            """,connection);
        AddExplorerParameters(command,user,new(filter));command.Parameters.AddWithValue("@Limit",limit);
        return await ReadBreakdownRowsAsync(command,token);
    }

    private static async Task<IReadOnlyList<SalesReportBreakdownRow>> ReadPaymentBreakdownAsync(
        SqlConnection connection,SalesReportingUserIdentity user,SalesReportFilter filter,int limit,CancellationToken token)
    {
        if(HasLineScope(filter))
            throw new SalesReportingValidationException("Los pagos no se distribuyen entre subconjuntos de líneas.");
        await using var command=new SqlCommand($"""
            WITH grouped AS(SELECT p.MethodCode [Key],p.MethodCode Label,COUNT_BIG(DISTINCT p.SourceDocumentId) Documents,SUM(p.Amount) Net
              FROM reporting.SalesReportPaymentFacts p
              WHERE p.TenantId=@TenantId AND p.BusinessId=@BusinessId AND p.BusinessLocalDate BETWEEN @From AND @To
                AND EXISTS(SELECT 1 {ExplorerSource} AND a.SourceDocumentId=p.SourceDocumentId AND a.SourceDocumentType=p.SourceDocumentType)
              GROUP BY p.MethodCode)
            SELECT TOP(@Limit) [Key],Label,Documents,CAST(0 AS decimal(19,6)),Net,
              CAST(0 AS decimal(19,4)),CAST(0 AS decimal(19,4)),Net,CAST(0 AS decimal(19,4)),Net,
              CAST(NULL AS decimal(19,4)),CAST(NULL AS decimal(19,4)),CAST(NULL AS decimal(19,4)),
              COALESCE(Net/NULLIF(SUM(Net) OVER(),0)*100,0)
            FROM grouped ORDER BY grouped.Net DESC,grouped.[Key];
            """,connection);
        AddExplorerParameters(command,user,new(filter));command.Parameters.AddWithValue("@Limit",limit);
        return await ReadBreakdownRowsAsync(command,token);
    }

    private static async Task<IReadOnlyList<SalesReportBreakdownRow>> ReadTaxBreakdownAsync(
        SqlConnection connection,SalesReportingUserIdentity user,SalesReportFilter filter,int limit,CancellationToken token)
    {
        await using var command=new SqlCommand($"""
            WITH grouped AS(SELECT CONCAT(COALESCE(a.TaxCode,N'Sin snapshot'),N' · ',a.TaxRate,N'%') [Key],
              COUNT_BIG(DISTINCT a.OriginalSaleDocumentId) Documents,SUM(a.UntaxedAmount) Base,SUM(a.TaxAmount) Tax,SUM(a.TotalAmount) Net
              {ExplorerSource} GROUP BY a.TaxCode,a.TaxRate)
            SELECT TOP(@Limit) [Key],[Key],Documents,CAST(0 AS decimal(19,6)),Base,
              CAST(0 AS decimal(19,4)),CAST(0 AS decimal(19,4)),Base,Tax,Net,
              CAST(NULL AS decimal(19,4)),CAST(NULL AS decimal(19,4)),CAST(NULL AS decimal(19,4)),
              COALESCE(Net/NULLIF(SUM(Net) OVER(),0)*100,0)
            FROM grouped ORDER BY grouped.Net DESC,grouped.[Key];
            """,connection);
        AddExplorerParameters(command,user,new(filter));command.Parameters.AddWithValue("@Limit",limit);
        return await ReadBreakdownRowsAsync(command,token);
    }

    private static async Task<IReadOnlyList<SalesReportBreakdownRow>> ReadBreakdownRowsAsync(SqlCommand command,CancellationToken token)
    {var rows=new List<SalesReportBreakdownRow>();await using var r=await ExecuteExplorerReaderAsync(command,(Guid)command.Parameters["@BusinessId"].Value,token);while(await r.ReadAsync(token))rows.Add(new(r.GetString(0),r.GetString(1),r.GetInt64(2),r.GetDecimal(3),r.GetDecimal(4),r.GetDecimal(5),r.GetDecimal(6),r.GetDecimal(7),r.GetDecimal(8),r.GetDecimal(9),NullableDecimal(r,10),NullableDecimal(r,11),r.IsDBNull(12)?null:decimal.Round(r.GetDecimal(12),2),decimal.Round(r.GetDecimal(13),2)));return rows;}

    private async Task<SalesReportFilter> ConstrainAsync(SqlConnection connection,SalesReportingUserIdentity user,
        SalesReportFilter filter,CancellationToken token)
    {
        var access=await ResolveAccessAsync(connection,user,token);
        DemandRestaurantDimensions(access,filter,[]);
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
        await using var command=new SqlCommand("""
          SELECT seller.SellerId,supplier.SupplierId,b.TimeZone,b.PointOfSaleMode,
            CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM reporting.SalesReportingCheckpoints c WHERE c.BusinessId=b.BusinessId AND c.RebuildInProgress=1)
              OR EXISTS(SELECT 1 FROM reporting.SalesReportDocuments d WHERE d.BusinessId=b.BusinessId AND d.ProjectionVersion<@ProjectionVersion)
              OR EXISTS(SELECT 1 FROM reporting.PurchaseReportDocuments purchaseValue WHERE purchaseValue.BusinessId=b.BusinessId AND purchaseValue.ProjectionVersion<@ProjectionVersion)
              THEN 1 ELSE 0 END) RebuildRequired,
            CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM reporting.SalesReportingJobs pending WHERE pending.BusinessId=b.BusinessId AND pending.Status IN(N'Pending',N'Processing')) THEN 1 ELSE 0 END),
            CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM reporting.SalesReportingJobs failed WHERE failed.BusinessId=b.BusinessId AND failed.Status=N'Failed') THEN 1 ELSE 0 END)
          FROM dbo.Businesses b
          LEFT JOIN dbo.AppUsers app ON app.UserId=@UserId AND app.TenantId=b.TenantId AND app.IsActive=1 AND @ReadAll=0
          LEFT JOIN dbo.CommerceSellers seller ON seller.PartyId=app.PartyId AND seller.TenantId=@TenantId AND seller.IsActive=1
          LEFT JOIN dbo.Suppliers supplier ON supplier.PartyId=app.PartyId AND supplier.TenantId=@TenantId AND supplier.IsActive=1
          WHERE b.BusinessId=@BusinessId AND b.TenantId=@TenantId AND (@ReadAll=1 OR app.UserId IS NOT NULL);
          """,connection);
        command.Parameters.AddWithValue("@UserId",user.UserId);command.Parameters.AddWithValue("@TenantId",user.TenantId);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        command.Parameters.AddWithValue("@ReadAll",user.Permissions.Contains(SalesReportingPermissionCodes.ReadAll));
        command.Parameters.AddWithValue("@ProjectionVersion",SqlSalesReportingProjectionWriter.ProjectionVersion);
        await using var reader=await command.ExecuteReaderAsync(token);
        if(!await reader.ReadAsync(token))throw new SalesReportingForbiddenException("The reporting identity is not active in this tenant.");
        Guid? seller=reader.IsDBNull(0)?null:reader.GetGuid(0);Guid? supplier=reader.IsDBNull(1)?null:reader.GetGuid(1);
        var access=new ReportingAccess(seller,supplier,reader.GetString(2),reader.GetString(3),reader.GetBoolean(5),reader.GetBoolean(6));
        var rebuildRequired=reader.GetBoolean(4);
        if((seller is not null&&supplier is not null)||await reader.ReadAsync(token))
            throw new SalesReportingForbiddenException("The reporting identity maps ambiguously to both seller and supplier.");
        if(rebuildRequired)throw new SalesReportingUnavailableException("La proyección requiere completar su reconstrucción antes de consultar cifras.");
        return access;
    }
    private sealed record ReportingAccess(Guid? SellerId,Guid? SupplierId,string TimeZoneId,string PointOfSaleMode,bool HasPendingSources,bool HasFailedSources);
    private static decimal Percent(long numerator,long denominator)=>denominator==0?0:decimal.Round(numerator*100m/denominator,2);
    private static decimal? Growth(decimal current,decimal previous)=>previous==0?(current==0?0:null):
        decimal.Round((current-previous)*100m/decimal.Abs(previous),2);
    private static void AddFilter(SqlCommand c,SalesReportingUserIdentity u,SalesReportFilter f){Scope(c,u);Date(c,"@From",f.From);Date(c,"@To",f.To);c.Parameters.AddWithValue("@CustomerId",(object?)f.CustomerId??DBNull.Value);c.Parameters.AddWithValue("@SellerId",(object?)f.SellerId??DBNull.Value);c.Parameters.AddWithValue("@SupplierId",(object?)f.SupplierId??DBNull.Value);c.Parameters.AddWithValue("@ProductId",(object?)f.ProductId??DBNull.Value);c.Parameters.AddWithValue("@CategoryId",(object?)f.CategoryId??DBNull.Value);c.Parameters.AddWithValue("@WarehouseId",(object?)f.WarehouseId??DBNull.Value);c.Parameters.AddWithValue("@DocumentType",(object?)f.DocumentType??DBNull.Value);}
    private static bool HasDimensionFilter(SalesReportFilter f)=>f.CustomerId is not null||f.SellerId is not null||f.SupplierId is not null||f.ProductId is not null||f.CategoryId is not null||f.WarehouseId is not null||f.DocumentType is not null||f.Selections().Any(selection=>selection.Values is {Count:>0});
    private static bool HasLineScope(SalesReportFilter f)=>f.SupplierId is not null||f.ProductId is not null||f.CategoryId is not null||HasMultipleLineScope(f);
    private static void Scope(SqlCommand c,SalesReportingUserIdentity u){c.Parameters.AddWithValue("@TenantId",u.TenantId);c.Parameters.AddWithValue("@BusinessId",u.BusinessId);}
    private static void Date(SqlCommand c,string name,DateOnly value)=>c.Parameters.Add(new SqlParameter(name,SqlDbType.Date){Value=value.ToDateTime(TimeOnly.MinValue)});
}
