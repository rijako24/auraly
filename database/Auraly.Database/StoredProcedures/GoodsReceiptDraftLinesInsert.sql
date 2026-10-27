CREATE PROCEDURE [purchasing].[GoodsReceiptDraftLinesInsert]
    @DocumentId UNIQUEIDENTIFIER,
    @LinesJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.GoodsReceiptDraftLines
        (GoodsReceiptDraftId,LineNumber,ProductId,DescriptionSnapshot,Quantity,UnitCost,
         DiscountAmount,CommercialDiscountPercent,TaxCode,TaxRate,TaxTreatment,
         NetAmount,TaxAmount,LineTotal,PresentationNameSnapshot,PresentationQuantity,
         UnitsPerPresentation,PurchaseOrderLineId,OverReceiptReason,TotalGrossWeightKg,TotalVolumeM3)
    SELECT @DocumentId,LineNumber,ProductId,Description,Quantity,UnitCost,
         DiscountAmount,CommercialDiscountPercent,TaxCode,TaxRate,TaxTreatment,
         NetAmount,TaxAmount,LineTotal,PresentationName,PresentationQuantity,
         UnitsPerPresentation,PurchaseOrderLineId,OverReceiptReason,TotalGrossWeightKg,TotalVolumeM3
    FROM OPENJSON(@LinesJson) WITH (
         LineNumber INT,ProductId UNIQUEIDENTIFIER,Description NVARCHAR(MAX),
         Quantity DECIMAL(19,6),UnitCost DECIMAL(19,6),DiscountAmount DECIMAL(19,4),
         CommercialDiscountPercent DECIMAL(9,6),TaxCode NVARCHAR(MAX),TaxRate DECIMAL(9,6),
         TaxTreatment NVARCHAR(MAX),NetAmount DECIMAL(19,4),TaxAmount DECIMAL(19,4),
         LineTotal DECIMAL(19,4),PresentationName NVARCHAR(MAX),PresentationQuantity DECIMAL(19,6),
         UnitsPerPresentation DECIMAL(19,6),PurchaseOrderLineId UNIQUEIDENTIFIER,
         OverReceiptReason NVARCHAR(MAX),TotalGrossWeightKg DECIMAL(19,6),TotalVolumeM3 DECIMAL(19,6));
END;
