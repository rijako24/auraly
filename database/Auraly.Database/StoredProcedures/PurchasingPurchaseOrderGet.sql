CREATE PROCEDURE [purchasing].[PurchaseOrderGet]
    @BusinessId UNIQUEIDENTIFIER,
    @PurchaseOrderId UNIQUEIDENTIFIER,
    @ReceiptOnly BIT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT PurchaseOrderId,DocumentNumber,Status,WarehouseId,WarehouseName,SupplierId,SupplierName,
        OrderedAt,ExpectedAt,CurrencyCode,Notes,NetAmount,TaxAmount,GrandTotal,UpdatedAt,ConcurrencyToken,IsDraft
    INTO #Header
    FROM
    (
        SELECT d.PurchaseOrderId,CONVERT(nvarchar(40),NULL) DocumentNumber,N'Draft' Status,d.WarehouseId,w.Name WarehouseName,
            d.SupplierId,s.Name SupplierName,d.OrderedAt,d.ExpectedAt,d.CurrencyCode,d.Notes,d.NetAmount,d.TaxAmount,d.GrandTotal,d.UpdatedAt,
            CAST(N'' AS XML).value(
                'xs:base64Binary(xs:hexBinary(sql:column("d.RowVersion")))',
                'varchar(64)') ConcurrencyToken,CONVERT(bit,1) IsDraft
        FROM purchasing.PurchaseOrderDrafts d
        LEFT JOIN dbo.Warehouses w ON w.WarehouseId=d.WarehouseId AND w.BusinessId=d.BusinessId
        LEFT JOIN dbo.Suppliers s ON s.SupplierId=d.SupplierId AND s.BusinessId=d.BusinessId
        WHERE d.BusinessId=@BusinessId AND d.PurchaseOrderId=@PurchaseOrderId AND @ReceiptOnly=0
        UNION ALL
        SELECT o.PurchaseOrderId,o.DocumentNumber,o.Status,o.WarehouseId,w.Name,o.SupplierId,s.Name,o.OrderedAt,o.ExpectedAt,o.CurrencyCode,o.Notes,
            o.NetAmount,o.TaxAmount,o.GrandTotal,o.UpdatedAt,
            CAST(N'' AS XML).value(
                'xs:base64Binary(xs:hexBinary(sql:column("o.RowVersion")))',
                'varchar(64)'),CONVERT(bit,0)
        FROM purchasing.PurchaseOrders o
        JOIN dbo.Warehouses w ON w.WarehouseId=o.WarehouseId AND w.BusinessId=o.BusinessId
        JOIN dbo.Suppliers s ON s.SupplierId=o.SupplierId AND s.BusinessId=o.BusinessId
        WHERE o.BusinessId=@BusinessId AND o.PurchaseOrderId=@PurchaseOrderId
          AND (@ReceiptOnly=0 OR o.Status IN(N'Open',N'PartiallyReceived'))
    ) source;

    SELECT * FROM #Header;
    IF NOT EXISTS(SELECT 1 FROM #Header) RETURN;

    DECLARE @Draft BIT=(SELECT TOP(1) IsDraft FROM #Header);
    DECLARE @WarehouseId UNIQUEIDENTIFIER=(SELECT TOP(1) WarehouseId FROM #Header);

    SELECT l.LineId,l.LineNumber,l.ProductId,COALESCE(p.ProductCode,N''),l.DescriptionSnapshot,l.OrderedQuantity,
        CASE WHEN @Draft=1 THEN CONVERT(decimal(19,6),0) ELSE l.ReceivedQuantity END,
        CASE WHEN @Draft=1 THEN CONVERT(decimal(19,6),0) ELSE l.CancelledQuantity END,
        CASE WHEN l.OrderedQuantity
             -CASE WHEN @Draft=1 THEN 0 ELSE l.ReceivedQuantity END
             -CASE WHEN @Draft=1 THEN 0 ELSE l.CancelledQuantity END
             -CASE WHEN @Draft=0 AND @ReceiptOnly=1 THEN COALESCE(pendingReceipt.Quantity,0) ELSE 0 END>0
          THEN l.OrderedQuantity
             -CASE WHEN @Draft=1 THEN 0 ELSE l.ReceivedQuantity END
             -CASE WHEN @Draft=1 THEN 0 ELSE l.CancelledQuantity END
             -CASE WHEN @Draft=0 AND @ReceiptOnly=1 THEN COALESCE(pendingReceipt.Quantity,0) ELSE 0 END ELSE 0 END,
        l.UnitCost,l.DiscountAmount,l.TaxCode,l.TaxRate,l.TaxTreatment,l.NetAmount,l.TaxAmount,l.LineTotal,
        l.PresentationNameSnapshot,l.PresentationQuantity,l.UnitsPerPresentation,
        COALESCE(r.NetUnitsSold30Days,0),COALESCE(r.NetUnitsSold90Days,0),COALESCE(r.DailyDemand90Days,0),
        COALESCE(b.QuantityOnHand,0),COALESCE(incoming.Quantity,0),r.CalculatedAt
    FROM
    (
        SELECT LineId,LineNumber,ProductId,DescriptionSnapshot,OrderedQuantity,
            CONVERT(decimal(19,6),0) ReceivedQuantity,CONVERT(decimal(19,6),0) CancelledQuantity,
            UnitCost,DiscountAmount,TaxCode,TaxRate,TaxTreatment,NetAmount,TaxAmount,LineTotal,
            PresentationNameSnapshot,PresentationQuantity,UnitsPerPresentation
        FROM purchasing.PurchaseOrderDraftLines WHERE PurchaseOrderId=@PurchaseOrderId AND @Draft=1
        UNION ALL
        SELECT LineId,LineNumber,ProductId,DescriptionSnapshot,OrderedQuantity,ReceivedQuantity,CancelledQuantity,
            UnitCost,DiscountAmount,TaxCode,TaxRate,TaxTreatment,NetAmount,TaxAmount,LineTotal,
            PresentationNameSnapshot,PresentationQuantity,UnitsPerPresentation
        FROM purchasing.PurchaseOrderLines WHERE PurchaseOrderId=@PurchaseOrderId AND @Draft=0
    ) l
    JOIN dbo.Products p ON p.ProductId=l.ProductId
    LEFT JOIN reporting.ProductRotationSnapshots r ON r.BusinessId=@BusinessId AND r.WarehouseId=@WarehouseId AND r.ProductId=l.ProductId
    LEFT JOIN dbo.InventoryBalances b ON b.BusinessId=@BusinessId AND b.WarehouseId=@WarehouseId AND b.ProductId=l.ProductId
    OUTER APPLY
    (
        SELECT SUM(receiptLine.Quantity) Quantity
        FROM dbo.GoodsReceiptLines receiptLine
        JOIN dbo.GoodsReceipts receipt ON receipt.GoodsReceiptId=receiptLine.GoodsReceiptId
        WHERE receiptLine.PurchaseOrderLineId=l.LineId AND receipt.Status=N'Accepted'
    ) pendingReceipt
    OUTER APPLY
    (
        SELECT SUM(CASE WHEN x.OrderedQuantity-x.ReceivedQuantity-x.CancelledQuantity>0
            THEN x.OrderedQuantity-x.ReceivedQuantity-x.CancelledQuantity ELSE 0 END) Quantity
        FROM purchasing.PurchaseOrderLines x
        JOIN purchasing.PurchaseOrders o ON o.PurchaseOrderId=x.PurchaseOrderId
        WHERE o.BusinessId=@BusinessId AND o.WarehouseId=@WarehouseId
          AND o.Status IN(N'Open',N'PartiallyReceived') AND x.ProductId=l.ProductId
    ) incoming
    ORDER BY l.LineNumber;
END;
