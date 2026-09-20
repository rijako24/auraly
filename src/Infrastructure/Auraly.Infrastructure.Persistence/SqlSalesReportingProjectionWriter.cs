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
/// Maintains the sales read model within the canonical reporting transaction.
/// The writer is deliberately idempotent at source-document level and never reads this model
/// to make operational, inventory, fiscal or accounting decisions.
/// </summary>
public sealed class SqlSalesReportingProjectionWriter(
    IAuralyIdGenerator ids,
    TimeProvider timeProvider)
{
    private const short ProjectionVersion = 2;

    public async Task ProjectSaleAsync(
        SalesReportingSqlSession session,
        PosSaleUploadRequest value,
        CancellationToken cancellationToken)
    {
        var localDate = await ResolveLocalDateAsync(
            session, value.BusinessId, value.CommercialSnapshot.IssuedAt, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var seller = await ResolveSellerAttributionAsync(session, value, cancellationToken);
        var recognizedCost = await InsertSaleLineFactsAsync(
            session, value, seller.SellerId, localDate.Date, now, cancellationToken);

        await InsertSaleDocumentAsync(
            session, value, seller, localDate, recognizedCost, now, cancellationToken);
        await InsertSalePaymentFactsAsync(session, value, localDate.Date, now, cancellationToken);
        await InsertSaleTaxFactsAsync(session, value, localDate.Date, now, cancellationToken);
        await ApplyDimensionDeltasAsync(session, value.BusinessId, value.DocumentId,
            value.CommercialSnapshot.DocumentType, 1, now, cancellationToken,
            chargeUntaxed: value.Charges?.Sum(charge => charge.InvoicedUntaxedAmount) ?? 0,
            chargeTax: value.Charges?.Sum(charge => charge.InvoicedTaxAmount) ?? 0,
            chargeTotal: value.Charges?.Sum(charge => charge.InvoicedAmount) ?? 0);

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
            grossProfit: value.CommercialSnapshot.PayableAmount - recognizedCost,
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
            grossProfit: returnedCost - value.TotalAmount,
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

    public Task ProjectGoodsReceiptAsync(
        SalesReportingSqlSession session,
        GoodsReceiptDocumentPayload value,
        CancellationToken cancellationToken) =>
        ProjectPurchaseAsync(
            session, value.TenantId, value.BusinessId, value.DocumentId,
            "GoodsReceipt", null, value.DocumentNumber, value.ReceivedAt,
            value.SupplierId, value.SupplierNameSnapshot, value.WarehouseId,
            value.WarehouseNameSnapshot, value.CurrencyCode, value.NetAmount,
            value.TaxAmount, value.GrandTotal,
            value.Lines.Select(line => new PurchaseProjectionLine(
                line.LineNumber, null, line.ProductId, line.Description,
                line.Quantity, line.UnitCost, line.DiscountAmount,
                line.NetAmount, line.TaxAmount, line.LineTotal)).ToArray(),
            1, cancellationToken);

    public Task ProjectPurchaseReturnAsync(
        SalesReportingSqlSession session,
        PurchaseReturnDocumentPayload value,
        CancellationToken cancellationToken) =>
        ProjectPurchaseAsync(
            session, value.TenantId, value.BusinessId, value.ReturnId,
            "PurchaseReturn", value.OriginalGoodsReceiptId, value.DocumentNumber,
            value.ReturnedAt, value.SupplierId, value.SupplierNameSnapshot,
            value.WarehouseId, value.WarehouseNameSnapshot, value.CurrencyCode,
            value.NetAmount, value.TaxAmount, value.TotalAmount,
            value.Lines.Select(line => new PurchaseProjectionLine(
                line.LineNumber, line.OriginalLineNumber, line.ProductId,
                line.Description, line.Quantity, line.UnitCost,
                line.DiscountAmount, line.NetAmount, line.TaxAmount,
                line.LineTotal)).ToArray(), -1, cancellationToken);

    private async Task ProjectPurchaseAsync(
        SalesReportingSqlSession session,
        Guid tenantId,
        Guid businessId,
        Guid documentId,
        string documentType,
        Guid? originalReceiptId,
        string documentNumber,
        DateTimeOffset occurredAt,
        Guid supplierId,
        string? supplierName,
        Guid warehouseId,
        string? warehouseName,
        string currencyCode,
        decimal net,
        decimal tax,
        decimal total,
        IReadOnlyList<PurchaseProjectionLine> lines,
        int sign,
        CancellationToken cancellationToken)
    {
        var local = await ResolveLocalDateAsync(
            session, businessId, occurredAt, cancellationToken);
        var names = await ResolvePurchaseNamesAsync(
            session, supplierId, warehouseId, supplierName, warehouseName,
            cancellationToken);
        await using (var document = new SqlCommand("""
            INSERT reporting.PurchaseReportDocuments
              (SourceDocumentId,TenantId,BusinessId,SourceDocumentType,OriginalGoodsReceiptId,
               DocumentNumber,OccurredAt,BusinessLocalDate,TimeZoneId,SupplierId,SupplierName,
               WarehouseId,WarehouseName,CurrencyCode,NetAmount,TaxAmount,TotalAmount,
               ProjectionVersion,ProjectedAt)
            VALUES(@Id,@Tenant,@Business,@Type,@Original,@Number,@At,@Date,@TimeZone,
               @Supplier,@SupplierName,@Warehouse,@WarehouseName,@Currency,@Net,@Tax,@Total,
               @Version,SYSDATETIMEOFFSET());
            """, session.Connection, session.Transaction))
        {
            document.Parameters.AddWithValue("@Id", documentId);
            document.Parameters.AddWithValue("@Tenant", tenantId);
            document.Parameters.AddWithValue("@Business", businessId);
            document.Parameters.AddWithValue("@Type", documentType);
            document.Parameters.AddWithValue("@Original", (object?)originalReceiptId ?? DBNull.Value);
            document.Parameters.AddWithValue("@Number", documentNumber);
            document.Parameters.AddWithValue("@At", occurredAt);
            document.Parameters.Add("@Date", SqlDbType.Date).Value = local.Date.ToDateTime(TimeOnly.MinValue);
            document.Parameters.AddWithValue("@TimeZone", local.TimeZoneId);
            document.Parameters.AddWithValue("@Supplier", supplierId);
            document.Parameters.AddWithValue("@SupplierName", names.Supplier);
            document.Parameters.AddWithValue("@Warehouse", warehouseId);
            document.Parameters.AddWithValue("@WarehouseName", names.Warehouse);
            document.Parameters.AddWithValue("@Currency", currencyCode);
            AddDecimal(document, "@Net", sign * net, 19, 4);
            AddDecimal(document, "@Tax", sign * tax, 19, 4);
            AddDecimal(document, "@Total", sign * total, 19, 4);
            document.Parameters.AddWithValue("@Version", ProjectionVersion);
            await document.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var line in lines)
        {
            await using var item = new SqlCommand("""
                INSERT reporting.PurchaseReportLineFacts
                  (PurchaseFactId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,
                   SourceLineNumber,OriginalGoodsReceiptId,OriginalLineNumber,OccurredAt,
                   BusinessLocalDate,SupplierId,SupplierName,WarehouseId,WarehouseName,
                   ProductId,ProductName,Quantity,UnitCost,DiscountAmount,NetAmount,TaxAmount,
                   TotalAmount,CurrencyCode,ProjectionVersion,ProjectedAt)
                VALUES(@Fact,@Tenant,@Business,@Document,@Type,@Line,@Original,@OriginalLine,
                   @At,@Date,@Supplier,@SupplierName,@Warehouse,@WarehouseName,@Product,
                   @ProductName,@Quantity,@UnitCost,@Discount,@Net,@Tax,@Total,@Currency,
                   @Version,SYSDATETIMEOFFSET());
                """, session.Connection, session.Transaction);
            item.Parameters.AddWithValue("@Fact", ids.NewId());
            item.Parameters.AddWithValue("@Tenant", tenantId);
            item.Parameters.AddWithValue("@Business", businessId);
            item.Parameters.AddWithValue("@Document", documentId);
            item.Parameters.AddWithValue("@Type", documentType);
            item.Parameters.AddWithValue("@Line", line.LineNumber);
            item.Parameters.AddWithValue("@Original", (object?)originalReceiptId ?? DBNull.Value);
            item.Parameters.AddWithValue("@OriginalLine", (object?)line.OriginalLineNumber ?? DBNull.Value);
            item.Parameters.AddWithValue("@At", occurredAt);
            item.Parameters.Add("@Date", SqlDbType.Date).Value = local.Date.ToDateTime(TimeOnly.MinValue);
            item.Parameters.AddWithValue("@Supplier", supplierId);
            item.Parameters.AddWithValue("@SupplierName", names.Supplier);
            item.Parameters.AddWithValue("@Warehouse", warehouseId);
            item.Parameters.AddWithValue("@WarehouseName", names.Warehouse);
            item.Parameters.AddWithValue("@Product", line.ProductId);
            item.Parameters.AddWithValue("@ProductName", line.ProductName);
            AddDecimal(item, "@Quantity", sign * line.Quantity, 19, 6);
            AddDecimal(item, "@UnitCost", line.UnitCost, 19, 6);
            AddDecimal(item, "@Discount", sign * line.Discount, 19, 4);
            AddDecimal(item, "@Net", sign * line.Net, 19, 4);
            AddDecimal(item, "@Tax", sign * line.Tax, 19, 4);
            AddDecimal(item, "@Total", sign * line.Total, 19, 4);
            item.Parameters.AddWithValue("@Currency", currencyCode);
            item.Parameters.AddWithValue("@Version", ProjectionVersion);
            await item.ExecuteNonQueryAsync(cancellationToken);
        }
        await UpdateCheckpointAsync(
            session, businessId, documentId, documentType,
            timeProvider.GetUtcNow(), cancellationToken);
    }

    private static async Task<(string Supplier, string Warehouse)> ResolvePurchaseNamesAsync(
        SalesReportingSqlSession session,
        Guid supplierId,
        Guid warehouseId,
        string? supplierName,
        string? warehouseName,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT supplier.Name,warehouse.Name
            FROM dbo.Suppliers supplier
            CROSS JOIN dbo.Warehouses warehouse
            WHERE supplier.SupplierId=@SupplierId AND warehouse.WarehouseId=@WarehouseId;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@SupplierId", supplierId);
        command.Parameters.AddWithValue("@WarehouseId", warehouseId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) &&
            (supplierName is null || warehouseName is null))
            throw new InvalidOperationException(
                "The purchase reporting dimensions could not be resolved.");
        return (supplierName ?? reader.GetString(0), warehouseName ?? reader.GetString(1));
    }

    private sealed record PurchaseProjectionLine(
        int LineNumber,
        int? OriginalLineNumber,
        Guid ProductId,
        string ProductName,
        decimal Quantity,
        decimal UnitCost,
        decimal Discount,
        decimal Net,
        decimal Tax,
        decimal Total);

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
              CustomerId,PartySiteId,CustomerIdentification,CustomerName,SourceMode,FiscalStatus,
              CurrencyCode,GrossAmount,DiscountAmount,UntaxedAmount,TaxAmount,TotalAmount,
              CreditAmount,CollectedAmount,RecognizedCostAmount,ProjectionVersion,
              SourcePayloadHash,ProjectedAt
            )
            SELECT d.DocumentId,@TenantId,d.BusinessId,d.DocumentType,d.DocumentNumber,d.FiscalNumber,
                   d.IssuedAt,@LocalDate,@TimeZoneId,d.WarehouseId,w.Name,d.WorkSessionId,@SellerId,@SellerName,
                   d.CustomerId,d.CustomerPartySiteId,d.CustomerIdentification,
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

    private async Task<decimal> InsertSaleLineFactsAsync(
        SalesReportingSqlSession session,
        PosSaleUploadRequest value,
        Guid? sellerId,
        DateOnly localDate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT reporting.SalesReportLineFacts
            (
              FactId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourceLineNumber,
              OriginalSaleDocumentId,OriginalLineNumber,MovementType,OccurredAt,BusinessLocalDate,
              WarehouseId,WorkSessionId,SellerId,CustomerId,PartySiteId,ProductId,ProductCode,ProductName,
              CategoryId,CategoryName,SupplierId,SupplierName,Quantity,GrossAmount,DiscountAmount,UntaxedAmount,TaxAmount,
              TotalAmount,RecognizedCostAmount,ProjectionVersion,ProjectedAt
            )
            SELECT batch.FactId,@TenantId,@BusinessId,@DocumentId,@DocumentType,batch.LineNumber,
                   @DocumentId,batch.LineNumber,N'Sale',@OccurredAt,@LocalDate,@WarehouseId,
                   @WorkSessionId,@SellerId,@CustomerId,@PartySiteId,p.ProductId,
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
                   batch.Quantity,batch.Gross,batch.Discount,batch.Untaxed,batch.Tax,
                   batch.Total,batch.Cost,@Version,@ProjectedAt
            FROM OPENJSON(@Lines) WITH
              (FactId uniqueidentifier,LineNumber int,ProductId uniqueidentifier,
               Quantity decimal(19,6),Gross decimal(19,4),Discount decimal(19,4),
               Untaxed decimal(19,4),Tax decimal(19,4),Total decimal(19,4),Cost decimal(19,4)) batch
            INNER JOIN dbo.SalesDocumentLines sourceLine
              ON sourceLine.DocumentId=@DocumentId AND sourceLine.LineNumber=batch.LineNumber
              AND sourceLine.ProductId=batch.ProductId
            INNER JOIN dbo.Products p ON p.ProductId=sourceLine.ProductId
            LEFT JOIN dbo.ProductCategories pc ON pc.ProductCategoryId=p.ProductCategoryId
            OUTER APPLY(SELECT TOP(1) s.SupplierId,s.Name FROM dbo.SupplierProducts sp
              INNER JOIN dbo.Suppliers s ON s.SupplierId=sp.SupplierId AND s.BusinessId=@BusinessId
              WHERE sp.ProductId=p.ProductId AND sp.BusinessId=@BusinessId AND sp.IsActive=1 AND s.IsActive=1
              ORDER BY sp.IsPrimary DESC,sp.CreatedAt,sp.SupplierProductId) supplier
            WHERE p.TenantId=@TenantId;
            """;
        var lines = value.Lines.Select(line => new
        {
            FactId = ids.NewId(), line.LineNumber, line.ProductId, line.Quantity,
            Gross = line.UntaxedAmount + line.DiscountAmount, Discount = line.DiscountAmount,
            Untaxed = line.UntaxedAmount, Tax = line.TaxAmount, Total = line.LineTotal,
            Cost = decimal.Round(line.Quantity * line.DocumentUnitCost, 4, MidpointRounding.AwayFromZero)
        }).ToArray();
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.Add("@Lines", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(lines);
        command.Parameters.AddWithValue("@Version", ProjectionVersion);
        command.Parameters.AddWithValue("@TenantId", value.TenantId);
        command.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        command.Parameters.AddWithValue("@DocumentId", value.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", value.CommercialSnapshot.DocumentType);
        command.Parameters.AddWithValue("@OccurredAt", value.CommercialSnapshot.IssuedAt);
        command.Parameters.AddWithValue("@LocalDate", localDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@WarehouseId", value.WarehouseId);
        command.Parameters.AddWithValue("@WorkSessionId", value.WorkSessionId);
        command.Parameters.AddWithValue("@SellerId", (object?)sellerId ?? DBNull.Value);
        command.Parameters.AddWithValue("@CustomerId", (object?)value.CustomerId ?? DBNull.Value);
        command.Parameters.AddWithValue("@PartySiteId", (object?)value.CustomerPartySiteId ?? DBNull.Value);
        command.Parameters.AddWithValue("@ProjectedAt", now);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != lines.Length)
            throw new InvalidOperationException("The sale lines could not be projected completely.");
        return lines.Sum(line => line.Cost);
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
            SELECT @DocumentId,@DocumentType,p.PaymentNumber,@TenantId,@BusinessId,@LocalDate,
                   p.MovementType,p.MethodCode,p.Amount,p.Reference,@WorkSessionId,@ProjectedAt
            FROM OPENJSON(@Payments) WITH
              (PaymentNumber int,MovementType nvarchar(24),MethodCode nvarchar(32),
               Amount decimal(19,4),Reference nvarchar(max)) p;
            """;
        var payments = value.Payments.Select(payment => new
        {
            payment.PaymentNumber, MovementType = "Payment", payment.MethodCode, payment.Amount, payment.Reference
        }).ToList();
        if (value.Credit is not null)
            payments.Add(new { PaymentNumber = 0, MovementType = "Credit", MethodCode = "Credit",
                value.Credit.Amount, Reference = (string?)null });
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", value.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", value.CommercialSnapshot.DocumentType);
        command.Parameters.AddWithValue("@TenantId", value.TenantId);
        command.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        command.Parameters.AddWithValue("@LocalDate", localDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@WorkSessionId", value.WorkSessionId);
        command.Parameters.AddWithValue("@ProjectedAt", now);
        command.Parameters.Add("@Payments", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(payments);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != payments.Count)
            throw new InvalidOperationException("The sale payments could not be projected completely.");
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
                    line.TaxAmount, line.LineTotal)).Concat((value.Charges ?? [])
                .Where(charge => charge.InvoicedAmount > 0).Select(charge =>
                    new TaxFact(charge.TaxCode, charge.TaxRate, charge.InvoicedUntaxedAmount,
                        charge.InvoicedTaxAmount, charge.InvoicedAmount))), 1m, now, cancellationToken);

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
            SELECT @DocumentId,@DocumentType,t.Code,t.Rate,@TenantId,@BusinessId,@LocalDate,
                   t.Taxable,t.Tax,t.Total,@ProjectedAt
            FROM OPENJSON(@Taxes) WITH
              (Code nvarchar(16),Rate decimal(9,6),Taxable decimal(19,4),
               Tax decimal(19,4),Total decimal(19,4)) t;
            """;
        var taxes = lines.GroupBy(x => new { x.Code, x.Rate }).Select(group => new
        {
            group.Key.Code, group.Key.Rate, Taxable = sign * group.Sum(x => x.Taxable),
            Tax = sign * group.Sum(x => x.Tax), Total = sign * group.Sum(x => x.Total)
        }).ToArray();
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@LocalDate", localDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.Add("@Taxes", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(taxes);
        command.Parameters.AddWithValue("@ProjectedAt", now);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != taxes.Length)
            throw new InvalidOperationException("The document taxes could not be projected completely.");
    }

    private static async Task<OriginalSaleDimensions> ReadOriginalSaleDimensionsAsync(
        SalesReportingSqlSession session, Guid businessId,
        Guid documentId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT WarehouseId,WorkSessionId,SoldByUserId,CustomerId,CustomerPartySiteId
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
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4));
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
              WarehouseId,WorkSessionId,SellerId,CustomerId,PartySiteId,ProductId,ProductCode,ProductName,
              CategoryId,CategoryName,SupplierId,SupplierName,Quantity,GrossAmount,DiscountAmount,UntaxedAmount,TaxAmount,
              TotalAmount,RecognizedCostAmount,ReturnReasonCode,ReturnDisposition,
              ProjectionVersion,ProjectedAt
            )
            SELECT @FactId,@TenantId,@BusinessId,@ReturnId,N'SalesReturn',@LineNumber,
                   @OriginalId,@OriginalLine,N'Return',@OccurredAt,@LocalDate,@WarehouseId,
                   @WorkSessionId,@SellerId,@CustomerId,@PartySiteId,p.ProductId,
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
        command.Parameters.AddWithValue("@PartySiteId", (object?)original.PartySiteId ?? DBNull.Value);
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
        CancellationToken cancellationToken,
        decimal chargeUntaxed = 0, decimal chargeTax = 0, decimal chargeTotal = 0)
    {
        const string sql = """
            WITH lineDimensions AS
            (
              SELECT f.BusinessLocalDate,v.DimensionType,v.DimensionKey,MAX(v.DimensionLabel) DimensionLabel,
                     @DocumentCount DocumentCount,SUM(f.Quantity) Quantity,
                     SUM(CASE WHEN f.MovementType=N'Sale' THEN f.GrossAmount ELSE 0 END) GrossSales,
                     SUM(CASE WHEN f.MovementType=N'Sale' THEN f.DiscountAmount ELSE 0 END) Discounts,
                     -SUM(CASE WHEN f.MovementType=N'Return' THEN f.TotalAmount ELSE 0 END) Returns,
                     SUM(f.UntaxedAmount) NetUntaxed,SUM(f.TaxAmount) NetTax,
                     SUM(f.TotalAmount) NetTotal,SUM(f.RecognizedCostAmount) NetCost,
                     SUM(f.TotalAmount-f.RecognizedCostAmount) GrossProfit
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
              GROUP BY f.BusinessLocalDate,v.DimensionType,v.DimensionKey
            )
            ,dimensions AS
            (
              SELECT BusinessLocalDate,DimensionType,DimensionKey,DimensionLabel,DocumentCount,Quantity,
                GrossSales+CASE WHEN DimensionType IN(N'Customer',N'Seller',N'Warehouse') THEN @ChargeUntaxed ELSE 0 END GrossSales,
                Discounts,Returns,
                NetUntaxed+CASE WHEN DimensionType IN(N'Customer',N'Seller',N'Warehouse') THEN @ChargeUntaxed ELSE 0 END NetUntaxed,
                NetTax+CASE WHEN DimensionType IN(N'Customer',N'Seller',N'Warehouse') THEN @ChargeTax ELSE 0 END NetTax,
                NetTotal+CASE WHEN DimensionType IN(N'Customer',N'Seller',N'Warehouse') THEN @ChargeTotal ELSE 0 END NetTotal,
                NetCost,
                GrossProfit+CASE WHEN DimensionType IN(N'Customer',N'Seller',N'Warehouse') THEN @ChargeTotal ELSE 0 END GrossProfit
              FROM lineDimensions
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
        AddDecimal(command, "@ChargeUntaxed", chargeUntaxed, 19, 4);
        AddDecimal(command, "@ChargeTax", chargeTax, 19, 4);
        AddDecimal(command, "@ChargeTotal", chargeTotal, 19, 4);
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
        Guid WarehouseId, Guid? WorkSessionId, Guid? SellerId, Guid? CustomerId, Guid? PartySiteId);
}
