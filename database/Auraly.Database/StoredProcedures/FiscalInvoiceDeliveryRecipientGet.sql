CREATE PROCEDURE dbo.FiscalInvoiceDeliveryRecipientGet
    @DocumentId UNIQUEIDENTIFIER,
    @MessageId UNIQUEIDENTIFIER,
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT fiscal.DeliveryEmail,business.Name,sale.DocumentNumber,
           fiscal.FiscalNumber,fiscal.IssuedAt,
           COALESCE(party.DisplayName,party.LegalName,
             LTRIM(RTRIM(CONCAT(party.FirstName,N' ',party.LastName))),
             sale.CustomerIdentification),sale.PayableAmount,
           sale.CustomerIdentification,sale.DocumentType,
           signedXml.Content,signedXml.FileName,
           applicationResponse.Content,applicationResponse.FileName
    FROM dbo.FiscalDocuments fiscal
    JOIN dbo.Businesses business ON business.BusinessId=fiscal.BusinessId
     AND business.TenantId=@TenantId
    JOIN dbo.SalesDocuments sale ON sale.DocumentId=fiscal.DocumentId
     AND sale.BusinessId=fiscal.BusinessId
    LEFT JOIN dbo.Customers customer ON customer.CustomerId=sale.CustomerId
    LEFT JOIN dbo.Parties party ON party.PartyId=customer.PartyId
    CROSS APPLY(
      SELECT TOP(1) artifact.Content,artifact.FileName
      FROM dbo.FiscalArtifacts artifact
      WHERE artifact.DocumentId=fiscal.DocumentId AND artifact.ArtifactType=N'SignedXml'
      ORDER BY artifact.ArtifactVersion DESC) signedXml
    CROSS APPLY(
      SELECT TOP(1) artifact.Content,artifact.FileName
      FROM dbo.FiscalArtifacts artifact
      WHERE artifact.DocumentId=fiscal.DocumentId AND artifact.ArtifactType=N'DianApplicationResponse'
      ORDER BY artifact.ArtifactVersion DESC) applicationResponse
    WHERE fiscal.DocumentId=@DocumentId
      AND fiscal.DeliveryOutboxMessageId=@MessageId
      AND fiscal.FiscalStatus=N'DianAccepted'
      AND fiscal.DeliveredAt IS NULL
      AND NULLIF(LTRIM(RTRIM(fiscal.DeliveryEmail)),N'') IS NOT NULL;
END;
GO
