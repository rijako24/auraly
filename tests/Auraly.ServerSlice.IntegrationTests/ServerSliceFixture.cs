using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Core;
using Auraly.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ServerSliceCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Auraly server slice";
}

public sealed class ServerSliceFixture : IAsyncLifetime
{
    public const string TechnicalKeyValue = "AURALY-TEST-TECHNICAL-KEY";
    public const string TechnicalKeyVersion = "test-v1";
    public const string SupplierTaxId = "9001234567";
    public const string AuthorizationNumber = "18760000099";
    public const string Prefix = "FV99";
    public const string DeviceSecret = "Auraly-allowed-device-secret";
    public const string DeniedDeviceSecret = "Auraly-denied-device-secret";
    public const string JwtIssuer = "Auraly.Tests";
    public const string JwtAudience = "Auraly.Api.Tests";
    public const string JwtSigningKey = "Auraly-Catalog-Integration-Tests-Key-2026";
    public const string QrValidationUrl =
        "https://catalogo-vpfe.dian.gov.co/document/searchqr";

    private WebApplicationFactory<Program>? _factory;
    private string? _databaseName;
    private readonly Dictionary<string, string?> _originalEnvironment = new(StringComparer.Ordinal);

    public Guid TenantId { get; } = Guid.NewGuid();
    public Guid BusinessId { get; } = Guid.NewGuid();
    public Guid LocationId { get; } = Guid.NewGuid();
    public Guid WarehouseId { get; } = Guid.NewGuid();
    public Guid RegisterId { get; } = Guid.NewGuid();
    public Guid DeviceId { get; } = Guid.NewGuid();
    public Guid DeniedDeviceId { get; } = Guid.NewGuid();
    public Guid UserId { get; } = Guid.NewGuid();
    public Guid PriceChannelId { get; } = Guid.NewGuid();
    public Guid TaxProfileId { get; } = Guid.NewGuid();
    public Guid ProductId { get; } = Guid.NewGuid();
    public Guid DocumentSeriesId { get; } = Guid.NewGuid();
    public Guid SeriesId { get; } = Guid.NewGuid();
    public Guid FiscalAuthorizationId { get; } = Guid.NewGuid();
    public string SqlServer { get; } =
        Environment.GetEnvironmentVariable("AURALY_TEST_SQLSERVER") ?? @".\LOCAL";
    public string ConnectionString { get; private set; } = string.Empty;

    public HttpClient CreateClient() =>
        (_factory ?? throw new InvalidOperationException("The API fixture is not initialized."))
        .CreateClient();

    public async Task InitializeAsync()
    {
        _databaseName = $"AuralyServerSlice_{Guid.NewGuid():N}";
        ConnectionString =
            $"Server={SqlServer};Initial Catalog={_databaseName};Integrated Security=True;TrustServerCertificate=True;";
        await DeployDacpacAsync();
        await SeedAsync();
        ConfigureHostEnvironment();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Auraly"] = ConnectionString,
                    ["Authentication:Jwt:Issuer"] = JwtIssuer,
                    ["Authentication:Jwt:Audience"] = JwtAudience,
                    ["Authentication:Jwt:SigningKey"] = JwtSigningKey,
                    ["Auraly:Fiscal:TechnicalKeys:0:TenantId"] = TenantId.ToString("D"),
                    ["Auraly:Fiscal:TechnicalKeys:0:BusinessId"] = BusinessId.ToString("D"),
                    ["Auraly:Fiscal:TechnicalKeys:0:AuthorizationNumber"] = AuthorizationNumber,
                    ["Auraly:Fiscal:TechnicalKeys:0:Version"] = TechnicalKeyVersion,
                    ["Auraly:Fiscal:TechnicalKeys:0:Environment"] = "2",
                    ["Auraly:Fiscal:TechnicalKeys:0:Value"] = TechnicalKeyValue,
                    ["Auraly:Fiscal:TechnicalKeys:0:SupplierTaxId"] = SupplierTaxId,
                    ["Auraly:Fiscal:TechnicalKeys:0:QrValidationUrl"] = QrValidationUrl
                });
            });
        });
        using var client = CreateClient();
        using var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    public HttpClient CreateAdminClient(params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, UserId.ToString("D")),
            new(ClaimTypes.NameIdentifier, UserId.ToString("D")),
            new("tenant_id", TenantId.ToString("D")),
            new("business_id", BusinessId.ToString("D"))
        };
        claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));
        var token = new JwtSecurityToken(
            JwtIssuer, JwtAudience, claims, expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSigningKey)),
                SecurityAlgorithms.HmacSha256));
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        RestoreHostEnvironment();
        if (_databaseName is null)
        {
            return;
        }

        SqlConnection.ClearAllPools();
        var master =
            $"Server={SqlServer};Initial Catalog=master;Integrated Security=True;TrustServerCertificate=True;";
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        var escaped = _databaseName.Replace("]", "]]", StringComparison.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             IF DB_ID(N'{_databaseName}') IS NOT NULL
             BEGIN
                 ALTER DATABASE [{escaped}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                 DROP DATABASE [{escaped}];
             END;
             """;
        await command.ExecuteNonQueryAsync();
    }

    public PosSaleUploadRequest CreateValidRequest(long consecutive, Guid? documentId = null)
    {
        var issuedAt = new DateTimeOffset(
            2026,
            7,
            27,
            14,
            35,
            checked((int)(consecutive % 60)),
            TimeSpan.FromHours(-5));
        const decimal quantity = 1m;
        const decimal unitPrice = 10_000m;
        const decimal discount = 0m;
        const decimal tax = 1_900m;
        const decimal untaxed = 10_000m;
        const decimal payable = 11_900m;
        var fiscalNumber = $"{Prefix}{consecutive}";
        var cufe = CufeCalculator.Calculate(
            new CufeInput(
                fiscalNumber,
                issuedAt,
                untaxed,
                payable,
                SupplierTaxId,
                "222222222",
                new FiscalTechnicalKey(TechnicalKeyValue, TechnicalKeyVersion),
                FiscalEnvironment.Test,
                [new FiscalTaxAmount("01", tax)]),
            QrValidationUrl);
        return new PosSaleUploadRequest(
            TenantId,
            BusinessId,
            LocationId,
            WarehouseId,
            RegisterId,
            DeviceId,
            documentId ?? Guid.NewGuid(),
            new PosSaleDocumentNumberContract(
                DocumentSeriesId,
                PosSaleDocumentTypes.Invoice,
                "VTA",
                "03",
                consecutive,
                8,
                $"VTA03-{consecutive:D8}"),
            new PosSaleFiscalSnapshotContract(
                SeriesId,
                FiscalAuthorizationId,
                AuthorizationNumber,
                PosSaleDocumentTypes.Invoice,
                fiscalNumber,
                Prefix,
                consecutive,
                issuedAt,
                SupplierTaxId,
                "222222222",
                (int)FiscalEnvironment.Test,
                TechnicalKeyVersion,
                [new PosSaleTaxContract("01", tax)],
                untaxed,
                tax,
                payable,
                cufe.Cufe,
                cufe.QrPayload),
            [
                new PosSaleLineContract(
                    1,
                    ProductId,
                    "Producto E2E",
                    "01",
                    quantity,
                    unitPrice,
                    discount,
                    tax,
                    untaxed,
                    payable)
            ],
            [new PosSalePaymentContract(1, "Cash", payable, null)]);
    }

    public HttpRequestMessage CreateUploadMessage(
        PosSaleUploadRequest request,
        string? secret = DeviceSecret,
        Guid? deviceId = null,
        string? idempotencyKey = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/pos/v1/sales")
        {
            Content = JsonContent.Create(request)
        };
        if (secret is not null)
        {
            message.Headers.Add(
                "X-Auraly-Device-Id",
                (deviceId ?? request.DeviceId).ToString("D"));
            message.Headers.Add("X-Auraly-Device-Secret", secret);
        }

        message.Headers.Add(
            "Idempotency-Key",
            idempotencyKey ?? request.DocumentId.ToString("D"));
        return message;
    }

    public async Task<int> CountAsync(string table, Guid documentId)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "SalesDocuments",
            "SalesDocumentLines",
            "SalesPayments",
            "FiscalSnapshots",
            "DocumentProcessingReceipts",
            "InventoryMovements",
            "ServerOutboxMessages"
        };
        if (!allowed.Contains(table))
        {
            throw new ArgumentOutOfRangeException(nameof(table));
        }

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM dbo.[{table}] WHERE DocumentId = @DocumentId;";
        command.Parameters.AddWithValue("@DocumentId", documentId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private void ConfigureHostEnvironment()
    {
        SetHostEnvironment("ConnectionStrings__Auraly", ConnectionString);
        SetHostEnvironment("Authentication__Jwt__Issuer", JwtIssuer);
        SetHostEnvironment("Authentication__Jwt__Audience", JwtAudience);
        SetHostEnvironment("Authentication__Jwt__SigningKey", JwtSigningKey);
        SetHostEnvironment("Auraly__Fiscal__TechnicalKeys__0__TenantId", TenantId.ToString("D"));
        SetHostEnvironment("Auraly__Fiscal__TechnicalKeys__0__BusinessId", BusinessId.ToString("D"));
        SetHostEnvironment("Auraly__Fiscal__TechnicalKeys__0__AuthorizationNumber", AuthorizationNumber);
        SetHostEnvironment("Auraly__Fiscal__TechnicalKeys__0__Version", TechnicalKeyVersion);
        SetHostEnvironment("Auraly__Fiscal__TechnicalKeys__0__Environment", "2");
        SetHostEnvironment("Auraly__Fiscal__TechnicalKeys__0__Value", TechnicalKeyValue);
        SetHostEnvironment("Auraly__Fiscal__TechnicalKeys__0__SupplierTaxId", SupplierTaxId);
        SetHostEnvironment("Auraly__Fiscal__TechnicalKeys__0__QrValidationUrl", QrValidationUrl);
    }

    private void SetHostEnvironment(string name, string value)
    {
        _originalEnvironment[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    private void RestoreHostEnvironment()
    {
        foreach (var value in _originalEnvironment)
        {
            Environment.SetEnvironmentVariable(value.Key, value.Value);
        }

        _originalEnvironment.Clear();
    }

    private async Task SeedAsync()
    {
        var allowedCredential = PosDeviceCredentialHasher.Create(DeviceSecret);
        var deniedCredential = PosDeviceCredentialHasher.Create(DeniedDeviceSecret);
        const string sql = """
            INSERT INTO dbo.Tenants (TenantId, Name, Email, IsActive, CreatedAt)
            VALUES (@TenantId, N'Auraly E2E', @TenantEmail, 1, SYSUTCDATETIME());

            INSERT INTO dbo.Businesses
            (BusinessId, TenantId, Name, Description, Address, Phone, Email, Website, IsActive, CreatedAt)
            VALUES
            (@BusinessId, @TenantId, N'Auraly Commerce E2E', N'Integration test',
             N'Bogota', N'3000000000', @BusinessEmail, N'https://auraly.test', 1, SYSUTCDATETIME());

            INSERT INTO dbo.BusinessLocations
            (LocationId, BusinessId, Code, Name, IsActive, CreatedAt)
            VALUES (@LocationId, @BusinessId, N'S01', N'Sede E2E', 1, SYSDATETIMEOFFSET());

            INSERT INTO dbo.Warehouses
            (WarehouseId, BusinessId, LocationId, Code, Name, AllowNegativeStockSales, IsActive, CreatedAt)
            VALUES (@WarehouseId, @BusinessId, @LocationId, N'B01', N'Bodega E2E', 1, 1, SYSDATETIMEOFFSET());

            INSERT INTO dbo.CashRegisters
            (RegisterId, BusinessId, LocationId, WarehouseId, Code, Name, IsActive, CreatedAt)
            VALUES (@RegisterId, @BusinessId, @LocationId, @WarehouseId, N'03', N'Caja E2E', 1, SYSDATETIMEOFFSET());

            INSERT INTO dbo.PosDevices
            (DeviceId, BusinessId, LocationId, WarehouseId, RegisterId, Name,
             CredentialSalt, CredentialHash, CredentialIterations, IsActive, CreatedAt)
            VALUES
            (@DeviceId, @BusinessId, @LocationId, @WarehouseId, @RegisterId, N'POS permitido',
             @AllowedSalt, @AllowedHash, @AllowedIterations, 1, SYSDATETIMEOFFSET()),
            (@DeniedDeviceId, @BusinessId, @LocationId, @WarehouseId, @RegisterId, N'POS sin permiso',
             @DeniedSalt, @DeniedHash, @DeniedIterations, 1, SYSDATETIMEOFFSET());

            INSERT INTO dbo.PosDevicePermissions
            (DeviceId, PermissionCode, IsGranted, GrantedAt)
            VALUES (@DeviceId, @SalesCreate, 1, SYSDATETIMEOFFSET());

            INSERT INTO dbo.FiscalAuthorizations
            (FiscalAuthorizationId, BusinessId, AuthorizationNumber, SupplierTaxId,
             Environment, QrValidationUrl, ValidFrom, ValidUntil, IsActive, CreatedAt)
            VALUES
            (@FiscalAuthorizationId, @BusinessId, @AuthorizationNumber, @SupplierTaxId,
             2, @QrValidationUrl, '2026-01-01', '2028-12-31', 1, SYSDATETIMEOFFSET());

            INSERT INTO dbo.DocumentSeries
            (DocumentSeriesId, BusinessId, LocationId, RegisterId, DocumentType,
             Prefix, SeriesCode, Padding, RangeStart, RangeEnd,
             IsOfflineCapable, IsActive, CreatedAt)
            VALUES
            (@DocumentSeriesId, @BusinessId, @LocationId, @RegisterId, @DocumentType,
             N'VTA', N'03', 8, 1, 99999999, 1, 1, SYSDATETIMEOFFSET());

            INSERT INTO dbo.FiscalSeries
            (SeriesId, BusinessId, RegisterId, FiscalAuthorizationId,
             DocumentType, Prefix, RangeStart, RangeEnd, IsActive, CreatedAt)
            VALUES
            (@SeriesId, @BusinessId, @RegisterId, @FiscalAuthorizationId,
             @DocumentType, @Prefix, 1, 10000, 1, SYSDATETIMEOFFSET());

            INSERT INTO dbo.Products
            (ProductId, BusinessId, Source, Sku, Name, UnitPrice, Currency, ManageStock, IsActive, CreatedAt)
            VALUES
            (@ProductId, @BusinessId, 0, N'P-E2E', N'Producto E2E', 10000, N'COP', 1, 1, SYSUTCDATETIME());
            """;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", TenantId);
        command.Parameters.AddWithValue("@TenantEmail", $"e2e-{TenantId:N}@auraly.test");
        command.Parameters.AddWithValue("@BusinessId", BusinessId);
        command.Parameters.AddWithValue("@BusinessEmail", $"e2e-{BusinessId:N}@auraly.test");
        command.Parameters.AddWithValue("@LocationId", LocationId);
        command.Parameters.AddWithValue("@WarehouseId", WarehouseId);
        command.Parameters.AddWithValue("@RegisterId", RegisterId);
        command.Parameters.AddWithValue("@DeviceId", DeviceId);
        command.Parameters.AddWithValue("@DeniedDeviceId", DeniedDeviceId);
        command.Parameters.AddWithValue("@AllowedSalt", allowedCredential.Salt);
        command.Parameters.AddWithValue("@AllowedHash", allowedCredential.Hash);
        command.Parameters.AddWithValue("@AllowedIterations", allowedCredential.Iterations);
        command.Parameters.AddWithValue("@DeniedSalt", deniedCredential.Salt);
        command.Parameters.AddWithValue("@DeniedHash", deniedCredential.Hash);
        command.Parameters.AddWithValue("@DeniedIterations", deniedCredential.Iterations);
        command.Parameters.AddWithValue("@SalesCreate", CommercePermissionCodes.SalesCreate);
        command.Parameters.AddWithValue("@FiscalAuthorizationId", FiscalAuthorizationId);
        command.Parameters.AddWithValue("@AuthorizationNumber", AuthorizationNumber);
        command.Parameters.AddWithValue("@SupplierTaxId", SupplierTaxId);
        command.Parameters.AddWithValue("@QrValidationUrl", QrValidationUrl);
        command.Parameters.AddWithValue("@DocumentSeriesId", DocumentSeriesId);
        command.Parameters.AddWithValue("@SeriesId", SeriesId);
        command.Parameters.AddWithValue("@DocumentType", PosSaleDocumentTypes.Invoice);
        command.Parameters.AddWithValue("@Prefix", Prefix);
        command.Parameters.AddWithValue("@ProductId", ProductId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task DeployDacpacAsync()
    {
        var root = FindRepositoryRoot();
        var dacpac = Path.Combine(
            root,
            "database",
            "Auraly.Database",
            "bin",
            "Release",
            "Auraly.Database.dacpac");
        if (!File.Exists(dacpac))
        {
            throw new FileNotFoundException(
                "Build Auraly.Database in Release before running integration tests.",
                dacpac);
        }

        var sqlPackage = FindSqlPackage();
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(sqlPackage)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("/Action:Publish");
        process.StartInfo.ArgumentList.Add($"/SourceFile:{dacpac}");
        process.StartInfo.ArgumentList.Add($"/TargetConnectionString:{ConnectionString}");
        process.StartInfo.ArgumentList.Add("/p:CreateNewDatabase=True");
        process.StartInfo.ArgumentList.Add("/p:DropObjectsNotInSource=False");
        process.StartInfo.ArgumentList.Add("/p:BlockOnPossibleDataLoss=True");
        process.Start();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await standardOutput;
        var error = await standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"SqlPackage failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }
    }

    private static string FindSqlPackage()
    {
        var configured = Environment.GetEnvironmentVariable("SQLPACKAGE_PATH");
        var candidates = new[]
        {
            configured,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".dotnet",
                "tools",
                "sqlpackage.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft SQL Server",
                "160",
                "DAC",
                "bin",
                "SqlPackage.exe")
        };
        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            ?? throw new FileNotFoundException(
                "SqlPackage was not found. Set SQLPACKAGE_PATH before running SQL integration tests.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Auraly.Commerce.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Auraly.Commerce.sln.");
    }
}

