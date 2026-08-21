using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Fiscal;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlFiscalOnboardingStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    IConfiguration configuration,
    TimeProvider timeProvider) : IFiscalOnboardingStore
{
    private const string HabilitationEndpoint =
        "https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc";
    private const string ProductionEndpoint =
        "https://vpfe.dian.gov.co/WcfDianCustomerServices.svc";
    private const string QrValidationUrl =
        "https://catalogo-vpfe.dian.gov.co/document/searchqr";
    private const string TechnicalKeyVersion = "dian-get-numbering-range";

    public async Task<FiscalOnboardingConfiguration> GetAsync(
        Guid tenantId, Guid businessId, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;

            SELECT b.Name,p.LegalName,p.Nit,p.VerificationDigit,
                   i.SoftwareIdentificationCode,i.TestSetId,i.CertificateThumbprint,
                   i.ValidFrom,i.ValidTo,i.Environment,
                   accepted.AcceptedAt,
                   assigned.DianNumberingRangeId,assigned.AuthorizationNumber,
                   assigned.ResolutionDate,assigned.Prefix,assigned.RangeStart,assigned.RangeEnd,
                   assigned.ValidFrom,assigned.ValidUntil
            FROM dbo.Businesses b
            JOIN dbo.TenantLegalProfiles p ON p.TenantId=b.TenantId
            OUTER APPLY(
                SELECT TOP(1) SoftwareIdentificationCode,TestSetId,CertificateThumbprint,
                       ValidFrom,ValidTo,Environment
                FROM dbo.FiscalIssuerConfigurations
                WHERE BusinessId=b.BusinessId AND IsActive=1
                ORDER BY Version DESC) i
            OUTER APPLY(
                SELECT MAX(attempt.CompletedAt) AcceptedAt
                FROM dbo.FiscalDocumentProcesses fp
                JOIN dbo.FiscalIssuerConfigurations hi
                  ON hi.FiscalIssuerConfigurationId=fp.FiscalIssuerConfigurationId
                JOIN dbo.FiscalTransmissionAttempts attempt
                  ON attempt.DocumentId=fp.DocumentId
                 AND attempt.Operation=N'GetStatusZip'
                 AND attempt.Disposition=N'Accepted'
                 AND attempt.StatusCode=N'2'
                WHERE fp.BusinessId=b.BusinessId AND hi.Environment=2) accepted
            OUTER APPLY(
                SELECT TOP(1) r.DianNumberingRangeId,r.AuthorizationNumber,r.ResolutionDate,
                       r.Prefix,r.RangeStart,r.RangeEnd,r.ValidFrom,r.ValidUntil
                FROM fiscal.DianNumberingRanges r
                JOIN dbo.FiscalAuthorizations a
                  ON a.BusinessId=b.BusinessId AND a.AuthorizationNumber=r.AuthorizationNumber
                 AND a.Environment=1 AND a.IsActive=1
                WHERE r.TenantId=b.TenantId AND r.AssignedBusinessId=b.BusinessId
                ORDER BY r.AssignedAt DESC) assigned
            WHERE b.BusinessId=@BusinessId;

            SELECT r.DianNumberingRangeId,r.AuthorizationNumber,r.ResolutionDate,r.Prefix,
                   r.RangeStart,r.RangeEnd,r.ValidFrom,r.ValidUntil,r.AssignedBusinessId,b.Name
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

        var ranges = new List<DianNumberingRangeOption>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Guid? assignedBusinessId = reader.IsDBNull(8) ? null : reader.GetGuid(8);
            ranges.Add(new DianNumberingRangeOption(
                reader.GetGuid(0), reader.GetString(1), Date(reader, 2), reader.GetString(3),
                reader.GetInt64(4), reader.GetInt64(5), reader.GetFieldValue<DateOnly>(6),
                reader.GetFieldValue<DateOnly>(7), assignedBusinessId is null,
                assignedBusinessId, Text(reader, 9)));
        }

        var now = timeProvider.GetUtcNow();
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(softwareId)) missing.Add("SoftwareId");
        if (testSetId is null) missing.Add("TestSetId");
        if (string.IsNullOrWhiteSpace(thumbprint)) missing.Add("Certificado");
        if (certificateTo is not null && certificateTo <= now) missing.Add("CertificadoVencido");
        var productionActive = environment == 1 && assigned is not null;
        var stage = productionActive
            ? FiscalOnboardingStages.ProductionActive
            : acceptedAt is not null && ranges.Any(item => item.IsAvailable)
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
            productionActive, assigned, ranges, missing);
    }

    public async Task SaveHabilitationAsync(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        string softwareIdentificationCode,
        Guid testSetId,
        FiscalCredentialReference credentials,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON;
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;
            IF EXISTS(SELECT 1 FROM dbo.FiscalIssuerConfigurations WHERE BusinessId=@BusinessId AND IsActive=1 AND Environment=1)
                THROW 51022,'La configuración DIAN de producción ya está activa.',1;
            DECLARE @Version int=ISNULL((SELECT MAX(Version) FROM dbo.FiscalIssuerConfigurations WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId),0)+1;
            UPDATE dbo.FiscalIssuerConfigurations SET IsActive=0
            WHERE BusinessId=@BusinessId AND IsActive=1;
            INSERT dbo.FiscalIssuerConfigurations(
                FiscalIssuerConfigurationId,BusinessId,Version,SupplierTaxId,SupplierCheckDigit,
                LegalName,TradeName,TaxLevelCode,TaxSchemeId,TaxSchemeName,IdentificationTypeCode,
                AddressLine,CityCode,CityName,DepartmentCode,DepartmentName,PostalZone,
                CountryCode,CountryName,SoftwareIdentificationCode,SoftwarePinSecretReference,
                Environment,TestSetId,CertificateProvider,CertificateKeyReference,
                CertificateThumbprint,DianEndpoint,TechnicalAnnexVersion,GeneratorVersion,
                ValidFrom,ValidTo,IsActive,CreatedAt,CreatedByUserId)
            SELECT @Id,@BusinessId,@Version,p.Nit,p.VerificationDigit,p.LegalName,
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
        Guid tenantId, Guid businessId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP(1) i.SupplierTaxId,i.SoftwareIdentificationCode,
                   i.CertificateProvider,i.CertificateKeyReference,i.CertificateThumbprint
            FROM dbo.FiscalIssuerConfigurations i
            JOIN dbo.Businesses b ON b.BusinessId=i.BusinessId
            WHERE b.TenantId=@TenantId AND i.BusinessId=@BusinessId AND i.Environment=2
            ORDER BY i.Version DESC;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new FiscalConfigurationValidationException("No existe una configuración DIAN de habilitación.");
        return new DianNumberingRangeContext(
            businessId, reader.GetString(0), reader.GetString(0), reader.GetString(1),
            new FiscalCertificateReference(
                businessId, reader.GetString(2), reader.GetString(3), reader.GetString(4)));
    }

    public async Task ImportNumberingRangesAsync(
        Guid tenantId,
        IReadOnlyList<ImportedDianNumberingRange> ranges,
        CancellationToken cancellationToken)
    {
        const string sql = """
            MERGE fiscal.DianNumberingRanges WITH(HOLDLOCK) AS target
            USING (SELECT @TenantId TenantId,@AuthorizationNumber AuthorizationNumber,
                          @Prefix Prefix,@RangeStart RangeStart,@RangeEnd RangeEnd) AS source
            ON target.TenantId=source.TenantId
              AND target.AuthorizationNumber=source.AuthorizationNumber
              AND target.Prefix=source.Prefix
              AND target.RangeStart=source.RangeStart
              AND target.RangeEnd=source.RangeEnd
            WHEN MATCHED AND target.AssignedBusinessId IS NULL THEN UPDATE SET
                ResolutionDate=@ResolutionDate,ValidFrom=@ValidFrom,ValidUntil=@ValidUntil,
                ProtectedTechnicalKey=@TechnicalKey,LastSeenAt=@Now
            WHEN NOT MATCHED THEN INSERT(
                DianNumberingRangeId,TenantId,AuthorizationNumber,ResolutionDate,Prefix,
                RangeStart,RangeEnd,ValidFrom,ValidUntil,ProtectedTechnicalKey,
                ImportedAt,LastSeenAt)
              VALUES(@Id,@TenantId,@AuthorizationNumber,@ResolutionDate,@Prefix,
                     @RangeStart,@RangeEnd,@ValidFrom,@ValidUntil,@TechnicalKey,@Now,@Now);
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var range in ranges)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            Add(command, "@Id", ids.NewId());
            Add(command, "@TenantId", tenantId);
            Add(command, "@AuthorizationNumber", range.AuthorizationNumber.Trim());
            AddNullable(command, "@ResolutionDate", range.ResolutionDate);
            Add(command, "@Prefix", range.Prefix.Trim().ToUpperInvariant());
            Add(command, "@RangeStart", range.RangeStart);
            Add(command, "@RangeEnd", range.RangeEnd);
            Add(command, "@ValidFrom", range.ValidFrom);
            Add(command, "@ValidUntil", range.ValidUntil);
            Add(command, "@TechnicalKey", Protect(range.TechnicalKey.Trim()));
            Add(command, "@Now", timeProvider.GetUtcNow());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ActivateProductionAsync(
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
            IF EXISTS(SELECT 1 FROM dbo.FiscalIssuerConfigurations WHERE BusinessId=@BusinessId AND IsActive=1 AND Environment=1)
                THROW 51022,'La producción DIAN ya está activa para esta sede.',1;
            IF NOT EXISTS(
                SELECT 1 FROM dbo.FiscalDocumentProcesses fp
                JOIN dbo.FiscalIssuerConfigurations hi ON hi.FiscalIssuerConfigurationId=fp.FiscalIssuerConfigurationId
                JOIN dbo.FiscalTransmissionAttempts attempt
                  ON attempt.DocumentId=fp.DocumentId
                 AND attempt.Operation=N'GetStatusZip'
                 AND attempt.Disposition=N'Accepted'
                 AND attempt.StatusCode=N'2'
                WHERE fp.BusinessId=@BusinessId AND hi.Environment=2)
                THROW 51022,'La DIAN todavía no ha aceptado el set de pruebas de esta sede.',1;

            DECLARE @AuthorizationNumber nvarchar(64),@Prefix nvarchar(16),@RangeStart bigint,
                    @RangeEnd bigint,@ValidFrom date,@ValidUntil date,@ProtectedTechnicalKey varbinary(max),
                    @SupplierTaxId nvarchar(32),@AuthorizationId uniqueidentifier=@NewAuthorizationId,
                    @Version int,@OnlineRangeEnd bigint,@OfflineRangeStart bigint;
            SELECT @AuthorizationNumber=AuthorizationNumber,@Prefix=Prefix,@RangeStart=RangeStart,
                   @RangeEnd=RangeEnd,@ValidFrom=ValidFrom,@ValidUntil=ValidUntil,
                   @ProtectedTechnicalKey=ProtectedTechnicalKey
            FROM fiscal.DianNumberingRanges WITH(UPDLOCK,HOLDLOCK)
            WHERE DianNumberingRangeId=@RangeId AND TenantId=@TenantId
              AND (AssignedBusinessId IS NULL OR AssignedBusinessId=@BusinessId)
              AND ValidUntil>=CONVERT(date,@Now);
            IF @AuthorizationNumber IS NULL
                THROW 51022,'La resolución ya fue asignada a otra sede, venció o no existe.',1;
            IF @RangeStart=@RangeEnd
                THROW 51022,'La resolución necesita al menos dos consecutivos para servidor y POS offline.',1;

            UPDATE fiscal.DianNumberingRanges
            SET AssignedBusinessId=@BusinessId,AssignedAt=COALESCE(AssignedAt,@Now),
                AssignedByUserId=COALESCE(AssignedByUserId,@UserId)
            WHERE DianNumberingRangeId=@RangeId AND (AssignedBusinessId IS NULL OR AssignedBusinessId=@BusinessId);
            IF @@ROWCOUNT<>1 THROW 51022,'La resolución fue asignada simultáneamente a otra sede.',1;

            SELECT TOP(1) @SupplierTaxId=SupplierTaxId
            FROM dbo.FiscalIssuerConfigurations
            WHERE BusinessId=@BusinessId AND Environment=2
              AND ValidFrom<=@Now AND (ValidTo IS NULL OR ValidTo>@Now)
            ORDER BY Version DESC;
            IF @SupplierTaxId IS NULL
                THROW 51022,'El certificado o la configuración de habilitación ya no están vigentes.',1;
            SET @Version=ISNULL((SELECT MAX(Version) FROM dbo.FiscalIssuerConfigurations WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId),0)+1;
            UPDATE dbo.FiscalIssuerConfigurations SET IsActive=0 WHERE BusinessId=@BusinessId AND IsActive=1;
            INSERT dbo.FiscalIssuerConfigurations(
                FiscalIssuerConfigurationId,BusinessId,Version,SupplierTaxId,SupplierCheckDigit,
                LegalName,TradeName,TaxLevelCode,TaxSchemeId,TaxSchemeName,IdentificationTypeCode,
                AddressLine,CityCode,CityName,DepartmentCode,DepartmentName,PostalZone,
                CountryCode,CountryName,SoftwareIdentificationCode,SoftwarePinSecretReference,
                Environment,TestSetId,CertificateProvider,CertificateKeyReference,
                CertificateThumbprint,DianEndpoint,TechnicalAnnexVersion,GeneratorVersion,
                ValidFrom,ValidTo,IsActive,CreatedAt,CreatedByUserId)
            SELECT @NewIssuerId,BusinessId,@Version,SupplierTaxId,SupplierCheckDigit,
                   LegalName,TradeName,TaxLevelCode,TaxSchemeId,TaxSchemeName,IdentificationTypeCode,
                   AddressLine,CityCode,CityName,DepartmentCode,DepartmentName,PostalZone,
                   CountryCode,CountryName,SoftwareIdentificationCode,SoftwarePinSecretReference,
                   1,NULL,CertificateProvider,CertificateKeyReference,CertificateThumbprint,
                   @ProductionEndpoint,TechnicalAnnexVersion,GeneratorVersion,ValidFrom,ValidTo,
                   1,@Now,@UserId
            FROM dbo.FiscalIssuerConfigurations
            WHERE BusinessId=@BusinessId AND Environment=2
              AND Version=(
                  SELECT MAX(Version)
                  FROM dbo.FiscalIssuerConfigurations
                  WHERE BusinessId=@BusinessId AND Environment=2)
              AND ValidFrom<=@Now AND (ValidTo IS NULL OR ValidTo>@Now);
            IF @@ROWCOUNT<>1 THROW 51022,'No existe una configuración de habilitación vigente para activar.',1;

            UPDATE dbo.FiscalAuthorizations SET IsActive=0 WHERE BusinessId=@BusinessId AND IsActive=1;
            INSERT dbo.FiscalAuthorizations(
                FiscalAuthorizationId,BusinessId,AuthorizationNumber,SupplierTaxId,Environment,
                QrValidationUrl,TechnicalKeyVersion,ValidFrom,ValidUntil,AuthorizedRangeStart,
                AuthorizedRangeEnd,IsActive,CreatedAt)
            VALUES(@AuthorizationId,@BusinessId,@AuthorizationNumber,@SupplierTaxId,1,
                   @QrUrl,@TechnicalKeyVersion,@ValidFrom,@ValidUntil,@RangeStart,@RangeEnd,
                   1,@Now);
            INSERT dbo.FiscalTechnicalKeySecrets(
                FiscalTechnicalKeySecretId,BusinessId,FiscalAuthorizationId,TechnicalKeyVersion,
                Environment,ProtectedValue,CreatedAt,UpdatedAt)
            VALUES(@TechnicalKeySecretId,@BusinessId,@AuthorizationId,@TechnicalKeyVersion,
                   1,@ProtectedTechnicalKey,@Now,@Now);

            UPDATE dbo.FiscalSeries SET IsActive=0
            WHERE BusinessId=@BusinessId AND DocumentType=N'SalesInvoice' AND IsActive=1;
            SET @OnlineRangeEnd=@RangeStart+((@RangeEnd-@RangeStart)/2);
            SET @OfflineRangeStart=@OnlineRangeEnd+1;
            INSERT dbo.FiscalSeries(SeriesId,BusinessId,DeviceId,EmitterKind,FiscalAuthorizationId,
                DocumentType,Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
            VALUES(@OnlineSeriesId,@BusinessId,NULL,N'Server',@AuthorizationId,
                   N'SalesInvoice',@Prefix,@RangeStart,@OnlineRangeEnd,1,@Now);
            INSERT dbo.FiscalSeriesCursors(SeriesId,NextConsecutive,UpdatedAt)
            VALUES(@OnlineSeriesId,@RangeStart,@Now);
            INSERT dbo.FiscalSeries(SeriesId,BusinessId,DeviceId,EmitterKind,FiscalAuthorizationId,
                DocumentType,Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
            VALUES(@OfflineSeriesId,@BusinessId,NULL,N'Device',@AuthorizationId,
                   N'SalesInvoice',@Prefix,@OfflineRangeStart,@RangeEnd,1,@Now);
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection, transaction);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        Add(command, "@UserId", userId);
        Add(command, "@RangeId", dianNumberingRangeId);
        Add(command, "@NewIssuerId", ids.NewId());
        Add(command, "@NewAuthorizationId", ids.NewId());
        Add(command, "@TechnicalKeySecretId", ids.NewId());
        Add(command, "@OnlineSeriesId", ids.NewId());
        Add(command, "@OfflineSeriesId", ids.NewId());
        Add(command, "@ProductionEndpoint", ProductionEndpoint);
        Add(command, "@QrUrl", QrValidationUrl);
        Add(command, "@TechnicalKeyVersion", TechnicalKeyVersion);
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
