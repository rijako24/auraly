IF EXISTS(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.OrderItems')
      AND name = N'Quantity'
      AND (precision <> 19 OR scale <> 6))
BEGIN
    ALTER TABLE dbo.OrderItems ALTER COLUMN Quantity decimal(19, 6) NOT NULL;
END;
