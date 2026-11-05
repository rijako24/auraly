namespace Auraly.Contracts.Sales;

public static class SalesReportingPermissionCodes
{
    public const string Read = "sales.reports.read";
    public const string ReadAll = "sales.reports.read-all";
}

public static class SalesReportingDimensions
{
    public const string Customer = "customer";
    public const string Seller = "seller";
    public const string Supplier = "supplier";
    public const string Product = "product";
    public const string Category = "category";
    public const string Warehouse = "warehouse";
    public const string Day = "day";
    public const string Hour = "hour";
    public const string Month = "month";
    public const string PaymentMethod = "payment-method";
    public const string Tax = "tax";

    public static bool IsSupported(string value) => value is Customer or Seller or Supplier or Product or
        Category or Warehouse or Day or Hour or Month or PaymentMethod or Tax;
}

public sealed record SalesReportingUserIdentity(
    Guid UserId, Guid TenantId, Guid BusinessId, IReadOnlySet<string> Permissions);

public sealed record SalesReportFilter(
    DateOnly From, DateOnly To, Guid? CustomerId = null, Guid? SellerId = null,
    Guid? SupplierId = null, Guid? ProductId = null, Guid? CategoryId = null,
    Guid? WarehouseId = null, string? DocumentType = null,
    IReadOnlyList<Guid>? CustomerIds = null, IReadOnlyList<Guid>? SellerIds = null,
    IReadOnlyList<Guid>? SupplierIds = null, IReadOnlyList<Guid>? ProductIds = null,
    IReadOnlyList<Guid>? CategoryIds = null, IReadOnlyList<Guid>? WarehouseIds = null,
    IReadOnlyList<Guid>? BilledByUserIds = null, IReadOnlyList<Guid>? WaiterPartyIds = null,
    IReadOnlyList<Guid>? AreaIds = null, IReadOnlyList<Guid>? LineIds = null,
    IReadOnlyList<Guid>? GroupIds = null, IReadOnlyList<Guid>? SubgroupIds = null,IReadOnlyList<Guid>? TableIds = null)
{
    public IEnumerable<(string Kind, IReadOnlyList<Guid>? Values)> Selections()
    {
        yield return ("customer", CustomerIds); yield return ("seller", SellerIds);
        yield return ("supplier", SupplierIds); yield return ("product", ProductIds);
        yield return ("category", CategoryIds); yield return ("warehouse", WarehouseIds);
        yield return ("billed-by", BilledByUserIds); yield return ("waiter", WaiterPartyIds);
        yield return ("area", AreaIds); yield return ("category-line", LineIds);
        yield return ("category-group", GroupIds); yield return ("subgroup", SubgroupIds);
        yield return ("table", TableIds);
    }
}

public sealed record SalesReportSummaryRequest(SalesReportFilter Filter, DateOnly? ComparisonFrom = null, DateOnly? ComparisonTo = null,
    bool IncludeBreakdowns = false);
public sealed record SalesReportBreakdownRequest(SalesReportFilter Filter, string Dimension, int Limit = 100);

public sealed record SalesReportTotals(
    long DocumentCount, decimal UnitsSold, decimal UnitsReturned, decimal GrossSales,
    decimal Discounts, decimal Returns, decimal NetUntaxedSales, decimal NetTax,
    decimal NetTotalSales, decimal? NetRecognizedCost, decimal? GrossProfit,
    decimal? GrossMarginPercent, decimal? CreditSales, decimal? Collected, decimal? Refunded);

public sealed record SalesReportTrendPoint(
    DateOnly Period, long DocumentCount, decimal GrossSales, decimal Returns,
    decimal NetSales, decimal? GrossProfit);

public sealed record SalesReportSummary(
    SalesReportTotals Current, SalesReportTotals? Comparison,
    decimal? NetSalesChangePercent, IReadOnlyList<SalesReportTrendPoint> Trend,
    DateTimeOffset? ProjectedThrough, long CustomerCount = 0, decimal AverageTicket = 0,
    decimal ReturnRatePercent = 0, bool HasLineScope = false,decimal ExactNetTotal = 0,decimal? RoundingAdjustment = null,
    SalesReportSummaryBreakdowns? Breakdowns = null);

public sealed record SalesReportSummaryBreakdowns(
    IReadOnlyList<SalesReportBreakdownRow> Hourly,IReadOnlyList<SalesReportBreakdownRow> Payments,
    IReadOnlyList<SalesReportBreakdownRow> Sellers,IReadOnlyList<SalesReportBreakdownRow> Products,
    IReadOnlyList<SalesReportBreakdownRow> Suppliers,IReadOnlyList<SalesReportBreakdownRow> Warehouses);

public sealed record SalesTodayOverview(
    DateOnly BusinessDate, SalesReportTotals Totals, long CustomerCount,
    decimal AverageTicket, decimal ReturnRatePercent,
    DateTimeOffset? ProjectedThrough,bool HasLineScope=false,bool HasPendingSources=false,bool HasFailedSources=false,string TimeZoneId="UTC");

public sealed record SupplierImpactRow(
    Guid? SupplierId,string SupplierName,long CoveredCustomers,long ImpactedCustomers,
    decimal PenetrationPercent,decimal NetSales,decimal? GrossProfit,decimal SalesParticipationPercent,
    decimal NetPurchases,decimal? ComparableNetSales,decimal? ComparableNetPurchases,
    decimal? SalesGrowthPercent,decimal? PurchaseGrowthPercent);
public sealed record SupplierImpactOverview(
    long CoveredCustomers,long ImpactedCustomers,decimal NetSales,decimal NetPurchases,
    IReadOnlyList<SupplierImpactRow> Suppliers,DateTimeOffset? ProjectedThrough,int Page=1,int PageSize=200,long TotalCount=0);

public sealed record SalesReportBreakdownRow(
    string Key, string Label, long DocumentCount, decimal Quantity,
    decimal GrossSales, decimal Discounts, decimal Returns, decimal NetUntaxedSales,
    decimal Tax, decimal NetSales, decimal? RecognizedCost, decimal? GrossProfit,
    decimal? GrossMarginPercent, decimal ParticipationPercent);

public sealed record SalesReportDocumentRow(
    Guid DocumentId, string DocumentType, string DocumentNumber, string? FiscalNumber,
    DateTimeOffset IssuedAt, string CustomerName, string SellerName, string? WarehouseName,
    decimal GrossAmount, decimal DiscountAmount, decimal UntaxedAmount, decimal TaxAmount,
    decimal TotalAmount, decimal? RoundingAdjustmentAmount,
    decimal ReturnedTotalAmount, decimal NetTotalAmount,
    decimal? GrossProfit, string? FiscalStatus,
    decimal? RestaurantTipAmount = 0m);

public sealed record SalesReportDocumentPage(
    IReadOnlyList<SalesReportDocumentRow> Items, int Page, int PageSize, int TotalCount);

public sealed record SalesReportLineRow(
    Guid? FactId, string MovementType, DateTimeOffset? OccurredAt, string ProductCode,
    string ProductName, string? CategoryName, decimal Quantity, decimal GrossAmount,
    decimal DiscountAmount, decimal UntaxedAmount, decimal TaxAmount, decimal TotalAmount,
    decimal? RecognizedCostAmount, string? ReturnReasonCode, string? ReturnDisposition,
    string? ItemKind=null,string? Key=null);

public sealed record SalesReportDocumentDetail(
    SalesReportDocumentRow Document, IReadOnlyList<SalesReportLineRow> Lines,int Page=1,int PageSize=200,long TotalCount=0);

public sealed class SalesReportingForbiddenException(string message) : Exception(message);
public sealed class SalesReportingValidationException(string message) : Exception(message);
public sealed class SalesReportingUnavailableException(string message) : Exception(message);

public sealed record SalesReportOption(string Code,string Label);
public sealed record SalesReportFilterOptionsRequest(SalesReportFilter Filter,string Dimension,int Page=1,int PageSize=10,string? Search=null,bool IncludePurchases=false);
public sealed record SalesReportFilterOptionPage(IReadOnlyList<SalesReportOption> Items,int Page,int PageSize,long TotalCount);
public sealed record SalesReportContext(DateOnly BusinessDate,DateOnly MonthStart,string TimeZoneId,
    bool IsRestaurant,IReadOnlyList<SalesReportOption> Groupings,IReadOnlyList<SalesReportOption> Comparisons,
    IReadOnlyList<SalesReportOption> DocumentTypes,IReadOnlyList<SalesReportOption> Orders,
    IReadOnlyList<SalesReportOption> CustomerActivities,IReadOnlyList<SalesReportOption> PaymentMethods,
    bool HasPendingSources,bool HasFailedSources,
    IReadOnlyList<SalesReportOption>? ProductReturnActivities=null,IReadOnlyList<SalesReportOption>? ProductCompositions=null);

public sealed record SalesReportExplorerRequest(
    SalesReportFilter Filter,string GroupBy="document",int Page=1,int PageSize=25,
    string? Search=null,string OrderBy="net-desc",Guid? BilledByUserId=null,Guid? WaiterPartyId=null,
    string? DrillDimension=null,string? DrillKey=null,Guid? DocumentId=null,string Module="sales",
    string Comparison="none",string CustomerActivity="all",IReadOnlyList<SalesReportDrillStep>? DrillPath=null,SalesReportProductFilter? Products=null);

public sealed record SalesReportProductFilter(decimal? MarginFrom=null,decimal? MarginTo=null,string ReturnActivity="all",string? CompositionKind=null);
public sealed record SalesReportProductOverview(long Products,decimal NetUntaxed,decimal? Profit);

public sealed record SalesReportDrillStep(string Dimension,string Key);

public sealed record SalesReportMeasures(long Documents,long Customers,decimal Quantity,
    decimal GrossSales,decimal Discounts,decimal Returns,decimal NetUntaxed,decimal Tax,decimal NetTotal,
    decimal? RecognizedCost,decimal? Profit,decimal? MarginPercent,long MissingCostLines,decimal SalesBeforeReturns,
    decimal ReturnsWithTax,decimal ReturnedTax,decimal SalesWithTax,decimal SalesTax);

public sealed record SalesReportDocumentSnapshot(Guid DocumentId,string DocumentType,string DocumentNumber,string? FiscalNumber,
    DateTimeOffset IssuedAt,string CustomerName,string SellerName,string? BilledByName,string? WarehouseName,string? FiscalStatus,
    DateOnly BusinessLocalDate,decimal? OriginalOfficialTotal,decimal? OriginalRounding);

public sealed record SalesReportExplorerRow(string Key,string Label,SalesReportMeasures Measures,
    Guid? DocumentId=null,string? DocumentNumber=null,string? DocumentType=null,
    DateTimeOffset? OccurredAt=null,string? ItemKind=null,string? ItemCode=null,string? UnitCode=null,
    string? SupplierName=null,string? WaiterName=null,string? CompositionKind=null,string? CompositionDetailJson=null,
    decimal? TaxRate=null,string? TaxCode=null,bool? BoughtInPeriod=null,SalesReportSupplierMetrics? Supplier=null,
    string? MovementType=null,string? ReturnReasonCode=null,string? ReturnDisposition=null,string? ReturnReasonDescription=null,
    decimal? RoundingAdjustment=null,decimal? OfficialTotal=null,decimal? RestaurantTip=null,SalesReportDocumentSnapshot? Document=null,Guid? FactId=null,decimal? SalesParticipationPercent=null,decimal? ProfitParticipationPercent=null);
public sealed record SalesReportSupplierMetrics(decimal NetPurchases,decimal PurchaseTax,decimal PurchasesWithTax,decimal PurchaseQuantity,
    decimal? SalesPurchaseRatio,decimal PenetrationPercent,decimal? PreviousNetPurchases=null,decimal? PreviousNetSales=null,long? PreviousBuyers=null,
    decimal PurchaseReceipts=0,decimal PurchaseReturns=0);
public sealed record SalesReportSupplierOverview(long KnownCustomers,decimal NetPurchases,decimal PurchaseTax,decimal PurchasesWithTax,
    decimal PurchaseQuantity,decimal? PreviousNetPurchases=null,decimal PurchaseReceipts=0,decimal PurchaseReturns=0);

public sealed record SalesReportExplorerPage(IReadOnlyList<SalesReportExplorerRow> Items,
    int Page,int PageSize,long TotalCount,SalesReportMeasures Totals,DateTimeOffset? ProjectedThrough,
    bool HasLineScope,IReadOnlyList<SalesReportTaxRow> Taxes,IReadOnlyList<SalesReportPaymentRow> Payments,
    decimal? RoundingAdjustment=null,decimal? RestaurantTip=null,SalesReportImpactOverview? Impact=null,
    SalesReportComparison? Comparison=null,SalesReportSupplierOverview? Supplier=null,decimal? OfficialTotal=null,SalesReportDocumentSnapshot? Document=null,SalesReportProductOverview? Products=null);
public sealed record SalesReportComparison(DateOnly From,DateOnly To,SalesReportMeasures Totals);
public sealed record SalesReportImpactOverview(long KnownCustomers,long Buyers,long WithoutPurchase,decimal CoveragePercent,
    DateOnly? FirstHistoricalDate,long AnonymousDocuments);
public sealed record SalesReportTaxRow(string TaxCode,decimal TaxRate,decimal Base,decimal Tax,decimal Total);
public sealed record SalesReportPaymentRow(string MovementType,string MethodCode,decimal Amount);
