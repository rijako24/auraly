using System.Data;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Purchasing;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed record SalesReportingSqlSession(
    SqlConnection Connection,
    SqlTransaction Transaction);

/// <summary>
/// Maintains the sales read model as an intrinsic effect of the canonical sale transaction.
/// The writer is deliberately idempotent at source-document level and never reads this model
/// to make operational, inventory, fiscal or accounting decisions.
/// </summary>
public sealed class SqlSalesReportingProjectionWriter(
    IAuralyIdGenerator ids,
    TimeProvider timeProvider)
{
    private const short ProjectionVersion = 2;

    public async Task ProjectOrderAsync(SalesReportingSqlSession session,string payload,long sourceVersion,CancellationToken ct)
    {
        var v=JsonSerializer.Deserialize<CommercialOrderProjectionSource>(payload,new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The seller order reporting source is invalid.");
        await using var c=new SqlCommand("""
          MERGE reporting.CommercialReportOrderFacts WITH(HOLDLOCK) AS target
          USING (SELECT @Id AS OrderId) AS source ON target.OrderId=source.OrderId
          WHEN MATCHED AND target.SourceVersion<@SourceVersion THEN UPDATE SET
            CreatedDate=@Date,CreatedAt=@At,OrderNumber=@Number,SellerId=@Seller,SellerName=@SellerName,
            CustomerId=@Customer,CustomerName=@CustomerName,RouteId=@Route,RouteName=@RouteName,
            ZoneId=@ZoneId,ZoneName=@ZoneName,RouteStopId=@RouteStopId,PartySiteId=@PartySiteId,
            SourceChannel=@SourceChannel,CapturedOffline=@CapturedOffline,TotalAmount=@Total,
            Status=@Status,RequiresStockReview=@Review,ConfirmedAt=@ConfirmedAt,CancelledAt=@CancelledAt,
            InvoiceDocumentId=COALESCE(@InvoiceDocumentId,target.InvoiceDocumentId),
            InvoicedAt=COALESCE(@InvoicedAt,target.InvoicedAt),SourceVersion=@SourceVersion,
            ProjectionVersion=@Version,ProjectedAt=SYSDATETIMEOFFSET()
          WHEN NOT MATCHED THEN INSERT(OrderId,TenantId,BusinessId,CreatedDate,CreatedAt,OrderNumber,
            SellerId,SellerName,CustomerId,CustomerName,RouteId,RouteName,ZoneId,ZoneName,RouteStopId,PartySiteId,
            SourceChannel,CapturedOffline,TotalAmount,Status,RequiresStockReview,ConfirmedAt,CancelledAt,
            InvoiceDocumentId,InvoicedAt,SourceVersion,ProjectionVersion,ProjectedAt)
          VALUES(@Id,@Tenant,@Business,@Date,@At,@Number,@Seller,@SellerName,@Customer,@CustomerName,
            @Route,@RouteName,@ZoneId,@ZoneName,@RouteStopId,@PartySiteId,@SourceChannel,@CapturedOffline,
            @Total,@Status,@Review,@ConfirmedAt,@CancelledAt,@InvoiceDocumentId,@InvoicedAt,
            @SourceVersion,@Version,SYSDATETIMEOFFSET());
          """,session.Connection,session.Transaction);
        c.Parameters.AddWithValue("@Id",v.OrderId);c.Parameters.AddWithValue("@Tenant",v.TenantId);c.Parameters.AddWithValue("@Business",v.BusinessId);
        c.Parameters.Add("@Date",SqlDbType.Date).Value=v.CreatedDate.ToDateTime(TimeOnly.MinValue);c.Parameters.AddWithValue("@At",v.CreatedAt);
        c.Parameters.AddWithValue("@Number",v.OrderNumber);c.Parameters.AddWithValue("@Seller",v.SellerId);c.Parameters.AddWithValue("@SellerName",v.SellerName);
        c.Parameters.AddWithValue("@Customer",v.CustomerId);c.Parameters.AddWithValue("@CustomerName",v.CustomerName);c.Parameters.AddWithValue("@Route",(object?)v.RouteId??DBNull.Value);
        c.Parameters.AddWithValue("@RouteName",(object?)v.RouteName??DBNull.Value);c.Parameters.AddWithValue("@ZoneId",(object?)v.ZoneId??DBNull.Value);
        c.Parameters.AddWithValue("@ZoneName",(object?)v.ZoneName??DBNull.Value);c.Parameters.AddWithValue("@RouteStopId",(object?)v.RouteStopId??DBNull.Value);
        c.Parameters.AddWithValue("@PartySiteId",(object?)v.PartySiteId??DBNull.Value);c.Parameters.AddWithValue("@SourceChannel",v.SourceChannel);
        c.Parameters.AddWithValue("@CapturedOffline",v.CapturedOffline);c.Parameters.AddWithValue("@ConfirmedAt",(object?)v.ConfirmedAt??DBNull.Value);
        c.Parameters.AddWithValue("@CancelledAt",(object?)v.CancelledAt??DBNull.Value);c.Parameters.AddWithValue("@InvoiceDocumentId",(object?)v.InvoiceDocumentId??DBNull.Value);
        c.Parameters.AddWithValue("@InvoicedAt",(object?)v.InvoicedAt??DBNull.Value);c.Parameters.AddWithValue("@SourceVersion",sourceVersion);
        AddDecimal(c,"@Total",v.TotalAmount,19,4);c.Parameters.AddWithValue("@Status",v.Status);c.Parameters.AddWithValue("@Review",v.RequiresStockReview);
        c.Parameters.AddWithValue("@Version",ProjectionVersion);await c.ExecuteNonQueryAsync(ct);
    }

    public async Task ProjectVisitAsync(SalesReportingSqlSession session,string payload,
        long sourceVersion,CancellationToken cancellationToken)
    {
        var value=JsonSerializer.Deserialize<CommercialVisitProjectionSource>(payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The commercial visit reporting source is invalid.");
        await using var command=new SqlCommand("""
            INSERT reporting.CommercialReportVisitFacts
              (RouteVisitId,TenantId,BusinessId,VisitDate,OccurredAt,RouteId,RouteCode,RouteName,
               ZoneId,ZoneName,SellerId,SellerName,RouteStopId,CustomerId,CustomerName,PartySiteId,
               Status,HasOrder,OrderId,SkipReason,VisitObservation,RecordedByUserId,
               ProjectionVersion,SourceVersion,ProjectedAt)
            VALUES(@VisitId,@TenantId,@BusinessId,@VisitDate,@OccurredAt,@RouteId,@RouteCode,@RouteName,
               @ZoneId,@ZoneName,@SellerId,@SellerName,@StopId,@CustomerId,@CustomerName,@SiteId,
               @Status,@HasOrder,@OrderId,@Reason,@Observation,@RecordedBy,@ProjectionVersion,@SourceVersion,@Now);
            """,session.Connection,session.Transaction);
        command.Parameters.AddWithValue("@VisitId",value.RouteVisitId);
        command.Parameters.AddWithValue("@TenantId",value.TenantId);
        command.Parameters.AddWithValue("@BusinessId",value.BusinessId);
        command.Parameters.Add("@VisitDate",SqlDbType.Date).Value=value.VisitDate.ToDateTime(TimeOnly.MinValue);
        command.Parameters.AddWithValue("@OccurredAt",value.OccurredAt);
        command.Parameters.AddWithValue("@RouteId",value.RouteId);
        command.Parameters.AddWithValue("@RouteCode",value.RouteCode);
        command.Parameters.AddWithValue("@RouteName",value.RouteName);
        command.Parameters.AddWithValue("@ZoneId",(object?)value.ZoneId??DBNull.Value);
        command.Parameters.AddWithValue("@ZoneName",(object?)value.ZoneName??DBNull.Value);
        command.Parameters.AddWithValue("@SellerId",value.SellerId);
        command.Parameters.AddWithValue("@SellerName",value.SellerName);
        command.Parameters.AddWithValue("@StopId",value.RouteStopId);
        command.Parameters.AddWithValue("@CustomerId",value.CustomerId);
        command.Parameters.AddWithValue("@CustomerName",value.CustomerName);
        command.Parameters.AddWithValue("@SiteId",value.PartySiteId);
        command.Parameters.AddWithValue("@Status",value.Status);
        command.Parameters.AddWithValue("@HasOrder",value.OrderId.HasValue);
        command.Parameters.AddWithValue("@OrderId",(object?)value.OrderId??DBNull.Value);
        command.Parameters.AddWithValue("@Reason",(object?)value.SkipReason??DBNull.Value);
        command.Parameters.AddWithValue("@Observation",(object?)value.VisitObservation??DBNull.Value);
        command.Parameters.AddWithValue("@RecordedBy",value.RecordedByUserId);
        command.Parameters.AddWithValue("@ProjectionVersion",ProjectionVersion);
        command.Parameters.AddWithValue("@SourceVersion",sourceVersion);
        command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());
        if(await command.ExecuteNonQueryAsync(cancellationToken)!=1)
            throw new InvalidOperationException("The commercial visit could not be projected.");
    }

    public async Task ProjectSaleAsync(
        SalesReportingSqlSession session,
        PosSaleUploadRequest value,
        CancellationToken cancellationToken)
    {
        var localDate = await ResolveLocalDateAsync(
            session, value.BusinessId, value.CommercialSnapshot.IssuedAt, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var recognizedCost = 0m;
        var seller = await ResolveSellerAttributionAsync(session, value, cancellationToken);

        foreach (var line in value.Lines.OrderBy(line => line.LineNumber))
        {
            var lineCost = line.DocumentUnitCost is decimal documentUnitCost
                ? decimal.Round(line.Quantity * documentUnitCost, 4, MidpointRounding.AwayFromZero)
                : await ReadSaleLineCostAsync(
                    session, value.DocumentId, value.CommercialSnapshot.DocumentType,
                    line.LineNumber, cancellationToken);
            recognizedCost += lineCost;
            await InsertSaleLineFactAsync(
                session, value, line, seller.SellerId, lineCost, localDate.Date, now,
                cancellationToken);
        }

        await InsertSaleDocumentAsync(
            session, value, seller, localDate, recognizedCost, now, cancellationToken);
        if(value.SourceOrderId is not null)
            await MarkOrderInvoicedAsync(session,value.SourceOrderId.Value,value.DocumentId,
                value.CommercialSnapshot.IssuedAt,cancellationToken);
        await InsertSalePaymentFactsAsync(session, value, localDate.Date, now, cancellationToken);
        await InsertSaleTaxFactsAsync(session, value, localDate.Date, now, cancellationToken);
        await ApplyDimensionDeltasAsync(session, value.BusinessId, value.DocumentId,
            value.CommercialSnapshot.DocumentType, 1, now, cancellationToken);

        var discount = value.Lines.Sum(line => line.DiscountAmount);
        var gross = value.CommercialSnapshot.UntaxedAmount + discount;
        var credit = value.Credit?.Amount ?? 0m;
        await ApplyDailyDeltaAsync(
            session, value.BusinessId, localDate.Date,
            documentCount: 1,
            unitsSold: value.Lines.Sum(line => line.Quantity),
            unitsReturned: 0,
            grossSales: gross,
            discounts: discount,
            returns: 0,
            netUntaxed: value.CommercialSnapshot.UntaxedAmount,
            netTax: value.CommercialSnapshot.TaxAmount,
            netTotal: value.CommercialSnapshot.PayableAmount,
            netCost: recognizedCost,
            grossProfit: value.CommercialSnapshot.UntaxedAmount - recognizedCost,
            creditSales: credit,
            collected: value.Payments.Sum(payment => payment.Amount),
            refunded: 0,
            now, cancellationToken);
        await RefreshProductRotationAsync(session, value.BusinessId, value.DocumentId,
            value.CommercialSnapshot.DocumentType, localDate.Date, now, cancellationToken);
        await UpdateCheckpointAsync(
            session, value.BusinessId, value.DocumentId,
            value.CommercialSnapshot.DocumentType, now, cancellationToken);
    }

    public async Task ProjectReturnAsync(
        SalesReportingSqlSession session,
        SalesReturnDocumentPayload value,
        CancellationToken cancellationToken)
    {
        var localDate = await ResolveLocalDateAsync(
            session, value.BusinessId, value.ReturnedAt, cancellationToken);
        var original = await ReadOriginalSaleDimensionsAsync(
            session, value.BusinessId, value.OriginalDocumentId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var returnedCost = 0m;

        foreach (var line in value.Lines.OrderBy(line => line.LineNumber))
        {
            var lineCost = decimal.Round(
                line.Quantity * line.RecognizedUnitCost, 4, MidpointRounding.AwayFromZero);
            returnedCost += lineCost;
            await InsertReturnLineFactAsync(
                session, value, original, line, lineCost, localDate.Date, now, cancellationToken);
        }

        await UpdateReturnedDocumentAsync(
            session, value, returnedCost, now, cancellationToken);
        await InsertReturnPaymentFactAsync(session, value, localDate.Date, now, cancellationToken);
        await InsertReturnTaxFactsAsync(session, value, localDate.Date, now, cancellationToken);
        await ApplyDimensionDeltasAsync(session, value.BusinessId, value.ReturnId,
            SalesReturnDocumentTypes.SalesReturn, 0, now, cancellationToken);

        var discount = value.Lines.Sum(line => line.DiscountAmount);
        await ApplyDailyDeltaAsync(
            session, value.BusinessId, localDate.Date,
            documentCount: 0,
            unitsSold: 0,
            unitsReturned: value.Lines.Sum(line => line.Quantity),
            grossSales: 0,
            discounts: 0,
            returns: value.TotalAmount,
            netUntaxed: -value.UntaxedAmount,
            netTax: -value.TaxAmount,
            netTotal: -value.TotalAmount,
            netCost: -returnedCost,
            grossProfit: returnedCost - value.UntaxedAmount,
            creditSales: 0,
            collected: 0,
            refunded: value.EconomicResolution == ReturnEconomicResolutions.Refund
                ? value.TotalAmount : 0,
            now, cancellationToken);
        await RefreshProductRotationAsync(session, value.BusinessId, value.ReturnId,
            SalesReturnDocumentTypes.SalesReturn, localDate.Date, now, cancellationToken);
        await UpdateCheckpointAsync(
            session, value.BusinessId, value.ReturnId,
            SalesReturnDocumentTypes.SalesReturn, now, cancellationToken);
    }

    public async Task ProjectCoverageAsync(SalesReportingSqlSession session,string payload,
        long sourceVersion,CancellationToken cancellationToken)
    {
        var value=JsonSerializer.Deserialize<CommercialCoveragePlanProjectionSource>(payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The commercial coverage source is invalid.");
        var effectiveDate=(await ResolveLocalDateAsync(session,value.BusinessId,value.EffectiveAt,cancellationToken)).Date;
        await using(var close=new SqlCommand("""
          DELETE reporting.CommercialCoverageAssignmentFacts
          WHERE BusinessId=@BusinessId AND RouteId=@RouteId AND ValidFromBusinessDate=@EffectiveDate;
          UPDATE reporting.CommercialCoverageAssignmentFacts SET ValidToBusinessDateExclusive=@EffectiveDate
          WHERE BusinessId=@BusinessId AND RouteId=@RouteId AND ValidToBusinessDateExclusive IS NULL
            AND ValidFromBusinessDate<@EffectiveDate;
          """,session.Connection,session.Transaction))
        {
            close.Parameters.AddWithValue("@BusinessId",value.BusinessId);close.Parameters.AddWithValue("@RouteId",value.RouteId);
            close.Parameters.Add("@EffectiveDate",SqlDbType.Date).Value=effectiveDate.ToDateTime(TimeOnly.MinValue);
            await close.ExecuteNonQueryAsync(cancellationToken);
        }
        if(!value.IsActive)
        {
            await UpdateCheckpointAsync(session,value.BusinessId,value.RouteId,"CommercialCoveragePlan",timeProvider.GetUtcNow(),cancellationToken);
            return;
        }
        foreach(var schedule in value.Schedules)
        foreach(var stop in value.Stops)
        {
            await using var insert=new SqlCommand("""
              INSERT reporting.CommercialCoverageAssignmentFacts
              (CoverageAssignmentFactId,TenantId,BusinessId,RouteId,RouteCode,RouteName,RouteScheduleId,DayOfWeek,RunOrder,
               PlannedStartTime,ZoneId,ZoneName,SellerId,SellerName,RouteStopId,CustomerId,CustomerName,PartySiteId,
               PartySiteName,Sequence,PlannedVisitTime,CityName,Neighborhood,Latitude,Longitude,TimeZoneId,
               ValidFromBusinessDate,SourceVersion,ProjectionVersion,ProjectedAt)
              VALUES(@FactId,@TenantId,@BusinessId,@RouteId,@RouteCode,@RouteName,@ScheduleId,@Day,@RunOrder,
               @StartTime,@ZoneId,@ZoneName,@SellerId,@SellerName,@StopId,@CustomerId,@CustomerName,@SiteId,
               @SiteName,@Sequence,@VisitTime,@City,@Neighborhood,@Latitude,@Longitude,@TimeZoneId,
               @EffectiveDate,@SourceVersion,@ProjectionVersion,SYSDATETIMEOFFSET());
              """,session.Connection,session.Transaction);
            insert.Parameters.AddWithValue("@FactId",ids.NewId());insert.Parameters.AddWithValue("@TenantId",value.TenantId);
            insert.Parameters.AddWithValue("@BusinessId",value.BusinessId);insert.Parameters.AddWithValue("@RouteId",value.RouteId);
            insert.Parameters.AddWithValue("@RouteCode",value.RouteCode);insert.Parameters.AddWithValue("@RouteName",value.RouteName);
            insert.Parameters.AddWithValue("@ScheduleId",schedule.RouteScheduleId);insert.Parameters.AddWithValue("@Day",schedule.DayOfWeek);
            insert.Parameters.AddWithValue("@RunOrder",schedule.RunOrder);insert.Parameters.AddWithValue("@StartTime",(object?)schedule.PlannedStartTime??DBNull.Value);
            insert.Parameters.AddWithValue("@ZoneId",(object?)value.ZoneId??DBNull.Value);insert.Parameters.AddWithValue("@ZoneName",(object?)value.ZoneName??DBNull.Value);
            insert.Parameters.AddWithValue("@SellerId",value.SellerId);insert.Parameters.AddWithValue("@SellerName",value.SellerName);
            insert.Parameters.AddWithValue("@StopId",stop.RouteStopId);insert.Parameters.AddWithValue("@CustomerId",stop.CustomerId);
            insert.Parameters.AddWithValue("@CustomerName",stop.CustomerName);insert.Parameters.AddWithValue("@SiteId",stop.PartySiteId);
            insert.Parameters.AddWithValue("@SiteName",stop.PartySiteName);insert.Parameters.AddWithValue("@Sequence",stop.Sequence);
            insert.Parameters.AddWithValue("@VisitTime",(object?)stop.PlannedVisitTime??DBNull.Value);insert.Parameters.AddWithValue("@City",(object?)stop.CityName??DBNull.Value);
            insert.Parameters.AddWithValue("@Neighborhood",(object?)stop.Neighborhood??DBNull.Value);AddNullableDecimal(insert,"@Latitude",stop.Latitude,9,6);
            AddNullableDecimal(insert,"@Longitude",stop.Longitude,9,6);insert.Parameters.AddWithValue("@TimeZoneId",value.TimeZoneId);
            insert.Parameters.Add("@EffectiveDate",SqlDbType.Date).Value=effectiveDate.ToDateTime(TimeOnly.MinValue);
            insert.Parameters.AddWithValue("@SourceVersion",sourceVersion);insert.Parameters.AddWithValue("@ProjectionVersion",ProjectionVersion);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await UpdateCheckpointAsync(session,value.BusinessId,value.RouteId,"CommercialCoveragePlan",timeProvider.GetUtcNow(),cancellationToken);
    }

    public Task ProjectGoodsReceiptAsync(SalesReportingSqlSession session,GoodsReceiptDocumentPayload value,
        CancellationToken cancellationToken)=>ProjectPurchaseAsync(session,value.TenantId,value.BusinessId,value.DocumentId,
            "GoodsReceipt",null,value.DocumentNumber,value.ReceivedAt,value.SupplierId,value.SupplierNameSnapshot,
            value.WarehouseId,value.WarehouseNameSnapshot,value.CurrencyCode,value.NetAmount,value.TaxAmount,value.GrandTotal,
            value.Lines.Select(x=>new PurchaseProjectionLine(x.LineNumber,null,x.ProductId,x.Description,x.Quantity,x.UnitCost,
                x.DiscountAmount,x.NetAmount,x.TaxAmount,x.LineTotal)).ToArray(),1,cancellationToken);

    public Task ProjectPurchaseReturnAsync(SalesReportingSqlSession session,PurchaseReturnDocumentPayload value,
        CancellationToken cancellationToken)=>ProjectPurchaseAsync(session,value.TenantId,value.BusinessId,value.ReturnId,
            "PurchaseReturn",value.OriginalGoodsReceiptId,value.DocumentNumber,value.ReturnedAt,value.SupplierId,value.SupplierNameSnapshot,
            value.WarehouseId,value.WarehouseNameSnapshot,value.CurrencyCode,value.NetAmount,value.TaxAmount,value.TotalAmount,
            value.Lines.Select(x=>new PurchaseProjectionLine(x.LineNumber,x.OriginalLineNumber,x.ProductId,x.Description,x.Quantity,x.UnitCost,
                x.DiscountAmount,x.NetAmount,x.TaxAmount,x.LineTotal)).ToArray(),-1,cancellationToken);

    private async Task ProjectPurchaseAsync(SalesReportingSqlSession session,Guid tenantId,Guid businessId,
        Guid documentId,string documentType,Guid? originalReceiptId,string documentNumber,DateTimeOffset occurredAt,
        Guid supplierId,string? supplierName,Guid warehouseId,string? warehouseName,string currencyCode,
        decimal net,decimal tax,decimal total,IReadOnlyList<PurchaseProjectionLine> lines,int sign,CancellationToken ct)
    {
        var local=await ResolveLocalDateAsync(session,businessId,occurredAt,ct);
        var names=await ResolvePurchaseNamesAsync(session,supplierId,warehouseId,supplierName,warehouseName,ct);
        await using(var document=new SqlCommand("""
          INSERT reporting.PurchaseReportDocuments(SourceDocumentId,TenantId,BusinessId,SourceDocumentType,OriginalGoodsReceiptId,
            DocumentNumber,OccurredAt,BusinessLocalDate,TimeZoneId,SupplierId,SupplierName,WarehouseId,WarehouseName,CurrencyCode,
            NetAmount,TaxAmount,TotalAmount,ProjectionVersion,ProjectedAt)
          VALUES(@Id,@Tenant,@Business,@Type,@Original,@Number,@At,@Date,@TimeZone,@Supplier,@SupplierName,@Warehouse,@WarehouseName,
            @Currency,@Net,@Tax,@Total,@Version,SYSDATETIMEOFFSET());
          """,session.Connection,session.Transaction))
        {
            document.Parameters.AddWithValue("@Id",documentId);document.Parameters.AddWithValue("@Tenant",tenantId);document.Parameters.AddWithValue("@Business",businessId);
            document.Parameters.AddWithValue("@Type",documentType);document.Parameters.AddWithValue("@Original",(object?)originalReceiptId??DBNull.Value);
            document.Parameters.AddWithValue("@Number",documentNumber);document.Parameters.AddWithValue("@At",occurredAt);
            document.Parameters.Add("@Date",SqlDbType.Date).Value=local.Date.ToDateTime(TimeOnly.MinValue);document.Parameters.AddWithValue("@TimeZone",local.TimeZoneId);
            document.Parameters.AddWithValue("@Supplier",supplierId);document.Parameters.AddWithValue("@SupplierName",names.Supplier);
            document.Parameters.AddWithValue("@Warehouse",warehouseId);document.Parameters.AddWithValue("@WarehouseName",names.Warehouse);
            document.Parameters.AddWithValue("@Currency",currencyCode);AddDecimal(document,"@Net",sign*net,19,4);AddDecimal(document,"@Tax",sign*tax,19,4);
            AddDecimal(document,"@Total",sign*total,19,4);document.Parameters.AddWithValue("@Version",ProjectionVersion);await document.ExecuteNonQueryAsync(ct);
        }
        foreach(var line in lines)
        {
            await using var item=new SqlCommand("""
              INSERT reporting.PurchaseReportLineFacts(PurchaseFactId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,
                SourceLineNumber,OriginalGoodsReceiptId,OriginalLineNumber,OccurredAt,BusinessLocalDate,SupplierId,SupplierName,
                WarehouseId,WarehouseName,ProductId,ProductName,Quantity,UnitCost,DiscountAmount,NetAmount,TaxAmount,TotalAmount,
                CurrencyCode,ProjectionVersion,ProjectedAt)
              VALUES(@Fact,@Tenant,@Business,@Document,@Type,@Line,@Original,@OriginalLine,@At,@Date,@Supplier,@SupplierName,
                @Warehouse,@WarehouseName,@Product,@ProductName,@Quantity,@UnitCost,@Discount,@Net,@Tax,@Total,@Currency,@Version,SYSDATETIMEOFFSET());
              """,session.Connection,session.Transaction);
            item.Parameters.AddWithValue("@Fact",ids.NewId());item.Parameters.AddWithValue("@Tenant",tenantId);item.Parameters.AddWithValue("@Business",businessId);
            item.Parameters.AddWithValue("@Document",documentId);item.Parameters.AddWithValue("@Type",documentType);item.Parameters.AddWithValue("@Line",line.LineNumber);
            item.Parameters.AddWithValue("@Original",(object?)originalReceiptId??DBNull.Value);item.Parameters.AddWithValue("@OriginalLine",(object?)line.OriginalLineNumber??DBNull.Value);
            item.Parameters.AddWithValue("@At",occurredAt);item.Parameters.Add("@Date",SqlDbType.Date).Value=local.Date.ToDateTime(TimeOnly.MinValue);
            item.Parameters.AddWithValue("@Supplier",supplierId);item.Parameters.AddWithValue("@SupplierName",names.Supplier);item.Parameters.AddWithValue("@Warehouse",warehouseId);
            item.Parameters.AddWithValue("@WarehouseName",names.Warehouse);item.Parameters.AddWithValue("@Product",line.ProductId);item.Parameters.AddWithValue("@ProductName",line.ProductName);
            AddDecimal(item,"@Quantity",sign*line.Quantity,19,6);AddDecimal(item,"@UnitCost",line.UnitCost,19,6);AddDecimal(item,"@Discount",sign*line.Discount,19,4);
            AddDecimal(item,"@Net",sign*line.Net,19,4);AddDecimal(item,"@Tax",sign*line.Tax,19,4);AddDecimal(item,"@Total",sign*line.Total,19,4);
            item.Parameters.AddWithValue("@Currency",currencyCode);item.Parameters.AddWithValue("@Version",ProjectionVersion);await item.ExecuteNonQueryAsync(ct);
        }
        await UpdateCheckpointAsync(session,businessId,documentId,documentType,timeProvider.GetUtcNow(),ct);
    }

    private static async Task<(string Supplier,string Warehouse)> ResolvePurchaseNamesAsync(SalesReportingSqlSession session,
        Guid supplierId,Guid warehouseId,string? supplierName,string? warehouseName,CancellationToken ct)
    {
        await using var command=new SqlCommand("SELECT s.Name,w.Name FROM dbo.Suppliers s CROSS JOIN dbo.Warehouses w WHERE s.SupplierId=@SupplierId AND w.WarehouseId=@WarehouseId;",session.Connection,session.Transaction);
        command.Parameters.AddWithValue("@SupplierId",supplierId);command.Parameters.AddWithValue("@WarehouseId",warehouseId);
        await using var reader=await command.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct) && (supplierName is null||warehouseName is null))throw new InvalidOperationException("The purchase reporting dimensions could not be resolved.");
        return (supplierName??reader.GetString(0),warehouseName??reader.GetString(1));
    }

    private sealed record PurchaseProjectionLine(int LineNumber,int? OriginalLineNumber,Guid ProductId,string ProductName,
        decimal Quantity,decimal UnitCost,decimal Discount,decimal Net,decimal Tax,decimal Total);

    private static async Task<(DateOnly Date, string TimeZoneId)> ResolveLocalDateAsync(
        SalesReportingSqlSession session,
        Guid businessId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT TimeZone FROM dbo.Businesses WHERE BusinessId=@BusinessId;";
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        var timeZoneId = (string?)await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The reporting business does not exist.");
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
        var local = TimeZoneInfo.ConvertTime(occurredAt, timeZone);
        return (DateOnly.FromDateTime(local.Date), timeZoneId);
    }

    private static async Task MarkOrderInvoicedAsync(SalesReportingSqlSession session,Guid orderId,
        Guid documentId,DateTimeOffset invoicedAt,CancellationToken ct)
    {await using var command=new SqlCommand("""
       UPDATE reporting.CommercialReportOrderFacts SET InvoiceDocumentId=@DocumentId,InvoicedAt=@InvoicedAt,ProjectedAt=SYSDATETIMEOFFSET()
       WHERE OrderId=@OrderId AND (InvoiceDocumentId IS NULL OR InvoiceDocumentId=@DocumentId);
       """,session.Connection,session.Transaction);command.Parameters.AddWithValue("@OrderId",orderId);
       command.Parameters.AddWithValue("@DocumentId",documentId);command.Parameters.AddWithValue("@InvoicedAt",invoicedAt);
       await command.ExecuteNonQueryAsync(ct);}

    private static async Task<SellerAttribution> ResolveSellerAttributionAsync(
        SalesReportingSqlSession session,
        PosSaleUploadRequest value,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP(1) seller.SellerId,
              COALESCE(NULLIF(party.DisplayName,N''),NULLIF(party.LegalName,N''),
                       NULLIF(CONCAT(party.FirstName,N' ',party.LastName),N' '),seller.Code)
            FROM dbo.CommerceSellers seller
            INNER JOIN dbo.Parties party ON party.PartyId=seller.PartyId
            LEFT JOIN dbo.AppUsers app ON app.PartyId=seller.PartyId
            LEFT JOIN dbo.Orders sourceOrder
              ON sourceOrder.OrderId=@SourceOrderId AND sourceOrder.BusinessId=@BusinessId
            WHERE seller.BusinessId=@BusinessId
              AND ((@SourceOrderId IS NOT NULL AND seller.SellerId=sourceOrder.SellerId)
                OR (@SourceOrderId IS NULL AND app.UserId=@SoldByUserId))
            ORDER BY CASE WHEN sourceOrder.SellerId=seller.SellerId THEN 0 ELSE 1 END,seller.SellerId;
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        command.Parameters.AddWithValue("@SourceOrderId", (object?)value.SourceOrderId ?? DBNull.Value);
        command.Parameters.AddWithValue("@SoldByUserId", value.SoldByUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
            return new SellerAttribution(reader.GetGuid(0), reader.GetString(1));

        return new SellerAttribution(null, "Sin vendedor");
    }

    private static async Task<decimal> ReadSaleLineCostAsync(
        SalesReportingSqlSession session,
        Guid documentId,
        string documentType,
        int lineNumber,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CASE WHEN line.AttributionSnapshotVersion>0
                        THEN COALESCE(line.UnitCostSnapshot*line.Quantity,0)
                        ELSE COALESCE(ABS(movement.ValueChange),0) END
            FROM dbo.SalesDocumentLines line
            LEFT JOIN dbo.InventoryMovements movement
              ON movement.DocumentId=line.DocumentId
             AND movement.DocumentType=@DocumentType
             AND movement.LineNumber=line.LineNumber
             AND movement.MovementType=N'Sale'
            WHERE line.DocumentId=@DocumentId AND line.LineNumber=@LineNumber;
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.AddWithValue("@LineNumber", lineNumber);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? 0m : Convert.ToDecimal(result);
    }

    private sealed record SellerAttribution(Guid? SellerId, string SellerName);

    private async Task InsertSaleDocumentAsync(
        SalesReportingSqlSession session,
        PosSaleUploadRequest value,
        SellerAttribution seller,
        (DateOnly Date, string TimeZoneId) localDate,
        decimal recognizedCost,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT reporting.SalesReportDocuments
            (
              DocumentId,TenantId,BusinessId,DocumentType,DocumentNumber,FiscalNumber,
              IssuedAt,BusinessLocalDate,TimeZoneId,WarehouseId,WarehouseName,WorkSessionId,SellerId,SellerName,
              CustomerId,CustomerIdentification,CustomerName,SourceMode,FiscalStatus,
              CurrencyCode,GrossAmount,DiscountAmount,UntaxedAmount,TaxAmount,TotalAmount,
              CreditAmount,CollectedAmount,RecognizedCostAmount,ProjectionVersion,
              SourcePayloadHash,ProjectedAt
            )
            SELECT d.DocumentId,@TenantId,d.BusinessId,d.DocumentType,d.DocumentNumber,d.FiscalNumber,
                   d.IssuedAt,@LocalDate,@TimeZoneId,d.WarehouseId,w.Name,d.WorkSessionId,@SellerId,@SellerName,
                   d.CustomerId,d.CustomerIdentification,
                   COALESCE(NULLIF(p.DisplayName,N''),NULLIF(p.LegalName,N''),
                            NULLIF(CONCAT(p.FirstName,N' ',p.LastName),N' '),N'Consumidor final'),
                   d.SourceMode,d.FiscalStatus,@Currency,@Gross,@Discount,d.UntaxedAmount,
                   d.TaxAmount,d.PayableAmount,d.CreditAmount,@Collected,@Cost,@Version,
                   d.PayloadHash,@ProjectedAt
            FROM dbo.SalesDocuments d
            INNER JOIN dbo.Warehouses w ON w.WarehouseId=d.WarehouseId
            LEFT JOIN dbo.Customers c ON c.CustomerId=d.CustomerId
            LEFT JOIN dbo.Parties p ON p.PartyId=c.PartyId
            WHERE d.DocumentId=@DocumentId AND d.BusinessId=@BusinessId;
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", value.DocumentId);
        command.Parameters.AddWithValue("@TenantId", value.TenantId);
        command.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        command.Parameters.AddWithValue("@LocalDate", localDate.Date.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@TimeZoneId", localDate.TimeZoneId);
        command.Parameters.AddWithValue("@Currency", value.UblSnapshot?.CurrencyCode ?? "COP");
        command.Parameters.AddWithValue("@SellerId", (object?)seller.SellerId ?? DBNull.Value);
        command.Parameters.AddWithValue("@SellerName", seller.SellerName);
        AddDecimal(command, "@Gross", value.CommercialSnapshot.UntaxedAmount + value.Lines.Sum(x => x.DiscountAmount), 19, 4);
        AddDecimal(command, "@Discount", value.Lines.Sum(x => x.DiscountAmount), 19, 4);
        AddDecimal(command, "@Collected", value.Payments.Sum(x => x.Amount), 19, 4);
        AddDecimal(command, "@Cost", recognizedCost, 19, 4);
        command.Parameters.AddWithValue("@Version", ProjectionVersion);
        command.Parameters.AddWithValue("@ProjectedAt", now);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The sale reporting document could not be projected.");
    }

    private async Task InsertSaleLineFactAsync(
        SalesReportingSqlSession session,
        PosSaleUploadRequest value,
        PosSaleLineContract line,
        Guid? sellerId,
        decimal cost,
        DateOnly localDate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT reporting.SalesReportLineFacts
            (
              FactId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourceLineNumber,
              OriginalSaleDocumentId,OriginalLineNumber,MovementType,OccurredAt,BusinessLocalDate,
              WarehouseId,WorkSessionId,SellerId,CustomerId,ProductId,ProductCode,ProductName,
              CategoryId,CategoryName,SupplierId,SupplierName,Quantity,GrossAmount,DiscountAmount,UntaxedAmount,TaxAmount,
              TotalAmount,RecognizedCostAmount,ProjectionVersion,ProjectedAt
            )
            SELECT @FactId,@TenantId,@BusinessId,@DocumentId,@DocumentType,@LineNumber,
                   @DocumentId,@LineNumber,N'Sale',@OccurredAt,@LocalDate,@WarehouseId,
                   @WorkSessionId,@SellerId,@CustomerId,p.ProductId,
                   CASE WHEN sourceLine.AttributionSnapshotVersion>0
                        THEN COALESCE(sourceLine.ProductCodeSnapshot,N'')
                        ELSE COALESCE(p.ProductCode,p.Sku,p.Reference,N'') END,
                   CASE WHEN sourceLine.AttributionSnapshotVersion>0
                        THEN COALESCE(sourceLine.ProductNameSnapshot,sourceLine.Description)
                        ELSE p.Name END,
                   CASE WHEN sourceLine.AttributionSnapshotVersion>0
                        THEN sourceLine.CategoryIdSnapshot ELSE p.ProductCategoryId END,
                   CASE WHEN sourceLine.AttributionSnapshotVersion>0
                        THEN sourceLine.CategoryNameSnapshot ELSE COALESCE(pc.Name,p.CategoryName) END,
                   CASE WHEN sourceLine.AttributionSnapshotVersion>0
                        THEN sourceLine.SupplierIdSnapshot ELSE supplier.SupplierId END,
                   CASE WHEN sourceLine.AttributionSnapshotVersion>0
                        THEN sourceLine.SupplierNameSnapshot ELSE supplier.Name END,
                   @Quantity,@Gross,@Discount,@Untaxed,@Tax,
                   @Total,@Cost,@Version,@ProjectedAt
            FROM dbo.SalesDocumentLines sourceLine
            INNER JOIN dbo.Products p ON p.ProductId=sourceLine.ProductId
            LEFT JOIN dbo.ProductCategories pc ON pc.ProductCategoryId=p.ProductCategoryId
            OUTER APPLY(SELECT TOP(1) s.SupplierId,s.Name FROM dbo.SupplierProducts sp
              INNER JOIN dbo.Suppliers s ON s.SupplierId=sp.SupplierId AND s.BusinessId=@BusinessId
              WHERE sp.ProductId=p.ProductId AND sp.BusinessId=@BusinessId AND sp.IsActive=1 AND s.IsActive=1
              ORDER BY sp.IsPrimary DESC,sp.CreatedAt,sp.SupplierProductId) supplier
            WHERE sourceLine.DocumentId=@DocumentId AND sourceLine.LineNumber=@LineNumber
              AND p.ProductId=@ProductId
              AND p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId);
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@FactId", ids.NewId());
        AddSaleFactParameters(command, value, line, sellerId, localDate, now);
        AddDecimal(command, "@Cost", cost, 19, 4);
        command.Parameters.AddWithValue("@Version", ProjectionVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Sale line {line.LineNumber} could not be projected.");
    }

    private static void AddSaleFactParameters(
        SqlCommand command, PosSaleUploadRequest value, PosSaleLineContract line,
        Guid? sellerId, DateOnly localDate, DateTimeOffset now)
    {
        command.Parameters.AddWithValue("@TenantId", value.TenantId);
        command.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        command.Parameters.AddWithValue("@DocumentId", value.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", value.CommercialSnapshot.DocumentType);
        command.Parameters.AddWithValue("@LineNumber", line.LineNumber);
        command.Parameters.AddWithValue("@OccurredAt", value.CommercialSnapshot.IssuedAt);
        command.Parameters.AddWithValue("@LocalDate", localDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@WarehouseId", value.WarehouseId);
        command.Parameters.AddWithValue("@WorkSessionId", value.WorkSessionId);
        command.Parameters.AddWithValue("@SellerId", (object?)sellerId ?? DBNull.Value);
        command.Parameters.AddWithValue("@CustomerId", (object?)value.CustomerId ?? DBNull.Value);
        command.Parameters.AddWithValue("@ProductId", line.ProductId);
        command.Parameters.AddWithValue("@ProductName", line.Description);
        AddDecimal(command, "@Quantity", line.Quantity, 19, 6);
        AddDecimal(command, "@Gross", line.UntaxedAmount + line.DiscountAmount, 19, 4);
        AddDecimal(command, "@Discount", line.DiscountAmount, 19, 4);
        AddDecimal(command, "@Untaxed", line.UntaxedAmount, 19, 4);
        AddDecimal(command, "@Tax", line.TaxAmount, 19, 4);
        AddDecimal(command, "@Total", line.LineTotal, 19, 4);
        command.Parameters.AddWithValue("@ProjectedAt", now);
    }

    private static async Task InsertSalePaymentFactsAsync(
        SalesReportingSqlSession session,
        PosSaleUploadRequest value,
        DateOnly localDate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT reporting.SalesReportPaymentFacts
              (SourceDocumentId,SourceDocumentType,PaymentNumber,TenantId,BusinessId,
               BusinessLocalDate,MovementType,MethodCode,Amount,Reference,WorkSessionId,ProjectedAt)
            VALUES(@DocumentId,@DocumentType,@Number,@TenantId,@BusinessId,@LocalDate,
                   @MovementType,@Method,@Amount,@Reference,@WorkSessionId,@ProjectedAt);
            """;
        foreach (var payment in value.Payments.OrderBy(x => x.PaymentNumber))
            await InsertPaymentFactAsync(session, sql, value.DocumentId,
                value.CommercialSnapshot.DocumentType, payment.PaymentNumber, value.TenantId,
                value.BusinessId, localDate, "Payment", payment.MethodCode, payment.Amount,
                payment.Reference, value.WorkSessionId, now, cancellationToken);
        if (value.Credit is not null)
            await InsertPaymentFactAsync(session, sql, value.DocumentId,
                value.CommercialSnapshot.DocumentType, 0, value.TenantId, value.BusinessId,
                localDate, "Credit", "Credit", value.Credit.Amount, null,
                value.WorkSessionId, now, cancellationToken);
    }

    private static async Task InsertPaymentFactAsync(
        SalesReportingSqlSession session, string sql,
        Guid documentId, string documentType, int number, Guid tenantId, Guid businessId,
        DateOnly localDate, string movementType, string method, decimal amount,
        string? reference, Guid? workSessionId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.AddWithValue("@Number", number);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@LocalDate", localDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@MovementType", movementType);
        command.Parameters.AddWithValue("@Method", method);
        AddDecimal(command, "@Amount", amount, 19, 4);
        command.Parameters.AddWithValue("@Reference", (object?)reference ?? DBNull.Value);
        command.Parameters.AddWithValue("@WorkSessionId", (object?)workSessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("@ProjectedAt", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertSaleTaxFactsAsync(
        SalesReportingSqlSession session, PosSaleUploadRequest value,
        DateOnly localDate, DateTimeOffset now, CancellationToken cancellationToken) =>
        await InsertTaxFactsAsync(session, value.DocumentId, value.CommercialSnapshot.DocumentType,
            value.TenantId, value.BusinessId, localDate, value.Lines.Select(line =>
                new TaxFact(line.TaxCode, line.TaxRate, line.UntaxedAmount,
                    line.TaxAmount, line.LineTotal)), 1m, now, cancellationToken);

    private static async Task InsertReturnTaxFactsAsync(
        SalesReportingSqlSession session, SalesReturnDocumentPayload value,
        DateOnly localDate, DateTimeOffset now, CancellationToken cancellationToken) =>
        await InsertTaxFactsAsync(session, value.ReturnId, SalesReturnDocumentTypes.SalesReturn,
            value.TenantId, value.BusinessId, localDate, value.Lines.Select(line =>
                new TaxFact(line.TaxCode, line.TaxRate, line.UntaxedAmount,
                    line.TaxAmount, line.LineTotal)), -1m, now, cancellationToken);

    private static async Task InsertTaxFactsAsync(
        SalesReportingSqlSession session,
        Guid documentId, string documentType, Guid tenantId, Guid businessId,
        DateOnly localDate, IEnumerable<TaxFact> lines, decimal sign,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT reporting.SalesReportTaxFacts
              (SourceDocumentId,SourceDocumentType,TaxCode,TaxRate,TenantId,BusinessId,
               BusinessLocalDate,TaxableAmount,TaxAmount,TotalAmount,ProjectedAt)
            VALUES(@DocumentId,@DocumentType,@Code,@Rate,@TenantId,@BusinessId,@LocalDate,
                   @Taxable,@Tax,@Total,@ProjectedAt);
            """;
        foreach (var group in lines.GroupBy(x => new { x.Code, x.Rate }))
        {
            await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("@DocumentId", documentId);
            command.Parameters.AddWithValue("@DocumentType", documentType);
            command.Parameters.AddWithValue("@Code", group.Key.Code);
            AddDecimal(command, "@Rate", group.Key.Rate, 9, 6);
            command.Parameters.AddWithValue("@TenantId", tenantId);
            command.Parameters.AddWithValue("@BusinessId", businessId);
            command.Parameters.AddWithValue("@LocalDate", localDate.ToDateTime(TimeOnly.MinValue));
            AddDecimal(command, "@Taxable", sign * group.Sum(x => x.Taxable), 19, 4);
            AddDecimal(command, "@Tax", sign * group.Sum(x => x.Tax), 19, 4);
            AddDecimal(command, "@Total", sign * group.Sum(x => x.Total), 19, 4);
            command.Parameters.AddWithValue("@ProjectedAt", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<OriginalSaleDimensions> ReadOriginalSaleDimensionsAsync(
        SalesReportingSqlSession session, Guid businessId,
        Guid documentId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT WarehouseId,WorkSessionId,SoldByUserId,CustomerId
            FROM dbo.SalesDocuments
            WHERE BusinessId=@BusinessId AND DocumentId=@DocumentId;
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The original sale could not be projected.");
        return new OriginalSaleDimensions(reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3));
    }

    private async Task InsertReturnLineFactAsync(
        SalesReportingSqlSession session, SalesReturnDocumentPayload value,
        OriginalSaleDimensions original, SalesReturnLineSnapshot line, decimal cost,
        DateOnly localDate, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT reporting.SalesReportLineFacts
            (
              FactId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourceLineNumber,
              OriginalSaleDocumentId,OriginalLineNumber,MovementType,OccurredAt,BusinessLocalDate,
              WarehouseId,WorkSessionId,SellerId,CustomerId,ProductId,ProductCode,ProductName,
              CategoryId,CategoryName,SupplierId,SupplierName,Quantity,GrossAmount,DiscountAmount,UntaxedAmount,TaxAmount,
              TotalAmount,RecognizedCostAmount,ReturnReasonCode,ReturnDisposition,
              ProjectionVersion,ProjectedAt
            )
            SELECT @FactId,@TenantId,@BusinessId,@ReturnId,N'SalesReturn',@LineNumber,
                   @OriginalId,@OriginalLine,N'Return',@OccurredAt,@LocalDate,@WarehouseId,
                   @WorkSessionId,@SellerId,@CustomerId,p.ProductId,
                   COALESCE(p.ProductCode,p.Sku,p.Reference,N''),p.Name,p.ProductCategoryId,
                   COALESCE(pc.Name,p.CategoryName),supplier.SupplierId,supplier.Name,
                   -@Quantity,-@Gross,-@Discount,-@Untaxed,-@Tax,
                   -@Total,-@Cost,@Reason,@Disposition,@Version,@ProjectedAt
            FROM dbo.Products p
            LEFT JOIN dbo.ProductCategories pc ON pc.ProductCategoryId=p.ProductCategoryId
            OUTER APPLY(SELECT TOP(1) s.SupplierId,s.Name FROM dbo.SupplierProducts sp
              INNER JOIN dbo.Suppliers s ON s.SupplierId=sp.SupplierId AND s.BusinessId=@BusinessId
              WHERE sp.ProductId=p.ProductId AND sp.BusinessId=@BusinessId AND sp.IsActive=1 AND s.IsActive=1
              ORDER BY sp.IsPrimary DESC,sp.CreatedAt,sp.SupplierProductId) supplier
            WHERE p.ProductId=@ProductId AND p.TenantId=@TenantId;
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@FactId", ids.NewId());
        command.Parameters.AddWithValue("@TenantId", value.TenantId);
        command.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        command.Parameters.AddWithValue("@ReturnId", value.ReturnId);
        command.Parameters.AddWithValue("@LineNumber", line.LineNumber);
        command.Parameters.AddWithValue("@OriginalId", value.OriginalDocumentId);
        command.Parameters.AddWithValue("@OriginalLine", line.OriginalLineNumber);
        command.Parameters.AddWithValue("@OccurredAt", value.ReturnedAt);
        command.Parameters.AddWithValue("@LocalDate", localDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@WarehouseId", value.WarehouseId);
        command.Parameters.AddWithValue("@WorkSessionId", (object?)(value.WorkSessionId ?? original.WorkSessionId) ?? DBNull.Value);
        command.Parameters.AddWithValue("@SellerId", (object?)original.SellerId ?? DBNull.Value);
        command.Parameters.AddWithValue("@CustomerId", (object?)(value.CustomerId ?? original.CustomerId) ?? DBNull.Value);
        command.Parameters.AddWithValue("@ProductId", line.ProductId);
        AddDecimal(command, "@Quantity", line.Quantity, 19, 6);
        AddDecimal(command, "@Gross", line.UntaxedAmount + line.DiscountAmount, 19, 4);
        AddDecimal(command, "@Discount", line.DiscountAmount, 19, 4);
        AddDecimal(command, "@Untaxed", line.UntaxedAmount, 19, 4);
        AddDecimal(command, "@Tax", line.TaxAmount, 19, 4);
        AddDecimal(command, "@Total", line.LineTotal, 19, 4);
        AddDecimal(command, "@Cost", cost, 19, 4);
        command.Parameters.AddWithValue("@Reason", string.IsNullOrWhiteSpace(value.ReasonCode)
            ? value.CorrectionCode : value.ReasonCode);
        command.Parameters.AddWithValue("@Disposition", line.InventoryDisposition);
        command.Parameters.AddWithValue("@Version", ProjectionVersion);
        command.Parameters.AddWithValue("@ProjectedAt", now);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Return line {line.LineNumber} could not be projected.");
    }

    private static async Task UpdateReturnedDocumentAsync(
        SalesReportingSqlSession session, SalesReturnDocumentPayload value,
        decimal returnedCost, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE reporting.SalesReportDocuments
            SET ReturnedUntaxedAmount=ReturnedUntaxedAmount+@Untaxed,
                ReturnedTaxAmount=ReturnedTaxAmount+@Tax,
                ReturnedTotalAmount=ReturnedTotalAmount+@Total,
                ReturnedCostAmount=ReturnedCostAmount+@Cost,
                ProjectedAt=@ProjectedAt
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId;
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", value.OriginalDocumentId);
        command.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        AddDecimal(command, "@Untaxed", value.UntaxedAmount, 19, 4);
        AddDecimal(command, "@Tax", value.TaxAmount, 19, 4);
        AddDecimal(command, "@Total", value.TotalAmount, 19, 4);
        AddDecimal(command, "@Cost", returnedCost, 19, 4);
        command.Parameters.AddWithValue("@ProjectedAt", now);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The original sale is missing from the sales projection.");
    }

    private static Task InsertReturnPaymentFactAsync(
        SalesReportingSqlSession session, SalesReturnDocumentPayload value,
        DateOnly localDate, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT reporting.SalesReportPaymentFacts
              (SourceDocumentId,SourceDocumentType,PaymentNumber,TenantId,BusinessId,
               BusinessLocalDate,MovementType,MethodCode,Amount,Reference,WorkSessionId,ProjectedAt)
            VALUES(@DocumentId,@DocumentType,@Number,@TenantId,@BusinessId,@LocalDate,
                   @MovementType,@Method,@Amount,@Reference,@WorkSessionId,@ProjectedAt);
            """;
        return InsertPaymentFactAsync(session, sql, value.ReturnId,
            SalesReturnDocumentTypes.SalesReturn, 1, value.TenantId, value.BusinessId,
            localDate,
            value.EconomicResolution == ReturnEconomicResolutions.Refund ? "Refund" : "CreditApplication",
            value.RefundMethodCode ?? "CustomerCredit", -value.TotalAmount, value.DocumentNumber,
            value.WorkSessionId, now, cancellationToken);
    }

    private static async Task ApplyDimensionDeltasAsync(
        SalesReportingSqlSession session, Guid businessId, Guid sourceDocumentId,
        string sourceDocumentType, long documentCount, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH dimensions AS
            (
              SELECT f.BusinessLocalDate,v.DimensionType,v.DimensionKey,v.DimensionLabel,
                     @DocumentCount DocumentCount,SUM(f.Quantity) Quantity,
                     SUM(CASE WHEN f.MovementType=N'Sale' THEN f.GrossAmount ELSE 0 END) GrossSales,
                     SUM(CASE WHEN f.MovementType=N'Sale' THEN f.DiscountAmount ELSE 0 END) Discounts,
                     -SUM(CASE WHEN f.MovementType=N'Return' THEN f.TotalAmount ELSE 0 END) Returns,
                     SUM(f.UntaxedAmount) NetUntaxed,SUM(f.TaxAmount) NetTax,
                     SUM(f.TotalAmount) NetTotal,SUM(f.RecognizedCostAmount) NetCost,
                     SUM(f.UntaxedAmount-f.RecognizedCostAmount) GrossProfit
              FROM reporting.SalesReportLineFacts f
              INNER JOIN reporting.SalesReportDocuments d ON d.DocumentId=f.OriginalSaleDocumentId
              CROSS APPLY(VALUES
                (N'Customer',COALESCE(CONVERT(nvarchar(80),d.CustomerId),N'final-consumer'),d.CustomerName),
                (N'Seller',COALESCE(CONVERT(nvarchar(80),d.SellerId),N'no-seller'),d.SellerName),
                (N'Supplier',COALESCE(CONVERT(nvarchar(80),f.SupplierId),N'no-supplier'),COALESCE(f.SupplierName,N'Sin proveedor asociado')),
                (N'Product',CONVERT(nvarchar(80),f.ProductId),f.ProductName),
                (N'Category',COALESCE(CONVERT(nvarchar(80),f.CategoryId),N'no-category'),COALESCE(f.CategoryName,N'Sin categoría')),
                (N'Warehouse',CONVERT(nvarchar(80),d.WarehouseId),d.WarehouseName)
              ) v(DimensionType,DimensionKey,DimensionLabel)
              WHERE f.BusinessId=@BusinessId AND f.SourceDocumentId=@SourceDocumentId
                AND f.SourceDocumentType=@SourceDocumentType
              GROUP BY f.BusinessLocalDate,v.DimensionType,v.DimensionKey,v.DimensionLabel
            )
            MERGE reporting.SalesReportDailyDimensionTotals WITH(HOLDLOCK) AS target
            USING dimensions source
            ON target.BusinessId=@BusinessId AND target.BusinessLocalDate=source.BusinessLocalDate
              AND target.DimensionType=source.DimensionType AND target.DimensionKey=source.DimensionKey
              AND target.CurrencyCode=N'COP'
            WHEN MATCHED THEN UPDATE SET DimensionLabel=source.DimensionLabel,
              DocumentCount=target.DocumentCount+source.DocumentCount,
              Quantity=target.Quantity+source.Quantity,GrossSales=target.GrossSales+source.GrossSales,
              Discounts=target.Discounts+source.Discounts,Returns=target.Returns+source.Returns,
              NetUntaxedSales=target.NetUntaxedSales+source.NetUntaxed,
              NetTax=target.NetTax+source.NetTax,NetTotalSales=target.NetTotalSales+source.NetTotal,
              NetRecognizedCost=target.NetRecognizedCost+source.NetCost,
              GrossProfit=target.GrossProfit+source.GrossProfit,ProjectionVersion=@Version,UpdatedAt=@Now
            WHEN NOT MATCHED THEN INSERT
              (BusinessId,BusinessLocalDate,DimensionType,DimensionKey,DimensionLabel,CurrencyCode,
               DocumentCount,Quantity,GrossSales,Discounts,Returns,NetUntaxedSales,NetTax,
               NetTotalSales,NetRecognizedCost,GrossProfit,ProjectionVersion,UpdatedAt)
            VALUES(@BusinessId,source.BusinessLocalDate,source.DimensionType,source.DimensionKey,
              source.DimensionLabel,N'COP',source.DocumentCount,source.Quantity,source.GrossSales,
              source.Discounts,source.Returns,source.NetUntaxed,source.NetTax,source.NetTotal,
              source.NetCost,source.GrossProfit,@Version,@Now);
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@SourceDocumentId", sourceDocumentId);
        command.Parameters.AddWithValue("@SourceDocumentType", sourceDocumentType);
        command.Parameters.AddWithValue("@DocumentCount", documentCount);
        command.Parameters.AddWithValue("@Version", ProjectionVersion);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ApplyDailyDeltaAsync(
        SalesReportingSqlSession session, Guid businessId, DateOnly date,
        long documentCount, decimal unitsSold, decimal unitsReturned, decimal grossSales,
        decimal discounts, decimal returns, decimal netUntaxed, decimal netTax, decimal netTotal,
        decimal netCost, decimal grossProfit, decimal creditSales, decimal collected,
        decimal refunded, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            MERGE reporting.SalesReportDailyTotals WITH(HOLDLOCK) AS target
            USING (SELECT @BusinessId BusinessId,@Date BusinessLocalDate,@Currency CurrencyCode) source
            ON target.BusinessId=source.BusinessId
               AND target.BusinessLocalDate=source.BusinessLocalDate
               AND target.CurrencyCode=source.CurrencyCode
            WHEN MATCHED THEN UPDATE SET
              DocumentCount=target.DocumentCount+@DocumentCount,
              UnitsSold=target.UnitsSold+@UnitsSold,
              UnitsReturned=target.UnitsReturned+@UnitsReturned,
              GrossSales=target.GrossSales+@GrossSales,
              Discounts=target.Discounts+@Discounts,
              Returns=target.Returns+@Returns,
              NetUntaxedSales=target.NetUntaxedSales+@NetUntaxed,
              NetTax=target.NetTax+@NetTax,
              NetTotalSales=target.NetTotalSales+@NetTotal,
              NetRecognizedCost=target.NetRecognizedCost+@NetCost,
              GrossProfit=target.GrossProfit+@GrossProfit,
              CreditSales=target.CreditSales+@CreditSales,
              Collected=target.Collected+@Collected,
              Refunded=target.Refunded+@Refunded,
              ProjectionVersion=@Version,UpdatedAt=@Now
            WHEN NOT MATCHED THEN INSERT
              (BusinessId,BusinessLocalDate,CurrencyCode,DocumentCount,UnitsSold,UnitsReturned,
               GrossSales,Discounts,Returns,NetUntaxedSales,NetTax,NetTotalSales,
               NetRecognizedCost,GrossProfit,CreditSales,Collected,Refunded,ProjectionVersion,UpdatedAt)
            VALUES
              (@BusinessId,@Date,@Currency,@DocumentCount,@UnitsSold,@UnitsReturned,
               @GrossSales,@Discounts,@Returns,@NetUntaxed,@NetTax,@NetTotal,
               @NetCost,@GrossProfit,@CreditSales,@Collected,@Refunded,@Version,@Now);
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Date", date.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@Currency", "COP");
        command.Parameters.AddWithValue("@DocumentCount", documentCount);
        AddDecimal(command, "@UnitsSold", unitsSold, 19, 6);
        AddDecimal(command, "@UnitsReturned", unitsReturned, 19, 6);
        AddDecimal(command, "@GrossSales", grossSales, 19, 4);
        AddDecimal(command, "@Discounts", discounts, 19, 4);
        AddDecimal(command, "@Returns", returns, 19, 4);
        AddDecimal(command, "@NetUntaxed", netUntaxed, 19, 4);
        AddDecimal(command, "@NetTax", netTax, 19, 4);
        AddDecimal(command, "@NetTotal", netTotal, 19, 4);
        AddDecimal(command, "@NetCost", netCost, 19, 4);
        AddDecimal(command, "@GrossProfit", grossProfit, 19, 4);
        AddDecimal(command, "@CreditSales", creditSales, 19, 4);
        AddDecimal(command, "@Collected", collected, 19, 4);
        AddDecimal(command, "@Refunded", refunded, 19, 4);
        command.Parameters.AddWithValue("@Version", ProjectionVersion);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateCheckpointAsync(
        SalesReportingSqlSession session, Guid businessId,
        Guid documentId, string documentType, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            MERGE reporting.SalesReportingCheckpoints WITH(HOLDLOCK) AS target
            USING (SELECT @BusinessId BusinessId,@Version ProjectionVersion) source
            ON target.BusinessId=source.BusinessId AND target.ProjectionVersion=source.ProjectionVersion
            WHEN MATCHED THEN UPDATE SET LastSourceDocumentId=@DocumentId,
              LastSourceDocumentType=@DocumentType,LastProjectedAt=@Now,LastError=NULL
            WHEN NOT MATCHED THEN INSERT
              (BusinessId,ProjectionVersion,LastSourceDocumentId,LastSourceDocumentType,LastProjectedAt,LastError)
            VALUES(@BusinessId,@Version,@DocumentId,@DocumentType,@Now,NULL);
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Version", ProjectionVersion);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RefreshProductRotationAsync(
        SalesReportingSqlSession session, Guid businessId, Guid sourceDocumentId,
        string sourceDocumentType, DateOnly windowEndDate, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "reporting.ProductRotationRefresh", session.Connection, session.Transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@SourceDocumentId", sourceDocumentId);
        command.Parameters.AddWithValue("@SourceDocumentType", sourceDocumentType);
        command.Parameters.Add("@EndDate", SqlDbType.Date).Value = windowEndDate.ToDateTime(TimeOnly.MinValue);
        command.Parameters.AddWithValue("@ProjectionVersion", ProjectionVersion);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddDecimal(
        SqlCommand command, string name, decimal value, byte precision, byte scale)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = value;
    }

    private static void AddNullableDecimal(
        SqlCommand command,string name,decimal? value,byte precision,byte scale)
    {
        var parameter=command.Parameters.Add(name,SqlDbType.Decimal);
        parameter.Precision=precision;parameter.Scale=scale;
        parameter.Value=(object?)value??DBNull.Value;
    }

    private sealed record TaxFact(
        string Code, decimal Rate, decimal Taxable, decimal Tax, decimal Total);

    private sealed record OriginalSaleDimensions(
        Guid WarehouseId, Guid? WorkSessionId, Guid? SellerId, Guid? CustomerId);
}
