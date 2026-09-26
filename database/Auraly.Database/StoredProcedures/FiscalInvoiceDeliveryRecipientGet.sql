CREATE PROCEDURE dbo.FiscalInvoiceDeliveryRecipientGet
    @DocumentId UNIQUEIDENTIFIER,
    @MessageId UNIQUEIDENTIFIER,
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT fiscal.BusinessId,fiscal.DeliveryEmail,
           COALESCE(sale.DocumentNumber,returned.DocumentNumber),
           fiscal.FiscalNumber,fiscal.IssuedAt,
           COALESCE(sale.PayableAmount,returned.TotalAmount),
           signedXml.Content,applicationResponse.Content,
           attachedDocument.Content,attachedDocument.FileName,
           CAST(NULL AS VARBINARY(MAX)),CAST(NULL AS NVARCHAR(256)),
           issuer.CertificateProvider,issuer.CertificateKeyReference,
           issuer.CertificateThumbprint,issuer.TestSetId,statusResponse.Content,
           COALESCE((SELECT payment.MethodCode,payment.Amount,payment.Reference,
                            payment.CardFranchiseCode,payment.ApprovalNumber,payment.BankAccountId,
                            payment.Notes,payment.TenderedAmount,payment.RoundingAdjustment
                     FROM dbo.SalesPayments payment WHERE payment.DocumentId=sale.DocumentId
                     ORDER BY payment.PaymentNumber FOR JSON PATH),N'[]') AS PaymentsJson,
           COALESCE(sale.CreditAmount,0) AS CreditAmount,
           JSON_QUERY(payload.PayloadJson,'$.commercialSnapshot.withholding') AS WithholdingJson,
           sale.CreditDueDate,
           COALESCE(creditSnapshot.SnapshotJson,payload.PayloadJson,
             serviceSnapshot.SnapshotJson) AS SnapshotJson,
           COALESCE(sale.DocumentType,N'SalesReturn') AS DocumentType
    FROM dbo.FiscalDocuments fiscal
    JOIN dbo.Businesses business ON business.BusinessId=fiscal.BusinessId
     AND business.TenantId=@TenantId
    LEFT JOIN dbo.SalesDocuments sale ON sale.DocumentId=fiscal.DocumentId
     AND sale.BusinessId=fiscal.BusinessId
     AND fiscal.FiscalDocumentType=N'Invoice'
    LEFT JOIN dbo.SalesReturns returned ON returned.ReturnId=fiscal.DocumentId
     AND returned.BusinessId=fiscal.BusinessId
     AND fiscal.FiscalDocumentType=N'CreditNote'
    LEFT JOIN dbo.DocumentProcessingPayloads payload
      ON payload.DocumentId=fiscal.DocumentId
     AND payload.DocumentType=COALESCE(sale.DocumentType,N'SalesReturn')
     AND payload.BusinessId=fiscal.BusinessId
    LEFT JOIN sales.SalesDocumentServiceFiscalSnapshots serviceSnapshot
      ON serviceSnapshot.DocumentId=sale.DocumentId
    LEFT JOIN dbo.SalesReturnFiscalSnapshots creditSnapshot
      ON creditSnapshot.DocumentId=returned.ReturnId
    JOIN dbo.FiscalDocumentProcesses process ON process.DocumentId=fiscal.DocumentId
     AND process.BusinessId=fiscal.BusinessId
    JOIN dbo.FiscalIssuerConfigurations issuer
      ON issuer.FiscalIssuerConfigurationId=process.FiscalIssuerConfigurationId
    CROSS APPLY(
      SELECT TOP(1) artifact.Content,artifact.FileName
      FROM dbo.FiscalArtifacts artifact
      WHERE artifact.DocumentId=fiscal.DocumentId AND artifact.ArtifactType=N'SignedXml'
      ORDER BY artifact.ArtifactVersion DESC) signedXml
    OUTER APPLY(
      SELECT TOP(1) artifact.Content,artifact.FileName
      FROM dbo.FiscalArtifacts artifact
      WHERE artifact.DocumentId=fiscal.DocumentId AND artifact.ArtifactType=N'DianApplicationResponse'
      ORDER BY artifact.ArtifactVersion DESC) applicationResponse
    OUTER APPLY(
      SELECT TOP(1) artifact.Content,artifact.FileName
      FROM dbo.FiscalArtifacts artifact
      WHERE artifact.DocumentId=fiscal.DocumentId
        AND artifact.ArtifactType=N'SignedAttachedDocument'
      ORDER BY artifact.ArtifactVersion DESC) attachedDocument
    OUTER APPLY(
      SELECT TOP(1) artifact.Content
      FROM dbo.FiscalArtifacts artifact
      WHERE artifact.DocumentId=fiscal.DocumentId
        AND artifact.ArtifactType=N'SanitizedSoapResponse'
      ORDER BY artifact.ArtifactVersion DESC) statusResponse
    WHERE fiscal.DocumentId=@DocumentId
      AND fiscal.DeliveryOutboxMessageId=@MessageId
      AND fiscal.FiscalStatus=N'DianAccepted'
      AND fiscal.DeliveredAt IS NULL
      AND (sale.DocumentId IS NOT NULL OR returned.ReturnId IS NOT NULL)
      AND COALESCE(creditSnapshot.SnapshotJson,payload.PayloadJson,
        serviceSnapshot.SnapshotJson) IS NOT NULL
      AND NULLIF(LTRIM(RTRIM(fiscal.DeliveryEmail)),N'') IS NOT NULL;
END;
GO
