SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.Products',N'IsGenericProduct') IS NULL
BEGIN
    ALTER TABLE dbo.Products
        ADD IsGenericProduct BIT NOT NULL
            CONSTRAINT DF_Products_IsGenericProduct DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.Products')
      AND name=N'CK_Products_GenericNoStock')
BEGIN
    ALTER TABLE dbo.Products WITH CHECK
        ADD CONSTRAINT CK_Products_GenericNoStock
        CHECK (IsGenericProduct=0 OR ManageStock=0);
END;
GO

IF COL_LENGTH(N'dbo.SalesDraftLines',N'IsGenericProductSnapshot') IS NULL
BEGIN
    ALTER TABLE dbo.SalesDraftLines
        ADD IsGenericProductSnapshot BIT NOT NULL
            CONSTRAINT DF_SalesDraftLines_IsGenericProductSnapshot DEFAULT 0;
END;
GO

IF COL_LENGTH(N'dbo.OrderItems',N'IsGenericProductSnapshot') IS NULL
BEGIN
    ALTER TABLE dbo.OrderItems
        ADD IsGenericProductSnapshot BIT NOT NULL
            CONSTRAINT DF_OrderItems_IsGenericProductSnapshot DEFAULT 0;
END;
GO

UPDATE item
SET IsGenericProductSnapshot=1
FROM dbo.OrderItems item
INNER JOIN dbo.Products product ON product.ProductId=item.ProductId
WHERE item.IsGenericProductSnapshot=0
  AND product.IsGenericProduct=1;
GO

UPDATE line
SET IsGenericProductSnapshot=1
FROM dbo.SalesDraftLines line
INNER JOIN dbo.Products product ON product.ProductId=line.ProductId
WHERE line.IsGenericProductSnapshot=0
  AND product.IsGenericProduct=1;
GO

IF COL_LENGTH(N'dbo.SalesDocumentLines',N'IsGenericProductSnapshot') IS NULL
BEGIN
    ALTER TABLE dbo.SalesDocumentLines
        ADD IsGenericProductSnapshot BIT NOT NULL
            CONSTRAINT DF_SalesDocumentLines_IsGenericProductSnapshot DEFAULT 0;
END;
GO

UPDATE line
SET IsGenericProductSnapshot=1
FROM dbo.SalesDocumentLines line
INNER JOIN dbo.Products product ON product.ProductId=line.ProductId
WHERE line.IsGenericProductSnapshot=0
  AND product.IsGenericProduct=1;
GO
