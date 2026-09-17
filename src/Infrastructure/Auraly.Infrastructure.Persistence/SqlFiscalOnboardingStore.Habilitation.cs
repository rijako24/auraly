using System.Data;
using System.Security.Cryptography;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Application.Fiscal;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Core;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlFiscalOnboardingStore
{
    public async Task<(Guid DocumentId, bool IsReplay)> CreateHabilitationDocumentAsync(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        string family,
        CancellationToken cancellationToken)
    {
        var support = family == FiscalHabilitationFamilies.SupportDocument;
        var documentType = support
            ? FiscalDocumentTypeCodes.SupportDocument
            : FiscalDocumentTypeCodes.Invoice;
        var now = timeProvider.GetUtcNow();
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var replay = await FindActiveHabilitationAsync(
            connection, transaction, tenantId, businessId, documentType, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return (replay.Value, true);
        }

        if (!support)
            await EnsureInvoiceHabilitationSeriesAsync(
                connection, transaction, tenantId, businessId, now, cancellationToken);

        var context = await ReadHabilitationContextAsync(
            connection, transaction, tenantId, businessId, support, now, cancellationToken);
        var consecutive = await ConsumeFiscalConsecutiveAsync(
            connection, transaction, context.SeriesId, context.RangeEnd, now, cancellationToken);
        var fiscalNumber = $"{context.Prefix}{consecutive}";
        var documentId = ids.NewId();

        if (support)
        {
            var snapshot = BuildSupportSnapshot(
                tenantId, businessId, userId, documentId, fiscalNumber, now, context);
            await InsertFiscalRootAsync(
                connection, transaction, documentId, businessId, documentType,
                fiscalNumber, "CUDS", context.IssuerId, context.TestSetId, now,
                PurchaseSupportFiscalSnapshotSerializer.Serialize(snapshot), support: true,
                uniqueCode: null, qrPayload: null,
                cancellationToken);
        }
        else
        {
            var snapshot = BuildInvoiceSnapshot(
                tenantId, businessId, userId, documentId, fiscalNumber, consecutive, now, context);
            await InsertFiscalRootAsync(
                connection, transaction, documentId, businessId, documentType,
                fiscalNumber, "CUFE", context.IssuerId, context.TestSetId, now,
                PosSaleContractSerializer.Serialize(snapshot), support: false,
                snapshot.FiscalSnapshot!.Cufe, snapshot.FiscalSnapshot.QrPayload,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return (documentId, false);
    }

    private static async Task<Guid?> FindActiveHabilitationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        Guid businessId,
        string documentType,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS(
                SELECT 1 FROM dbo.Businesses WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;
            SELECT TOP(1) process.DocumentId
            FROM dbo.FiscalDocumentProcesses process WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.FiscalDocuments document ON document.DocumentId=process.DocumentId
            WHERE process.BusinessId=@BusinessId AND process.TestSetId IS NOT NULL
              AND document.FiscalDocumentType=@DocumentType
              AND process.Status NOT IN(N'DianAccepted',N'DianRejected',N'PermanentFailure',
                  N'SignatureFailed',N'SchemaValidationFailed',N'MissingMandatoryFiscalData')
            ORDER BY process.CreatedAt DESC,process.DocumentId DESC;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid value ? value : null;
    }

    private async Task EnsureInvoiceHabilitationSeriesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        Guid businessId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @IssuerId uniqueidentifier,@SupplierTaxId nvarchar(32),
                    @AuthorizationId uniqueidentifier,@SeriesId uniqueidentifier;
            SELECT TOP(1) @IssuerId=issuer.FiscalIssuerConfigurationId,
                          @SupplierTaxId=issuer.SupplierTaxId
            FROM dbo.FiscalIssuerConfigurations issuer WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Businesses business ON business.BusinessId=issuer.BusinessId
            WHERE issuer.BusinessId=@BusinessId AND business.TenantId=@TenantId
              AND issuer.IsActive=1 AND issuer.TestSetId IS NOT NULL
              AND issuer.ValidFrom<=@Now AND (issuer.ValidTo IS NULL OR issuer.ValidTo>@Now)
            ORDER BY issuer.Version DESC;
            IF @IssuerId IS NULL
                THROW 51022,'La configuración de habilitación de factura no está completa o vigente.',1;

            SELECT @AuthorizationId=FiscalAuthorizationId
            FROM dbo.FiscalAuthorizations WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND AuthorizationNumber=@AuthorizationNumber
              AND Environment=2;
            IF @AuthorizationId IS NULL
            BEGIN
                SET @AuthorizationId=@NewAuthorizationId;
                INSERT dbo.FiscalAuthorizations(
                    FiscalAuthorizationId,BusinessId,AuthorizationNumber,SupplierTaxId,
                    Environment,QrValidationUrl,TechnicalKeyVersion,ValidFrom,ValidUntil,
                    AuthorizedRangeStart,AuthorizedRangeEnd,IsActive,CreatedAt)
                VALUES(@AuthorizationId,@BusinessId,@AuthorizationNumber,@SupplierTaxId,
                    2,@QrUrl,@TechnicalKeyVersion,@ValidFrom,@ValidUntil,
                    @RangeStart,@RangeEnd,1,@Now);
            END
            ELSE UPDATE dbo.FiscalAuthorizations SET IsActive=1
                 WHERE FiscalAuthorizationId=@AuthorizationId;

            SELECT @SeriesId=SeriesId
            FROM dbo.FiscalSeries WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND DeviceId IS NULL AND EmitterKind=N'Server'
              AND FiscalAuthorizationId=@AuthorizationId AND DocumentType=N'SalesInvoice'
              AND Prefix=@Prefix;
            IF @SeriesId IS NULL
            BEGIN
                SET @SeriesId=@NewSeriesId;
                INSERT dbo.FiscalSeries(
                    SeriesId,BusinessId,DeviceId,EmitterKind,FiscalAuthorizationId,
                    DocumentType,Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
                VALUES(@SeriesId,@BusinessId,NULL,N'Server',@AuthorizationId,
                    N'SalesInvoice',@Prefix,@RangeStart,@RangeEnd,1,@Now);
            END
            ELSE UPDATE dbo.FiscalSeries SET IsActive=1 WHERE SeriesId=@SeriesId;
            IF NOT EXISTS(SELECT 1 FROM dbo.FiscalSeriesCursors WITH(UPDLOCK,HOLDLOCK)
                          WHERE SeriesId=@SeriesId)
                INSERT dbo.FiscalSeriesCursors(SeriesId,NextConsecutive,UpdatedAt)
                VALUES(@SeriesId,@RangeStart,@Now);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@NewAuthorizationId", ids.NewId());
        command.Parameters.AddWithValue("@NewSeriesId", ids.NewId());
        command.Parameters.AddWithValue("@AuthorizationNumber", DianFiscalDefaults.HabilitationAuthorizationNumber);
        command.Parameters.AddWithValue("@Prefix", DianFiscalDefaults.HabilitationPrefix);
        command.Parameters.AddWithValue("@RangeStart", DianFiscalDefaults.HabilitationRangeStart);
        command.Parameters.AddWithValue("@RangeEnd", DianFiscalDefaults.HabilitationRangeEnd);
        command.Parameters.AddWithValue("@TechnicalKeyVersion", DianFiscalDefaults.HabilitationTechnicalKeyVersion);
        command.Parameters.AddWithValue("@QrUrl", DianFiscalDefaults.HabilitationQrValidationUrl);
        command.Parameters.AddWithValue("@ValidFrom", new DateOnly(2019, 1, 19));
        command.Parameters.AddWithValue("@ValidUntil", new DateOnly(2030, 1, 19));
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HabilitationContext> ReadHabilitationContextAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        Guid businessId,
        bool support,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP(1)
              issuer.FiscalIssuerConfigurationId,
              CASE WHEN @Support=1 THEN issuer.SupportDocumentTestSetId ELSE issuer.TestSetId END,
              issuer.SupplierTaxId,issuer.SupplierCheckDigit,issuer.IdentificationTypeCode,
              issuer.LegalName,COALESCE(issuer.TradeName,issuer.LegalName),
              issuer.TaxLevelCode,issuer.TaxSchemeId,issuer.TaxSchemeName,
              issuer.AddressLine,issuer.CityCode,issuer.CityName,
              issuer.DepartmentCode,issuer.DepartmentName,issuer.CountryCode,issuer.CountryName,
              CASE WHEN @Support=1 THEN issuer.SupportDocumentSoftwareIdentificationCode
                   ELSE issuer.SoftwareIdentificationCode END,
              profile.EntityType,profile.Email,profile.Phone,
              fiscalAuthorization.FiscalAuthorizationId,fiscalAuthorization.AuthorizationNumber,
              fiscalAuthorization.ValidFrom,fiscalAuthorization.ValidUntil,
              series.SeriesId,series.Prefix,series.RangeStart,series.RangeEnd
            FROM dbo.FiscalIssuerConfigurations issuer WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Businesses business ON business.BusinessId=issuer.BusinessId
            JOIN dbo.TenantLegalProfiles profile ON profile.TenantId=business.TenantId
            JOIN dbo.FiscalAuthorizations fiscalAuthorization
              ON fiscalAuthorization.BusinessId=issuer.BusinessId AND fiscalAuthorization.IsActive=1
             AND fiscalAuthorization.Environment=CASE WHEN @Support=1 THEN 1 ELSE 2 END
            JOIN dbo.FiscalSeries series
              ON series.FiscalAuthorizationId=fiscalAuthorization.FiscalAuthorizationId
             AND series.BusinessId=issuer.BusinessId AND series.DeviceId IS NULL
             AND series.EmitterKind=N'Server' AND series.IsActive=1
             AND series.DocumentType=CASE WHEN @Support=1 THEN N'SupportDocument' ELSE N'SalesInvoice' END
            WHERE issuer.BusinessId=@BusinessId AND business.TenantId=@TenantId
              AND issuer.IsActive=1 AND issuer.ValidFrom<=@Now
              AND (issuer.ValidTo IS NULL OR issuer.ValidTo>@Now)
              AND CASE WHEN @Support=1 THEN issuer.SupportDocumentTestSetId ELSE issuer.TestSetId END IS NOT NULL
              AND CASE WHEN @Support=1 THEN issuer.SupportDocumentSoftwareIdentificationCode
                       ELSE issuer.SoftwareIdentificationCode END IS NOT NULL
            ORDER BY issuer.Version DESC,series.CreatedAt DESC;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Support", support);
        command.Parameters.AddWithValue("@Now", now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new FiscalConfigurationValidationException(support
                ? "Documento soporte requiere credenciales, TestSetId y una resolución activa."
                : "La configuración de habilitación de factura no está completa.");
        return new HabilitationContext(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
            reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11),
            reader.GetString(12), reader.GetString(13), reader.GetString(14), reader.GetString(15),
            reader.GetString(16), reader.GetString(17), reader.GetString(18),
            reader.IsDBNull(19) ? null : reader.GetString(19),
            reader.IsDBNull(20) ? null : reader.GetString(20),
            reader.GetGuid(21), reader.GetString(22), reader.GetFieldValue<DateOnly>(23),
            reader.GetFieldValue<DateOnly>(24), reader.GetGuid(25), reader.GetString(26),
            reader.GetInt64(27), reader.GetInt64(28));
    }

    private static async Task<long> ConsumeFiscalConsecutiveAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid seriesId,
        long rangeEnd,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.FiscalSeriesCursors WITH(UPDLOCK,HOLDLOCK)
            SET NextConsecutive=NextConsecutive+1,UpdatedAt=@Now
            OUTPUT deleted.NextConsecutive
            WHERE SeriesId=@SeriesId AND NextConsecutive<=@RangeEnd;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@SeriesId", seriesId);
        command.Parameters.AddWithValue("@RangeEnd", rangeEnd);
        command.Parameters.AddWithValue("@Now", now);
        return await command.ExecuteScalarAsync(cancellationToken) is long value
            ? value
            : throw new FiscalConfigurationValidationException(
                "La numeración fiscal disponible para la prueba se agotó.");
    }

    private static PosSaleUploadRequest BuildInvoiceSnapshot(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        Guid documentId,
        string fiscalNumber,
        long consecutive,
        DateTimeOffset issuedAt,
        HabilitationContext context)
    {
        const decimal untaxed = 10000m;
        const decimal tax = 1900m;
        const decimal total = 11900m;
        var taxes = new[] { new PosSaleTaxContract("01", tax) };
        var cufe = CufeCalculator.Calculate(new CufeInput(
            fiscalNumber, issuedAt, untaxed, total, context.SupplierTaxId,
            "222222222222", new FiscalTechnicalKey(
                DianFiscalDefaults.HabilitationTechnicalKey,
                DianFiscalDefaults.HabilitationTechnicalKeyVersion),
            FiscalEnvironment.Test, [new FiscalTaxAmount("01", tax)]),
            DianFiscalDefaults.HabilitationQrValidationUrl);
        var supplier = context.SupplierParty();
        var customer = new PosSaleUblPartyContract(
            "222222222222", "0", "13", "2", "Consumidor final", "Consumidor final",
            "R-99-PN", "ZZ", "No aplica", supplier.Address);
        var authorization = context.Authorization();
        return new PosSaleUploadRequest(
            tenantId, businessId, Guid.Empty, Guid.Empty, Guid.Empty, userId, documentId,
            new PosSaleDocumentNumberContract(
                context.SeriesId, PosSaleDocumentTypes.Invoice, "HAB", "FISCAL", consecutive,
                0, $"HAB-{fiscalNumber}"),
            new PosSaleCommercialSnapshotContract(
                PosSaleDocumentTypes.Invoice, issuedAt, customer.Identification,
                taxes, untaxed, tax, total),
            new PosSaleFiscalSnapshotContract(
                context.SeriesId, context.AuthorizationId, context.AuthorizationNumber,
                PosSaleDocumentTypes.Invoice, fiscalNumber, context.Prefix, consecutive,
                issuedAt, context.SupplierTaxId, customer.Identification, 2,
                DianFiscalDefaults.HabilitationTechnicalKeyVersion, taxes,
                untaxed, tax, total, cufe.Cufe, cufe.QrPayload),
            [new PosSaleLineContract(
                1, Guid.Empty, "Prueba técnica de habilitación DIAN", "01", 1m,
                untaxed, 0m, tax, untaxed, total, 19m, 0m,
                IsGenericProductSnapshot: true, ProductCodeSnapshot: "HAB-DIAN")],
            [],
            new PosSaleUblSnapshotContract(
                context.IssuerId, "COP", "01", supplier, customer, authorization,
                context.SoftwareId,
                [new PosSaleUblLineContract(1, "HAB-DIAN", "999", "EA", "IVA", 19m)],
                "1", "ZZZ", DateOnly.FromDateTime(issuedAt.Date), null),
            SourceMode: SaleSourceModes.Online,
            CustomerPartySiteId: null);
    }

    private static PurchaseSupportFiscalSnapshot BuildSupportSnapshot(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        Guid documentId,
        string fiscalNumber,
        DateTimeOffset issuedAt,
        HabilitationContext context)
    {
        const decimal untaxed = 10000m;
        const decimal tax = 1900m;
        const decimal total = 11900m;
        var expense = new ExpenseDocumentPayload(
            tenantId, businessId, documentId, Guid.Empty, Guid.Empty, Guid.Empty, null,
            userId, $"HAB-{fiscalNumber}", Guid.Empty, "HAB", "FISCAL", 1,
            $"TEST-{fiscalNumber}", issuedAt, issuedAt, "COP",
            "Prueba técnica de habilitación DIAN", untaxed, tax, total, null,
            new WithholdingCalculationSnapshot(total, 0m, total, []));
        var seller = new PosSaleUblPartyContract(
            "900999999", "0", "41", "2", "Proveedor prueba habilitación",
            "Proveedor prueba habilitación", "R-99-PN", "ZZ", "No aplica",
            context.SupplierParty().Address);
        return new PurchaseSupportFiscalSnapshot(
            null, context.IssuerId, fiscalNumber, 2,
            DianFiscalDefaults.HabilitationQrValidationUrl, seller,
            context.Authorization(),
            [new PurchaseSupportLineMetadata(1, "HAB-DIAN", "999", "EA", "IVA", "01")],
            SellerOriginCode: "10", Expense: expense, SellerPostalZone: "000000");
    }

    private static async Task InsertFiscalRootAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid documentId,
        Guid businessId,
        string documentType,
        string fiscalNumber,
        string uniqueCodeType,
        Guid issuerId,
        Guid testSetId,
        DateTimeOffset now,
        string snapshotJson,
        bool support,
        string? uniqueCode,
        string? qrPayload,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.FiscalDocuments(
              DocumentId,BusinessId,SourceDocumentType,FiscalDocumentType,
              AuralyDocumentNumber,FiscalNumber,UniqueCodeType,IssuedAt,FiscalStatus,
              CreatedAt,UpdatedAt)
            VALUES(@DocumentId,@BusinessId,N'FiscalHabilitation',@DocumentType,
              @AuralyNumber,@FiscalNumber,@UniqueCodeType,@Now,N'PendingGeneration',@Now,@Now);
            IF @Support=1
              INSERT fiscal.PurchaseSupportFiscalSnapshots(
                DocumentId,SnapshotJson,Environment,CreatedAt)
              VALUES(@DocumentId,@SnapshotJson,2,@Now);
            ELSE
              INSERT dbo.FiscalSnapshots(
                DocumentId,SnapshotJson,PayloadHash,TechnicalKeyVersion,Environment,
                CufeReceived,CufeCalculated,QrPayload,IntegrityStatus,VerifiedAt,CreatedAt)
              VALUES(@DocumentId,@SnapshotJson,@PayloadHash,@TechnicalKeyVersion,2,
                @UniqueCode,NULL,@QrPayload,N'FiscalHabilitation',@Now,@Now);
            INSERT dbo.FiscalDocumentProcesses(
              DocumentId,BusinessId,FiscalIssuerConfigurationId,TestSetId,Status,
              NextAttemptAt,CreatedAt,UpdatedAt)
            VALUES(@DocumentId,@BusinessId,@IssuerId,@TestSetId,N'PendingGeneration',
              @Now,@Now,@Now);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.AddWithValue("@AuralyNumber", $"HAB-{fiscalNumber}");
        command.Parameters.AddWithValue("@FiscalNumber", fiscalNumber);
        command.Parameters.AddWithValue("@UniqueCodeType", uniqueCodeType);
        command.Parameters.AddWithValue("@IssuerId", issuerId);
        command.Parameters.AddWithValue("@TestSetId", testSetId);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@SnapshotJson", snapshotJson);
        command.Parameters.AddWithValue("@PayloadHash", SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(snapshotJson)));
        command.Parameters.AddWithValue("@TechnicalKeyVersion", DianFiscalDefaults.HabilitationTechnicalKeyVersion);
        command.Parameters.AddWithValue("@Support", support);
        command.Parameters.AddWithValue("@UniqueCode", (object?)uniqueCode ?? string.Empty);
        command.Parameters.AddWithValue("@QrPayload", (object?)qrPayload ?? string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record HabilitationContext(
        Guid IssuerId,
        Guid TestSetId,
        string SupplierTaxId,
        string SupplierCheckDigit,
        string IdentificationTypeCode,
        string LegalName,
        string TradeName,
        string TaxLevelCode,
        string TaxSchemeId,
        string TaxSchemeName,
        string AddressLine,
        string CityCode,
        string CityName,
        string DepartmentCode,
        string DepartmentName,
        string CountryCode,
        string CountryName,
        string SoftwareId,
        string EntityType,
        string? Email,
        string? Phone,
        Guid AuthorizationId,
        string AuthorizationNumber,
        DateOnly ValidFrom,
        DateOnly ValidUntil,
        Guid SeriesId,
        string Prefix,
        long RangeStart,
        long RangeEnd)
    {
        public PosSaleUblAddressContract Address() => new(
            CityCode, CityName, DepartmentName, DepartmentCode, AddressLine,
            CountryCode, CountryName);

        public PosSaleUblPartyContract SupplierParty() => new(
            SupplierTaxId, SupplierCheckDigit, IdentificationTypeCode,
            EntityType == "NaturalPerson" ? "2" : "1", LegalName, TradeName,
            TaxLevelCode, TaxSchemeId, TaxSchemeName, Address(), Email, Phone);

        public PosSaleUblAuthorizationContract Authorization() => new(
            AuthorizationNumber, ValidFrom, ValidUntil, Prefix, RangeStart, RangeEnd);
    }
}
