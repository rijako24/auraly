CREATE PROCEDURE dbo.FiscalInvoiceDeliveryArtifactSave
    @DocumentId UNIQUEIDENTIFIER,
    @MessageId UNIQUEIDENTIFIER,
    @TenantId UNIQUEIDENTIFIER,
    @LeaseId UNIQUEIDENTIFIER,
    @Content VARBINARY(MAX),
    @ContentHash BINARY(32),
    @FileName NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    IF DATALENGTH(@Content)=0 OR DATALENGTH(@ContentHash)<>32
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
          AND ArtifactVersion=1 AND ContentHash=@ContentHash)
        THROW 51292,'A different signed AttachedDocument artifact already exists.',1;
    END
    ELSE
      INSERT dbo.FiscalArtifacts
        (FiscalArtifactId,DocumentId,ArtifactType,ArtifactVersion,Content,ContentHash,
         ContentType,FileName,TechnicalAnnexVersion,GeneratorVersion,CreatedAt)
      VALUES(NEWID(),@DocumentId,N'SignedAttachedDocument',1,@Content,@ContentHash,
             N'application/xml',@FileName,NULL,N'Auraly.Fiscal.Ubl',SYSDATETIMEOFFSET());
END;
GO
