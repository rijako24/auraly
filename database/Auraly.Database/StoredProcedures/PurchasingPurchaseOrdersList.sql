CREATE PROCEDURE [purchasing].[PurchaseOrdersList]
    @BusinessId UNIQUEIDENTIFIER,
    @Search NVARCHAR(160) = NULL,
    @Status NVARCHAR(24) = NULL,
    @Offset INT,
    @PageSize INT
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #Orders
    (
        PurchaseOrderId UNIQUEIDENTIFIER NOT NULL,
        DocumentNumber NVARCHAR(40) NULL,
        Status NVARCHAR(24) NOT NULL,
        SupplierName NVARCHAR(250) NULL,
        WarehouseName NVARCHAR(200) NULL,
        OrderedAt DATETIMEOFFSET(7) NOT NULL,
        ExpectedAt DATETIMEOFFSET(7) NULL,
        GrandTotal DECIMAL(19,4) NOT NULL,
        FulfillmentPercent DECIMAL(9,4) NOT NULL,
        UpdatedAt DATETIMEOFFSET(7) NOT NULL
    );

    INSERT #Orders
    SELECT d.PurchaseOrderId,NULL,N'Draft',s.Name,w.Name,d.OrderedAt,d.ExpectedAt,d.GrandTotal,0,d.UpdatedAt
    FROM purchasing.PurchaseOrderDrafts d
    LEFT JOIN dbo.Suppliers s ON s.SupplierId=d.SupplierId AND s.BusinessId=d.BusinessId
    LEFT JOIN dbo.Warehouses w ON w.WarehouseId=d.WarehouseId AND w.BusinessId=d.BusinessId
    WHERE d.BusinessId=@BusinessId;

    INSERT #Orders
    SELECT o.PurchaseOrderId,o.DocumentNumber,o.Status,s.Name,w.Name,o.OrderedAt,o.ExpectedAt,o.GrandTotal,
        CONVERT(decimal(9,4),COALESCE(100*SUM(l.ReceivedQuantity)/NULLIF(SUM(l.OrderedQuantity),0),0)),o.UpdatedAt
    FROM purchasing.PurchaseOrders o
    JOIN dbo.Suppliers s ON s.SupplierId=o.SupplierId AND s.BusinessId=o.BusinessId
    JOIN dbo.Warehouses w ON w.WarehouseId=o.WarehouseId AND w.BusinessId=o.BusinessId
    JOIN purchasing.PurchaseOrderLines l ON l.PurchaseOrderId=o.PurchaseOrderId
    WHERE o.BusinessId=@BusinessId
    GROUP BY o.PurchaseOrderId,o.DocumentNumber,o.Status,s.Name,w.Name,o.OrderedAt,o.ExpectedAt,o.GrandTotal,o.UpdatedAt;

    SELECT COUNT(*)
    FROM #Orders
    WHERE (@Status IS NULL OR Status=@Status)
      AND (@Search IS NULL OR DocumentNumber LIKE N'%'+@Search+N'%' OR SupplierName LIKE N'%'+@Search+N'%');

    SELECT PurchaseOrderId,DocumentNumber,Status,SupplierName,WarehouseName,OrderedAt,ExpectedAt,
        GrandTotal,FulfillmentPercent,UpdatedAt
    FROM #Orders
    WHERE (@Status IS NULL OR Status=@Status)
      AND (@Search IS NULL OR DocumentNumber LIKE N'%'+@Search+N'%' OR SupplierName LIKE N'%'+@Search+N'%')
    ORDER BY UpdatedAt DESC,PurchaseOrderId
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
