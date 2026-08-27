CREATE PROCEDURE dbo.DispatchSettlementReturnsGet
    @DispatchId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT source.SourceDocumentId,
           delivery.DeliveryStatus,
           line.LineNumber,
           CASE WHEN delivery.DeliveryStatus = N'NotDelivered' THEN line.Quantity ELSE returned.Quantity END,
           CASE WHEN delivery.DeliveryStatus = N'NotDelivered' THEN N'Sellable' ELSE returned.InventoryDisposition END,
           reason.Code
    FROM dbo.DispatchSourceDocuments source
    INNER JOIN dbo.DispatchDeliveryEvents delivery
        ON delivery.DispatchSourceDocumentId = source.DispatchSourceDocumentId
    INNER JOIN dbo.SalesDocumentLines line ON line.DocumentId = source.SourceDocumentId
    LEFT JOIN dbo.DispatchDeliveryReturns returned
        ON returned.DispatchSourceDocumentId = source.DispatchSourceDocumentId
       AND returned.OriginalLineNumber = line.LineNumber
    OUTER APPLY
    (
        SELECT TOP (1) businessReason.Code
        FROM dbo.BusinessReasons businessReason
        WHERE businessReason.BusinessId = @BusinessId
          AND businessReason.ReasonType = N'SalesReturn'
          AND businessReason.IsActive = 1
        ORDER BY businessReason.DisplayOrder, businessReason.Name, businessReason.Code
    ) reason
    WHERE source.DispatchId = @DispatchId
      AND (delivery.DeliveryStatus = N'NotDelivered' OR returned.DispatchDeliveryReturnId IS NOT NULL)
    ORDER BY source.SourceDocumentId, line.LineNumber;
END
