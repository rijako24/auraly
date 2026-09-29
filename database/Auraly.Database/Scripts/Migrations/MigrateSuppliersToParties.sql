PRINT 'Reconciling existing suppliers with canonical Party identities.';
IF OBJECT_ID(N'dbo.Suppliers',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Suppliers',N'PartyId') IS NOT NULL
BEGIN
    DECLARE @PendingSupplierParties TABLE
      (SupplierId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
       PartyId UNIQUEIDENTIFIER NOT NULL,
       TenantId UNIQUEIDENTIFIER NOT NULL,
       Name NVARCHAR(200) NOT NULL);
    INSERT @PendingSupplierParties(SupplierId,PartyId,TenantId,Name)
    SELECT SupplierId,NEWID(),TenantId,Name
    FROM dbo.Suppliers WHERE PartyId IS NULL;

    INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,LegalName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
    SELECT PartyId,TenantId,N'Organization',Name,Name,N'Incomplete',1,
      '00000000-0000-0000-0000-000000000000',SYSDATETIMEOFFSET()
    FROM @PendingSupplierParties;

    UPDATE supplier SET PartyId=pending.PartyId
    FROM dbo.Suppliers supplier
    JOIN @PendingSupplierParties pending ON pending.SupplierId=supplier.SupplierId;
END
GO
