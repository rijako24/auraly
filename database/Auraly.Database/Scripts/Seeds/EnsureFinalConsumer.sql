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

;WITH Candidate AS(
    SELECT s.PartySiteId,profile.CountryId TargetCountryId,
           profile.AdministrativeDivisionId TargetDivisionId,
           profile.CityId TargetCityId,profile.Address TargetAddress,
           ROW_NUMBER() OVER(PARTITION BY s.PartyId ORDER BY
               s.IsPrimary DESC,s.CreatedAt,s.PartySiteId) Position
    FROM dbo.PartySites s
    JOIN @FinalConsumers f ON f.PartyId=s.PartyId
    JOIN dbo.TenantLegalProfiles profile ON profile.TenantId=f.TenantId
    WHERE NOT EXISTS(
        SELECT 1 FROM dbo.PartySites activeSite
        WHERE activeSite.PartyId=s.PartyId AND activeSite.IsActive=1))
UPDATE site
SET IsActive=1,IsPrimary=1,CountryId=candidate.TargetCountryId,
    AdministrativeDivisionId=candidate.TargetDivisionId,
    CityId=candidate.TargetCityId,AddressLine=candidate.TargetAddress,
    UpdatedAt=@Now
FROM dbo.PartySites site
JOIN Candidate candidate ON candidate.PartySiteId=site.PartySiteId
WHERE candidate.Position=1;

INSERT dbo.PartySites(
    PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,
    CityId,AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
SELECT NEWID(),f.PartyId,N'PRINCIPAL',N'Principal',profile.CountryId,
       profile.AdministrativeDivisionId,profile.CityId,profile.Address,
       1,1,creator.UserId,@Now
FROM @FinalConsumers f
JOIN dbo.TenantLegalProfiles profile ON profile.TenantId=f.TenantId
CROSS APPLY(
    SELECT TOP(1) userValue.UserId
    FROM dbo.AppUsers userValue
    WHERE userValue.TenantId=f.TenantId AND userValue.IsActive=1
    ORDER BY userValue.CreatedAt,userValue.UserId) creator
WHERE NOT EXISTS(
    SELECT 1 FROM dbo.PartySites site WHERE site.PartyId=f.PartyId);

PRINT N'Consumidor final DIAN garantizado para todos los negocios.';
