using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlSalesReportingStore
{
    private static string PurchaseScope => """
        FROM reporting.PurchaseReportLineFacts p
        WHERE p.TenantId=@TenantId AND p.BusinessId=@BusinessId AND p.BusinessLocalDate BETWEEN @From AND @To
          AND (@SupplierId IS NULL OR p.SupplierId=@SupplierId) AND (@ProductId IS NULL OR p.ProductId=@ProductId)
          AND (@WarehouseId IS NULL OR p.WarehouseId=@WarehouseId)
          AND (@CategoryId IS NULL OR TRY_CONVERT(uniqueidentifier,COALESCE(JSON_VALUE(p.CategoryHierarchyJson,'$.SubgroupId'),JSON_VALUE(p.CategoryHierarchyJson,'$.GroupId'),JSON_VALUE(p.CategoryHierarchyJson,'$.LineId'),JSON_VALUE(p.CategoryHierarchyJson,'$.AreaId')))=@CategoryId)
        """+PurchaseSelectionSql;

    private static async Task<SalesReportExplorerPage> ExploreSuppliersAsync(SqlConnection connection,
        SalesReportingUserIdentity user,SalesReportExplorerRequest request,Guid? requiredSupplierId,CancellationToken cancellationToken)
    {
        var order=request.OrderBy switch {"net-desc"=>"NetUntaxed DESC","net-asc"=>"NetUntaxed ASC",
            "label"=>"Label ASC","profit-desc"=>"Profit DESC","quantity-desc"=>"Quantity DESC",
            _=>throw new SalesReportingValidationException("El orden no es válido.")};
        var source=ExplorerSource+" AND a.ItemKind=N'Product'";
        var comparison=ComparisonRange(request);
        var previousSql=comparison is null?"""
            SELECT TOP(0) GroupKey,Label,NetUntaxed Net,Customers Buyers INTO #PreviousSales FROM #Sales;
            SELECT TOP(0) GroupKey,Label,Net INTO #PreviousPurchases FROM #Purchases;
            """:$"""
            SELECT COALESCE(CONVERT(nvarchar(36),a.SupplierId),N'unattributed') GroupKey,
              COALESCE(a.SupplierName,N'Sin proveedor atribuido') GroupLabel,{FinancialActivityColumns} INTO #PreviousActivity {ComparisonSource(source)};
            SELECT a.GroupKey,MAX(a.GroupLabel) Label,SUM(a.UntaxedAmount) Net,
              COUNT_BIG(DISTINCT CASE WHEN a.MovementType=N'Sale' AND a.UntaxedAmount>0 THEN a.CustomerId END) Buyers
            INTO #PreviousSales FROM #PreviousActivity a GROUP BY a.GroupKey;
            SELECT CONVERT(nvarchar(36),p.SupplierId) GroupKey,MAX(p.SupplierName) Label,SUM(p.FunctionalNetAmount) Net
            INTO #PreviousPurchases {ComparisonSource(PurchaseScope)} GROUP BY p.SupplierId;
            """;
        var comparisonSql=comparison is null?$"SELECT {ExplorerMeasures} FROM #Activity a WHERE 1=0;":$"""
            SELECT {ExplorerMeasures} FROM #PreviousActivity a
            WHERE EXISTS(SELECT 1 FROM #Candidates selected WHERE selected.GroupKey=a.GroupKey);
            """;
        await using var command=new SqlCommand($"""
            {ImpactUniverseSql}
            SELECT COALESCE(CONVERT(nvarchar(36),a.SupplierId),N'unattributed') GroupKey,
              COALESCE(a.SupplierName,N'Sin proveedor atribuido') GroupLabel,{FinancialActivityColumns}
            INTO #Activity {source};
            SELECT a.GroupKey,MAX(a.GroupLabel) Label,{ExplorerMeasures}
            INTO #Sales FROM #Activity a GROUP BY a.GroupKey;
            SELECT CONVERT(nvarchar(36),p.SupplierId) GroupKey,MAX(p.SupplierName) Label,
              SUM(p.Quantity) Quantity,SUM(p.FunctionalNetAmount) Net,SUM(p.FunctionalTaxAmount) Tax,SUM(p.FunctionalTotalAmount) Total,MAX(p.ProjectedAt) ProjectedAt,
              SUM(CASE WHEN p.SourceDocumentType=N'GoodsReceipt' THEN p.FunctionalNetAmount ELSE 0 END) Receipts,
              -SUM(CASE WHEN p.SourceDocumentType=N'PurchaseReturn' THEN p.FunctionalNetAmount ELSE 0 END) Returns
            INTO #Purchases {PurchaseScope} GROUP BY p.SupplierId;
            {previousSql}
            WITH supplierKeys AS(SELECT GroupKey FROM #Sales UNION SELECT GroupKey FROM #Purchases
              UNION SELECT GroupKey FROM #PreviousSales UNION SELECT GroupKey FROM #PreviousPurchases)
            SELECT keysValue.GroupKey,COALESCE(s.Label,p.Label,ps.Label,pp.Label) Label,
              COALESCE(s.Documents,0) Documents,COALESCE(s.Customers,0) Customers,COALESCE(s.Quantity,0) Quantity,
              COALESCE(s.GrossSales,0) GrossSales,COALESCE(s.Discounts,0) Discounts,COALESCE(s.Returns,0) Returns,
              COALESCE(s.NetUntaxed,0) NetUntaxed,COALESCE(s.Tax,0) Tax,COALESCE(s.NetTotal,0) NetTotal,
              CASE WHEN s.GroupKey IS NULL THEN 0 ELSE s.RecognizedCost END RecognizedCost,
              CASE WHEN s.GroupKey IS NULL THEN 0 ELSE s.Profit END Profit,COALESCE(s.MissingCostLines,0) MissingCostLines,
              COALESCE(p.Net,0) PurchaseNet,COALESCE(p.Tax,0) PurchaseTax,COALESCE(p.Total,0) PurchaseTotal,COALESCE(p.Quantity,0) PurchaseQuantity,
              COALESCE(ps.Net,0) PreviousSales,COALESCE(ps.Buyers,0) PreviousBuyers,COALESCE(pp.Net,0) PreviousPurchases,p.ProjectedAt,
              COALESCE(s.ReturnsWithTax,0) ReturnsWithTax,COALESCE(s.ReturnedTax,0) ReturnedTax,
              COALESCE(p.Receipts,0) PurchaseReceipts,COALESCE(p.Returns,0) PurchaseReturns
            INTO #Candidates FROM supplierKeys keysValue LEFT JOIN #Sales s ON s.GroupKey=keysValue.GroupKey
            LEFT JOIN #Purchases p ON p.GroupKey=keysValue.GroupKey LEFT JOIN #PreviousSales ps ON ps.GroupKey=keysValue.GroupKey
            LEFT JOIN #PreviousPurchases pp ON pp.GroupKey=keysValue.GroupKey;
            DELETE #Candidates WHERE @SupplierSearch IS NOT NULL AND Label NOT LIKE @SupplierSearch ESCAPE N'~';
            SELECT COUNT_BIG(*) KnownCustomers FROM #Universe;
            SELECT * FROM #Candidates ORDER BY {order},GroupKey OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            SELECT {ExplorerMeasures},(SELECT COUNT_BIG(*) FROM #Candidates) TotalGroups,MAX(a.ProjectedAt) ProjectedThrough
            FROM #Activity a WHERE EXISTS(SELECT 1 FROM #Candidates selected WHERE selected.GroupKey=a.GroupKey);
            SELECT COALESCE(SUM(PurchaseNet),0),COALESCE(SUM(PurchaseTax),0),COALESCE(SUM(PurchaseTotal),0),
              COALESCE(SUM(PurchaseQuantity),0),COALESCE(SUM(PreviousPurchases),0),MAX(ProjectedAt),
              COALESCE(SUM(PurchaseReceipts),0),COALESCE(SUM(PurchaseReturns),0) FROM #Candidates;
            {comparisonSql}
            """,connection);
        AddExplorerParameters(command,user,request with {Search=null});
        var search=string.IsNullOrWhiteSpace(request.Search)?null:request.Search.Trim();
        Text(command,"@SupplierSearch",search is null?null:$"%{search.Replace("~","~~").Replace("%","~%").Replace("_","~_").Replace("[","~[")}%",322);
        Identifier(command,"@RequiredSupplier",requiredSupplierId);
        await using var reader=await ExecuteExplorerReaderAsync(command,user.BusinessId,cancellationToken);await reader.ReadAsync(cancellationToken);
        var known=reader.GetInt64(0);var rows=new List<SalesReportExplorerRow>();await reader.NextResultAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))
        {
            var measures=ReadExplorerMeasures(reader,2);var purchased=reader.GetDecimal(14);
            rows.Add(new(reader.GetString(0),reader.GetString(1),measures,Supplier:new(purchased,reader.GetDecimal(15),reader.GetDecimal(16),
                reader.GetDecimal(17),purchased==0?null:decimal.Round(measures.NetUntaxed/purchased,4),Percent(measures.Customers,known),
                comparison is null?null:reader.GetDecimal(20),comparison is null?null:reader.GetDecimal(18),comparison is null?null:reader.GetInt64(19),
                reader.GetDecimal(24),reader.GetDecimal(25))));
        }
        await reader.NextResultAsync(cancellationToken);await reader.ReadAsync(cancellationToken);
        var totals=ReadExplorerMeasures(reader,0);var totalCount=reader.GetInt64(reader.GetOrdinal("TotalGroups"));var through=NullableDate(reader,reader.GetOrdinal("ProjectedThrough"));
        await reader.NextResultAsync(cancellationToken);await reader.ReadAsync(cancellationToken);
        var overview=new SalesReportSupplierOverview(known,reader.GetDecimal(0),reader.GetDecimal(1),reader.GetDecimal(2),reader.GetDecimal(3),
            comparison is null?null:reader.GetDecimal(4),reader.GetDecimal(6),reader.GetDecimal(7));
        var purchasesThrough=NullableDate(reader,5);
        if(purchasesThrough is not null&&(through is null||purchasesThrough>through))through=purchasesThrough;
        await reader.NextResultAsync(cancellationToken);await reader.ReadAsync(cancellationToken);
        return new(rows,request.Page,request.PageSize,totalCount,totals,through,true,[],[],
            Comparison:comparison is null?null:new(comparison.Value.From,comparison.Value.To,ReadExplorerMeasures(reader,0)),Supplier:overview);
    }
}
