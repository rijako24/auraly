using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Contracts.Authentication;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Organization;
using Auraly.Pos.Edge.Host;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosEdgeHostTests : IAsyncLifetime
{
    private const string Token = "test-session-token-with-at-least-32-bytes";
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"auraly-edge-host-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program>? _factory;
    private readonly string _secretPath =
        Path.Combine(Path.GetTempPath(), $"auraly-edge-secrets-{Guid.NewGuid():N}");
    private HttpClient? _client;
    private string? _userSessionToken;
    private readonly List<string> _environmentKeys = [];
    private readonly RecordingPrinter _printer = new();
    private HttpClient Client =>
        _client ?? throw new InvalidOperationException("The test host has not started.");

    [Fact]
    public void Enrollment_package_created_before_offline_leases_still_loads()
    {
        var deviceId = Guid.NewGuid();
        var package = new PosEnrollmentPackage(
            deviceId,
            "device-secret",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "01",
            "Punto principal",
            true,
            Guid.NewGuid(),
            "Usuario",
            ["sales.create"],
            new PosEnrollmentDocumentSeries(
                Guid.NewGuid(), "SalesInvoice", "VTA", "01", 8, 1, 99999999),
            new PosEnrollmentFiscalSeries(
                Guid.NewGuid(), Guid.NewGuid(), "FV", "18760000001",
                1, 100, new DateOnly(2027, 12, 31), 2,
                "9001234567", "technical-key", "v1", "https://example.test/qr"),
            null,
            DateTimeOffset.UtcNow);

        var configuration = PosEdgeEnrollmentStore.ToConfiguration(
            package,
            _secretPath,
            _path);

        Assert.Equal(deviceId.ToString("D"), configuration["PosEdge:DeviceId"]);
        Assert.DoesNotContain(
            configuration.Keys,
            key => key.StartsWith(
                "PosEdge:OfflineLeaseTrust:TrustedPublicKeys:",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Loopback_api_requires_the_local_session_token()
    {
        using var anonymous = _factory!.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/edge/v1/health")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await _client!.GetAsync("/edge/v1/health")).StatusCode);
    }

    [Fact]
    public async Task Host_without_device_configuration_starts_in_enrollment_mode()
    {
        var enrollmentPath =
            Path.Combine(Path.GetTempPath(), $"auraly-enrollment-{Guid.NewGuid():N}.protected");
        try
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(webHost =>
                {
                    webHost.UseSetting("PosEdge:DatabasePath", _path + ".unenrolled");
                    webHost.UseSetting("PosEdge:SessionToken", Token);
                    webHost.UseSetting(
                        "PosEdge:AllowedOrigin",
                        "http://127.0.0.1:47830");
                    webHost.UseSetting(
                        "PosEdge:ServerUrl",
                        "http://127.0.0.1:59999");
                    webHost.UseSetting(
                        "PosEdge:EnrollmentPackagePath",
                        enrollmentPath);
                    webHost.UseSetting(
                        "PosEdge:SecretKeyDirectory",
                        _secretPath + "-unenrolled");
                    webHost.UseSetting("PosEdge:DeviceId", "");
                });
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Auraly-Edge-Session", Token);

            using var response = await client.GetAsync("/edge/v1/health");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("EnrollmentRequired", body.GetProperty("status").GetString());
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync("/edge/v1/drafts/active")).StatusCode);
        }
        finally
        {
            if (File.Exists(enrollmentPath)) File.Delete(enrollmentPath);
        }
    }

    [Fact]
    public async Task Browser_preflight_is_limited_to_the_configured_origin()
    {
        using var allowed = new HttpRequestMessage(HttpMethod.Options, "/edge/v1/capture");
        allowed.Headers.Add("Origin", "http://127.0.0.1:47830");
        allowed.Headers.Add("Access-Control-Request-Method", "POST");
        allowed.Headers.Add(
            "Access-Control-Request-Headers",
            "content-type,x-auraly-edge-session");
        var accepted = await Client.SendAsync(allowed);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.Equal(
            "http://127.0.0.1:47830",
            accepted.Headers.GetValues("Access-Control-Allow-Origin").Single());

        using var rejected = new HttpRequestMessage(HttpMethod.Options, "/edge/v1/capture");
        rejected.Headers.Add("Origin", "https://malicious.example");
        rejected.Headers.Add("Access-Control-Request-Method", "POST");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await Client.SendAsync(rejected)).StatusCode);
    }


    [Fact]
    public async Task Authorized_user_can_delete_the_current_sale_durably()
    {
        var capture = await Client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", null));
        capture.EnsureSuccessStatusCode();
        var captured = await capture.Content.ReadFromJsonAsync<PosCaptureResult>();
        Assert.Single(captured!.Draft!.Lines);

        var deleted = await Client.DeleteAsync(
            $"/edge/v1/drafts/{captured.Draft.DraftId.Value:D}");
        deleted.EnsureSuccessStatusCode();
        var next = await deleted.Content.ReadFromJsonAsync<PosDraft>();

        Assert.NotNull(next);
        Assert.Empty(next!.Lines);
        Assert.NotEqual(captured.Draft.DraftId, next.DraftId);
        await using var database =
            new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}");
        await database.OpenAsync();
        await using var command = database.CreateCommand();
        command.CommandText = "SELECT Status FROM PosDrafts WHERE DraftId=$id;";
        command.Parameters.AddWithValue("$id", captured.Draft.DraftId.Value.ToString("D"));
        Assert.Equal(PosDraftStatus.Deleted, (string)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Scanner_and_temporaries_flow_through_the_protected_http_api()
    {
        var active = await Client.GetFromJsonAsync<PosDraft>("/edge/v1/drafts/active");
        Assert.NotNull(active);
        Assert.Empty(active!.Lines);
        var products = await Client.GetFromJsonAsync<CatalogSearchPageContract>(
            "/edge/v1/catalog/products?search=Product&take=20");
        Assert.Single(products!.Items);
        Assert.Equal("P-1", products.Items[0].ProductCode);


        var capture = await Client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", null));
        capture.EnsureSuccessStatusCode();
        var captured = await capture.Content.ReadFromJsonAsync<PosCaptureResult>();
        Assert.Single(captured!.Draft!.Lines);

        var saved = await Client.PostAsJsonAsync(
            $"/edge/v1/drafts/{captured.Draft.DraftId.Value:D}/temporary",
            new SaveTemporaryRequest("Cliente espera", "REF-1", null));
        saved.EnsureSuccessStatusCode();
        var temporaries = await Client.GetFromJsonAsync<PosDraft[]>(
            "/edge/v1/temporaries?search=REF-1");
        Assert.Single(temporaries!);

        var recovered = await Client.PostAsync(
            $"/edge/v1/temporaries/{temporaries![0].DraftId.Value:D}/recover",
            null);
        recovered.EnsureSuccessStatusCode();
        var restored = await recovered.Content.ReadFromJsonAsync<PosDraft>();
        Assert.Single(restored!.Lines);

        var completed = await Client.PostAsJsonAsync(
            $"/edge/v1/drafts/{restored.DraftId.Value:D}/complete",
            new CompleteDraftRequest(
                null,
                [new CompletePaymentRequest("Cash", restored.PayableAmount, null)]));
        completed.EnsureSuccessStatusCode();
        var result = await completed.Content.ReadFromJsonAsync<CompletePosSaleResult>();

        Assert.Equal("VTA03-00000001", result!.IssuedSale.DocumentNumber);
        Assert.Equal("FV1", result.IssuedSale.FiscalNumber);
        Assert.Empty(result.NextDraft.Lines);
        Assert.Equal("VTA03-00000002", result.NextDocumentNumber.FullNumber);
        Assert.Equal("FV2", result.NextFiscalNumber.FullNumber);
        Assert.Single(_printer.Receipts);
        Assert.Equal(result.IssuedSale.DocumentNumber, _printer.Receipts.Single().DocumentNumber);
        Assert.Equal(result.IssuedSale.FiscalNumber, _printer.Receipts.Single().FiscalNumber);
        Assert.Equal(result.IssuedSale.Cufe, _printer.Receipts.Single().Cufe);
        Assert.Contains(result.IssuedSale.Cufe, _printer.Receipts.Single().QrPayload);
        var localFiscal = await Client.GetFromJsonAsync<PosLocalFiscalStatus>(
            $"/edge/v1/sales/{result.IssuedSale.DocumentId.Value:D}/fiscal-status");
        Assert.NotNull(localFiscal);
        Assert.Equal(FiscalDocumentStatusCodes.LocallyIssuedPendingSync, localFiscal.Status);

        var sales = await Client.GetFromJsonAsync<SaleSearchPageContract>(
            $"/edge/v1/sales?search={result.IssuedSale.DocumentNumber}&skip=0&take=50");
        var found = Assert.Single(sales!.Items);
        Assert.Equal(result.IssuedSale.DocumentId, found.DocumentId);
        Assert.Equal(result.IssuedSale.DocumentNumber, found.DocumentNumber);
        Assert.Equal(result.IssuedSale.FiscalNumber, found.FiscalNumber);
        Assert.Equal(result.IssuedSale.Total, found.Total);
        Assert.Equal("Consumidor final", found.CustomerName);
        Assert.False(sales.HasMore);

        var reprint = await Client.PostAsync(
            $"/edge/v1/sales/{result.IssuedSale.DocumentId.Value:D}/reprint",
            null);
        Assert.True(
            reprint.StatusCode == HttpStatusCode.NoContent,
            await reprint.Content.ReadAsStringAsync());
        Assert.Equal(2, _printer.Receipts.Count);
        var reprinted = _printer.Receipts[1];
        Assert.Equal(result.IssuedSale.DocumentNumber, reprinted.DocumentNumber);
        Assert.Equal(result.IssuedSale.FiscalNumber, reprinted.FiscalNumber);
        Assert.Equal(result.IssuedSale.Cufe, reprinted.Cufe);
        Assert.Equal(result.IssuedSale.QrPayload, reprinted.QrPayload);
        await using var audit = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}");
        await audit.OpenAsync();
        await using var command = audit.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM PosPrintAudit WHERE DocumentId=$id;";
        command.Parameters.AddWithValue("$id", result.IssuedSale.DocumentId.Value);
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);

        var nextCapture = await Client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", null));
        nextCapture.EnsureSuccessStatusCode();
        var nextCaptureResult = await nextCapture.Content.ReadFromJsonAsync<PosCaptureResult>();
        Assert.NotNull(nextCaptureResult);
        Assert.Equal(PosCaptureStatus.Added, nextCaptureResult.Status);
        Assert.Equal(result.NextDraft.DraftId, nextCaptureResult.Draft!.DraftId);
        Assert.Single(nextCaptureResult.Draft.Lines);

        var pausedResponse = await Client.PostAsJsonAsync(
            $"/edge/v1/drafts/{nextCaptureResult.Draft.DraftId.Value:D}/temporary",
            new SaveTemporaryRequest("Venta para eliminar", null, null));
        pausedResponse.EnsureSuccessStatusCode();
        var paused = await pausedResponse.Content.ReadFromJsonAsync<PosDraft>();

        var deleted = await Client.DeleteAsync(
            $"/edge/v1/temporaries/{paused!.DraftId.Value:D}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var remaining = await Client.GetFromJsonAsync<PosDraft[]>(
            "/edge/v1/temporaries");
        Assert.Empty(remaining!);
    }

    [Fact]
    public async Task Closing_and_reopening_the_POS_client_restores_the_active_sale()
    {
        var customers = await Client.GetFromJsonAsync<CustomerSearchPageContract>(
            "/edge/v1/customers?search=300&take=50");
        var customer = Assert.Single(customers!.Items);

        var active = await Client.GetFromJsonAsync<PosDraft>("/edge/v1/drafts/active");
        var selectedResponse = await Client.PutAsJsonAsync(
            $"/edge/v1/drafts/{active!.DraftId.Value:D}/customer",
            new SelectCustomerRequest(customer.CustomerId));
        selectedResponse.EnsureSuccessStatusCode();

        var captureResponse = await Client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", customer.CustomerId));
        captureResponse.EnsureSuccessStatusCode();
        var captured = await captureResponse.Content.ReadFromJsonAsync<PosCaptureResult>();
        var line = Assert.Single(captured!.Draft!.Lines);

        var quantityResponse = await Client.PutAsJsonAsync(
            $"/edge/v1/drafts/{captured.Draft.DraftId.Value:D}/lines/{line.LineId:D}/quantity",
            new QuantityRequest(3m));
        quantityResponse.EnsureSuccessStatusCode();
        var quantityChanged = await quantityResponse.Content.ReadFromJsonAsync<PosCaptureResult>();

        var discountResponse = await Client.PutAsJsonAsync(
            $"/edge/v1/drafts/{captured.Draft.DraftId.Value:D}/lines/{line.LineId:D}/discount",
            new DiscountRequest(12m));
        discountResponse.EnsureSuccessStatusCode();

        using var reopenedWindow = _factory!.CreateClient();
        reopenedWindow.DefaultRequestHeaders.Add("X-Auraly-Edge-Session", Token);
        reopenedWindow.DefaultRequestHeaders.Add("X-Auraly-User-Session", _userSessionToken);
        var restored = await reopenedWindow.GetFromJsonAsync<PosDraft>(
            "/edge/v1/drafts/active");

        Assert.NotNull(restored);
        Assert.Equal(captured.Draft.DraftId, restored!.DraftId);
        Assert.Equal(customer.CustomerId, restored.CustomerId);
        var restoredLine = Assert.Single(restored.Lines);
        Assert.Equal(3m, restoredLine.Quantity);
        Assert.Equal(12m, restoredLine.Discount);
        Assert.Equal(
            quantityChanged!.Draft!.Lines.Single().UnitPrice,
            restoredLine.UnitPrice);
    }

    [Fact]
    public async Task Cashier_can_find_a_local_customer_apply_its_price_and_discount_the_line()
    {
        var customers = await Client.GetFromJsonAsync<CustomerSearchPageContract>(
            "/edge/v1/customers?search=300&take=50");
        var customer = Assert.Single(customers!.Items);
        Assert.Equal("Cliente POS", customer.Name);

        var active = await Client.GetFromJsonAsync<PosDraft>("/edge/v1/drafts/active");
        var selectedResponse = await Client.PutAsJsonAsync(
            $"/edge/v1/drafts/{active!.DraftId.Value:D}/customer",
            new SelectCustomerRequest(customer.CustomerId));
        selectedResponse.EnsureSuccessStatusCode();
        var selected = await selectedResponse.Content.ReadFromJsonAsync<PosCustomerSelection>();
        Assert.Equal(customer.CustomerId, selected!.Draft.CustomerId);

        var capture = await Client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", customer.CustomerId));
        capture.EnsureSuccessStatusCode();
        var captured = await capture.Content.ReadFromJsonAsync<PosCaptureResult>();
        var line = Assert.Single(captured!.Draft!.Lines);
        Assert.Equal(80m, line.UnitPrice);
        Assert.Equal("PriceChannel", line.PriceSource);

        var discountResponse = await Client.PutAsJsonAsync(
            $"/edge/v1/drafts/{captured.Draft.DraftId.Value:D}/lines/{line.LineId:D}/discount",
            new DiscountRequest(5m));
        discountResponse.EnsureSuccessStatusCode();
        var discounted = await discountResponse.Content.ReadFromJsonAsync<PosDraft>();
        Assert.Equal(5m, Assert.Single(discounted!.Lines).Discount);
        Assert.Equal(75m, discounted.UntaxedAmount);
        Assert.Equal(89.25m, discounted.PayableAmount);

        var consumerResponse = await Client.PutAsJsonAsync(
            $"/edge/v1/drafts/{captured.Draft.DraftId.Value:D}/customer",
            new SelectCustomerRequest(null));
        consumerResponse.EnsureSuccessStatusCode();
        var consumer = await consumerResponse.Content.ReadFromJsonAsync<PosCustomerSelection>();
        Assert.Null(consumer!.Draft.CustomerId);
        Assert.Equal(100m, Assert.Single(consumer.Draft.Lines).UnitPrice);
        Assert.Null(consumer.Customer);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_local_cashier_temporarily()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await Client.PostAsJsonAsync(
                "/edge/v1/auth/login",
                new PosLocalLoginRequest("cashier", "wrong-password"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var locked = await Client.PostAsJsonAsync(
            "/edge/v1/auth/login",
            new PosLocalLoginRequest("cashier", "Cashier-Password-1"));
        Assert.Equal((HttpStatusCode)423, locked.StatusCode);
    }

    [Fact]
    public async Task A_new_local_login_invalidates_the_previous_session_and_restores_the_user_draft()
    {
        var capture = await Client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", null));
        capture.EnsureSuccessStatusCode();
        var captured = await capture.Content.ReadFromJsonAsync<PosCaptureResult>();

        using var loginClient = _factory!.CreateClient();
        loginClient.DefaultRequestHeaders.Add("X-Auraly-Edge-Session", Token);
        var login = await loginClient.PostAsJsonAsync(
            "/edge/v1/auth/login",
            new PosLocalLoginRequest("cashier", "Cashier-Password-1"));
        login.EnsureSuccessStatusCode();
        var replacement = await login.Content.ReadFromJsonAsync<PosLocalUserSession>();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await Client.GetAsync("/edge/v1/drafts/active")).StatusCode);

        using var replacementClient = _factory.CreateClient();
        replacementClient.DefaultRequestHeaders.Add("X-Auraly-Edge-Session", Token);
        replacementClient.DefaultRequestHeaders.Add(
            "X-Auraly-User-Session", replacement!.Token);
        var restored = await replacementClient.GetFromJsonAsync<PosDraft>(
            "/edge/v1/drafts/active");
        Assert.Equal(captured!.Draft!.DraftId, restored!.DraftId);
        Assert.Single(restored.Lines);
    }

    public async Task InitializeAsync()
    {
        var customerId = Guid.NewGuid();
        var priceChannelId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        using var leaseSigningKey = RSA.Create(2048);
        const string leaseKeyId = "pos-host-test";
        var ids = new Dictionary<string, string?>
        {
            ["PosEdge:DatabasePath"] = _path,
            ["PosEdge:SessionToken"] = Token,
            ["PosEdge:AllowedOrigin"] = "http://127.0.0.1:47830",
            ["PosEdge:ServerUrl"] = "http://127.0.0.1:59999",
            ["PosEdge:DeviceId"] = deviceId.ToString("D"),
            ["PosEdge:DeviceSecret"] = "test-device-secret",
            ["PosEdge:BusinessId"] = Guid.NewGuid().ToString("D"),
            ["PosEdge:WarehouseId"] = Guid.NewGuid().ToString("D"),
            ["PosEdge:RegisterId"] = Guid.NewGuid().ToString("D"),
            ["PosEdge:UserId"] = userId.ToString("D"),
            ["PosEdge:UserDisplayName"] = "Cajera de prueba",
            ["PosEdge:RegisterCode"] = "03",
            ["PosEdge:WarehouseAllowsNegativeStock"] = "true",
            ["PosEdge:TenantId"] = tenantId.ToString("D"),
            [$"PosEdge:OfflineLeaseTrust:TrustedPublicKeys:{leaseKeyId}"] =
                leaseSigningKey.ExportSubjectPublicKeyInfoPem(),
            ["PosEdge:SupplierTaxId"] = "9001234567",
            ["PosEdge:DefaultCustomerIdentification"] = "222222222",
            ["PosEdge:Permissions:0"] = "sales.create",
            ["PosEdge:Permissions:1"] = "sales.reprint",
            ["PosEdge:Permissions:2"] = "sales.void",
            ["PosEdge:Permissions:3"] = "sales.discount",
            ["PosEdge:PrinterName"] = "Test printer",
            ["PosEdge:PaperWidthMillimeters"] = "80",
            ["PosEdge:Documents:SalesInvoice:SeriesId"] = Guid.NewGuid().ToString("D"),
            ["PosEdge:Documents:SalesInvoice:Padding"] = "8",
            ["PosEdge:Documents:SalesInvoice:RangeStart"] = "1",
            ["PosEdge:Documents:SalesInvoice:RangeEnd"] = "99999999",
            ["PosEdge:SecretKeyDirectory"] = _secretPath,
            ["PosEdge:Fiscal:ProtectedTechnicalKey"] =
                PosEdgeProtectedSecret.ProtectTechnicalKey(
                    _secretPath,
                    "TEST-TECHNICAL-KEY"),
            ["PosEdge:Fiscal:TechnicalKeyVersion"] = "v1",
            ["PosEdge:Fiscal:Environment"] = "Test",
            ["PosEdge:Fiscal:QrValidationUrl"] = "https://catalogo-vpfe.dian.gov.co/document/searchqr",
            ["PosEdge:Fiscal:SeriesId"] = Guid.NewGuid().ToString("D"),
            ["PosEdge:Fiscal:FiscalAuthorizationId"] = Guid.NewGuid().ToString("D"),
            ["PosEdge:Fiscal:Prefix"] = "FV",
            ["PosEdge:Fiscal:AuthorizationNumber"] = "18760000001",
            ["PosEdge:Fiscal:RangeStart"] = "1",
            ["PosEdge:Fiscal:RangeEnd"] = "100",
            ["PosEdge:Fiscal:ValidUntil"] = "2027-07-28"
        };
        foreach (var setting in ids)
        {
            var key = setting.Key.Replace(":", "__", StringComparison.Ordinal);
            Environment.SetEnvironmentVariable(key, setting.Value);
            _environmentKeys.Add(key);
        }
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
            webHost.ConfigureServices(services =>
            {
                services.RemoveAll<IPosReceiptPrinter>();
                services.AddSingleton<IPosReceiptPrinter>(_printer);
                services.RemoveAll<HttpClient>();
                services.AddSingleton(new HttpClient(new UnavailableServerHandler())
                    { BaseAddress = new Uri("http://127.0.0.1:59999") });
            }));
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Auraly-Edge-Session", Token);

        using var scope = _factory.Services.CreateScope();
        var identities = scope.ServiceProvider.GetRequiredService<PosLocalIdentityStore>();
        var password = PosOfflinePasswordHasher.Hash("Cashier-Password-1", DateTimeOffset.UtcNow);
        await identities.ApplySnapshotAsync(new PosOfflineIdentitySnapshot(
            "test-identity-revision",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(1),
            [
                new PosOfflineUserProjection(
                    userId,
                    "cashier",
                    "Cajera de prueba",
                    ["sales.create", "sales.discount", "sales.reprint", "sales.void"],
                    password)
            ]));
        var issuedAt = DateTimeOffset.UtcNow;
        var payload = new OfflineAuthenticationLeasePayload(
            1, Guid.NewGuid(), tenantId, userId, deviceId,
            issuedAt, issuedAt, issuedAt.AddHours(8), Guid.NewGuid());
        var payloadBytes = OfflineAuthenticationLeaseTokenCodec.Serialize(payload);
        var signature = leaseSigningKey.SignData(
            payloadBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
        var signedLease = new SignedOfflineAuthenticationLease(
            leaseKeyId,
            OfflineAuthenticationLeaseAlgorithms.RsaPssSha256,
            OfflineAuthenticationLeaseTokenCodec.Encode(payloadBytes),
            OfflineAuthenticationLeaseTokenCodec.Encode(signature));
        await scope.ServiceProvider.GetRequiredService<PosOfflineLeaseStore>().SaveAsync(
            new OfflineAuthenticationLeaseAcquireResponse(
                signedLease,
                new OfflineAuthenticationLeaseUser(
                    userId,
                    "cashier",
                    "Cajera de prueba",
                    ["sales.create", "sales.discount", "sales.reprint", "sales.void"],
                    password.Salt,
                    password.Hash,
                    password.Iterations,
                    password.ChangedAt)));
        var loginResponse = await _client.PostAsJsonAsync(
            "/edge/v1/auth/login",
            new PosLocalLoginRequest("cashier", "Cashier-Password-1"));
        loginResponse.EnsureSuccessStatusCode();
        var localSession = await loginResponse.Content.ReadFromJsonAsync<PosLocalUserSession>();
        _userSessionToken = Assert.IsType<string>(localSession!.Token);
        _client.DefaultRequestHeaders.Add("X-Auraly-User-Session", _userSessionToken);

        var store = scope.ServiceProvider.GetRequiredService<PosCatalogStore>();
        var product = new PosCatalogItem(
            Guid.NewGuid(), "P-1", "REF-1", "Product", "EA", "VAT19", 19m,
            100m, "COP", true, null, ["770123"], []);
        var sessionId = Guid.NewGuid();
        var items = new[] { product };
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(items))))
            .ToLowerInvariant();
        await store.BeginBootstrapAsync(
            new CatalogSyncSessionResponse(sessionId, 0, 1, DateTimeOffset.UtcNow.AddHours(1)));
        await store.ApplyBootstrapPageAsync(
            new CatalogBootstrapPage(sessionId, 0, null, false, hash, items));
        await store.PromoteBootstrapAsync();
        await store.ApplyPricingSnapshotAsync(new PosPricingSnapshot(
            [],
            [
                new PosPriceChannelItem(
                    priceChannelId,
                    product.ProductId,
                    80m,
                    "COP",
                    false)
            ],
            [
                new PosCustomerPricing(
                    customerId,
                    "3001234567",
                    "Cliente POS",
                    null,
                    priceChannelId,
                    true)
            ]));
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _path, $"{_path}-wal", $"{_path}-shm" })
            if (File.Exists(path)) File.Delete(path);
        if (Directory.Exists(_secretPath))
            Directory.Delete(_secretPath, recursive: true);
        foreach (var key in _environmentKeys)
            Environment.SetEnvironmentVariable(key, null);
    }

    private sealed record CatalogSearchPageContract(
        IReadOnlyList<PosCatalogItem> Items,
        bool HasMore,
        int? NextOffset);

    private sealed record CustomerSearchPageContract(
        IReadOnlyList<PosCustomerPricing> Items,
        bool HasMore,
        int? NextOffset);

    private sealed record SaleSearchPageContract(
        IReadOnlyList<PosIssuedSaleSummary> Items,
        bool HasMore,
        int? NextOffset);

    private sealed class RecordingPrinter : IPosReceiptPrinter
    {
        public List<PosReceipt> Receipts { get; } = [];

        public Task PrintAsync(
            PosReceipt receipt,
            CancellationToken cancellationToken = default)
        {
            Receipts.Add(receipt);
            return Task.CompletedTask;
        }
    }

    private sealed class UnavailableServerHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(
                new HttpRequestException("Auraly Server is offline."));
    }
}
