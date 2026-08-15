SET NOCOUNT ON;

DECLARE @Now DATETIMEOFFSET(7)=SYSUTCDATETIME();
DECLARE @ColombiaId UNIQUEIDENTIFIER=(SELECT TOP(1) CountryId FROM dbo.Countries WHERE Code='CO');

IF @ColombiaId IS NULL
    THROW 51000, 'Colombia must be seeded before the final consumer.', 1;

DECLARE @FinalConsumers TABLE(
    TenantId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    PartyId UNIQUEIDENTIFIER NOT NULL
);

INSERT @FinalConsumers(TenantId,PartyId)
SELECT TenantId,PartyId
FROM (
    SELECT p.TenantId,p.PartyId,
           ROW_NUMBER() OVER(PARTITION BY p.TenantId ORDER BY
               CASE WHEN p.IdentificationTypeCode=N'CC' AND p.NormalizedIdentification=N'222222222222' THEN 0 ELSE 1 END,
               p.CreatedAt,p.PartyId) Position
    FROM dbo.Parties p
    WHERE (p.IdentificationTypeCode=N'CC' AND p.NormalizedIdentification=N'222222222222')
       OR p.DisplayName=N'Consumidor final'
) candidates
WHERE Position=1;

UPDATE p
SET IdentificationCountryId=@ColombiaId,
    IdentificationTypeCode=N'CC',
    Identification=N'222222222222',
    NormalizedIdentification=N'222222222222',
    DisplayName=N'Consumidor final',
    LegalName=N'Consumidor final',
    CompletionStatus=N'Complete',
    IsActive=1,
    UpdatedAt=@Now
FROM dbo.Parties p
JOIN @FinalConsumers f ON f.PartyId=p.PartyId
WHERE NOT EXISTS(
    SELECT 1 FROM dbo.Parties conflict
    WHERE conflict.TenantId=p.TenantId
      AND conflict.PartyId<>p.PartyId
      AND conflict.IdentificationCountryId=@ColombiaId
      AND conflict.IdentificationTypeCode=N'CC'
      AND conflict.NormalizedIdentification=N'222222222222');

INSERT dbo.Parties(
    PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,
    Identification,NormalizedIdentification,DisplayName,LegalName,
    CompletionStatus,IsActive,CreatedAt)
OUTPUT inserted.TenantId,inserted.PartyId INTO @FinalConsumers(TenantId,PartyId)
SELECT NEWID(),t.TenantId,N'Organization',@ColombiaId,N'CC',
       N'222222222222',N'222222222222',N'Consumidor final',N'Consumidor final',
       N'Complete',1,@Now
FROM dbo.Tenants t
WHERE NOT EXISTS(SELECT 1 FROM @FinalConsumers f WHERE f.TenantId=t.TenantId);

UPDATE c
SET IsActive=1,UpdatedAt=@Now
FROM dbo.Customers c
JOIN @FinalConsumers f ON f.PartyId=c.PartyId;

INSERT dbo.Customers(CustomerId,PartyId,BusinessId,IsActive,CreatedAt)
SELECT NEWID(),f.PartyId,b.BusinessId,1,@Now
FROM dbo.Businesses b
JOIN @FinalConsumers f ON f.TenantId=b.TenantId
WHERE NOT EXISTS(
    SELECT 1 FROM dbo.Customers c
    WHERE c.PartyId=f.PartyId AND c.BusinessId=b.BusinessId);

PRINT N'Consumidor final DIAN garantizado para todos los negocios.';
