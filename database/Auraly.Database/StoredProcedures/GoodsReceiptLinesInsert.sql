CREATE PROCEDURE [purchasing].[GoodsReceiptLinesInsert]
    @DocumentId UNIQUEIDENTIFIER,
    @LinesJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.GoodsReceiptLines
        (GoodsReceiptId,LineNumber,ProductId,DescriptionSnapshot,Quantity,UnitCost,
         DiscountAmount,CommercialDiscountPercent,TaxCode,TaxRate,TaxTreatment,
         NetAmount,TaxAmount,LineTotal,PresentationNameSnapshot,PresentationQuantity,
         UnitsPerPresentation,PurchaseOrderLineId,OverReceiptReason,OverReceiptAuthorized,
         TotalGrossWeightKg,TotalVolumeM3,FunctionalNetAmount,FunctionalTaxAmount,
         FunctionalLineTotal,AllocatedLandedCostAmount,RecognizedInventoryCostAmount)
    SELECT @DocumentId,LineNumber,ProductId,Description,Quantity,UnitCost,
         DiscountAmount,CommercialDiscountPercent,TaxCode,TaxRate,TaxTreatment,
         NetAmount,TaxAmount,LineTotal,PresentationName,PresentationQuantity,
         UnitsPerPresentation,PurchaseOrderLineId,OverReceiptReason,OverReceiptAuthorized,
         TotalGrossWeightKg,TotalVolumeM3,FunctionalNetAmount,FunctionalTaxAmount,
         FunctionalLineTotal,AllocatedLandedCostAmount,RecognizedInventoryCostAmount
    FROM OPENJSON(@LinesJson) WITH (
         LineNumber INT,ProductId UNIQUEIDENTIFIER,Description NVARCHAR(MAX),
         Quantity DECIMAL(19,6),UnitCost DECIMAL(19,6),DiscountAmount DECIMAL(19,4),
         CommercialDiscountPercent DECIMAL(9,6),TaxCode NVARCHAR(MAX),TaxRate DECIMAL(9,6),
         TaxTreatment NVARCHAR(MAX),NetAmount DECIMAL(19,4),TaxAmount DECIMAL(19,4),
         LineTotal DECIMAL(19,4),PresentationName NVARCHAR(MAX),PresentationQuantity DECIMAL(19,6),
         UnitsPerPresentation DECIMAL(19,6),PurchaseOrderLineId UNIQUEIDENTIFIER,
         OverReceiptReason NVARCHAR(MAX),OverReceiptAuthorized BIT,TotalGrossWeightKg DECIMAL(19,6),
         TotalVolumeM3 DECIMAL(19,6),FunctionalNetAmount DECIMAL(19,4),FunctionalTaxAmount DECIMAL(19,4),
         FunctionalLineTotal DECIMAL(19,4),AllocatedLandedCostAmount DECIMAL(19,4),
         RecognizedInventoryCostAmount DECIMAL(19,4));
END;
