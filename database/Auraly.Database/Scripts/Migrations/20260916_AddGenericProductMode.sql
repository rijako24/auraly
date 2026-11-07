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
    -- Only the first introduction has no captured value to preserve. Compile
    -- after adding the column; later publishes must not reread today's product.
    EXEC sys.sp_executesql N'
        UPDATE line SET IsGenericProductSnapshot=1
        FROM dbo.SalesDraftLines line JOIN dbo.Products product ON product.ProductId=line.ProductId
        WHERE product.IsGenericProduct=1;';
END;
GO

IF COL_LENGTH(N'dbo.OrderItems',N'IsGenericProductSnapshot') IS NULL
BEGIN
    ALTER TABLE dbo.OrderItems
        ADD IsGenericProductSnapshot BIT NOT NULL
            CONSTRAINT DF_OrderItems_IsGenericProductSnapshot DEFAULT 0;
    EXEC sys.sp_executesql N'
        UPDATE item SET IsGenericProductSnapshot=1
        FROM dbo.OrderItems item JOIN dbo.Products product ON product.ProductId=item.ProductId
        WHERE product.IsGenericProduct=1;';
END;
GO

IF COL_LENGTH(N'dbo.SalesDocumentLines',N'IsGenericProductSnapshot') IS NULL
BEGIN
    ALTER TABLE dbo.SalesDocumentLines
        ADD IsGenericProductSnapshot BIT NOT NULL
            CONSTRAINT DF_SalesDocumentLines_IsGenericProductSnapshot DEFAULT 0;
    EXEC sys.sp_executesql N'
        UPDATE line SET IsGenericProductSnapshot=1
        FROM dbo.SalesDocumentLines line JOIN dbo.Products product ON product.ProductId=line.ProductId
        WHERE product.IsGenericProduct=1;';
END;
GO
