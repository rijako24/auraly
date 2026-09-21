IF COL_LENGTH(N'dbo.OrderItems', N'Quantity') IS NOT NULL
BEGIN
    ALTER TABLE dbo.OrderItems ALTER COLUMN Quantity decimal(19, 6) NOT NULL;
END;
