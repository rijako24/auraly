using System.Data;
using System.Text.Json;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlSalesReportingStore
{
    public async Task<SalesReportFilterOptionPage> GetFilterOptionsAsync(SalesReportingUserIdentity user,SalesReportFilterOptionsRequest request,CancellationToken cancellationToken)
    {
        await using var connection=connections.Create();await connection.OpenAsync(cancellationToken);
        var access=await ResolveAccessAsync(connection,user,cancellationToken);
        DemandRestaurantDimensions(access,request.Filter,[request.Dimension]);
        if(request.IncludePurchases&&access.SellerId is not null)throw new SalesReportingForbiddenException("Un vendedor no puede consultar las compras a proveedores.");
        var filter=request.Filter with {SellerId=Constrain(request.Filter.SellerId,access.SellerId,"seller"),SupplierId=Constrain(request.Filter.SupplierId,access.SupplierId,"supplier")};
        var (saleKey,saleLabel)=ExplorerDimension(request.Dimension);
        var leafId="COALESCE(JSON_VALUE(p.CategoryHierarchyJson,'$.SubgroupId'),JSON_VALUE(p.CategoryHierarchyJson,'$.GroupId'),JSON_VALUE(p.CategoryHierarchyJson,'$.LineId'),JSON_VALUE(p.CategoryHierarchyJson,'$.AreaId'))";
        var leafName="COALESCE(JSON_VALUE(p.CategoryHierarchyJson,'$.SubgroupName'),JSON_VALUE(p.CategoryHierarchyJson,'$.GroupName'),JSON_VALUE(p.CategoryHierarchyJson,'$.LineName'),JSON_VALUE(p.CategoryHierarchyJson,'$.AreaName'))";
        var (purchaseKey,purchaseLabel)=request.Dimension switch {
            "product"=>("CONVERT(nvarchar(36),p.ProductId)","p.ProductName"),
            "warehouse"=>("CONVERT(nvarchar(36),p.WarehouseId)","p.WarehouseName"),
            "table"=>("CAST(NULL AS nvarchar(36))","CAST(NULL AS nvarchar(140))"),
            "category"=>(leafId,leafName),
            _=>PurchaseHierarchyDimension(request.Dimension)};
        await using var command=new SqlCommand($"""
            WITH candidates AS(
              SELECT {saleKey} Code,{saleLabel} Label {ExplorerSource}
              UNION ALL SELECT {purchaseKey},{purchaseLabel} {PurchaseScope} AND @IncludePurchases=1
            )
            SELECT Code,MAX(Label) Label INTO #FilterOptions FROM candidates
            WHERE Code IS NOT NULL AND Code<>N'unattributed' AND Label IS NOT NULL
              AND (@OptionSearch IS NULL OR Label LIKE @OptionSearch ESCAPE N'~') GROUP BY Code;
            SELECT Code,Label FROM #FilterOptions ORDER BY Label,Code OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            SELECT COUNT_BIG(*) FROM #FilterOptions;
            """,connection);
        AddExplorerParameters(command,user,new(filter,Page:request.Page,PageSize:request.PageSize));
        command.Parameters.AddWithValue("@IncludePurchases",request.IncludePurchases);
        var search=string.IsNullOrWhiteSpace(request.Search)?null:request.Search.Trim();
        Text(command,"@OptionSearch",search is null?null:$"%{search.Replace("~","~~").Replace("%","~%").Replace("_","~_").Replace("[","~[")}%",322);
        var items=new List<SalesReportOption>();await using var reader=await ExecuteExplorerReaderAsync(command,user.BusinessId,cancellationToken);
        while(await reader.ReadAsync(cancellationToken))items.Add(new(reader.GetString(0),reader.GetString(1)));
        await reader.NextResultAsync(cancellationToken);await reader.ReadAsync(cancellationToken);
        return new(items,request.Page,request.PageSize,reader.GetInt64(0));
    }

    private static (string Key,string Label) PurchaseHierarchyDimension(string dimension)
    {
        var name=dimension switch {"area"=>"Area","category-line"=>"Line","category-group"=>"Group","subgroup"=>"Subgroup",_=>throw new SalesReportingValidationException("La categoría no es válida.")};
        return ($"JSON_VALUE(p.CategoryHierarchyJson,'$.{name}Id')",$"JSON_VALUE(p.CategoryHierarchyJson,'$.{name}Name')");
    }

    private const string SelectionTableSql = """
        DECLARE @SelectedFilters TABLE(Kind nvarchar(20),Id uniqueidentifier,PRIMARY KEY(Kind,Id));
        INSERT @SelectedFilters SELECT DISTINCT Kind,Id FROM OPENJSON(@Selections)
          WITH(Kind nvarchar(20),Id uniqueidentifier);
        """;

    private static string SelectionPredicate(string kind,string expression) =>
        $" AND (@Selected_{kind.Replace('-','_')}=0 OR EXISTS(SELECT 1 FROM @SelectedFilters selected WHERE selected.Kind=N'{kind}' AND selected.Id={expression}))";

    private static string HierarchyPredicates(string json) =>
        SelectionPredicate("area",$"TRY_CONVERT(uniqueidentifier,JSON_VALUE({json},'$.AreaId'))")+
        SelectionPredicate("category-line",$"TRY_CONVERT(uniqueidentifier,JSON_VALUE({json},'$.LineId'))")+
        SelectionPredicate("category-group",$"TRY_CONVERT(uniqueidentifier,JSON_VALUE({json},'$.GroupId'))")+
        SelectionPredicate("subgroup",$"TRY_CONVERT(uniqueidentifier,JSON_VALUE({json},'$.SubgroupId'))");

    private static readonly string ActivitySelectionSql =
        SelectionPredicate("customer","a.CustomerId")+SelectionPredicate("seller","a.SellerId")+
        SelectionPredicate("supplier","a.SupplierId")+SelectionPredicate("product","a.ProductId")+
        SelectionPredicate("category","a.CategoryId")+SelectionPredicate("warehouse","a.WarehouseId")+
        SelectionPredicate("billed-by","d.BilledByUserId")+SelectionPredicate("waiter","a.WaiterPartyId")+SelectionPredicate("table","d.TableId")+
        HierarchyPredicates("a.CategoryHierarchyJson");

    private static readonly string UniverseSelectionSql =
        SelectionPredicate("customer","d.CustomerId")+SelectionPredicate("seller","d.SellerId")+
        SelectionPredicate("warehouse","d.WarehouseId")+SelectionPredicate("billed-by","d.BilledByUserId")+SelectionPredicate("table","d.TableId");

    private static readonly string PurchaseSelectionSql =
        SelectionPredicate("supplier","p.SupplierId")+SelectionPredicate("product","p.ProductId")+
        SelectionPredicate("warehouse","p.WarehouseId")+
        SelectionPredicate("category","TRY_CONVERT(uniqueidentifier,COALESCE(JSON_VALUE(p.CategoryHierarchyJson,'$.SubgroupId'),JSON_VALUE(p.CategoryHierarchyJson,'$.GroupId'),JSON_VALUE(p.CategoryHierarchyJson,'$.LineId'),JSON_VALUE(p.CategoryHierarchyJson,'$.AreaId')))")+
        HierarchyPredicates("p.CategoryHierarchyJson");

    private static void AddSelections(SqlCommand command,SalesReportFilter filter)
    {
        var selections=filter.Selections().ToArray();
        command.Parameters.Add("@Selections",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(
            selections.SelectMany(selection=>(selection.Values??[]).Distinct().Select(id=>new {selection.Kind,Id=id})));
        foreach(var selection in selections)
            command.Parameters.AddWithValue($"@Selected_{selection.Kind.Replace('-','_')}",selection.Values is {Count:>0});
    }

    private static bool HasMultipleLineScope(SalesReportFilter filter) => filter.Selections().Any(selection=>
        selection.Values is {Count:>0} && selection.Kind is "supplier" or "product" or "category" or "waiter" or "area" or "category-line" or "category-group" or "subgroup");

    private static void DemandRestaurantDimensions(ReportingAccess access,SalesReportFilter filter,IEnumerable<string?> dimensions,bool scalarWaiter=false)
    {
        if(access.PointOfSaleMode!="RestaurantBar"&&(scalarWaiter||filter.WaiterPartyIds is {Count:>0}||filter.TableIds is {Count:>0}||dimensions.Any(dimension=>dimension is "waiter" or "table")))
            throw new SalesReportingValidationException("Meseros y mesas solo aplican a restaurantes.");
    }
}
