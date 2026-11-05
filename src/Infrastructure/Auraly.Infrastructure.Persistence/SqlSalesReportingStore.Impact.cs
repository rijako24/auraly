using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlSalesReportingStore
{
    private static string ImpactUniverseSql => $"""
            SELECT CONVERT(nvarchar(36),d.CustomerId) GroupKey,MAX(d.CustomerName) Label,MIN(d.BusinessLocalDate) FirstKnown
            INTO #Universe FROM reporting.SalesReportDocuments d
            WHERE d.TenantId=@TenantId AND d.BusinessId=@BusinessId AND d.BusinessLocalDate<=@To
              AND d.CustomerId IS NOT NULL AND d.UntaxedAmount>0
              AND (@CustomerId IS NULL OR d.CustomerId=@CustomerId) AND (@SellerId IS NULL OR d.SellerId=@SellerId)
              AND (@WarehouseId IS NULL OR d.WarehouseId=@WarehouseId) AND (@BilledBy IS NULL OR d.BilledByUserId=@BilledBy)
              AND (@DocumentType IS NULL OR d.DocumentType=@DocumentType)
              AND (@RequiredSupplier IS NULL OR EXISTS(SELECT 1 FROM reporting.SalesReportLineFacts fact
                WHERE fact.TenantId=@TenantId AND fact.BusinessId=@BusinessId AND fact.OriginalSaleDocumentId=d.DocumentId
                  AND fact.MovementType=N'Sale' AND fact.UntaxedAmount>0 AND fact.SupplierId=@RequiredSupplier))
            {UniverseSelectionSql}
            GROUP BY d.CustomerId;
        """;

    private static async Task<SalesReportExplorerPage> ExploreImpactAsync(SqlConnection connection,
        SalesReportingUserIdentity user,SalesReportExplorerRequest request,Guid? requiredSupplierId,CancellationToken cancellationToken)
    {
        if(request.CustomerActivity is not ("all" or "buyers" or "without-purchase"))
            throw new SalesReportingValidationException("El filtro de actividad del cliente no es válido.");
        var order=request.OrderBy switch {"net-desc"=>"NetUntaxed DESC","net-asc"=>"NetUntaxed ASC",
            "label"=>"Label ASC","profit-desc"=>"Profit DESC","quantity-desc"=>"Quantity DESC",
            _=>throw new SalesReportingValidationException("El orden no es válido.")};
        var comparison=ComparisonRange(request);
        var comparisonSql=comparison is null?$"SELECT {ExplorerMeasures} FROM #Activity a WHERE 1=0;":$"""
            SELECT {ExplorerMeasures} {ComparisonSource(ExplorerSource)}
              AND EXISTS(SELECT 1 FROM #Candidates selected WHERE selected.GroupKey=COALESCE(CONVERT(nvarchar(36),a.CustomerId),N'unattributed'));
            """;
        await using var command=new SqlCommand($"""
            {ImpactUniverseSql}
            SELECT COALESCE(CONVERT(nvarchar(36),a.CustomerId),N'unattributed') GroupKey,d.CustomerName GroupLabel,
              {FinancialActivityColumns} INTO #Activity {ExplorerSource};
            SELECT a.GroupKey,MAX(a.GroupLabel) Label,
              {ExplorerMeasures},COUNT_BIG(CASE WHEN a.MovementType=N'Sale' AND a.UntaxedAmount>0 THEN 1 END) PositiveSaleLines
            INTO #Current FROM #Activity a GROUP BY a.GroupKey;
            SELECT universe.GroupKey,universe.Label,
              COALESCE(currentValue.Documents,0) Documents,COALESCE(currentValue.Customers,0) Customers,
              COALESCE(currentValue.Quantity,0) Quantity,COALESCE(currentValue.GrossSales,0) GrossSales,
              COALESCE(currentValue.Discounts,0) Discounts,COALESCE(currentValue.Returns,0) Returns,
              COALESCE(currentValue.NetUntaxed,0) NetUntaxed,COALESCE(currentValue.Tax,0) Tax,COALESCE(currentValue.NetTotal,0) NetTotal,
              CASE WHEN currentValue.GroupKey IS NULL THEN 0 ELSE currentValue.RecognizedCost END RecognizedCost,
              CASE WHEN currentValue.GroupKey IS NULL THEN 0 ELSE currentValue.Profit END Profit,
              COALESCE(currentValue.MissingCostLines,0) MissingCostLines,
              CONVERT(bit,CASE WHEN currentValue.PositiveSaleLines>0 THEN 1 ELSE 0 END) BoughtInPeriod,
              COALESCE(currentValue.ReturnsWithTax,0) ReturnsWithTax,COALESCE(currentValue.ReturnedTax,0) ReturnedTax
            INTO #Candidates FROM #Universe universe LEFT JOIN #Current currentValue ON currentValue.GroupKey=universe.GroupKey
            WHERE @CustomerSearch IS NULL OR universe.Label LIKE @CustomerSearch ESCAPE N'~'
            UNION ALL
            SELECT currentValue.GroupKey,currentValue.Label,currentValue.Documents,currentValue.Customers,currentValue.Quantity,
              currentValue.GrossSales,currentValue.Discounts,currentValue.Returns,currentValue.NetUntaxed,currentValue.Tax,currentValue.NetTotal,
              currentValue.RecognizedCost,currentValue.Profit,currentValue.MissingCostLines,
              CONVERT(bit,CASE WHEN currentValue.PositiveSaleLines>0 THEN 1 ELSE 0 END),currentValue.ReturnsWithTax,currentValue.ReturnedTax
            FROM #Current currentValue WHERE currentValue.GroupKey=N'unattributed'
              AND (@CustomerSearch IS NULL OR currentValue.Label LIKE @CustomerSearch ESCAPE N'~');
            SELECT COUNT_BIG(CASE WHEN GroupKey<>N'unattributed' THEN 1 END),
              COUNT_BIG(CASE WHEN GroupKey<>N'unattributed' AND BoughtInPeriod=1 THEN 1 END),
              (SELECT MIN(FirstKnown) FROM #Universe),
              COALESCE(SUM(CASE WHEN GroupKey=N'unattributed' THEN Documents ELSE 0 END),0)
            FROM #Candidates;
            DELETE #Candidates WHERE (@Activity=N'buyers' AND BoughtInPeriod=0)
              OR (@Activity=N'without-purchase' AND (BoughtInPeriod=1 OR GroupKey=N'unattributed'));
            SELECT * FROM #Candidates ORDER BY {order},GroupKey OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            SELECT {ExplorerMeasures},(SELECT COUNT_BIG(*) FROM #Candidates) TotalGroups,MAX(a.ProjectedAt) ProjectedThrough
            FROM #Activity a WHERE EXISTS(SELECT 1 FROM #Candidates selected WHERE selected.GroupKey=a.GroupKey);
            {comparisonSql}
            """,connection);
        AddExplorerParameters(command,user,request with {Search=null});
        command.Parameters["@WholeActivity"].Value=!HasLineScope(request.Filter)&&request.WaiterPartyId is null;
        var search=string.IsNullOrWhiteSpace(request.Search)?null:request.Search.Trim();
        Text(command,"@CustomerSearch",search is null?null:$"%{search.Replace("~","~~").Replace("%","~%").Replace("_","~_").Replace("[","~[")}%",322);
        Identifier(command,"@RequiredSupplier",requiredSupplierId);
        Text(command,"@Activity",request.CustomerActivity,16);
        await using var reader=await ExecuteExplorerReaderAsync(command,user.BusinessId,cancellationToken);await reader.ReadAsync(cancellationToken);
        var universe=reader.GetInt64(0);var buyers=reader.GetInt64(1);
        var impact=new SalesReportImpactOverview(universe,buyers,universe-buyers,Percent(buyers,universe),
            reader.IsDBNull(2)?null:DateOnly.FromDateTime(reader.GetDateTime(2)),reader.GetInt64(3));
        var rows=new List<SalesReportExplorerRow>();await reader.NextResultAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))rows.Add(new(reader.GetString(0),reader.GetString(1),ReadExplorerMeasures(reader,2),
            BoughtInPeriod:reader.GetBoolean(14)));
        await reader.NextResultAsync(cancellationToken);await reader.ReadAsync(cancellationToken);
        var totalCount=reader.GetInt64(reader.GetOrdinal("TotalGroups"));var totals=ReadExplorerMeasures(reader,0);var through=NullableDate(reader,reader.GetOrdinal("ProjectedThrough"));
        await reader.NextResultAsync(cancellationToken);await reader.ReadAsync(cancellationToken);
        return new(rows,request.Page,request.PageSize,totalCount,totals,through,
            HasLineScope(request.Filter),[],[],Impact:impact,
            Comparison:comparison is null?null:new(comparison.Value.From,comparison.Value.To,ReadExplorerMeasures(reader,0)));
    }
}
