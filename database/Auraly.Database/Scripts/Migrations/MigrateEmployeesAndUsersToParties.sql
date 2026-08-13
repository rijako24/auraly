SET NOCOUNT ON;

-- Todo empleado histórico debe participar en la identidad Party.
DECLARE @EmployeeParties TABLE(EmployeeId UNIQUEIDENTIFIER PRIMARY KEY,PartyId UNIQUEIDENTIFIER NOT NULL,TenantId UNIQUEIDENTIFIER NOT NULL,DisplayName NVARCHAR(200) NOT NULL);
INSERT @EmployeeParties(EmployeeId,PartyId,TenantId,DisplayName)
SELECT employee.EmployeeId,NEWID(),business.TenantId,COALESCE(NULLIF(LTRIM(RTRIM(employee.Name)),N''),N'Empleado')
FROM dbo.Employees employee
JOIN dbo.Businesses business ON business.BusinessId=employee.BusinessId
WHERE employee.PartyId IS NULL;

INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,FirstName,CompletionStatus,IsActive,CreatedAt)
SELECT PartyId,TenantId,N'NaturalPerson',DisplayName,DisplayName,N'Incomplete',1,SYSUTCDATETIME()
FROM @EmployeeParties;

UPDATE employee SET PartyId=mapping.PartyId
FROM dbo.Employees employee
JOIN @EmployeeParties mapping ON mapping.EmployeeId=employee.EmployeeId;

-- Reutiliza una identidad inequívoca por correo para usuarios históricos.
;WITH EmailMatches AS (
  SELECT appUser.UserId,MIN(party.PartyId) PartyId,COUNT_BIG(*) MatchCount
  FROM dbo.AppUsers appUser
  JOIN dbo.PartyContacts contact ON contact.ContactType=N'Email' AND contact.IsActive=1
    AND contact.NormalizedValue=UPPER(LTRIM(RTRIM(appUser.Email)))
  JOIN dbo.Parties party ON party.PartyId=contact.PartyId AND party.TenantId=appUser.TenantId
  WHERE appUser.PartyId IS NULL
  GROUP BY appUser.UserId
)
UPDATE appUser SET PartyId=matching.PartyId
FROM dbo.AppUsers appUser
JOIN EmailMatches matching ON matching.UserId=appUser.UserId AND matching.MatchCount=1;

-- Si el usuario y un empleado tienen exactamente el mismo nombre dentro del tenant, comparten Party.
;WITH NameMatches AS (
  SELECT appUser.UserId,MIN(employee.PartyId) PartyId,COUNT_BIG(*) MatchCount
  FROM dbo.AppUsers appUser
  JOIN dbo.Businesses business ON business.TenantId=appUser.TenantId
  JOIN dbo.Employees employee ON employee.BusinessId=business.BusinessId AND employee.PartyId IS NOT NULL
    AND UPPER(LTRIM(RTRIM(employee.Name)))=UPPER(LTRIM(RTRIM(CONCAT(appUser.FirstName,N' ',appUser.LastName))))
  WHERE appUser.PartyId IS NULL
  GROUP BY appUser.UserId
)
UPDATE appUser SET PartyId=matching.PartyId
FROM dbo.AppUsers appUser
JOIN NameMatches matching ON matching.UserId=appUser.UserId AND matching.MatchCount=1;

DECLARE @UserParties TABLE(UserId UNIQUEIDENTIFIER PRIMARY KEY,PartyId UNIQUEIDENTIFIER NOT NULL,TenantId UNIQUEIDENTIFIER NOT NULL,DisplayName NVARCHAR(200) NOT NULL,FirstName NVARCHAR(100) NULL,LastName NVARCHAR(100) NULL,Email NVARCHAR(256) NULL,Phone NVARCHAR(20) NULL);
INSERT @UserParties(UserId,PartyId,TenantId,DisplayName,FirstName,LastName,Email,Phone)
SELECT appUser.UserId,NEWID(),appUser.TenantId,
  COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(appUser.FirstName,N' ',appUser.LastName))),N''),appUser.Username),
  NULLIF(LTRIM(RTRIM(appUser.FirstName)),N''),NULLIF(LTRIM(RTRIM(appUser.LastName)),N''),appUser.Email,appUser.PhoneNumber
FROM dbo.AppUsers appUser
WHERE appUser.PartyId IS NULL;

INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,FirstName,LastName,CompletionStatus,IsActive,CreatedAt)
SELECT PartyId,TenantId,N'NaturalPerson',DisplayName,FirstName,LastName,N'Incomplete',1,SYSUTCDATETIME()
FROM @UserParties;

UPDATE appUser SET PartyId=mapping.PartyId
FROM dbo.AppUsers appUser
JOIN @UserParties mapping ON mapping.UserId=appUser.UserId;

INSERT dbo.PartyContacts(PartyContactId,PartyId,ContactType,Value,NormalizedValue,IsPrimary,IsActive,CreatedAt)
SELECT NEWID(),PartyId,N'Email',Email,UPPER(LTRIM(RTRIM(Email))),1,1,SYSUTCDATETIME()
FROM @UserParties WHERE NULLIF(LTRIM(RTRIM(Email)),N'') IS NOT NULL;

INSERT dbo.PartyContacts(PartyContactId,PartyId,ContactType,Value,NormalizedValue,IsPrimary,IsActive,CreatedAt)
SELECT NEWID(),PartyId,N'Phone',Phone,
  REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Phone,N' ',N''),N'-',N''),N'(',N''),N')',N''),N'+',N''),1,1,SYSUTCDATETIME()
FROM @UserParties WHERE NULLIF(LTRIM(RTRIM(Phone)),N'') IS NOT NULL;
