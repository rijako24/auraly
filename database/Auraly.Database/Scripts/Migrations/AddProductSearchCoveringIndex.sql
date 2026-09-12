IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Products')
      AND name = N'IX_Products_BusinessId_Active_Name')
BEGIN
    CREATE INDEX [IX_Products_BusinessId_Active_Name]
        ON [dbo].[Products] ([BusinessId], [IsActive], [Name], [ProductId])
        INCLUDE ([TenantId], [ProductCode], [Sku], [Reference], [BaseUnitCode],
                 [TaxProfileId], [IsWeighable], [AllowsFractionalSale]);
END;
