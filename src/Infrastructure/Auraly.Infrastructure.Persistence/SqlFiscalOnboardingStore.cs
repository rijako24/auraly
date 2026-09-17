using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Fiscal;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlFiscalOnboardingStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    IConfiguration configuration,
    TimeProvider timeProvider) : IFiscalOnboardingStore
{
    private const string HabilitationEndpoint =
        "https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc";
    private const string ProductionEndpoint =
        "https://vpfe.dian.gov.co/WcfDianCustomerServices.svc";
    private const string QrValidationUrl = DianFiscalDefaults.ProductionQrValidationUrl;
    private const string TechnicalKeyVersion = DianFiscalDefaults.NumberingRangeTechnicalKeyVersion;

    public async Task<FiscalOnboardingConfiguration> GetAsync(
        Guid tenantId, Guid businessId, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;

            SELECT b.Name,COALESCE(NULLIF(p.LegalName,N''),b.Name),
                   COALESCE(p.Nit,N''),
                   COALESCE(NULLIF(i.SupplierCheckDigit,N''),NULLIF(p.VerificationDigit,N''),N''),
                   i.SoftwareIdentificationCode,i.TestSetId,i.CertificateThumbprint,
                   i.ValidFrom,i.ValidTo,i.Environment,
                   accepted.AcceptedAt,
                   assigned.DianNumberingRangeId,assigned.AuthorizationNumber,
                   assigned.ResolutionDate,assigned.Prefix,assigned.RangeStart,assigned.RangeEnd,
                   assigned.ValidFrom,assigned.ValidUntil,
                   latest.DocumentId,latest.Status,latest.LastStatusCode,
                   latest.LastStatusDescription,latest.LastErrorCode,
                   latest.LastErrorMessage,latest.UpdatedAt,
                   supportAssigned.AuthorizationNumber,
                   supportAssigned.ResolutionDate,supportAssigned.Prefix,
                   supportAssigned.RangeStart,supportAssigned.RangeEnd,
                   supportAssigned.ValidFrom,supportAssigned.ValidUntil,
                   i.SupportDocumentSoftwareIdentificationCode,
                   i.SupportDocumentSoftwarePinSecretReference,
                   i.SupportDocumentTestSetId,
                   supportAccepted.AcceptedAt,
                   supportLatest.DocumentId,supportLatest.Status,
                   supportLatest.LastStatusCode,supportLatest.LastStatusDescription,
                   supportLatest.LastErrorCode,supportLatest.LastErrorMessage,
                   supportLatest.UpdatedAt
            FROM dbo.Businesses b
            LEFT JOIN dbo.TenantLegalProfiles p ON p.TenantId=b.TenantId
            OUTER APPLY(
                SELECT TOP(1) SoftwareIdentificationCode,TestSetId,CertificateThumbprint,
                       SupplierCheckDigit,
                       ValidFrom,ValidTo,Environment,
                       SupportDocumentSoftwareIdentificationCode,
                       SupportDocumentSoftwarePinSecretReference,
                       SupportDocumentTestSetId
                FROM dbo.FiscalIssuerConfigurations configuration
                JOIN dbo.Businesses configuredBusiness
                  ON configuredBusiness.BusinessId=configuration.BusinessId
                 AND configuredBusiness.TenantId=b.TenantId
                WHERE configuration.IsActive=1
                ORDER BY CASE
                           WHEN configuration.BusinessId=b.BusinessId AND Environment=1 THEN 0
                           WHEN Environment=2 THEN 1
                           ELSE 2
                         END,
                         configuration.CreatedAt DESC,configuration.Version DESC) i
            OUTER APPLY(
                SELECT MAX(attempt.CompletedAt) AcceptedAt
                FROM dbo.FiscalDocumentProcesses fp
                JOIN dbo.FiscalIssuerConfigurations configuration
                  ON configuration.FiscalIssuerConfigurationId=fp.FiscalIssuerConfigurationId
                JOIN dbo.FiscalTransmissionAttempts attempt
                  ON attempt.DocumentId=fp.DocumentId
                 AND attempt.Operation=N'GetStatusZip'
                 AND attempt.Disposition=N'Accepted'
                 AND attempt.StatusCode=N'2'
                JOIN dbo.FiscalDocuments acceptedDocument
                  ON acceptedDocument.DocumentId=fp.DocumentId
                 AND acceptedDocument.FiscalDocumentType=N'Invoice'
                WHERE fp.BusinessId=b.BusinessId
                  AND (fp.TestSetId IS NOT NULL OR configuration.Environment=2)) accepted
            OUTER APPLY(
                SELECT MAX(attempt.CompletedAt) AcceptedAt
                FROM dbo.FiscalDocumentProcesses fp
                JOIN dbo.FiscalDocuments acceptedDocument
                  ON acceptedDocument.DocumentId=fp.DocumentId
                 AND acceptedDocument.FiscalDocumentType=N'SupportDocument'
                JOIN dbo.FiscalTransmissionAttempts attempt
                  ON attempt.DocumentId=fp.DocumentId
                 AND attempt.Operation=N'GetStatusZip'
                 AND attempt.Disposition=N'Accepted'
                 AND attempt.StatusCode=N'2'
                WHERE fp.BusinessId=b.BusinessId AND fp.TestSetId IS NOT NULL) supportAccepted
            OUTER APPLY(
                SELECT TOP(1) r.DianNumberingRangeId,r.AuthorizationNumber,r.ResolutionDate,
                       r.Prefix,r.RangeStart,r.RangeEnd,r.ValidFrom,r.ValidUntil
                FROM fiscal.DianNumberingRanges r
                JOIN dbo.FiscalAuthorizations a
                  ON a.BusinessId=b.BusinessId AND a.DianNumberingRangeId=r.DianNumberingRangeId
                 AND a.Environment=1 AND a.IsActive=1
                JOIN dbo.FiscalSeries series
                  ON series.FiscalAuthorizationId=a.FiscalAuthorizationId
                 AND series.DocumentType=N'SalesInvoice' AND series.EmitterKind=N'Server'
                 AND series.DeviceId IS NULL AND series.IsActive=1
                WHERE r.TenantId=b.TenantId AND r.AssignedBusinessId=b.BusinessId
                ORDER BY r.AssignedAt DESC) assigned
            OUTER APPLY(
                SELECT TOP(1) fp.DocumentId,fp.Status,fp.LastStatusCode,
                       fp.LastStatusDescription,fp.LastErrorCode,
                       fp.LastErrorMessage,fp.UpdatedAt
                FROM dbo.FiscalDocumentProcesses fp
                JOIN dbo.FiscalIssuerConfigurations configuration
                  ON configuration.FiscalIssuerConfigurationId=fp.FiscalIssuerConfigurationId
                JOIN dbo.FiscalDocuments latestDocument
                  ON latestDocument.DocumentId=fp.DocumentId
                 AND latestDocument.FiscalDocumentType=N'Invoice'
                WHERE fp.BusinessId=b.BusinessId
                  AND (fp.TestSetId IS NOT NULL OR configuration.Environment=2)
                ORDER BY fp.CreatedAt DESC,fp.DocumentId DESC) latest
            OUTER APPLY(
                SELECT TOP(1) fp.DocumentId,fp.Status,fp.LastStatusCode,
                       fp.LastStatusDescription,fp.LastErrorCode,
                       fp.LastErrorMessage,fp.UpdatedAt
                FROM dbo.FiscalDocumentProcesses fp
                JOIN dbo.FiscalDocuments latestDocument
                  ON latestDocument.DocumentId=fp.DocumentId
                 AND latestDocument.FiscalDocumentType=N'SupportDocument'
                WHERE fp.BusinessId=b.BusinessId AND fp.TestSetId IS NOT NULL
                ORDER BY fp.CreatedAt DESC,fp.DocumentId DESC) supportLatest
            OUTER APPLY(
                SELECT TOP(1) a.AuthorizationNumber,COALESCE(a.ResolutionDate,r.ResolutionDate) ResolutionDate,
                       series.Prefix,series.RangeStart,series.RangeEnd,a.ValidFrom,a.ValidUntil
                FROM dbo.FiscalAuthorizations a
                JOIN dbo.FiscalSeries series
                  ON series.FiscalAuthorizationId=a.FiscalAuthorizationId
                 AND series.DocumentType=N'SupportDocument' AND series.EmitterKind=N'Server'
                 AND series.DeviceId IS NULL AND series.IsActive=1
                LEFT JOIN fiscal.DianNumberingRanges r
                  ON r.DianNumberingRangeId=a.DianNumberingRangeId
                WHERE a.BusinessId=b.BusinessId AND a.Environment=1 AND a.IsActive=1
                ORDER BY series.CreatedAt DESC) supportAssigned
            WHERE b.BusinessId=@BusinessId;

            SELECT r.DianNumberingRangeId,r.AuthorizationNumber,r.ResolutionDate,r.Prefix,
                   r.RangeStart,r.RangeEnd,r.ValidFrom,r.ValidUntil,r.AssignedBusinessId,b.Name,
                   r.DocumentPurpose
            FROM fiscal.DianNumberingRanges r
            LEFT JOIN dbo.Businesses b ON b.BusinessId=r.AssignedBusinessId
            WHERE r.TenantId=@TenantId AND r.ValidUntil>=CONVERT(date,@Now)
            ORDER BY CASE WHEN r.AssignedBusinessId IS NULL THEN 0 ELSE 1 END,
                     r.ValidUntil,r.Prefix,r.RangeStart;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        Add(command, "@Now", timeProvider.GetUtcNow());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new FiscalConfigurationValidationException(
                "El perfil legal del negocio no está configurado.");

        var businessName = reader.GetString(0);
        var legalName = reader.GetString(1);
        var nit = reader.GetString(2);
        var checkDigit = reader.GetString(3);
        var softwareId = Text(reader, 4);
        Guid? testSetId = reader.IsDBNull(5) ? null : reader.GetGuid(5);
        var thumbprint = Text(reader, 6);
        DateTimeOffset? certificateFrom = reader.IsDBNull(7) ? null : reader.GetDateTimeOffset(7);
        DateTimeOffset? certificateTo = reader.IsDBNull(8) ? null : reader.GetDateTimeOffset(8);
        byte? environment = reader.IsDBNull(9) ? null : reader.GetByte(9);
        DateTimeOffset? acceptedAt = reader.IsDBNull(10) ? null : reader.GetDateTimeOffset(10);
        DianNumberingRangeOption? assigned = reader.IsDBNull(11) ? null : new(
            reader.GetGuid(11), reader.GetString(12), Date(reader, 13), reader.GetString(14),
            reader.GetInt64(15), reader.GetInt64(16), reader.GetFieldValue<DateOnly>(17),
            reader.GetFieldValue<DateOnly>(18), false, businessId, businessName);
        FiscalHabilitationAttempt? latestAttempt = null;
        if (!reader.IsDBNull(19))
        {
            var status = reader.GetString(20);
            var terminalFailure = IsTerminalFailure(status);
            latestAttempt = new FiscalHabilitationAttempt(
                reader.GetGuid(19),
                status,
                terminalFailure,
                terminalFailure ? Text(reader, 23) ?? Text(reader, 21) : null,
                terminalFailure ? Text(reader, 24) ?? Text(reader, 22) : null,
                reader.GetDateTimeOffset(25));
        }
        SupportDocumentNumberingConfiguration? assignedSupport = reader.IsDBNull(26) ? null : new(
            reader.GetString(26), Date(reader, 27), reader.GetString(28),
            reader.GetInt64(29), reader.GetInt64(30), reader.GetFieldValue<DateOnly>(31),
            reader.GetFieldValue<DateOnly>(32));
        var supportSoftwareId = Text(reader, 33);
        var hasSupportSoftwarePin = !reader.IsDBNull(34) &&
            !string.IsNullOrWhiteSpace(reader.GetString(34));
        Guid? supportTestSetId = reader.IsDBNull(35) ? null : reader.GetGuid(35);
        DateTimeOffset? supportAcceptedAt = reader.IsDBNull(36)
            ? null : reader.GetDateTimeOffset(36);
        FiscalHabilitationAttempt? latestSupportAttempt = null;
        if (!reader.IsDBNull(37))
        {
            var status = reader.GetString(38);
            var terminalFailure = IsTerminalFailure(status);
            latestSupportAttempt = new FiscalHabilitationAttempt(
                reader.GetGuid(37), status, terminalFailure,
                terminalFailure ? Text(reader, 41) ?? Text(reader, 39) : null,
                terminalFailure ? Text(reader, 42) ?? Text(reader, 40) : null,
                reader.GetDateTimeOffset(43));
        }

        var ranges = new List<DianNumberingRangeOption>();
        var supportRanges = new List<DianNumberingRangeOption>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Guid? assignedBusinessId = reader.IsDBNull(8) ? null : reader.GetGuid(8);
            var option = new DianNumberingRangeOption(
                reader.GetGuid(0), reader.GetString(1), Date(reader, 2), reader.GetString(3),
                reader.GetInt64(4), reader.GetInt64(5), reader.GetFieldValue<DateOnly>(6),
                reader.GetFieldValue<DateOnly>(7), assignedBusinessId is null,
                assignedBusinessId, Text(reader, 9));
            if (string.Equals(reader.GetString(10), FiscalNumberingPurposes.SupportDocument,
                    StringComparison.Ordinal))
                supportRanges.Add(option);
            else
                ranges.Add(option);
        }

        var now = timeProvider.GetUtcNow();
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(nit)) missing.Add("PerfilLegal");
        if (string.IsNullOrWhiteSpace(softwareId)) missing.Add("SoftwareId");
        if (testSetId is null) missing.Add("TestSetId");
        if (string.IsNullOrWhiteSpace(thumbprint)) missing.Add("Certificado");
        if (certificateTo is not null && certificateTo <= now) missing.Add("CertificadoVencido");
        var productionActive = environment == 1;
        var stage = productionActive
            ? FiscalOnboardingStages.ProductionActive
            : acceptedAt is not null && (assigned is not null || ranges.Any(item => item.IsAvailable))
                ? FiscalOnboardingStages.ProductionReady
                : acceptedAt is not null
                    ? FiscalOnboardingStages.HabilitationAccepted
                    : missing.Count == 0
                        ? FiscalOnboardingStages.HabilitationReady
                        : FiscalOnboardingStages.NotConfigured;
        return new FiscalOnboardingConfiguration(
            businessId, businessName, legalName, nit, checkDigit, stage, softwareId,
            testSetId, thumbprint is not null,
            thumbprint is null ? null : thumbprint[^Math.Min(8, thumbprint.Length)..],
            certificateFrom, certificateTo, acceptedAt is not null, acceptedAt,
            productionActive, assigned, ranges, missing, latestAttempt, assignedSupport,
            supportSoftwareId, hasSupportSoftwarePin, supportRanges,
            supportTestSetId, supportAcceptedAt is not null, supportAcceptedAt,
            latestSupportAttempt);
    }

    private static bool IsTerminalFailure(string status) => status is
        FiscalDocumentStatusCodes.FiscalIntegrityConflict or
        FiscalDocumentStatusCodes.MissingMandatoryFiscalData or
        FiscalDocumentStatusCodes.SchemaValidationFailed or
        FiscalDocumentStatusCodes.SignatureFailed or
        FiscalDocumentStatusCodes.DianRejected or
        FiscalDocumentStatusCodes.PermanentFailure;

    public async Task SaveHabilitationAsync(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        string softwareIdentificationCode,
        Guid testSetId,
        string supplierCheckDigit,
        FiscalCredentialReference credentials,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON;
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;
            IF EXISTS(
                SELECT 1 FROM dbo.FiscalIssuerConfigurations configuration
                JOIN dbo.Businesses business ON business.BusinessId=configuration.BusinessId
                WHERE business.TenantId=@TenantId AND configuration.IsActive=1 AND configuration.Environment=1)
                THROW 51022,'La configuración DIAN de producción ya está activa en una sede del tenant.',1;
            DECLARE @Version int=ISNULL((SELECT MAX(Version) FROM dbo.FiscalIssuerConfigurations WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId),0)+1;
            UPDATE configuration SET IsActive=0
            FROM dbo.FiscalIssuerConfigurations configuration
            JOIN dbo.Businesses business ON business.BusinessId=configuration.BusinessId
            WHERE business.TenantId=@TenantId AND configuration.IsActive=1 AND configuration.Environment=2;
            INSERT dbo.FiscalIssuerConfigurations(
                FiscalIssuerConfigurationId,BusinessId,Version,SupplierTaxId,SupplierCheckDigit,
                LegalName,TradeName,TaxLevelCode,TaxSchemeId,TaxSchemeName,IdentificationTypeCode,
                AddressLine,CityCode,CityName,DepartmentCode,DepartmentName,PostalZone,
                CountryCode,CountryName,SoftwareIdentificationCode,SoftwarePinSecretReference,
                Environment,TestSetId,CertificateProvider,CertificateKeyReference,
                CertificateThumbprint,DianEndpoint,TechnicalAnnexVersion,GeneratorVersion,
                ValidFrom,ValidTo,IsActive,CreatedAt,CreatedByUserId)
            SELECT @Id,@BusinessId,@Version,p.Nit,@SupplierCheckDigit,p.LegalName,
                   NULLIF(p.TradeName,N''),p.TaxResponsibilities,N'01',N'IVA',N'31',p.Address,
                   c.Code,c.Name,d.Code,d.Name,NULL,country.Code,country.Name,
                   @SoftwareId,@PinReference,2,@TestSetId,@CertificateProvider,
                   @CertificateReference,@Thumbprint,@Endpoint,N'1.9',N'Auraly.Commerce',
                   @ValidFrom,@ValidTo,1,@Now,@UserId
            FROM dbo.TenantLegalProfiles p
            JOIN dbo.Countries country ON country.CountryId=p.CountryId
            JOIN dbo.AdministrativeDivisions d ON d.AdministrativeDivisionId=p.AdministrativeDivisionId
            JOIN dbo.Cities c ON c.CityId=p.CityId
            WHERE p.TenantId=@TenantId;
            IF @@ROWCOUNT<>1 THROW 51022,'El perfil legal del tenant está incompleto.',1;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection, transaction);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        Add(command, "@UserId", userId);
        Add(command, "@Id", ids.NewId());
        Add(command, "@SoftwareId", softwareIdentificationCode);
        Add(command, "@PinReference", credentials.SoftwarePinReference);
        Add(command, "@TestSetId", testSetId);
        Add(command, "@SupplierCheckDigit", supplierCheckDigit);
        Add(command, "@CertificateProvider", credentials.Provider);
        Add(command, "@CertificateReference", credentials.CertificateKeyReference);
        Add(command, "@Thumbprint", credentials.CertificateThumbprint);
        Add(command, "@Endpoint", HabilitationEndpoint);
        Add(command, "@ValidFrom", credentials.CertificateValidFrom);
        Add(command, "@ValidTo", credentials.CertificateValidTo);
        Add(command, "@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<DianNumberingRangeContext> GetNumberingRangeContextAsync(
        Guid tenantId, Guid businessId, string documentPurpose,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;

            SELECT TOP(1) i.SupplierTaxId,
                   CASE WHEN @Purpose=N'SupportDocument'
                        THEN i.SupportDocumentSoftwareIdentificationCode
                        ELSE i.SoftwareIdentificationCode END,
                   i.CertificateProvider,i.CertificateKeyReference,i.CertificateThumbprint
            FROM dbo.FiscalIssuerConfigurations i
            JOIN dbo.Businesses b ON b.BusinessId=i.BusinessId
            WHERE b.TenantId=@TenantId
              AND ((@Purpose=N'SalesInvoice' AND i.Environment=2)
                OR (@Purpose=N'SupportDocument' AND i.BusinessId=@BusinessId
                    AND i.Environment=1 AND i.IsActive=1))
              AND i.ValidFrom<=@Now AND (i.ValidTo IS NULL OR i.ValidTo>@Now)
            ORDER BY i.CreatedAt DESC,i.Version DESC;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        Add(command, "@Purpose", documentPurpose);
        Add(command, "@Now", timeProvider.GetUtcNow());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new FiscalConfigurationValidationException(
                documentPurpose == FiscalNumberingPurposes.SupportDocument
                    ? "Configura primero el Software ID y PIN de documento soporte."
                    : "No existe una configuración DIAN de habilitación.");
        if (reader.IsDBNull(1) || string.IsNullOrWhiteSpace(reader.GetString(1)))
            throw new FiscalConfigurationValidationException(
                "Configura primero el Software ID y PIN de documento soporte.");
        return new DianNumberingRangeContext(
            businessId, reader.GetString(0), reader.GetString(0), reader.GetString(1),
            new FiscalCertificateReference(
                businessId, reader.GetString(2), reader.GetString(3), reader.GetString(4)));
    }

    public async Task ImportNumberingRangesAsync(
        Guid tenantId,
        Guid userId,
        string documentPurpose,
        IReadOnlyList<ImportedDianNumberingRange> ranges,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF @Purpose=N'SupportDocument'
            BEGIN
                UPDATE existingRange
                SET DocumentPurpose=N'SupportDocument'
                FROM fiscal.DianNumberingRanges existingRange
                JOIN dbo.FiscalAuthorizations existingAuthorization
                  ON existingAuthorization.DianNumberingRangeId=existingRange.DianNumberingRangeId
                JOIN dbo.FiscalSeries series
                  ON series.FiscalAuthorizationId=existingAuthorization.FiscalAuthorizationId
                 AND series.DocumentType=N'SupportDocument'
                WHERE existingRange.TenantId=@TenantId
                  AND existingRange.DocumentPurpose<>N'SupportDocument';
            END;

            ;WITH source AS (
                SELECT @TenantId TenantId,@Purpose DocumentPurpose,
                       CONVERT(uniqueidentifier,j.Id) DianNumberingRangeId,
                       j.AuthorizationNumber,CONVERT(date,j.ResolutionDate) ResolutionDate,
                       j.Prefix,j.RangeStart,j.RangeEnd,
                       CONVERT(date,j.ValidFrom) ValidFrom,CONVERT(date,j.ValidUntil) ValidUntil,
                       CONVERT(varbinary(max),j.TechnicalKeyBase64,1) ProtectedTechnicalKey
                FROM OPENJSON(@Ranges) WITH(
                    Id nvarchar(36) '$.id',
                    AuthorizationNumber nvarchar(64) '$.authorizationNumber',
                    ResolutionDate nvarchar(10) '$.resolutionDate',
                    Prefix nvarchar(16) '$.prefix',
                    RangeStart bigint '$.rangeStart',RangeEnd bigint '$.rangeEnd',
                    ValidFrom nvarchar(10) '$.validFrom',ValidUntil nvarchar(10) '$.validUntil',
                    TechnicalKeyBase64 varchar(max) '$.technicalKeyHex') j
            )
            MERGE fiscal.DianNumberingRanges WITH(HOLDLOCK) AS target
            USING source
            ON target.TenantId=source.TenantId
              AND target.DocumentPurpose=source.DocumentPurpose
              AND target.AuthorizationNumber=source.AuthorizationNumber
              AND target.Prefix=source.Prefix
              AND target.RangeStart=source.RangeStart
              AND target.RangeEnd=source.RangeEnd
            WHEN MATCHED AND target.AssignedBusinessId IS NULL THEN UPDATE SET
                ResolutionDate=source.ResolutionDate,ValidFrom=source.ValidFrom,
                ValidUntil=source.ValidUntil,
                ProtectedTechnicalKey=source.ProtectedTechnicalKey,LastSeenAt=@Now
            WHEN NOT MATCHED THEN INSERT(
                DianNumberingRangeId,TenantId,DocumentPurpose,AuthorizationNumber,ResolutionDate,Prefix,
                RangeStart,RangeEnd,ValidFrom,ValidUntil,ProtectedTechnicalKey,
                ImportedAt,LastSeenAt)
              VALUES(source.DianNumberingRangeId,source.TenantId,source.DocumentPurpose,
                     source.AuthorizationNumber,source.ResolutionDate,source.Prefix,
                     source.RangeStart,source.RangeEnd,source.ValidFrom,source.ValidUntil,
                     source.ProtectedTechnicalKey,@Now,@Now);

            IF @Purpose=N'SupportDocument'
            BEGIN
                UPDATE existingAuthorization
                SET DianNumberingRangeId=range.DianNumberingRangeId,
                    ResolutionDate=COALESCE(existingAuthorization.ResolutionDate,range.ResolutionDate)
                FROM dbo.FiscalAuthorizations existingAuthorization
                JOIN dbo.FiscalSeries series
                  ON series.FiscalAuthorizationId=existingAuthorization.FiscalAuthorizationId
                 AND series.DocumentType=N'SupportDocument' AND series.IsActive=1
                JOIN dbo.Businesses business
                  ON business.BusinessId=existingAuthorization.BusinessId AND business.TenantId=@TenantId
                JOIN fiscal.DianNumberingRanges range
                  ON range.TenantId=@TenantId AND range.DocumentPurpose=N'SupportDocument'
                 AND range.AuthorizationNumber=existingAuthorization.AuthorizationNumber
                 AND range.Prefix=series.Prefix AND range.RangeStart=series.RangeStart
                 AND range.RangeEnd=series.RangeEnd
                WHERE existingAuthorization.DianNumberingRangeId IS NULL
                   OR NOT EXISTS(
                       SELECT 1 FROM fiscal.DianNumberingRanges currentRange
                       WHERE currentRange.DianNumberingRangeId=existingAuthorization.DianNumberingRangeId
                         AND currentRange.DocumentPurpose=N'SupportDocument');

                UPDATE range
                SET AssignedBusinessId=existingAuthorization.BusinessId,AssignedAt=@Now,
                    AssignedByUserId=COALESCE(range.AssignedByUserId,@UserId)
                FROM fiscal.DianNumberingRanges range
                JOIN dbo.FiscalAuthorizations existingAuthorization
                  ON existingAuthorization.DianNumberingRangeId=range.DianNumberingRangeId
                WHERE range.TenantId=@TenantId AND range.DocumentPurpose=N'SupportDocument'
                  AND range.AssignedBusinessId IS NULL;
            END;
            """;
        var payload = ranges.Select(range => new
        {
            id = ids.NewId(),
            authorizationNumber = range.AuthorizationNumber.Trim(),
            resolutionDate = range.ResolutionDate?.ToString("yyyy-MM-dd"),
            prefix = range.Prefix.Trim().ToUpperInvariant(),
            rangeStart = range.RangeStart,
            rangeEnd = range.RangeEnd,
            validFrom = range.ValidFrom.ToString("yyyy-MM-dd"),
            validUntil = range.ValidUntil.ToString("yyyy-MM-dd"),
            technicalKeyHex = "0x" + Convert.ToHexString(Protect(range.TechnicalKey.Trim()))
        });
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@UserId", userId);
        Add(command, "@Purpose", documentPurpose);
        Add(command, "@Ranges", JsonSerializer.Serialize(payload));
        Add(command, "@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AssignOnlineResolutionAsync(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        Guid dianNumberingRangeId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON;
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;
            IF NOT EXISTS(
                SELECT 1 FROM dbo.FiscalDocumentProcesses fp
                JOIN dbo.FiscalIssuerConfigurations hi ON hi.FiscalIssuerConfigurationId=fp.FiscalIssuerConfigurationId
                JOIN dbo.FiscalTransmissionAttempts attempt
                  ON attempt.DocumentId=fp.DocumentId
                 AND attempt.Operation=N'GetStatusZip'
                 AND attempt.Disposition=N'Accepted'
                 AND attempt.StatusCode=N'2'
                JOIN dbo.FiscalDocuments document ON document.DocumentId=fp.DocumentId
                WHERE fp.BusinessId=@BusinessId
                  AND document.FiscalDocumentType=N'SalesInvoice'
                  AND (fp.TestSetId IS NOT NULL OR hi.Environment=2))
                THROW 51022,'La DIAN todavía no ha aceptado el set de pruebas del negocio.',1;

            DECLARE @AuthorizationNumber nvarchar(64),@Prefix nvarchar(16),@RangeStart bigint,
                    @RangeEnd bigint,@ValidFrom date,@ValidUntil date,@ProtectedTechnicalKey varbinary(max),
                    @SupplierTaxId nvarchar(32),@AuthorizationId uniqueidentifier=@NewAuthorizationId;
            SELECT @AuthorizationNumber=AuthorizationNumber,@Prefix=Prefix,@RangeStart=RangeStart,
                   @RangeEnd=RangeEnd,@ValidFrom=ValidFrom,@ValidUntil=ValidUntil,
                   @ProtectedTechnicalKey=ProtectedTechnicalKey
            FROM fiscal.DianNumberingRanges WITH(UPDLOCK,HOLDLOCK)
            WHERE DianNumberingRangeId=@RangeId AND TenantId=@TenantId
              AND DocumentPurpose=N'SalesInvoice'
              AND AssignedBusinessId IS NULL
              AND ValidFrom<=CONVERT(date,@Now)
              AND ValidUntil>=CONVERT(date,@Now);
            IF @AuthorizationNumber IS NULL
                THROW 51022,'La resolución ya fue asignada a otra sede, venció o no existe.',1;
            UPDATE fiscal.DianNumberingRanges
            SET AssignedBusinessId=@BusinessId,AssignedAt=@Now,
                AssignedByUserId=@UserId
            WHERE DianNumberingRangeId=@RangeId AND AssignedBusinessId IS NULL;
            IF @@ROWCOUNT<>1 THROW 51022,'La resolución fue asignada simultáneamente a otra sede.',1;

            SELECT TOP(1) @SupplierTaxId=SupplierTaxId
            FROM dbo.FiscalIssuerConfigurations configuration
            JOIN dbo.Businesses configuredBusiness
              ON configuredBusiness.BusinessId=configuration.BusinessId
            WHERE configuredBusiness.TenantId=@TenantId AND configuration.Environment IN(1,2)
              AND configuration.IsActive=1
              AND configuration.ValidFrom<=@Now
              AND (configuration.ValidTo IS NULL OR configuration.ValidTo>@Now)
            ORDER BY CASE WHEN configuration.BusinessId=@BusinessId AND configuration.Environment=1 THEN 0 ELSE 1 END,
                     configuration.CreatedAt DESC,Version DESC;
            IF @SupplierTaxId IS NULL
                THROW 51022,'El certificado o la configuración de habilitación ya no están vigentes.',1;

            DECLARE @PreviousAuthorizations TABLE(FiscalAuthorizationId uniqueidentifier PRIMARY KEY);
            UPDATE series SET IsActive=0
            OUTPUT deleted.FiscalAuthorizationId INTO @PreviousAuthorizations
            FROM dbo.FiscalSeries series
            WHERE series.BusinessId=@BusinessId AND series.DeviceId IS NULL
              AND series.EmitterKind=N'Server' AND series.DocumentType=N'SalesInvoice'
              AND series.IsActive=1;
            UPDATE existingAuthorization SET IsActive=0
            FROM dbo.FiscalAuthorizations existingAuthorization
            JOIN @PreviousAuthorizations previous
              ON previous.FiscalAuthorizationId=existingAuthorization.FiscalAuthorizationId
            WHERE NOT EXISTS(SELECT 1 FROM dbo.FiscalSeries activeSeries
                             WHERE activeSeries.FiscalAuthorizationId=existingAuthorization.FiscalAuthorizationId
                               AND activeSeries.IsActive=1);

            INSERT dbo.FiscalAuthorizations(
                FiscalAuthorizationId,BusinessId,DianNumberingRangeId,AuthorizationNumber,SupplierTaxId,Environment,
                QrValidationUrl,TechnicalKeyVersion,ValidFrom,ValidUntil,AuthorizedRangeStart,
                AuthorizedRangeEnd,IsActive,CreatedAt)
            VALUES(@AuthorizationId,@BusinessId,@RangeId,@AuthorizationNumber,@SupplierTaxId,1,
                   @QrUrl,@TechnicalKeyVersion,@ValidFrom,@ValidUntil,@RangeStart,@RangeEnd,
                   1,@Now);
            INSERT dbo.FiscalTechnicalKeySecrets(
                FiscalTechnicalKeySecretId,BusinessId,FiscalAuthorizationId,TechnicalKeyVersion,
                Environment,ProtectedValue,CreatedAt,UpdatedAt)
            VALUES(@TechnicalKeySecretId,@BusinessId,@AuthorizationId,@TechnicalKeyVersion,
                   1,@ProtectedTechnicalKey,@Now,@Now);

            INSERT dbo.FiscalSeries(SeriesId,BusinessId,DeviceId,EmitterKind,FiscalAuthorizationId,
                DocumentType,Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
            VALUES(@OnlineSeriesId,@BusinessId,NULL,N'Server',@AuthorizationId,
                   N'SalesInvoice',@Prefix,@RangeStart,@RangeEnd,1,@Now);
            INSERT dbo.FiscalSeriesCursors(SeriesId,NextConsecutive,UpdatedAt)
            VALUES(@OnlineSeriesId,@RangeStart,@Now);
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection, transaction);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        Add(command, "@UserId", userId);
        Add(command, "@RangeId", dianNumberingRangeId);
        Add(command, "@NewAuthorizationId", ids.NewId());
        Add(command, "@TechnicalKeySecretId", ids.NewId());
        Add(command, "@OnlineSeriesId", ids.NewId());
        Add(command, "@QrUrl", QrValidationUrl);
        Add(command, "@TechnicalKeyVersion", TechnicalKeyVersion);
        Add(command, "@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ActivateProductionAsync(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON;
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;
            IF EXISTS(SELECT 1 FROM dbo.FiscalIssuerConfigurations
                      WHERE BusinessId=@BusinessId AND IsActive=1 AND Environment=1)
                RETURN;
            IF NOT EXISTS(
                SELECT 1 FROM dbo.FiscalDocumentProcesses fp
                JOIN dbo.FiscalIssuerConfigurations hi ON hi.FiscalIssuerConfigurationId=fp.FiscalIssuerConfigurationId
                JOIN dbo.FiscalTransmissionAttempts attempt ON attempt.DocumentId=fp.DocumentId
                 AND attempt.Operation=N'GetStatusZip' AND attempt.Disposition=N'Accepted' AND attempt.StatusCode=N'2'
                JOIN dbo.Businesses processBusiness ON processBusiness.BusinessId=fp.BusinessId
                WHERE processBusiness.TenantId=@TenantId AND hi.Environment=2)
                THROW 51022,'La DIAN todavía no ha aceptado el set de pruebas del tenant.',1;

            DECLARE @Version int=ISNULL((SELECT MAX(Version) FROM dbo.FiscalIssuerConfigurations WITH(UPDLOCK,HOLDLOCK)
                                         WHERE BusinessId=@BusinessId),0)+1;
            INSERT dbo.FiscalIssuerConfigurations(
                FiscalIssuerConfigurationId,BusinessId,Version,SupplierTaxId,SupplierCheckDigit,
                LegalName,TradeName,TaxLevelCode,TaxSchemeId,TaxSchemeName,IdentificationTypeCode,
                AddressLine,CityCode,CityName,DepartmentCode,DepartmentName,PostalZone,
                CountryCode,CountryName,SoftwareIdentificationCode,SoftwarePinSecretReference,
                Environment,TestSetId,CertificateProvider,CertificateKeyReference,
                CertificateThumbprint,DianEndpoint,TechnicalAnnexVersion,GeneratorVersion,
                ValidFrom,ValidTo,IsActive,CreatedAt,CreatedByUserId)
            SELECT TOP(1) @NewIssuerId,@BusinessId,@Version,SupplierTaxId,SupplierCheckDigit,
                   LegalName,TradeName,TaxLevelCode,TaxSchemeId,TaxSchemeName,IdentificationTypeCode,
                   AddressLine,CityCode,CityName,DepartmentCode,DepartmentName,PostalZone,
                   CountryCode,CountryName,SoftwareIdentificationCode,SoftwarePinSecretReference,
                   1,NULL,CertificateProvider,CertificateKeyReference,CertificateThumbprint,
                   @ProductionEndpoint,TechnicalAnnexVersion,GeneratorVersion,ValidFrom,ValidTo,
                   0,@Now,@UserId
            FROM dbo.FiscalIssuerConfigurations configuration
            WHERE configuration.BusinessId=@BusinessId AND configuration.Environment=2
              AND configuration.IsActive=1 AND configuration.ValidFrom<=@Now
              AND (configuration.ValidTo IS NULL OR configuration.ValidTo>@Now)
            ORDER BY configuration.CreatedAt DESC,configuration.Version DESC;
            IF @@ROWCOUNT<>1 THROW 51022,'No existe una configuración de habilitación vigente para activar.',1;
            UPDATE dbo.FiscalIssuerConfigurations
            SET IsActive=0
            WHERE BusinessId=@BusinessId AND Environment=2 AND IsActive=1;
            UPDATE dbo.FiscalIssuerConfigurations
            SET IsActive=1
            WHERE FiscalIssuerConfigurationId=@NewIssuerId;
            UPDATE series
            SET IsActive=0
            FROM dbo.FiscalSeries series
            JOIN dbo.FiscalAuthorizations fiscalAuthorization
              ON fiscalAuthorization.FiscalAuthorizationId=series.FiscalAuthorizationId
            WHERE series.BusinessId=@BusinessId
              AND fiscalAuthorization.Environment=2
              AND series.IsActive=1;
            UPDATE dbo.FiscalAuthorizations
            SET IsActive=0
            WHERE BusinessId=@BusinessId AND Environment=2 AND IsActive=1;

            DECLARE @Cursor bigint;
            SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
            FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND Stream=N'FiscalProvisioning';
            INSERT dbo.PosSynchronizationOutboxMessages(
                NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
            VALUES(@NotificationId,@BusinessId,N'FiscalProvisioning',@Cursor,@Now);
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection, transaction);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        Add(command, "@UserId", userId);
        Add(command, "@NewIssuerId", ids.NewId());
        Add(command, "@NotificationId", ids.NewId());
        Add(command, "@ProductionEndpoint", ProductionEndpoint);
        Add(command, "@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveSupportDocumentSoftwareAsync(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        string softwareIdentificationCode,
        string softwarePinSecretReference,
        Guid testSetId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;
            UPDATE dbo.FiscalIssuerConfigurations
            SET SupportDocumentSoftwareIdentificationCode=@SoftwareId,
                SupportDocumentSoftwarePinSecretReference=@PinReference,
                SupportDocumentTestSetId=@TestSetId
            WHERE BusinessId=@BusinessId AND Environment=1 AND IsActive=1;
            IF @@ROWCOUNT<>1
                THROW 51022,'Activa primero la configuración DIAN de producción.',1;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        Add(command, "@SoftwareId", softwareIdentificationCode);
        Add(command, "@PinReference", softwarePinSecretReference);
        Add(command, "@TestSetId", testSetId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ActivateSupportDocumentAsync(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        Guid dianNumberingRangeId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON;
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.FiscalIssuerConfigurations
                          WHERE BusinessId=@BusinessId AND Environment=1 AND IsActive=1
                            AND SupportDocumentSoftwareIdentificationCode IS NOT NULL
                            AND SupportDocumentSoftwarePinSecretReference IS NOT NULL)
                THROW 51022,'Configura primero el Software ID y PIN de documento soporte.',1;

            DECLARE @AuthorizationNumber nvarchar(64),@ResolutionDate date,
                    @Prefix nvarchar(16),@RangeStart bigint,@RangeEnd bigint,
                    @ValidFrom date,@ValidUntil date,@SupplierTaxId nvarchar(32),
                    @AuthorizationId uniqueidentifier=@NewAuthorizationId;
            SELECT @AuthorizationNumber=AuthorizationNumber,@ResolutionDate=ResolutionDate,
                   @Prefix=Prefix,@RangeStart=RangeStart,@RangeEnd=RangeEnd,
                   @ValidFrom=ValidFrom,@ValidUntil=ValidUntil
            FROM fiscal.DianNumberingRanges WITH(UPDLOCK,HOLDLOCK)
            WHERE DianNumberingRangeId=@RangeId AND TenantId=@TenantId
              AND DocumentPurpose=N'SupportDocument'
              AND AssignedBusinessId IS NULL
              AND ValidFrom<=CONVERT(date,@Now) AND ValidUntil>=CONVERT(date,@Now);
            IF @AuthorizationNumber IS NULL
                THROW 51022,'La resolución de documento soporte ya fue asignada, venció o no existe.',1;

            SELECT @SupplierTaxId=SupplierTaxId
            FROM dbo.FiscalIssuerConfigurations
            WHERE BusinessId=@BusinessId AND Environment=1 AND IsActive=1;

            UPDATE fiscal.DianNumberingRanges
            SET AssignedBusinessId=@BusinessId,AssignedAt=@Now,AssignedByUserId=@UserId
            WHERE DianNumberingRangeId=@RangeId AND TenantId=@TenantId
              AND DocumentPurpose=N'SupportDocument' AND AssignedBusinessId IS NULL;
            IF @@ROWCOUNT<>1
                THROW 51022,'La resolución fue asignada simultáneamente a otra sede.',1;

            DECLARE @PreviousAuthorizations TABLE(FiscalAuthorizationId uniqueidentifier PRIMARY KEY);
            UPDATE series SET IsActive=0
            OUTPUT deleted.FiscalAuthorizationId INTO @PreviousAuthorizations
            FROM dbo.FiscalSeries series
            WHERE series.BusinessId=@BusinessId
              AND series.DocumentType=N'SupportDocument' AND series.IsActive=1;
            UPDATE existingAuthorization SET IsActive=0
            FROM dbo.FiscalAuthorizations existingAuthorization
            JOIN @PreviousAuthorizations previous
              ON previous.FiscalAuthorizationId=existingAuthorization.FiscalAuthorizationId
            WHERE NOT EXISTS(SELECT 1 FROM dbo.FiscalSeries activeSeries
                             WHERE activeSeries.FiscalAuthorizationId=existingAuthorization.FiscalAuthorizationId
                               AND activeSeries.IsActive=1);
            INSERT dbo.FiscalAuthorizations(
                FiscalAuthorizationId,BusinessId,DianNumberingRangeId,AuthorizationNumber,ResolutionDate,SupplierTaxId,Environment,
                QrValidationUrl,TechnicalKeyVersion,ValidFrom,ValidUntil,
                AuthorizedRangeStart,AuthorizedRangeEnd,IsActive,CreatedAt)
            VALUES(@AuthorizationId,@BusinessId,@RangeId,@AuthorizationNumber,@ResolutionDate,@SupplierTaxId,1,
                   @QrUrl,N'cuds-sha384',@ValidFrom,@ValidUntil,@RangeStart,@RangeEnd,1,@Now);
            INSERT dbo.FiscalSeries(
                SeriesId,BusinessId,DeviceId,EmitterKind,FiscalAuthorizationId,
                DocumentType,Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
            VALUES(@SeriesId,@BusinessId,NULL,N'Server',@AuthorizationId,
                   N'SupportDocument',@Prefix,@RangeStart,@RangeEnd,1,@Now);
            INSERT dbo.FiscalSeriesCursors(SeriesId,NextConsecutive,UpdatedAt)
            VALUES(@SeriesId,@RangeStart,@Now);
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection, transaction);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        Add(command, "@UserId", userId);
        Add(command, "@RangeId", dianNumberingRangeId);
        Add(command, "@NewAuthorizationId", ids.NewId());
        Add(command, "@SeriesId", ids.NewId());
        Add(command, "@QrUrl", QrValidationUrl);
        Add(command, "@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private byte[] Protect(string value)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(value);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(ProtectionKey(), tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return [.. nonce, .. tag, .. ciphertext];
    }

    private byte[] ProtectionKey()
    {
        try
        {
            var key = Convert.FromBase64String(
                configuration["Auraly:Fiscal:SecretProtectionKey"] ?? string.Empty);
            if (key.Length != 32) throw new FormatException();
            return key;
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Auraly:Fiscal:SecretProtectionKey must be a Base64-encoded 256-bit key.");
        }
    }

    private static string? Text(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateOnly? Date(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateOnly>(ordinal);

    private static void Add(SqlCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);

    private static void AddNullable(SqlCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
