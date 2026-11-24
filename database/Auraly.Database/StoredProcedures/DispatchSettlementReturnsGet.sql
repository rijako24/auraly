CREATE PROCEDURE dbo.DispatchSettlementReturnsGet
    @DispatchId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @TenantId uniqueidentifier=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId);
    SELECT source.SourceDocumentId,
           delivery.DeliveryStatus,
           line.LineNumber,
           CASE WHEN delivery.DeliveryStatus = N'NotDelivered' THEN assigned.AssignedQuantity ELSE returned.Quantity END,
           CASE WHEN delivery.DeliveryStatus = N'NotDelivered' THEN N'Sellable' ELSE returned.InventoryDisposition END,
           reason.Code,
           sale.WarehouseId,CASE WHEN EXISTS(SELECT 1 FROM accounting.SalePaymentPostingFacts(@TenantId,@BusinessId,sale.DocumentId) payment
             WHERE payment.CollectedOutsideDispatch=1) THEN N'Session' ELSE sale.CollectionMode END,
           source.CreditAmountSnapshot,source.DocumentTotalSnapshot
    FROM dbo.DispatchSourceDocuments source
    INNER JOIN dbo.DispatchDeliveryEvents delivery
        ON delivery.DispatchSourceDocumentId = source.DispatchSourceDocumentId AND delivery.IsCurrent=1
    INNER JOIN dbo.SalesDocuments sale
        ON sale.DocumentId = source.SourceDocumentId AND sale.BusinessId = @BusinessId
    INNER JOIN dbo.SalesDocumentLines line ON line.DocumentId = source.SourceDocumentId
    INNER JOIN dbo.DispatchLines assigned ON assigned.DispatchSourceDocumentId=source.DispatchSourceDocumentId AND assigned.SourceLineNumber=line.LineNumber
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
      AND NOT EXISTS(SELECT 1 FROM dbo.DispatchSettlementOperations operation
        CROSS APPLY OPENJSON(operation.ReturnIdsJson) WITH(ReturnId uniqueidentifier '$') selected
        JOIN dbo.SalesReturns linked ON linked.ReturnId=selected.ReturnId AND linked.BusinessId=@BusinessId
        WHERE operation.DispatchId=@DispatchId AND linked.OriginalDocumentId=source.SourceDocumentId)
    ORDER BY source.SourceDocumentId, line.LineNumber;

    -- Full failed deliveries cancel invoiced charges through the same return
    -- contract. Keep the accepted snapshot stable on a settlement replay.
    SELECT source.SourceDocumentId,charge.AppliedChargeId
    FROM dbo.DispatchSourceDocuments source
    JOIN dbo.DispatchDeliveryEvents delivery ON delivery.DispatchSourceDocumentId=source.DispatchSourceDocumentId
      AND delivery.IsCurrent=1 AND delivery.DeliveryStatus=N'NotDelivered'
    JOIN dbo.DocumentProcessingPayloads payload ON payload.DocumentId=source.SourceDocumentId
      AND payload.DocumentType=source.SourceDocumentType AND payload.BusinessId=@BusinessId
    CROSS APPLY OPENJSON(payload.PayloadJson,'$.charges') WITH(AppliedChargeId UNIQUEIDENTIFIER '$.appliedChargeId') charge
    WHERE source.DispatchId=@DispatchId
      AND NOT EXISTS(SELECT 1 FROM dbo.SalesReturnCharges returned
        JOIN dbo.SalesReturns owner ON owner.ReturnId=returned.ReturnId
        WHERE returned.AppliedChargeId=charge.AppliedChargeId AND owner.IdempotencyKey<>
          CONCAT(N'dispatch-settlement:',LOWER(REPLACE(CONVERT(nvarchar(36),@DispatchId),N'-',N'')),
            N':return:',LOWER(REPLACE(CONVERT(nvarchar(36),source.SourceDocumentId),N'-',N''))))
      AND NOT EXISTS(SELECT 1 FROM dbo.DispatchSettlementOperations operation
        CROSS APPLY OPENJSON(operation.ReturnIdsJson) WITH(ReturnId uniqueidentifier '$') selected
        JOIN dbo.SalesReturns linked ON linked.ReturnId=selected.ReturnId AND linked.BusinessId=@BusinessId
        WHERE operation.DispatchId=@DispatchId AND linked.OriginalDocumentId=source.SourceDocumentId)
    ORDER BY source.SourceDocumentId,charge.AppliedChargeId;
END
