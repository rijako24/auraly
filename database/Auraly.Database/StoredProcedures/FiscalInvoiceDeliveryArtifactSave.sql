CREATE PROCEDURE dbo.FiscalInvoiceDeliveryArtifactSave
    @DocumentId UNIQUEIDENTIFIER,
    @MessageId UNIQUEIDENTIFIER,
    @TenantId UNIQUEIDENTIFIER,
    @LeaseId UNIQUEIDENTIFIER,
    @AttachedDocument VARBINARY(MAX),
    @AttachedDocumentHash BINARY(32),
    @AttachedDocumentFileName NVARCHAR(256),
    @GraphicalRepresentation VARBINARY(MAX),
    @GraphicalRepresentationHash BINARY(32),
    @GraphicalRepresentationFileName NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    -- The graphical parameters remain only for rolling compatibility with the
    -- previous API. PDF representations are generated in memory and are not stored.
    IF DATALENGTH(@AttachedDocument)=0 OR DATALENGTH(@AttachedDocumentHash)<>32
      THROW 51290,'The signed AttachedDocument artifact is invalid.',1;

    IF NOT EXISTS(
      SELECT 1
      FROM dbo.TenantProvisioningOutboxMessages message WITH(UPDLOCK,HOLDLOCK)
      JOIN dbo.FiscalDocuments fiscal WITH(UPDLOCK,HOLDLOCK)
        ON fiscal.DeliveryOutboxMessageId=message.MessageId
      JOIN dbo.Businesses business ON business.BusinessId=fiscal.BusinessId
      WHERE message.MessageId=@MessageId AND message.TenantId=@TenantId
        AND message.Type=N'FiscalInvoiceDelivery' AND message.LeaseId=@LeaseId
        AND message.ProcessedAt IS NULL AND fiscal.DocumentId=@DocumentId
        AND fiscal.FiscalStatus=N'DianAccepted' AND fiscal.DeliveredAt IS NULL
        AND business.TenantId=@TenantId)
      THROW 51291,'The fiscal delivery lease is no longer valid.',1;

    IF EXISTS(
      SELECT 1 FROM dbo.FiscalArtifacts WITH(UPDLOCK,HOLDLOCK)
      WHERE DocumentId=@DocumentId AND ArtifactType=N'SignedAttachedDocument')
    BEGIN
      IF NOT EXISTS(
        SELECT 1 FROM dbo.FiscalArtifacts
        WHERE DocumentId=@DocumentId AND ArtifactType=N'SignedAttachedDocument'
          AND ArtifactVersion=1 AND ContentHash=@AttachedDocumentHash)
        THROW 51292,'A different signed AttachedDocument artifact already exists.',1;
    END
    ELSE
      INSERT dbo.FiscalArtifacts
        (FiscalArtifactId,DocumentId,ArtifactType,ArtifactVersion,Content,ContentHash,
         ContentType,FileName,TechnicalAnnexVersion,GeneratorVersion,CreatedAt)
      VALUES(NEWID(),@DocumentId,N'SignedAttachedDocument',1,@AttachedDocument,@AttachedDocumentHash,
             N'application/xml',@AttachedDocumentFileName,NULL,N'Auraly.Fiscal.Ubl',SYSDATETIMEOFFSET());

END;
GO
