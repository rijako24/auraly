SET XACT_ABORT ON;

IF OBJECT_ID(N'fiscal.FiscalCredentialSecrets', N'U') IS NOT NULL
   AND COL_LENGTH(N'fiscal.FiscalCredentialSecrets', N'BusinessId') IS NOT NULL
BEGIN
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'fiscal.FiscalCredentialSecrets', N'TenantId') IS NULL
        ALTER TABLE fiscal.FiscalCredentialSecrets ADD TenantId UNIQUEIDENTIFIER NULL;

    UPDATE credentials
    SET TenantId = business.TenantId
    FROM fiscal.FiscalCredentialSecrets credentials
    JOIN dbo.Businesses business ON business.BusinessId = credentials.BusinessId
    WHERE credentials.TenantId IS NULL;

    IF EXISTS (SELECT 1 FROM fiscal.FiscalCredentialSecrets WHERE TenantId IS NULL)
        THROW 51024, 'No se pudo identificar el tenant de una credencial fiscal.', 1;

    ;WITH ranked AS
    (
        SELECT BusinessId,
               ROW_NUMBER() OVER (
                   PARTITION BY TenantId
                   ORDER BY UpdatedAt DESC, CreatedAt DESC, BusinessId) AS Position
        FROM fiscal.FiscalCredentialSecrets
    )
    DELETE credentials
    FROM fiscal.FiscalCredentialSecrets credentials
    JOIN ranked ON ranked.BusinessId = credentials.BusinessId
    WHERE ranked.Position > 1;

    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FiscalCredentialSecrets_Businesses')
        ALTER TABLE fiscal.FiscalCredentialSecrets DROP CONSTRAINT FK_FiscalCredentialSecrets_Businesses;
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name=N'PK_FiscalCredentialSecrets')
        ALTER TABLE fiscal.FiscalCredentialSecrets DROP CONSTRAINT PK_FiscalCredentialSecrets;

    ALTER TABLE fiscal.FiscalCredentialSecrets ALTER COLUMN TenantId UNIQUEIDENTIFIER NOT NULL;
    ALTER TABLE fiscal.FiscalCredentialSecrets DROP COLUMN BusinessId;
    ALTER TABLE fiscal.FiscalCredentialSecrets
        ADD CONSTRAINT PK_FiscalCredentialSecrets PRIMARY KEY (TenantId);
    ALTER TABLE fiscal.FiscalCredentialSecrets
        ADD CONSTRAINT FK_FiscalCredentialSecrets_Tenants
        FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(TenantId);

    COMMIT TRANSACTION;
END;
