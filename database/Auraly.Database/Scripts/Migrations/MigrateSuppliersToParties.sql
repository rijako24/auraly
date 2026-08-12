PRINT 'Reconciling existing suppliers with canonical Party identities.';
IF OBJECT_ID(N'dbo.Suppliers',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Suppliers',N'PartyId') IS NOT NULL
BEGIN
    DECLARE @SupplierId UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER,@TenantId UNIQUEIDENTIFIER;
    DECLARE @Identification NVARCHAR(40),@Name NVARCHAR(200),@PartyId UNIQUEIDENTIFIER;
    DECLARE supplier_cursor CURSOR LOCAL FAST_FORWARD FOR
      SELECT s.SupplierId,s.BusinessId,b.TenantId,s.Identification,s.Name
      FROM dbo.Suppliers s JOIN dbo.Businesses b ON b.BusinessId=s.BusinessId WHERE s.PartyId IS NULL;
    OPEN supplier_cursor;
    FETCH NEXT FROM supplier_cursor INTO @SupplierId,@BusinessId,@TenantId,@Identification,@Name;
    WHILE @@FETCH_STATUS=0
    BEGIN
      SET @PartyId=NEWID();
      INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,LegalName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
      VALUES(@PartyId,@TenantId,N'Organization',@Name,@Name,N'Incomplete',1,'00000000-0000-0000-0000-000000000000',SYSDATETIMEOFFSET());
      UPDATE dbo.Suppliers SET PartyId=@PartyId WHERE SupplierId=@SupplierId;
      FETCH NEXT FROM supplier_cursor INTO @SupplierId,@BusinessId,@TenantId,@Identification,@Name;
    END
    CLOSE supplier_cursor; DEALLOCATE supplier_cursor;
END
GO
