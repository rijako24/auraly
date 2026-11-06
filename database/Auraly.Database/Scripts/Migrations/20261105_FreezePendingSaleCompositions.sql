-- Enrich only unfinished jobs. The accepted public payload and its hash do not change.
SET NOCOUNT ON;
WHILE 1=1
BEGIN
    DECLARE @Pending TABLE(DocumentId UNIQUEIDENTIFIER,DocumentType NVARCHAR(64),PRIMARY KEY(DocumentId,DocumentType));
    INSERT @Pending
    SELECT TOP(100) payload.DocumentId,payload.DocumentType
    FROM dbo.DocumentProcessingPayloads payload
    JOIN dbo.DocumentProcessingJobs job ON job.DocumentId=payload.DocumentId AND job.DocumentType=payload.DocumentType
    WHERE payload.ContractVersion<2 AND job.Status<>N'Completed'
      AND payload.DocumentType IN(N'SalesInvoice',N'SalesReceipt')
    ORDER BY payload.AcceptedAt,payload.DocumentId;
    IF @@ROWCOUNT=0 BREAK;
    UPDATE payload
    SET InventoryCompositionJson=NULLIF((SELECT frozen.*
          FROM inventory.SaleCompositionSnapshot(businessValue.TenantId,payload.DocumentId,payload.PayloadJson) frozen
          ORDER BY frozen.LineNumber FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'),
        ContractVersion=2
    FROM dbo.DocumentProcessingPayloads payload
    JOIN @Pending batch ON batch.DocumentId=payload.DocumentId AND batch.DocumentType=payload.DocumentType
    JOIN dbo.Businesses businessValue ON businessValue.BusinessId=payload.BusinessId;
    DELETE FROM @Pending;
END;
GO
