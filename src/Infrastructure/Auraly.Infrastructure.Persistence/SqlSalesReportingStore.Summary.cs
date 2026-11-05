using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlSalesReportingStore
{
    private const string SummaryTotalsSql="""
        SELECT COALESCE(SUM(DocumentCount),0),COALESCE(SUM(UnitsSold),0),COALESCE(SUM(UnitsReturned),0),
          COALESCE(SUM(GrossSales),0),COALESCE(SUM(Discounts),0),COALESCE(SUM(Returns),0),
          COALESCE(SUM(NetUntaxedSales),0),COALESCE(SUM(NetTax),0),COALESCE(SUM(NetTotalSales),0),
          CASE WHEN COUNT_BIG(*)=COUNT_BIG(NetRecognizedCost) THEN COALESCE(SUM(NetRecognizedCost),0) END,
          CASE WHEN COUNT_BIG(*)=COUNT_BIG(NetRecognizedCost) THEN COALESCE(SUM(NetUntaxedSales-NetRecognizedCost),0) END,
          CASE WHEN @WholeDocuments=1 THEN COALESCE(SUM(CreditSales),0) END,
          CASE WHEN @WholeDocuments=1 THEN COALESCE(SUM(Collected),0) END,
          CASE WHEN @WholeDocuments=1 THEN COALESCE(SUM(Refunded),0) END
        FROM #Summary WHERE BusinessLocalDate BETWEEN @CurrentFrom AND @CurrentTo;
        """;

    private static async Task<(SalesReportSummary Report,long Customers)> ReadSummaryAsync(SqlConnection connection,
        SalesReportingUserIdentity user,SalesReportFilter filter,DateOnly? comparisonFrom,DateOnly? comparisonTo,CancellationToken ct,
        bool includeBreakdowns=false)
    {
        var first=comparisonFrom is { } cf&&cf<filter.From?cf:filter.From;
        var last=comparisonTo is { } cto&&cto>filter.To?cto:filter.To;
        var chartColumns=includeBreakdowns?"""
            ,a.BusinessLocalHour,a.SellerId,d.SellerName,a.ProductId,a.ItemName,
              a.SupplierId,a.SupplierName,a.WarehouseId,d.WarehouseName
            """:"";
        var activitySql=$"""
            SELECT a.SourceDocumentId,a.SourceDocumentType,a.BusinessLocalDate,a.MovementType,a.ItemKind,
              a.Quantity,a.GrossAmount,a.DiscountAmount,a.UntaxedAmount,a.TaxAmount,a.TotalAmount,
              a.RecognizedCostAmount,a.OriginalSaleDocumentId,a.CustomerId,a.MissingCostLines,a.ProjectedAt {chartColumns}
            INTO #Activity {ExplorerSource};
            """;
        // Keep document dimensions separate from product/supplier detail. The
        // same canonical measures preserve nullable cost and signed returns.
        var documentColumns="a.SourceDocumentId,a.SourceDocumentType,a.OriginalSaleDocumentId,a.BusinessLocalDate,"+
            "a.CustomerId,a.MovementType,a.ItemKind,a.BusinessLocalHour,a.SellerId,a.SellerName,a.WarehouseId,a.WarehouseName";
        var documentSql=includeBreakdowns?$"""
            WITH documents AS(SELECT {documentColumns},{ExplorerMeasures}
              FROM #Activity a GROUP BY {documentColumns})
            SELECT SourceDocumentId,SourceDocumentType,OriginalSaleDocumentId,BusinessLocalDate,
              CustomerId,MovementType,ItemKind,BusinessLocalHour,SellerId,SellerName,WarehouseId,WarehouseName,
              Quantity,GrossSales GrossAmount,Discounts DiscountAmount,NetUntaxed UntaxedAmount,Tax TaxAmount,
              NetTotal TotalAmount,RecognizedCost RecognizedCostAmount,MissingCostLines
            INTO #DocumentActivity FROM documents;
            """:"";
        var summaryActivity=includeBreakdowns?"#DocumentActivity":"#Activity";
        var activitySummary=HasDimensionFilter(filter)||includeBreakdowns;
        var source=activitySummary?activitySql+documentSql+$"""
            SELECT m.BusinessLocalDate,SUM(m.OfficialTotal) OfficialTotal INTO #Money
            FROM reporting.SalesReportDocumentMovements(@TenantId,@BusinessId,@From,@To) m
            WHERE @WholeDocuments=1 AND EXISTS(SELECT 1 FROM #Activity a WHERE a.SourceDocumentId=m.SourceDocumentId AND a.SourceDocumentType=m.SourceDocumentType)
            GROUP BY m.BusinessLocalDate;
            SELECT p.BusinessLocalDate,
              SUM(CASE WHEN p.MovementType=N'Credit' THEN p.Amount ELSE 0 END) CreditSales,
              SUM(CASE WHEN p.MovementType=N'Payment' THEN p.Amount ELSE 0 END) Collected,
              -SUM(CASE WHEN p.MovementType=N'Refund' THEN p.Amount ELSE 0 END) Refunded
            INTO #Payments FROM reporting.SalesReportPaymentFacts p
            WHERE @WholeDocuments=1 AND p.TenantId=@TenantId AND p.BusinessId=@BusinessId AND p.BusinessLocalDate BETWEEN @From AND @To
              AND EXISTS(SELECT 1 FROM #Activity a WHERE a.SourceDocumentId=p.SourceDocumentId AND a.SourceDocumentType=p.SourceDocumentType)
            GROUP BY p.BusinessLocalDate;
            SELECT a.BusinessLocalDate,COUNT_BIG(DISTINCT CASE WHEN a.MovementType=N'Sale' THEN a.OriginalSaleDocumentId END) DocumentCount,
              SUM(CASE WHEN a.ItemKind=N'Product' AND a.MovementType=N'Sale' THEN a.Quantity ELSE 0 END) UnitsSold,
              -SUM(CASE WHEN a.ItemKind=N'Product' AND a.MovementType=N'Return' THEN a.Quantity ELSE 0 END) UnitsReturned,
              SUM(CASE WHEN a.MovementType=N'Sale' THEN a.GrossAmount ELSE 0 END) GrossSales,
              SUM(CASE WHEN a.MovementType=N'Sale' THEN a.DiscountAmount ELSE 0 END) Discounts,
              -SUM(CASE WHEN a.MovementType=N'Return' THEN a.UntaxedAmount ELSE 0 END) Returns,
              SUM(a.UntaxedAmount) NetUntaxedSales,SUM(a.TaxAmount) NetTax,
              CASE WHEN @WholeDocuments=1 THEN MAX(m.OfficialTotal) ELSE SUM(a.TotalAmount) END NetTotalSales,
              CASE WHEN COUNT_BIG(*)=COUNT_BIG(a.RecognizedCostAmount) THEN SUM(a.RecognizedCostAmount) END NetRecognizedCost,
              MAX(p.CreditSales) CreditSales,MAX(p.Collected) Collected,MAX(p.Refunded) Refunded
            INTO #Summary FROM {summaryActivity} a LEFT JOIN #Money m ON m.BusinessLocalDate=a.BusinessLocalDate
              LEFT JOIN #Payments p ON p.BusinessLocalDate=a.BusinessLocalDate GROUP BY a.BusinessLocalDate;
            """:"""
            SELECT BusinessLocalDate,DocumentCount,UnitsSold,UnitsReturned,GrossSales,Discounts,Returns,NetUntaxedSales,NetTax,NetTotalSales,
              CASE WHEN UnknownCostDocumentCount=0 THEN NetRecognizedCost END NetRecognizedCost,CreditSales,Collected,Refunded
            INTO #Summary FROM reporting.SalesReportDailyTotals
            WHERE BusinessId=@BusinessId AND BusinessLocalDate BETWEEN @From AND @To;
            """;
        var customerSource=activitySummary?"""
            SELECT COUNT_BIG(DISTINCT CustomerId) FROM #Activity
            WHERE MovementType=N'Sale' AND UntaxedAmount>0 AND BusinessLocalDate BETWEEN @CurrentFrom AND @CurrentTo;
            """:"""
            SELECT COUNT_BIG(DISTINCT CustomerId) FROM reporting.SalesReportDocuments
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND UntaxedAmount>0 AND BusinessLocalDate BETWEEN @CurrentFrom AND @CurrentTo;
            """;
        var projectedSource=activitySummary?"SELECT MAX(ProjectedAt) FROM #Activity;":
            "SELECT MAX(LastProjectedAt) FROM reporting.SalesReportingCheckpoints WHERE BusinessId=@BusinessId;";
        await using var command=new SqlCommand(source+SummaryTotalsSql+
            SummaryTotalsSql.Replace("@CurrentFrom","@ComparisonFrom",StringComparison.Ordinal).Replace("@CurrentTo","@ComparisonTo",StringComparison.Ordinal)
              .Replace("FROM #Summary WHERE","FROM #Summary WHERE @HasComparison=1 AND",StringComparison.Ordinal)+"""
            SELECT BusinessLocalDate,SUM(DocumentCount),SUM(GrossSales),SUM(Returns),SUM(NetTotalSales),
              CASE WHEN COUNT_BIG(*)=COUNT_BIG(NetRecognizedCost) THEN SUM(NetUntaxedSales-NetRecognizedCost) END
            FROM #Summary WHERE BusinessLocalDate BETWEEN @CurrentFrom AND @CurrentTo GROUP BY BusinessLocalDate ORDER BY BusinessLocalDate;
            """+projectedSource+customerSource+(includeBreakdowns?SummaryBreakdownsSql(HasLineScope(filter)):""),connection);
        AddExplorerParameters(command,user,new(filter with {From=first,To=last}));
        Date(command,"@CurrentFrom",filter.From);Date(command,"@CurrentTo",filter.To);
        command.Parameters["@ComparisonFrom"].Value=(comparisonFrom??filter.From).ToDateTime(TimeOnly.MinValue);
        command.Parameters["@ComparisonTo"].Value=(comparisonTo??filter.To).ToDateTime(TimeOnly.MinValue);
        command.Parameters["@HasComparison"].Value=comparisonFrom is not null;
        command.Parameters.AddWithValue("@WholeDocuments",!HasLineScope(filter));
        if(includeBreakdowns)command.Parameters.AddWithValue("@Limit",20);
        await using var reader=await ExecuteExplorerReaderAsync(command,user.BusinessId,ct);
        await reader.ReadAsync(ct);var current=ReadSummaryTotals(reader);
        await reader.NextResultAsync(ct);await reader.ReadAsync(ct);
        var comparison=comparisonFrom is null?null:ReadSummaryTotals(reader);
        await reader.NextResultAsync(ct);var trend=new List<SalesReportTrendPoint>();
        while(await reader.ReadAsync(ct))trend.Add(new(DateOnly.FromDateTime(reader.GetDateTime(0)),reader.GetInt64(1),reader.GetDecimal(2),reader.GetDecimal(3),reader.GetDecimal(4),NullableDecimal(reader,5)));
        await reader.NextResultAsync(ct);await reader.ReadAsync(ct);var projected=NullableDate(reader,0);
        await reader.NextResultAsync(ct);await reader.ReadAsync(ct);var customers=reader.GetInt64(0);
        SalesReportSummaryBreakdowns? breakdowns=null;
        if(includeBreakdowns)
        {
            async Task<IReadOnlyList<SalesReportBreakdownRow>> NextBreakdownAsync()
            {
                await reader.NextResultAsync(ct);
                return await ReadBreakdownRowsAsync(reader,ct);
            }
            var hourly=await NextBreakdownAsync();
            IReadOnlyList<SalesReportBreakdownRow> payments=HasLineScope(filter)?[]:await NextBreakdownAsync();
            breakdowns=new(hourly,payments,await NextBreakdownAsync(),await NextBreakdownAsync(),
                await NextBreakdownAsync(),await NextBreakdownAsync());
        }
        var salesBeforeReturns=current.NetUntaxedSales+current.Returns;
        return (new(current,comparison,comparison is null?null:Growth(current.NetTotalSales,comparison.NetTotalSales),trend,projected,
            customers,current.DocumentCount==0?0:decimal.Round(current.NetTotalSales/current.DocumentCount,2),
            salesBeforeReturns==0?0:decimal.Round(current.Returns/salesBeforeReturns*100m,2),HasLineScope(filter),current.NetUntaxedSales+current.NetTax,HasLineScope(filter)?null:current.NetTotalSales-current.NetUntaxedSales-current.NetTax,breakdowns),customers);
    }

    private static string SummaryBreakdownsSql(bool lineScope)
    {
        // All panels share the same filtered activity and the same projection cut.
        const string source="FROM #Activity a WHERE a.BusinessLocalDate BETWEEN @CurrentFrom AND @CurrentTo";
        const string documents="FROM #DocumentActivity a WHERE a.BusinessLocalDate BETWEEN @CurrentFrom AND @CurrentTo";
        string Breakdown(string dimension,int limit)
        {
            var (key,label)=ExplorerDimension(dimension);
            var documentDimension=dimension is SalesReportingDimensions.Hour or SalesReportingDimensions.Seller or SalesReportingDimensions.Warehouse;
            return LineBreakdownSql(documentDimension?documents:source,key,label.Replace("d.","a.",StringComparison.Ordinal),limit);
        }
        return Breakdown(SalesReportingDimensions.Hour,25)+
            (lineScope?"":PaymentBreakdownSql(documents))+
            Breakdown(SalesReportingDimensions.Seller,8)+Breakdown(SalesReportingDimensions.Product,8)+
            Breakdown(SalesReportingDimensions.Supplier,8)+Breakdown(SalesReportingDimensions.Warehouse,8);
    }

    private static SalesReportTotals ReadSummaryTotals(SqlDataReader r)
    {
        var net=r.GetDecimal(6);var profit=NullableDecimal(r,10);
        return new(r.GetInt64(0),r.GetDecimal(1),r.GetDecimal(2),r.GetDecimal(3),r.GetDecimal(4),r.GetDecimal(5),net,r.GetDecimal(7),r.GetDecimal(8),
            NullableDecimal(r,9),profit,net==0||profit is null?null:decimal.Round(profit.Value/net*100m,2),NullableDecimal(r,11),NullableDecimal(r,12),NullableDecimal(r,13));
    }
}
