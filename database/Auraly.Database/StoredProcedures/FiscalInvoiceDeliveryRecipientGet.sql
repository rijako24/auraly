CREATE PROCEDURE dbo.FiscalInvoiceDeliveryRecipientGet
    @DocumentId UNIQUEIDENTIFIER,
    @MessageId UNIQUEIDENTIFIER,
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT fiscal.BusinessId,fiscal.DeliveryEmail,sale.DocumentNumber,
           fiscal.FiscalNumber,fiscal.IssuedAt,sale.PayableAmount,
           signedXml.Content,applicationResponse.Content,
           attachedDocument.Content,attachedDocument.FileName,
           graphicalRepresentation.Content,graphicalRepresentation.FileName,
           issuer.CertificateProvider,issuer.CertificateKeyReference,
           issuer.CertificateThumbprint,issuer.TestSetId,statusResponse.Content,
           (SELECT payment.MethodCode,payment.Amount,payment.Reference,
                   payment.CardFranchiseCode,payment.ApprovalNumber,payment.BankAccountId,
                   payment.Notes,payment.TenderedAmount
            FROM dbo.SalesPayments payment WHERE payment.DocumentId=sale.DocumentId
            ORDER BY payment.PaymentNumber FOR JSON PATH) AS PaymentsJson,
           sale.CreditAmount,
           JSON_QUERY(payload.PayloadJson,'$.commercialSnapshot.withholding') AS WithholdingJson
    FROM dbo.FiscalDocuments fiscal
    JOIN dbo.Businesses business ON business.BusinessId=fiscal.BusinessId
     AND business.TenantId=@TenantId
    JOIN dbo.SalesDocuments sale ON sale.DocumentId=fiscal.DocumentId
     AND sale.BusinessId=fiscal.BusinessId
    LEFT JOIN dbo.DocumentProcessingPayloads payload
      ON payload.DocumentId=sale.DocumentId AND payload.DocumentType=sale.DocumentType
     AND payload.BusinessId=sale.BusinessId
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
      SELECT TOP(1) artifact.Content,artifact.FileName
      FROM dbo.FiscalArtifacts artifact
      WHERE artifact.DocumentId=fiscal.DocumentId
        AND artifact.ArtifactType=N'GraphicalRepresentationPdf'
      ORDER BY artifact.ArtifactVersion DESC) graphicalRepresentation
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
      AND NULLIF(LTRIM(RTRIM(fiscal.DeliveryEmail)),N'') IS NOT NULL;
END;
GO
