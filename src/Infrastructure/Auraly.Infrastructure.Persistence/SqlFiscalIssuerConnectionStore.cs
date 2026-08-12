using Auraly.Application.Fiscal;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlFiscalIssuerConnectionStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids) : IFiscalIssuerConnectionStore
{
    public async Task<FiscalIssuerConnectionConfiguration> GetAsync(
        Guid tenantId, Guid businessId, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;
            SELECT TOP(1) FiscalIssuerConfigurationId,Version,SupplierTaxId,SupplierCheckDigit,
                LegalName,TradeName,TaxLevelCode,TaxSchemeId,TaxSchemeName,IdentificationTypeCode,
                AddressLine,CityCode,CityName,DepartmentCode,DepartmentName,PostalZone,
                SoftwareIdentificationCode,SoftwarePinSecretReference,Environment,TestSetId,
                CertificateProvider,CertificateKeyReference,CertificateThumbprint,DianEndpoint,
                TechnicalAnnexVersion,GeneratorVersion,ValidFrom,ValidTo
            FROM dbo.FiscalIssuerConfigurations
            WHERE BusinessId=@BusinessId AND IsActive=1
            ORDER BY Version DESC;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return Empty(businessId);
        var value = new FiscalIssuerConnectionConfiguration(
            businessId, reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), Text(reader, 5), reader.GetString(6), reader.GetString(7),
            reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11),
            reader.GetString(12), reader.GetString(13), reader.GetString(14), Text(reader, 15),
            reader.GetString(16), reader.GetString(17), reader.GetByte(18),
            reader.IsDBNull(19) ? null : reader.GetGuid(19), reader.GetString(20),
            reader.GetString(21), reader.GetString(22), reader.GetString(23), reader.GetString(24),
            reader.GetString(25), reader.GetDateTimeOffset(26),
            reader.IsDBNull(27) ? null : reader.GetDateTimeOffset(27), true, false, []);
        var missing = Missing(value);
        return value with
        {
            IsReadyForHabilitation = value.Environment == 2 && missing.Count == 0,
            MissingRequirements = missing
        };
    }

    public async Task<FiscalIssuerConnectionConfiguration> SaveAsync(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        SaveFiscalIssuerConnectionConfiguration request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON;
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;
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
            VALUES(
                @Id,@BusinessId,@Version,@SupplierTaxId,@SupplierCheckDigit,@LegalName,@TradeName,
                @TaxLevelCode,@TaxSchemeId,@TaxSchemeName,@IdentificationTypeCode,@AddressLine,
                @CityCode,@CityName,@DepartmentCode,@DepartmentName,@PostalZone,N'CO',N'Colombia',
                @SoftwareId,@PinReference,@Environment,@TestSetId,@CertificateProvider,
                @CertificateKeyReference,@CertificateThumbprint,@DianEndpoint,@AnnexVersion,
                @GeneratorVersion,@ValidFrom,@ValidTo,1,@Now,@UserId);
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection, transaction);
        Add(command, "@Id", ids.NewId());
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        Add(command, "@UserId", userId);
        Add(command, "@SupplierTaxId", request.SupplierTaxId.Trim());
        Add(command, "@SupplierCheckDigit", request.SupplierCheckDigit.Trim());
        Add(command, "@LegalName", request.LegalName.Trim());
        AddNullable(command, "@TradeName", request.TradeName);
        Add(command, "@TaxLevelCode", request.TaxLevelCode.Trim());
        Add(command, "@TaxSchemeId", request.TaxSchemeId.Trim());
        Add(command, "@TaxSchemeName", request.TaxSchemeName.Trim());
        Add(command, "@IdentificationTypeCode", request.IdentificationTypeCode.Trim());
        Add(command, "@AddressLine", request.AddressLine.Trim());
        Add(command, "@CityCode", request.CityCode.Trim());
        Add(command, "@CityName", request.CityName.Trim());
        Add(command, "@DepartmentCode", request.DepartmentCode.Trim());
        Add(command, "@DepartmentName", request.DepartmentName.Trim());
        AddNullable(command, "@PostalZone", request.PostalZone);
        Add(command, "@SoftwareId", request.SoftwareIdentificationCode.Trim());
        Add(command, "@PinReference", request.SoftwarePinSecretReference.Trim());
        Add(command, "@Environment", request.Environment);
        AddNullable(command, "@TestSetId", request.TestSetId);
        Add(command, "@CertificateProvider", "WindowsCertificateStore");
        Add(command, "@CertificateKeyReference", request.CertificateKeyReference);
        Add(command, "@CertificateThumbprint",
            request.CertificateThumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant());
        Add(command, "@DianEndpoint", request.DianEndpoint.Trim());
        Add(command, "@AnnexVersion", request.TechnicalAnnexVersion.Trim());
        Add(command, "@GeneratorVersion", request.GeneratorVersion.Trim());
        Add(command, "@ValidFrom", request.ValidFrom);
        AddNullable(command, "@ValidTo", request.ValidTo);
        Add(command, "@Now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(tenantId, businessId, cancellationToken);
    }

    private static IReadOnlyList<string> Missing(FiscalIssuerConnectionConfiguration value)
    {
        var missing = new List<string>();
        if (value.TestSetId is null) missing.Add("TestSetId");
        if (string.IsNullOrWhiteSpace(value.SoftwareIdentificationCode)) missing.Add("SoftwareIdentificationCode");
        if (string.IsNullOrWhiteSpace(value.SoftwarePinSecretReference)) missing.Add("SoftwarePinSecretReference");
        if (string.IsNullOrWhiteSpace(value.CertificateThumbprint)) missing.Add("CertificateThumbprint");
        if (string.IsNullOrWhiteSpace(value.DianEndpoint)) missing.Add("DianEndpoint");
        if (value.ValidTo is not null && value.ValidTo <= DateTimeOffset.UtcNow) missing.Add("IssuerConfigurationExpired");
        return missing;
    }

    private static FiscalIssuerConnectionConfiguration Empty(Guid businessId) =>
        new(
            BusinessId: businessId,
            FiscalIssuerConfigurationId: null,
            Version: null,
            SupplierTaxId: null,
            SupplierCheckDigit: null,
            LegalName: null,
            TradeName: null,
            TaxLevelCode: null,
            TaxSchemeId: null,
            TaxSchemeName: null,
            IdentificationTypeCode: null,
            AddressLine: null,
            CityCode: null,
            CityName: null,
            DepartmentCode: null,
            DepartmentName: null,
            PostalZone: null,
            SoftwareIdentificationCode: null,
            SoftwarePinSecretReference: null,
            Environment: null,
            TestSetId: null,
            CertificateProvider: null,
            CertificateKeyReference: null,
            CertificateThumbprint: null,
            DianEndpoint: null,
            TechnicalAnnexVersion: null,
            GeneratorVersion: null,
            ValidFrom: null,
            ValidTo: null,
            IsConfigured: false,
            IsReadyForHabilitation: false,
            MissingRequirements: ["FiscalIssuerConfiguration", "TestSetId",
                "SoftwareIdentificationCode", "SoftwarePinSecretReference",
                "CertificateThumbprint", "DianEndpoint"]);

    private static string? Text(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static void Add(SqlCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);

    private static void AddNullable(SqlCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
