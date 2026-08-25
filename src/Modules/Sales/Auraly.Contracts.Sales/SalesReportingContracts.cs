namespace Auraly.Contracts.Sales;

public static class SalesReportingPermissionCodes
{
    public const string Read = "sales.reports.read";
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
    Guid? WarehouseId = null, string? DocumentType = null);

public sealed record SalesReportTotals(
    long DocumentCount, decimal UnitsSold, decimal UnitsReturned, decimal GrossSales,
    decimal Discounts, decimal Returns, decimal NetUntaxedSales, decimal NetTax,
    decimal NetTotalSales, decimal NetRecognizedCost, decimal GrossProfit,
    decimal GrossMarginPercent, decimal CreditSales, decimal Collected, decimal Refunded);

public sealed record SalesReportTrendPoint(
    DateOnly Period, long DocumentCount, decimal GrossSales, decimal Returns,
    decimal NetSales, decimal GrossProfit);

public sealed record SalesReportSummary(
    SalesReportTotals Current, SalesReportTotals? Comparison,
    decimal? NetSalesChangePercent, IReadOnlyList<SalesReportTrendPoint> Trend,
    DateTimeOffset? ProjectedThrough);

public sealed record SalesTodayOverview(
    DateOnly BusinessDate, SalesReportTotals Totals, long CustomerCount,
    decimal AverageTicket, decimal ReturnRatePercent,
    DateTimeOffset? ProjectedThrough);

public sealed record CommercialVisitProjectionSource(
    Guid TenantId,Guid BusinessId,Guid RouteVisitId,DateOnly VisitDate,
    DateTimeOffset OccurredAt,Guid RouteId,string RouteCode,string RouteName,
    Guid? ZoneId,string? ZoneName,Guid SellerId,string SellerName,
    Guid RouteStopId,Guid CustomerId,string CustomerName,Guid PartySiteId,
    string Status,Guid? OrderId,string? SkipReason,string? VisitObservation,
    Guid RecordedByUserId);

public sealed record CommercialVisitReportRow(
    Guid RouteVisitId,DateOnly VisitDate,DateTimeOffset OccurredAt,
    Guid SellerId,string SellerName,Guid RouteId,string RouteName,string? ZoneName,
    Guid CustomerId,string CustomerName,string Status,bool HasOrder,Guid? OrderId,
    string? SkipReason,string? VisitObservation);
public sealed record CommercialVisitReportPage(
    IReadOnlyList<CommercialVisitReportRow> Items,int Page,int PageSize,int TotalCount,
    long VisitedCount,long OrderedCount,decimal EffectivenessPercent);

public sealed record CommercialOrderProjectionSource(Guid TenantId,Guid BusinessId,Guid OrderId,
    DateOnly CreatedDate,DateTimeOffset CreatedAt,string OrderNumber,Guid SellerId,string SellerName,
    Guid CustomerId,string CustomerName,Guid? RouteId,decimal TotalAmount,int Status,bool RequiresStockReview,
    Guid? PartySiteId=null,Guid? RouteStopId=null,Guid? ZoneId=null,string? RouteName=null,string? ZoneName=null,
    string SourceChannel="SellerOrder",bool CapturedOffline=false,DateTimeOffset? ConfirmedAt=null,
    DateTimeOffset? CancelledAt=null,Guid? InvoiceDocumentId=null,DateTimeOffset? InvoicedAt=null);
public sealed record SellerOrderReportRow(Guid SellerId,string SellerName,long OrderCount,long CustomerCount,
    decimal OrderAmount,long ConfirmedCount,long ReviewCount,long InvoicedCount);

public sealed record CommercialCoverageScheduleProjectionSource(
    Guid RouteScheduleId,byte DayOfWeek,int RunOrder,TimeOnly? PlannedStartTime);
public sealed record CommercialCoverageStopProjectionSource(
    Guid RouteStopId,Guid CustomerId,string CustomerName,Guid PartySiteId,string PartySiteName,
    int Sequence,TimeOnly? PlannedVisitTime,string? CityName,string? Neighborhood,
    decimal? Latitude,decimal? Longitude);
public sealed record CommercialCoveragePlanProjectionSource(
    Guid TenantId,Guid BusinessId,Guid RouteId,string RouteCode,string RouteName,
    Guid? ZoneId,string? ZoneName,Guid SellerId,string SellerName,string TimeZoneId,
    DateTimeOffset EffectiveAt,bool IsActive,
    IReadOnlyList<CommercialCoverageScheduleProjectionSource> Schedules,
    IReadOnlyList<CommercialCoverageStopProjectionSource> Stops);

public sealed record SellerPerformanceRow(
    Guid SellerId,string SellerName,long PlannedVisits,long VisitedCount,long SkippedCount,
    long OrderCount,long InvoicedCount,long CustomerCount,decimal OrderAmount,
    decimal NetSales,decimal GrossProfit,decimal VisitCoveragePercent,
    decimal VisitToOrderPercent,decimal OrderToInvoicePercent);
public sealed record SellerPerformanceOverview(
    long PlannedVisits,long VisitedCount,long SkippedCount,long OrderCount,long InvoicedCount,
    decimal NetSales,decimal GrossProfit,decimal VisitCoveragePercent,
    decimal VisitToOrderPercent,decimal OrderToInvoicePercent,
    IReadOnlyList<SellerPerformanceRow> Sellers,DateTimeOffset? ProjectedThrough);

public sealed record CommercialCoverageRow(
    Guid SellerId,string SellerName,Guid RouteId,string RouteName,Guid? ZoneId,string? ZoneName,
    long PlannedVisits,long VisitedCount,long SkippedCount,long MissingCount,long OrderedCount,
    decimal OperationalCoveragePercent,decimal VisitCoveragePercent,decimal EffectiveCoveragePercent);
public sealed record CommercialCoverageOverview(
    long PlannedVisits,long VisitedCount,long SkippedCount,long MissingCount,long OrderedCount,
    decimal OperationalCoveragePercent,decimal VisitCoveragePercent,decimal EffectiveCoveragePercent,
    DateOnly? CoverageAvailableFrom,IReadOnlyList<CommercialCoverageRow> Rows,
    DateTimeOffset? ProjectedThrough);

public sealed record SupplierImpactRow(
    Guid SupplierId,string SupplierName,long CoveredCustomers,long ImpactedCustomers,
    decimal PenetrationPercent,decimal NetSales,decimal GrossProfit,decimal SalesParticipationPercent,
    decimal NetPurchases,decimal ComparableNetSales,decimal ComparableNetPurchases,
    decimal? SalesGrowthPercent,decimal? PurchaseGrowthPercent);
public sealed record SupplierImpactOverview(
    long CoveredCustomers,long ImpactedCustomers,decimal NetSales,decimal NetPurchases,
    IReadOnlyList<SupplierImpactRow> Suppliers,DateTimeOffset? ProjectedThrough);

public sealed record SalesReportBreakdownRow(
    string Key, string Label, long DocumentCount, decimal Quantity,
    decimal GrossSales, decimal Discounts, decimal Returns, decimal NetUntaxedSales,
    decimal Tax, decimal NetSales, decimal RecognizedCost, decimal GrossProfit,
    decimal GrossMarginPercent, decimal ParticipationPercent);

public sealed record SalesReportDocumentRow(
    Guid DocumentId, string DocumentType, string DocumentNumber, string? FiscalNumber,
    DateTimeOffset IssuedAt, string CustomerName, string SellerName, string WarehouseName,
    decimal GrossAmount, decimal DiscountAmount, decimal UntaxedAmount, decimal TaxAmount,
    decimal TotalAmount, decimal ReturnedTotalAmount, decimal NetTotalAmount,
    decimal GrossProfit, string? FiscalStatus);

public sealed record SalesReportDocumentPage(
    IReadOnlyList<SalesReportDocumentRow> Items, int Page, int PageSize, int TotalCount);

public sealed record SalesReportLineRow(
    Guid FactId, string MovementType, DateTimeOffset OccurredAt, string ProductCode,
    string ProductName, string? CategoryName, decimal Quantity, decimal GrossAmount,
    decimal DiscountAmount, decimal UntaxedAmount, decimal TaxAmount, decimal TotalAmount,
    decimal RecognizedCostAmount, string? ReturnReasonCode, string? ReturnDisposition);

public sealed record SalesReportDocumentDetail(
    SalesReportDocumentRow Document, IReadOnlyList<SalesReportLineRow> Lines);

public sealed class SalesReportingForbiddenException(string message) : Exception(message);
public sealed class SalesReportingValidationException(string message) : Exception(message);
