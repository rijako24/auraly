using System.Data;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlSalesReportingStore
{
    public async Task<SalesReportContext> GetContextAsync(SalesReportingUserIdentity user,CancellationToken cancellationToken)
    {
        await using var connection=connections.Create();
        await connection.OpenAsync(cancellationToken);
        var access=await ResolveAccessAsync(connection,user,cancellationToken);
        await using var command=new SqlCommand("""
            SELECT CatalogCode,Code,Label FROM reference.Options
            WHERE CatalogCode IN(N'sales-report-grouping',N'sales-report-comparison',N'sales-report-document-type',N'sales-report-order',N'sales-report-customer-activity',N'payment-method',N'sales-report-product-returns',N'sales-report-product-composition')
              AND IsActive=1 AND (RequiredPointOfSaleMode IS NULL OR RequiredPointOfSaleMode=@Mode)
            ORDER BY CatalogCode,SortOrder,Code;
            """,connection);
        command.Parameters.AddWithValue("@Mode",access.PointOfSaleMode);
        var catalogs=new Dictionary<string,List<SalesReportOption>>(StringComparer.Ordinal);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))
        {
            var catalog=reader.GetString(0);
            if(!catalogs.TryGetValue(catalog,out var items)) catalogs[catalog]=items=[];
            items.Add(new(reader.GetString(1),reader.GetString(2)));
        }
        var businessDate=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(),
            TimeZoneInfo.FindSystemTimeZoneById(access.TimeZoneId)).Date);
        return new(businessDate,new(businessDate.Year,businessDate.Month,1),access.TimeZoneId,
            access.PointOfSaleMode=="RestaurantBar",catalogs.GetValueOrDefault("sales-report-grouping")??[],
            catalogs.GetValueOrDefault("sales-report-comparison")??[],catalogs.GetValueOrDefault("sales-report-document-type")??[],
            catalogs.GetValueOrDefault("sales-report-order")??[],catalogs.GetValueOrDefault("sales-report-customer-activity")??[],catalogs.GetValueOrDefault("payment-method")??[],
            access.HasPendingSources,access.HasFailedSources,catalogs.GetValueOrDefault("sales-report-product-returns")??[],catalogs.GetValueOrDefault("sales-report-product-composition")??[]);
    }

    private static (string Key,string Label) ExplorerDimension(string dimension) => dimension switch
    {
        "document" => ("CONVERT(nvarchar(36),a.OriginalSaleDocumentId)","d.DocumentNumber"),
        "billed-by" => ("COALESCE(CONVERT(nvarchar(36),d.BilledByUserId),N'unattributed')","COALESCE(d.BilledByName,N'Sin atribución')"),
        "seller" => ("COALESCE(CONVERT(nvarchar(36),a.SellerId),N'unattributed')","d.SellerName"),
        "customer" => ("COALESCE(CONVERT(nvarchar(36),a.CustomerId),N'unattributed')","d.CustomerName"),
        "supplier" => ("COALESCE(CONVERT(nvarchar(36),a.SupplierId),N'unattributed')","COALESCE(a.SupplierName,N'Sin atribución a proveedor')"),
        "product" => ("COALESCE(CONVERT(nvarchar(36),a.ProductId),N'unattributed')","CASE WHEN a.ProductId IS NULL THEN N'Servicios y cargos sin atribución a producto' ELSE a.ItemName END"),
        "category" => ("COALESCE(CONVERT(nvarchar(36),a.CategoryId),N'unattributed')","COALESCE(a.CategoryName,N'Sin atribución a categoría')"),
        "area" => ("COALESCE(JSON_VALUE(a.CategoryHierarchyJson,'$.AreaId'),N'unattributed')","COALESCE(JSON_VALUE(a.CategoryHierarchyJson,'$.AreaName'),N'Sin área histórica')"),
        "category-line" => ("COALESCE(JSON_VALUE(a.CategoryHierarchyJson,'$.LineId'),N'unattributed')","COALESCE(JSON_VALUE(a.CategoryHierarchyJson,'$.LineName'),N'Sin línea histórica')"),
        "category-group" => ("COALESCE(JSON_VALUE(a.CategoryHierarchyJson,'$.GroupId'),N'unattributed')","COALESCE(JSON_VALUE(a.CategoryHierarchyJson,'$.GroupName'),N'Sin grupo histórico')"),
        "subgroup" => ("COALESCE(JSON_VALUE(a.CategoryHierarchyJson,'$.SubgroupId'),N'unattributed')","COALESCE(JSON_VALUE(a.CategoryHierarchyJson,'$.SubgroupName'),N'Sin subgrupo histórico')"),
        "warehouse" => ("COALESCE(CONVERT(nvarchar(36),a.WarehouseId),N'unattributed')","COALESCE(d.WarehouseName,N'Sin bodega')"),
        "table" => ("COALESCE(CONVERT(nvarchar(36),d.TableId),N'unattributed')","CASE WHEN d.TableId IS NULL THEN N'Sin mesa atribuida' ELSE COALESCE(d.TableName,N'Mesa histórica sin nombre') END"),
        "waiter" => ("COALESCE(CONVERT(nvarchar(36),a.WaiterPartyId),N'unattributed')","COALESCE(a.WaiterName,N'Sin atribución a mesero')"),
        "day" => ("CONVERT(nvarchar(10),a.BusinessLocalDate,23)","CONVERT(nvarchar(10),a.BusinessLocalDate,23)"),
        "month" => ("CONVERT(nvarchar(7),a.BusinessLocalDate,126)","CONVERT(nvarchar(7),a.BusinessLocalDate,126)"),
        "hour" => ("COALESCE(RIGHT(N'0'+CONVERT(nvarchar(2),a.BusinessLocalHour),2)+N':00',N'unattributed')",
            "COALESCE(RIGHT(N'0'+CONVERT(nvarchar(2),a.BusinessLocalHour),2)+N':00',N'Sin hora histórica')"),
        "line" => ("CONCAT(a.SourceDocumentId,N'/',a.SourceDocumentType,N'/',a.SourceLineNumber,N'/',a.ItemKind,N'/',a.TaxCode,N'/',a.TaxRate)","a.ItemName"),
        _ => throw new SalesReportingValidationException("La agrupación no es válida.")
    };

    private static string ExplorerSource => """
        FROM reporting.SalesReportActivity(@TenantId,@BusinessId,@From,@To,NULL,@Document,@WholeActivity) a
        JOIN reporting.SalesReportDocuments d ON d.DocumentId=a.OriginalSaleDocumentId AND d.TenantId=@TenantId AND d.BusinessId=@BusinessId
        WHERE (@CustomerId IS NULL OR a.CustomerId=@CustomerId) AND (@SellerId IS NULL OR a.SellerId=@SellerId)
          AND (@SupplierId IS NULL OR a.SupplierId=@SupplierId) AND (@ProductId IS NULL OR a.ProductId=@ProductId)
          AND (@CategoryId IS NULL OR a.CategoryId=@CategoryId) AND (@WarehouseId IS NULL OR a.WarehouseId=@WarehouseId)
          AND (@DocumentType IS NULL OR d.DocumentType=@DocumentType)
          AND (@BilledBy IS NULL OR d.BilledByUserId=@BilledBy) AND (@Waiter IS NULL OR a.WaiterPartyId=@Waiter)
          AND (@Document IS NULL OR a.OriginalSaleDocumentId=@Document)
          AND (@Search IS NULL OR d.DocumentNumber LIKE @Search ESCAPE N'~' OR d.CustomerName LIKE @Search ESCAPE N'~'
             OR a.ItemName LIKE @Search ESCAPE N'~' OR a.ItemCode LIKE @Search ESCAPE N'~')
        """+ActivitySelectionSql;

    private const string ExplorerMeasures="""
        COUNT_BIG(DISTINCT a.OriginalSaleDocumentId) Documents,
        COUNT_BIG(DISTINCT CASE WHEN a.MovementType=N'Sale' AND a.UntaxedAmount>0 THEN a.CustomerId END) Customers,
        COALESCE(SUM(CASE WHEN a.ItemKind=N'Product' OR @Lines=1 THEN a.Quantity ELSE 0 END),0) Quantity,
        COALESCE(SUM(CASE WHEN a.MovementType=N'Sale' THEN a.GrossAmount ELSE 0 END),0) GrossSales,
        COALESCE(SUM(CASE WHEN a.MovementType=N'Sale' THEN a.DiscountAmount ELSE 0 END),0) Discounts,
        COALESCE(-SUM(CASE WHEN a.MovementType=N'Return' THEN a.UntaxedAmount ELSE 0 END),0) Returns,
        COALESCE(SUM(a.UntaxedAmount),0) NetUntaxed,COALESCE(SUM(a.TaxAmount),0) Tax,COALESCE(SUM(a.TotalAmount),0) NetTotal,
        CASE WHEN COUNT_BIG(*)=COUNT_BIG(a.RecognizedCostAmount) THEN COALESCE(SUM(a.RecognizedCostAmount),0) END RecognizedCost,
        CASE WHEN COUNT_BIG(*)=COUNT_BIG(a.RecognizedCostAmount) THEN COALESCE(SUM(a.UntaxedAmount-a.RecognizedCostAmount),0) END Profit,
        COALESCE(SUM(a.MissingCostLines),0) MissingCostLines,
        COALESCE(-SUM(CASE WHEN a.MovementType=N'Return' THEN a.TotalAmount ELSE 0 END),0) ReturnsWithTax,
        COALESCE(-SUM(CASE WHEN a.MovementType=N'Return' THEN a.TaxAmount ELSE 0 END),0) ReturnedTax
        """;

    private const string DocumentSnapshotColumns="""
        metadata.DocumentId,metadata.DocumentType,metadata.DocumentNumber,metadata.FiscalNumber,metadata.IssuedAt,
        metadata.CustomerName,metadata.SellerName,metadata.BilledByName,metadata.WarehouseName,metadata.FiscalStatus,
        metadata.BusinessLocalDate,CASE WHEN @WholeDocuments=1 THEN metadata.TotalAmount END OriginalOfficialTotal,
        CASE WHEN @WholeDocuments=1 THEN metadata.RoundingAdjustmentAmount END OriginalRounding
        """;

    public async Task<SalesReportExplorerPage> ExploreAsync(SalesReportingUserIdentity user,
        SalesReportExplorerRequest request,CancellationToken cancellationToken)
    {
        await using var connection=connections.Create();
        await connection.OpenAsync(cancellationToken);
        var access=await ResolveAccessAsync(connection,user,cancellationToken);
        var filter=request.Filter with {SellerId=Constrain(request.Filter.SellerId,access.SellerId,"seller"),
            SupplierId=Constrain(request.Filter.SupplierId,access.SupplierId,"supplier")};
        DemandRestaurantDimensions(access,filter,new[]{request.GroupBy,request.DrillDimension}.Concat(request.DrillPath?.Select(step=>step.Dimension)??[]),request.WaiterPartyId is not null);
        if(request.Module is not ("sales" or "products" or "impact" or "supplier"))
            throw new SalesReportingValidationException("La modalidad solicitada aún no está disponible.");
        var comparison=ComparisonRange(request);
        if(comparison is not null && (request.DrillDimension is "day" or "month"||request.DrillPath?.Any(step=>step.Dimension is "day" or "month")==true))
            throw new SalesReportingValidationException("La comparación temporal se consulta antes de abrir un día o mes.");
        if(request.Module=="impact" && request.DocumentId is null && request.DrillDimension is null && request.DrillPath is not {Count:>0})
            return await ExploreImpactAsync(connection,user,request with {Filter=filter},access.SupplierId,cancellationToken);
        if(request.Module=="supplier" && request.DocumentId is null && request.DrillDimension is null && request.DrillPath is not {Count:>0})
        {
            if(access.SellerId is not null)throw new SalesReportingForbiddenException("Un vendedor no puede consultar las compras a proveedores.");
            return await ExploreSuppliersAsync(connection,user,request with {Filter=filter},access.SupplierId,cancellationToken);
        }
        var dimension=request.DocumentId is not null?"line":request.GroupBy;
        if(dimension=="line" && request.DocumentId is null)
            throw new SalesReportingValidationException("Selecciona primero un comprobante.");
        var lineScope=request.Module is "products" or "supplier"||HasLineScope(filter)||request.WaiterPartyId is not null
            ||request.DrillDimension is "supplier" or "product" or "category" or "waiter" or "area" or "category-line" or "category-group" or "subgroup"
            ||request.DrillPath?.Any(step=>step.Dimension is "supplier" or "product" or "category" or "waiter" or "area" or "category-line" or "category-group" or "subgroup")==true
            ||!string.IsNullOrWhiteSpace(request.Search);
        var (key,label)=ExplorerDimension(dimension);
        // Each document/line group has one original document and at most its
        // one frozen customer. Avoid independent distinct sorts at that grain.
        var groupMeasures=dimension is "document" or "line"?ExplorerMeasures
            .Replace("COUNT_BIG(DISTINCT a.OriginalSaleDocumentId) Documents","CONVERT(bigint,1) Documents",StringComparison.Ordinal)
            .Replace("COUNT_BIG(DISTINCT CASE WHEN a.MovementType=N'Sale' AND a.UntaxedAmount>0 THEN a.CustomerId END) Customers",
                "MAX(CONVERT(bigint,CASE WHEN a.MovementType=N'Sale' AND a.UntaxedAmount>0 AND a.CustomerId IS NOT NULL THEN 1 ELSE 0 END)) Customers",StringComparison.Ordinal)
            :ExplorerMeasures;
        var drill=request.DrillDimension is null?"":$" AND {ExplorerDimension(request.DrillDimension).Key}=@DrillKey";
        for(var index=0;index<(request.DrillPath?.Count??0);index++)
            drill+=$" AND {ExplorerDimension(request.DrillPath![index].Dimension).Key}=@PathKey{index}";
        var productScope=request.Module=="products";
        var composition=productScope?" AND (@ProductComposition IS NULL OR COALESCE(a.CompositionKind,N'Unknown')=@ProductComposition)":"";
        // Product eligibility and participation use the complete filtered period, independent of drill/page.
        var productCohort=productScope?$"""
            SELECT a.ProductId,SUM(a.UntaxedAmount) NetUntaxed,
              CASE WHEN COUNT_BIG(*)=COUNT_BIG(a.RecognizedCostAmount) THEN SUM(a.UntaxedAmount-a.RecognizedCostAmount) END Profit,
              COUNT_BIG(CASE WHEN a.MovementType=N'Return' THEN 1 END) ReturnLines
            INTO #ProductCohort {ExplorerSource.Replace(",NULL,@Document,@WholeActivity)",",NULL,NULL,@WholeActivity)",StringComparison.Ordinal).Replace(" AND (@Document IS NULL OR a.OriginalSaleDocumentId=@Document)","",StringComparison.Ordinal)}
              AND a.ItemKind=N'Product' {composition} GROUP BY a.ProductId OPTION(RECOMPILE);
            -- Compare the exact ratio by cross multiplication; avoid rounded division at a range boundary.
            DELETE #ProductCohort WHERE (@ProductMarginFrom IS NOT NULL AND (Profit IS NULL OR NetUntaxed=0
                OR (NetUntaxed>0 AND Profit*100<@ProductMarginFrom*NetUntaxed)
                OR (NetUntaxed<0 AND Profit*100>@ProductMarginFrom*NetUntaxed)))
              OR (@ProductMarginTo IS NOT NULL AND (Profit IS NULL OR NetUntaxed=0
                OR (NetUntaxed>0 AND Profit*100>@ProductMarginTo*NetUntaxed)
                OR (NetUntaxed<0 AND Profit*100<@ProductMarginTo*NetUntaxed)))
              OR (@ProductReturns=N'with-returns' AND ReturnLines=0) OR (@ProductReturns=N'without-returns' AND ReturnLines>0);
            CREATE UNIQUE CLUSTERED INDEX IX_ProductCohort ON #ProductCohort(ProductId);
            """:"";
        var source=ExplorerSource+drill+(request.Module is "products" or "supplier"?" AND a.ItemKind=N'Product'":"")+composition+
            (productScope?" AND EXISTS(SELECT 1 FROM #ProductCohort eligible WHERE eligible.ProductId=a.ProductId)":"");
        var order=request.OrderBy switch
        {
            "net-desc"=>"NetUntaxed DESC", "net-asc"=>"NetUntaxed ASC", "label"=>"Label ASC",
            "profit-desc"=>"Profit DESC", "quantity-desc"=>"Quantity DESC",
            _=>throw new SalesReportingValidationException("El orden no es válido.")
        };
        await using var command=new SqlCommand(productCohort+$"""
            SELECT {key} GroupKey,{label} GroupLabel,a.FactId,a.SourceDocumentId,a.SourceDocumentType,
              a.OriginalSaleDocumentId,a.SourceLineNumber,a.ItemKind,a.CustomerId,a.Quantity,a.GrossAmount,a.DiscountAmount,
              a.UntaxedAmount,a.TaxAmount,a.TotalAmount,a.RecognizedCostAmount,a.MovementType,a.OccurredAt,a.ProjectedAt,
              a.TaxCode,a.TaxRate,d.DocumentNumber,d.DocumentType,a.MissingCostLines
            INTO #Activity {source} OPTION(RECOMPILE);
            SELECT DISTINCT SourceDocumentId,SourceDocumentType INTO #MovementKeys FROM #Activity;
            CREATE UNIQUE CLUSTERED INDEX IX_MovementKeys ON #MovementKeys(SourceDocumentId,SourceDocumentType);
            SELECT m.SourceDocumentId,m.SourceDocumentType,m.OriginalSaleDocumentId,m.RoundingAdjustmentAmount,
              m.OfficialTotal,m.RestaurantTipAmount INTO #DocumentMoney
            FROM reporting.SalesReportDocumentMovements(@TenantId,@BusinessId,@From,@To) m
            WHERE @WholeDocuments=1 AND EXISTS(SELECT 1 FROM #MovementKeys selected
              WHERE selected.SourceDocumentId=m.SourceDocumentId AND selected.SourceDocumentType=m.SourceDocumentType)
            OPTION(RECOMPILE);
            SELECT a.GroupKey,MAX(a.GroupLabel) Label,{groupMeasures},
              MAX(CONVERT(nvarchar(36),a.FactId)) FactKey,MAX(a.ItemKind) ItemKind,
              MAX(a.DocumentNumber) DocumentNumber,MAX(a.DocumentType) DocumentType,MAX(a.OccurredAt) OccurredAt,MAX(a.MovementType) MovementType
            INTO #Groups FROM #Activity a GROUP BY a.GroupKey;
            SELECT * INTO #Page FROM #Groups ORDER BY {order},GroupKey
              OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            SELECT g.GroupKey,g.Label,g.Documents,g.Customers,g.Quantity,g.GrossSales,g.Discounts,g.Returns,
              g.NetUntaxed,g.Tax,g.NetTotal,g.RecognizedCost,g.Profit,g.MissingCostLines,g.FactKey,g.ItemKind,
              g.DocumentNumber,g.DocumentType,g.OccurredAt,p.ProductCode,p.UnitCode,p.SupplierName,p.WaiterName,p.CompositionKind,p.CompositionDetailJson,
              COALESCE(p.TaxRate,s.TaxRate) TaxRate,COALESCE(p.TaxCode,s.TaxCode) TaxCode,
              g.MovementType,p.ReturnReasonCode,p.ReturnDisposition,p.ReturnReasonDescription,
              money.RoundingAdjustment,money.OfficialTotal,money.RestaurantTip,{DocumentSnapshotColumns},g.ReturnsWithTax,g.ReturnedTax
            FROM #Page g
            LEFT JOIN reporting.SalesReportLineFacts p ON @Lines=1 AND g.ItemKind=N'Product' AND p.FactId=CONVERT(uniqueidentifier,g.FactKey)
              AND p.TenantId=@TenantId AND p.BusinessId=@BusinessId
            LEFT JOIN reporting.ServiceSalesReportLineFacts s ON @Lines=1 AND g.ItemKind=N'Service' AND s.FactId=CONVERT(uniqueidentifier,g.FactKey)
              AND s.TenantId=@TenantId AND s.BusinessId=@BusinessId
            LEFT JOIN reporting.SalesReportDocuments metadata ON @DocumentGroups=1 AND metadata.DocumentId=TRY_CONVERT(uniqueidentifier,g.GroupKey)
              AND metadata.TenantId=@TenantId AND metadata.BusinessId=@BusinessId
            LEFT JOIN(SELECT m.OriginalSaleDocumentId,SUM(m.RoundingAdjustmentAmount) RoundingAdjustment,
              SUM(m.OfficialTotal) OfficialTotal,SUM(m.RestaurantTipAmount) RestaurantTip
              FROM #DocumentMoney m
              WHERE @DocumentGroups=1
                AND EXISTS(SELECT 1 FROM #Page selected WHERE selected.GroupKey=CONVERT(nvarchar(36),m.OriginalSaleDocumentId))
              GROUP BY m.OriginalSaleDocumentId) money
              ON money.OriginalSaleDocumentId=COALESCE(@Document,TRY_CONVERT(uniqueidentifier,g.GroupKey))
            ORDER BY {order},GroupKey OPTION(RECOMPILE);
            SELECT {ExplorerMeasures},(SELECT COUNT_BIG(*) FROM #Groups) TotalGroups,
              MAX(a.ProjectedAt) ProjectedThrough FROM #Activity a;
            SELECT a.TaxCode,a.TaxRate,SUM(a.UntaxedAmount),SUM(a.TaxAmount),SUM(a.TotalAmount)
            FROM #Activity a WHERE @Lines=1 GROUP BY a.TaxCode,a.TaxRate ORDER BY a.TaxCode,a.TaxRate OPTION(RECOMPILE);
            SELECT payment.MovementType,payment.MethodCode,SUM(payment.Amount)
            FROM reporting.SalesReportPaymentFacts payment
            WHERE @Payments=1 AND payment.TenantId=@TenantId AND payment.BusinessId=@BusinessId
              AND payment.BusinessLocalDate BETWEEN @From AND @To
              AND EXISTS(SELECT 1 FROM #MovementKeys selected WHERE selected.SourceDocumentId=payment.SourceDocumentId AND selected.SourceDocumentType=payment.SourceDocumentType)
            GROUP BY payment.MovementType,payment.MethodCode OPTION(RECOMPILE);
            SELECT CASE WHEN @WholeDocuments=1 THEN COALESCE(SUM(m.RoundingAdjustmentAmount),0) END,
              CASE WHEN @WholeDocuments=1 THEN COALESCE(SUM(m.RestaurantTipAmount),0) END,
              CASE WHEN @WholeDocuments=1 THEN COALESCE(SUM(m.OfficialTotal),0) END
            FROM #DocumentMoney m;
            SELECT {ExplorerMeasures} {ComparisonSource(source)} AND @HasComparison=1 OPTION(RECOMPILE);
            SELECT {DocumentSnapshotColumns} FROM reporting.SalesReportDocuments metadata
            WHERE metadata.DocumentId=@Document AND metadata.TenantId=@TenantId AND metadata.BusinessId=@BusinessId
              AND EXISTS(SELECT 1 FROM #Groups);
            """+(productScope?"SELECT COUNT_BIG(*),COALESCE(SUM(NetUntaxed),0),CASE WHEN COUNT_BIG(*)=COUNT_BIG(Profit) THEN COALESCE(SUM(Profit),0) END FROM #ProductCohort;":""),connection);
        if(productScope)
        {
            var products=request.Products??new();
            command.Parameters.AddWithValue("@ProductComposition",(object?)products.CompositionKind??DBNull.Value);
            command.Parameters.AddWithValue("@ProductReturns",products.ReturnActivity);
            foreach(var (name,value) in new[]{("@ProductMarginFrom",products.MarginFrom),("@ProductMarginTo",products.MarginTo)})
            {var parameter=command.Parameters.Add(name,SqlDbType.Decimal);parameter.Precision=19;parameter.Scale=6;parameter.Value=(object?)value??DBNull.Value;}
        }
        AddExplorerParameters(command,user,request with {Filter=filter});
        command.Parameters["@WholeActivity"].Value=!lineScope&&dimension is "document" or "billed-by" or "customer" or "seller" or "warehouse" or "table" or "day" or "month" or "hour";
        command.Parameters.AddWithValue("@WholeDocuments",!lineScope);
        command.Parameters.AddWithValue("@DocumentGroups",dimension=="document");
        command.Parameters.AddWithValue("@Payments",request.DocumentId is not null&&!lineScope&&string.IsNullOrWhiteSpace(request.Search));
        var items=new List<SalesReportExplorerRow>();
        await using var reader=await ExecuteExplorerReaderAsync(command,user.BusinessId,cancellationToken);
        while(await reader.ReadAsync(cancellationToken))
        {
            var groupKey=reader.GetString(0);
            items.Add(new(groupKey,reader.GetString(1),ReadExplorerMeasures(reader,2),
                dimension=="document"?Guid.Parse(groupKey):request.DocumentId,
                reader.GetString(16),reader.GetString(17),NullableDate(reader,18),
                dimension=="line"?reader.GetString(15):null,NullableString(reader,19),NullableString(reader,20),
                NullableString(reader,21),NullableString(reader,22),NullableString(reader,23),NullableString(reader,24),
                NullableDecimal(reader,25),NullableString(reader,26),MovementType:NullableString(reader,27),
                ReturnReasonCode:NullableString(reader,28),ReturnDisposition:NullableString(reader,29),ReturnReasonDescription:NullableString(reader,30),
                RoundingAdjustment:NullableDecimal(reader,31),OfficialTotal:NullableDecimal(reader,32),RestaurantTip:NullableDecimal(reader,33),
                Document:ReadDocumentSnapshot(reader,34),FactId:dimension=="line"&&reader.GetString(15)!="Charge"?Guid.Parse(reader.GetString(14)):null));
        }
        await reader.NextResultAsync(cancellationToken);await reader.ReadAsync(cancellationToken);
        var totals=ReadExplorerMeasures(reader,0);var totalCount=reader.GetInt64(reader.GetOrdinal("TotalGroups"));var through=NullableDate(reader,reader.GetOrdinal("ProjectedThrough"));
        var taxes=new List<SalesReportTaxRow>();
        await reader.NextResultAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken)) taxes.Add(new(reader.IsDBNull(0)?"Sin snapshot":reader.GetString(0),
            reader.IsDBNull(1)?0:reader.GetDecimal(1),reader.GetDecimal(2),reader.GetDecimal(3),reader.GetDecimal(4)));
        var payments=new List<SalesReportPaymentRow>();
        await reader.NextResultAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken)) payments.Add(new(reader.GetString(0),reader.GetString(1),reader.GetDecimal(2)));
        await reader.NextResultAsync(cancellationToken);
        decimal? rounding=null,tip=null,official=null;
        if(await reader.ReadAsync(cancellationToken)){rounding=NullableDecimal(reader,0);tip=NullableDecimal(reader,1);official=NullableDecimal(reader,2);}
        await reader.NextResultAsync(cancellationToken);await reader.ReadAsync(cancellationToken);
        var comparisonValue=comparison is null?null:new SalesReportComparison(comparison.Value.From,comparison.Value.To,ReadExplorerMeasures(reader,0));
        await reader.NextResultAsync(cancellationToken);
        var document=await reader.ReadAsync(cancellationToken)?ReadDocumentSnapshot(reader,0):null;
        SalesReportProductOverview? productOverview=null;
        if(productScope)
        {
            await reader.NextResultAsync(cancellationToken);await reader.ReadAsync(cancellationToken);
            productOverview=new(reader.GetInt64(0),reader.GetDecimal(1),NullableDecimal(reader,2));
            for(var index=0;index<items.Count;index++)items[index]=items[index] with {
                SalesParticipationPercent=productOverview.NetUntaxed>0?decimal.Round(items[index].Measures.NetUntaxed/productOverview.NetUntaxed*100m,2):null,
                ProfitParticipationPercent=productOverview.Profit is >0&&items[index].Measures.Profit is {} profit?decimal.Round(profit/productOverview.Profit.Value*100m,2):null};
        }
        return new(items,request.Page,request.PageSize,totalCount,totals,through,lineScope,taxes,payments,rounding,tip,Comparison:comparisonValue,OfficialTotal:official,Document:document,Products:productOverview);
    }

    private static void AddExplorerParameters(SqlCommand command,SalesReportingUserIdentity user,SalesReportExplorerRequest request)
    {
        AddFilter(command,user,request.Filter);
        AddSelections(command,request.Filter);
        for(var index=0;index<(request.DrillPath?.Count??0);index++)command.Parameters.AddWithValue($"@PathKey{index}",request.DrillPath![index].Key);
        command.Parameters.AddWithValue("@WholeActivity",false);
        command.Parameters.AddWithValue("@BilledBy",(object?)request.BilledByUserId??DBNull.Value);
        command.Parameters.AddWithValue("@Waiter",(object?)request.WaiterPartyId??DBNull.Value);
        command.Parameters.AddWithValue("@Document",(object?)request.DocumentId??DBNull.Value);
        var search=string.IsNullOrWhiteSpace(request.Search)?null:request.Search.Trim();
        command.Parameters.AddWithValue("@Search",search is null?DBNull.Value:$"%{search.Replace("~","~~").Replace("%","~%").Replace("_","~_").Replace("[","~[")}%");
        command.Parameters.AddWithValue("@DrillKey",(object?)request.DrillKey??DBNull.Value);
        command.Parameters.AddWithValue("@Skip",(request.Page-1)*request.PageSize);
        command.Parameters.AddWithValue("@Take",request.PageSize);
        command.Parameters.AddWithValue("@Lines",request.DocumentId is not null);
        var comparison=ComparisonRange(request);
        Date(command,"@ComparisonFrom",comparison?.From??request.Filter.From);
        Date(command,"@ComparisonTo",comparison?.To??request.Filter.To);
        command.Parameters.AddWithValue("@HasComparison",comparison is not null);
    }

    private static string ComparisonSource(string source)=>source.Replace("@From","@ComparisonFrom",StringComparison.Ordinal)
        .Replace("@To","@ComparisonTo",StringComparison.Ordinal);

    private static (DateOnly From,DateOnly To)? ComparisonRange(SalesReportExplorerRequest request)
    {
        try
        {
            return request.Comparison switch
            {
                "none"=>null,
                "previous-period"=>(request.Filter.From.AddDays(-(request.Filter.To.DayNumber-request.Filter.From.DayNumber+1)),request.Filter.From.AddDays(-1)),
                "previous-year"=>(request.Filter.From.AddYears(-1),request.Filter.To.AddYears(-1)),
                _=>throw new SalesReportingValidationException("La comparación no es válida.")
            };
        }
        catch(ArgumentOutOfRangeException){throw new SalesReportingValidationException("El rango de comparación no es válido.");}
    }

    private static SalesReportMeasures ReadExplorerMeasures(SqlDataReader reader,int offset)
    {
        var net=reader.GetDecimal(offset+6);var profit=NullableDecimal(reader,offset+10);
        var returnedTotal=reader.GetDecimal(reader.GetOrdinal("ReturnsWithTax"));var returnedTax=reader.GetDecimal(reader.GetOrdinal("ReturnedTax"));
        return new(reader.GetInt64(offset),reader.GetInt64(offset+1),reader.GetDecimal(offset+2),reader.GetDecimal(offset+3),
            reader.GetDecimal(offset+4),reader.GetDecimal(offset+5),net,reader.GetDecimal(offset+7),reader.GetDecimal(offset+8),
            NullableDecimal(reader,offset+9),profit,net==0||profit is null?null:decimal.Round(profit.Value/net*100m,2),reader.GetInt64(offset+11),
            net+reader.GetDecimal(offset+5),returnedTotal,returnedTax,reader.GetDecimal(offset+8)+returnedTotal,reader.GetDecimal(offset+7)+returnedTax);
    }

    private static SalesReportDocumentSnapshot? ReadDocumentSnapshot(SqlDataReader reader,int offset)=>reader.IsDBNull(offset)?null:new(
        reader.GetGuid(offset),reader.GetString(offset+1),reader.GetString(offset+2),NullableString(reader,offset+3),reader.GetDateTimeOffset(offset+4),
        reader.GetString(offset+5),reader.GetString(offset+6),NullableString(reader,offset+7),NullableString(reader,offset+8),NullableString(reader,offset+9),
        DateOnly.FromDateTime(reader.GetDateTime(offset+10)),NullableDecimal(reader,offset+11),NullableDecimal(reader,offset+12));

    private static async Task<SqlDataReader> ExecuteExplorerReaderAsync(SqlCommand command,Guid businessId,CancellationToken cancellationToken)
    {
        // One bounded SQL batch owns the page, independent aggregates and detail cuts.
        // The business lock freezes Reporting writers for this cut without holding
        // serializable range locks across other tenants' projection rows.
        command.CommandText="SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL READ COMMITTED; BEGIN TRY BEGIN TRANSACTION;\n"+
            SqlSalesReportingProcessor.TransactionLockSql+"\n"+"""
            IF EXISTS(SELECT 1 FROM reporting.SalesReportingCheckpoints WHERE BusinessId=@BusinessId AND RebuildInProgress=1)
              OR EXISTS(SELECT 1 FROM reporting.SalesReportDocuments WHERE BusinessId=@BusinessId AND ProjectionVersion<@CurrentProjectionVersion)
              OR EXISTS(SELECT 1 FROM reporting.PurchaseReportDocuments WHERE BusinessId=@BusinessId AND ProjectionVersion<@CurrentProjectionVersion)
              THROW 51619,N'La proyección requiere completar su reconstrucción antes de consultar cifras.',1;
            """+"\n"+SelectionTableSql+"\n"+command.CommandText+"\nCOMMIT TRANSACTION; END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK TRANSACTION; THROW; END CATCH;";
        command.Parameters.AddWithValue("@ReportingLockResource",SqlSalesReportingProcessor.ReportingLockResource(businessId));
        command.Parameters.AddWithValue("@ReportingLockMode","Exclusive");
        command.Parameters.AddWithValue("@CurrentProjectionVersion",SqlSalesReportingProjectionWriter.ProjectionVersion);
        try{return await command.ExecuteReaderAsync(cancellationToken);}
        catch(SqlException error) when(error.Number is 51619 or 51620)
        {throw new SalesReportingUnavailableException("La proyección está ocupada o requiere completar su reconstrucción antes de consultar cifras.");}
    }
    private static string? NullableString(SqlDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetString(ordinal);
    private static decimal? NullableDecimal(SqlDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetDecimal(ordinal);
    private static DateTimeOffset? NullableDate(SqlDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetDateTimeOffset(ordinal);
}
