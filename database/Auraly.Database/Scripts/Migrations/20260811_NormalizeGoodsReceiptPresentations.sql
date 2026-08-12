IF OBJECT_ID(N'dbo.GoodsReceiptLines', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.GoodsReceiptLines', N'Quantity') IS NOT NULL
   AND COL_LENGTH(N'dbo.GoodsReceiptLines', N'PresentationQuantity') IS NOT NULL
   AND COL_LENGTH(N'dbo.GoodsReceiptLines', N'UnitsPerPresentation') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        UPDATE dbo.GoodsReceiptLines
           SET PresentationQuantity = Quantity / NULLIF(UnitsPerPresentation, 0)
         WHERE Quantity > 0
           AND UnitsPerPresentation > 0
           AND (
                PresentationQuantity <= 0
                OR ABS(Quantity - (PresentationQuantity * UnitsPerPresentation)) > 0.000001
           );';

    PRINT 'NormalizeGoodsReceiptPresentations: historical presentation quantities normalized.';
END;
ELSE
    PRINT 'NormalizeGoodsReceiptPresentations: compatible table shape not found; skipped.';
GO
