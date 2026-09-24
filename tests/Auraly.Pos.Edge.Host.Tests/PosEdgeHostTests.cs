using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Auraly.Contracts.Authentication;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Organization;
using Auraly.Contracts.Parties;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Pos.Edge.Host;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosEdgeHostTests(Xunit.Abstractions.ITestOutputHelper output) : IAsyncLifetime
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
    private readonly RecordingClosurePrinter _closurePrinter = new();
    private readonly UnavailableServerHandler _serverHandler = new();
    private HttpClient Client =>
        _client ?? throw new InvalidOperationException("The test host has not started.");

    [Fact]
    public async Task Provisional_closure_rows_remain_removable_after_a_retry()
    {
        var store = _factory!.Services.GetRequiredService<PosOfflineWorkSessionClosureStore>();
        var sessionId = Guid.NewGuid();
        var refund = new PosLocalWorkSessionRefund(Guid.NewGuid(), sessionId, "Cash", 3000m);
        Assert.True(await store.PrepareRefundAsync(refund));
        Assert.True(await store.PrepareRefundAsync(refund));
        await store.RecordRefundAsync(refund);
        Assert.False(await store.PrepareRefundAsync(refund));

        var payment = new PosLocalPortfolioPayment(Guid.NewGuid(), sessionId, "Receivable",
            "Pendiente de confirmación", DateTimeOffset.UtcNow,
            [new PosLocalPortfolioTender("Cash", 3000m)]);
        Assert.True(await store.PreparePortfolioPaymentAsync(payment));
        Assert.True(await store.PreparePortfolioPaymentAsync(payment));
        await store.RecordPortfolioPaymentAsync(payment with { DocumentNumber = "RCC-1" });
        Assert.False(await store.PreparePortfolioPaymentAsync(payment));
    }

    [Fact]
    public async Task Saving_an_order_with_a_new_local_customer_uploads_the_customer_first()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT WorkSessionId FROM PosLocalWorkSessions WHERE ClosedAt IS NULL LIMIT 1;";
        var workSessionId = Guid.Parse((string)(await command.ExecuteScalarAsync())!);
        command.CommandText = "UPDATE Outbox SET Status='Uploaded' WHERE Status<>'Uploaded';";
        await command.ExecuteNonQueryAsync();
        var customers = _factory!.Services.GetRequiredService<PosCustomerOutboxStore>();
        var customer = await customers.QueueAsync(new PosCreateCustomerInput(
            "NaturalPerson", Guid.NewGuid(), "CC", "1000001", null, "Cliente nuevo",
            null, "Cliente nuevo", null, null, null,
            new PartySiteInput("PRINCIPAL", "Principal", Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), "Calle 1", null, null, null, null)), workSessionId);
        var paths = new List<string>();
        _serverHandler.BeforeSend = request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return Task.CompletedTask;
        };
        _serverHandler.Reply = request => request.RequestUri!.AbsolutePath switch
        {
            "/api/pos/v1/customers" => new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { customerId = customer.CustomerId,
                    displayName = customer.Name, identification = customer.Identification })
            },
            "/api/commerce/v1/seller-orders" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { orderId = Guid.NewGuid(), orderNumber = "PED-1" })
            },
            _ => null
        };
        using var response = await Client.PostAsJsonAsync("/edge/v1/orders", new
        {
            customerId = customer.CustomerId,
            partySiteId = customer.PartySiteId
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/api/pos/v1/customers", paths);
        Assert.Contains("/api/commerce/v1/seller-orders", paths);
        Assert.True(paths.IndexOf("/api/pos/v1/customers") <
                    paths.IndexOf("/api/commerce/v1/seller-orders"));
        Assert.Equal("Uploaded", (await customers.DeliveryStatusAsync(customer.CustomerId))?.Status);
    }

    [Fact]
    public async Task Prepared_box_persists_returns_and_portfolio_before_calling_server()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}");
        await connection.OpenAsync();
        await using var current = connection.CreateCommand();
        current.CommandText = "SELECT WorkSessionId FROM PosLocalWorkSessions WHERE ClosedAt IS NULL LIMIT 1;";
        var workSessionId = Guid.Parse((string)(await current.ExecuteScalarAsync())!);
        var observed = new HashSet<string>();
        _serverHandler.BeforeSend = async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var table = path.Contains("sales-returns", StringComparison.Ordinal)
                ? "PosWorkSessionRefunds" : "PosWorkSessionPortfolioPayments";
            await using var check = connection.CreateCommand();
            check.CommandText = $"SELECT COUNT(*) FROM {table} WHERE WorkSessionId=$session;";
            check.Parameters.AddWithValue("$session", workSessionId.ToString("D"));
            Assert.True((long)(await check.ExecuteScalarAsync())! > 0);
            observed.Add(path);
        };
        var returnId = Guid.NewGuid();
        using var returned = await Client.PostAsJsonAsync("/edge/v1/server-returns/confirm", new
        {
            returnId, workSessionId, economicResolution = "Refund",
            refundMethodCode = "Cash", localRefundAmount = 12500m
        });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, returned.StatusCode);
        var customerCreditId = Guid.NewGuid();
        using var credited = await Client.PostAsJsonAsync("/edge/v1/server-returns/confirm", new
        {
            returnId = customerCreditId, workSessionId, economicResolution = "CustomerCredit"
        });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, credited.StatusCode);
        await using (var noLocalRefund = connection.CreateCommand())
        {
            noLocalRefund.CommandText = "SELECT COUNT(*) FROM PosWorkSessionRefunds WHERE ReturnId=$id;";
            noLocalRefund.Parameters.AddWithValue("$id", customerCreditId.ToString("D"));
            Assert.Equal(0L, await noLocalRefund.ExecuteScalarAsync());
        }
        foreach (var (path, kind) in new[] {
            ("receivable-payments", "Receivable"), ("payable-payments", "Payable") })
        {
            using var paid = await Client.PostAsJsonAsync($"/edge/v1/portfolio/{path}", new
            {
                paymentId = Guid.NewGuid(), workSessionId, paidAt = DateTimeOffset.UtcNow,
                payments = new[] { new { methodCode = "Cash", amount = 5000m } }
            });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, paid.StatusCode);
            Assert.Contains($"/api/pos/v1/{path}/confirm", observed);
        }
        Assert.Contains("/api/pos/v1/sales-returns/confirm", observed);
        var store = _factory!.Services.GetRequiredService<PosOfflineWorkSessionClosureStore>();
        Assert.Equal(12500m, Assert.Single(await store.ReadRefundsAsync(workSessionId)).Amount);
        Assert.Equal(2, (await store.ReadPortfolioPaymentsAsync(workSessionId)).Count);
    }

    [Fact]
    public async Task Failed_local_return_write_does_not_call_server()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE PosWorkSessionRefunds;";
        await command.ExecuteNonQueryAsync();
        var calls = _serverHandler.Count;
        try
        {
            using var response = await Client.PostAsJsonAsync("/edge/v1/server-returns/confirm", new
            {
                returnId = Guid.NewGuid(), economicResolution = "Refund",
                refundMethodCode = "Cash", localRefundAmount = 12500m
            });
            Assert.False(response.IsSuccessStatusCode);
        }
        catch (Microsoft.Data.Sqlite.SqliteException) { }
        Assert.Equal(calls, _serverHandler.Count);
    }

    [Fact]
    public async Task Confirmed_portfolio_payment_is_projected_once_into_its_local_session()
    {
        var store=_factory!.Services.GetRequiredService<PosOfflineWorkSessionClosureStore>();
        using var current = await Client.PostAsync("/edge/v1/work-sessions/current", null);
        current.EnsureSuccessStatusCode();
        var session = (await current.Content.ReadFromJsonAsync<PosLocalUserSession>())!;
        var sessionId=session.WorkSessionId;
        var payment=new PosLocalPortfolioPayment(Guid.NewGuid(),sessionId,"Receivable","RCC-1",
            DateTimeOffset.UtcNow,[new("Cash",12500m),new("BankTransfer",7500m)]);
        await store.RecordPortfolioPaymentAsync(payment);
        await store.RecordPortfolioPaymentAsync(payment);
        await store.RecordPortfolioPaymentAsync(new PosLocalPortfolioPayment(Guid.NewGuid(),sessionId,
            "Payable","PGP-1",DateTimeOffset.UtcNow,
            [new("Cash",3000m),new("BankTransfer",1000m),
             new("DebitCard",500m),new("CreditCard",500m)]));
        Assert.False(await store.PreparePortfolioPaymentAsync(payment with
        {
            DocumentNumber = "Pendiente de confirmación",
            Tenders = [new PosLocalPortfolioTender("Cash", 1m)]
        }));
        var actual=Assert.Single((await store.ReadPortfolioPaymentsAsync(sessionId))
            .Where(value=>value.Direction=="Receivable"));
        Assert.Equal(payment.PaymentId,actual.PaymentId);
        Assert.Equal("RCC-1",actual.DocumentNumber);
        Assert.Equal(20000m,actual.Tenders.Sum(tender=>tender.Amount));
        Assert.Empty(await store.ReadPortfolioPaymentsAsync(Guid.NewGuid()));
        var preview=await _factory.Services.GetRequiredService<PosOfflineWorkSessionClosureService>()
            .PreviewAsync(session,CancellationToken.None);
        var detail=Assert.Single(preview.ReceivablePayments!);
        Assert.Equal("RCC-1",detail.PaymentDocumentNumber);
        Assert.Equal(20000m,detail.TotalAmount);
        Assert.Single(preview.PayablePayments!);
        var cash=Assert.Single(preview.PaymentTotals.Where(value=>value.PaymentMethodCode=="Cash"));
        Assert.Equal(12500m,cash.ReceivableAmount);
        Assert.Equal(3000m,cash.PayableAmount);
        Assert.Equal(9500m,preview.ExpectedCash);
        var transfer=Assert.Single(preview.PaymentTotals.Where(value=>value.PaymentMethodCode=="Transfer"));
        Assert.Equal(7500m,transfer.ReceivableAmount);
        Assert.Equal(1000m,transfer.PayableAmount);
        Assert.Equal(6500m,transfer.NetAmount);
        var card=Assert.Single(preview.PaymentTotals.Where(value=>value.PaymentMethodCode=="Card"));
        Assert.Equal(1000m,card.PayableAmount);
        Assert.Equal(-1000m,card.NetAmount);
    }

    [Fact]
    public async Task Lost_enrollment_package_recovers_the_single_durable_device_identity()
    {
        var path = Path.Combine(
            Path.GetTempPath(), $"auraly-recovery-{Guid.NewGuid():N}.db");
        var deviceId = Guid.NewGuid();
        try
        {
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE DocumentSeriesCursors(
                    SeriesId TEXT PRIMARY KEY,
                    DeviceId TEXT NOT NULL,
                    IsActive INTEGER NOT NULL);
                INSERT INTO DocumentSeriesCursors(SeriesId,DeviceId,IsActive)
                VALUES($series,$device,1);
                """;
            command.Parameters.AddWithValue("$series", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$device", deviceId.ToString("D"));
            await command.ExecuteNonQueryAsync();
            await connection.CloseAsync();

            Assert.Equal(
                deviceId,
                new PosLocalDeviceIdentityRecovery(path).ReadSingleDeviceId());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Enrollment_client_preserves_server_conflict_detail()
    {
        using var http = new HttpClient(new EnrollmentConflictHandler())
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var client = new PosEdgeEnrollmentClient(
            http,
            new PosEdgeEnrollmentStore(_path + ".enrollment", _secretPath, _path),
            new PosLocalDeviceIdentityRecovery(_path));

        var exception = await Assert.ThrowsAsync<PosEnrollmentServerException>(() =>
            client.RedeemAsync(new LocalPosEnrollmentRequest(Guid.NewGuid(), "code")));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("PosEnrollmentConflict", exception.Title);
        Assert.Equal("La organización alcanzó el máximo de cajas enroladas.", exception.Message);
        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + ".enrollment"));
    }

    [Fact]
    public async Task Enrollment_client_retires_revoked_local_identity_and_enrolls_as_new_device()
    {
        var path = Path.Combine(
            Path.GetTempPath(), $"auraly-retired-device-{Guid.NewGuid():N}", "auraly-pos.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var oldDeviceId = Guid.NewGuid();
        var newDeviceId = Guid.NewGuid();
        var package = CreateEnrollmentPackage(newDeviceId);
        try
        {
            await SeedRecoverableNumberingAsync(path, oldDeviceId);

            var handler = new RetiredEnrollmentHandler(oldDeviceId, package);
            using var http = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://api.example.test/")
            };
            var recovery = new PosLocalDeviceIdentityRecovery(path);
            var client = new PosEdgeEnrollmentClient(
                http,
                new PosEdgeEnrollmentStore(path + ".enrollment", _secretPath, path),
                recovery);

            var result = await client.RedeemAsync(
                new LocalPosEnrollmentRequest(Guid.NewGuid(), "code"));

            Assert.Equal(newDeviceId, result.DeviceId);
            Assert.Equal(new Guid?[] { oldDeviceId, null }, handler.ExistingDeviceIds);
            new PosEdgeEnrollmentStore(path + ".enrollment", _secretPath, path).ResetLocalStorageIfRequired(path);
            Assert.Null(recovery.ReadSingleDeviceId());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".enrollment")) File.Delete(path + ".enrollment");
        }
    }

    [Fact]
    public async Task Enrollment_client_resets_all_previous_identities_only_when_the_new_host_starts()
    {
        var path = Path.Combine(
            Path.GetTempPath(), $"auraly-retired-devices-{Guid.NewGuid():N}", "auraly-pos.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var retiredDeviceIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var newDeviceId = Guid.NewGuid();
        var package = CreateEnrollmentPackage(newDeviceId);
        try
        {
            await SeedRecoverableNumberingAsync(path, retiredDeviceIds);

            var handler = new MultipleRetiredEnrollmentsHandler(retiredDeviceIds, package);
            using var http = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://api.example.test/")
            };
            var recovery = new PosLocalDeviceIdentityRecovery(path);
            var client = new PosEdgeEnrollmentClient(
                http,
                new PosEdgeEnrollmentStore(path + ".enrollment", _secretPath, path),
                recovery);

            var request = new LocalPosEnrollmentRequest(Guid.NewGuid(), "code");
            var result = await client.RedeemAsync(request);
            Assert.Equal(result, await client.RedeemAsync(request));

            Assert.Equal(newDeviceId, result.DeviceId);
            Assert.Equal(3, handler.ExistingDeviceIds.Count);
            Assert.Null(handler.ExistingDeviceIds[^1]);
            Assert.Equal(
                retiredDeviceIds.Order(),
                handler.ExistingDeviceIds.Take(2).Select(value => value!.Value).Order());
            Assert.Equal(2, recovery.ReadActiveDeviceIds().Count);
            var stored = new PosEdgeEnrollmentStore(path + ".enrollment", _secretPath, path);
            var directory = Path.GetDirectoryName(path)!;
            var receipts = Path.Combine(directory, "receipts");
            Directory.CreateDirectory(receipts);
            await File.WriteAllTextAsync(Path.Combine(receipts, "old-invoice.html"), "old invoice");
            for (var index = 0; index < 100; index++)
                await File.WriteAllBytesAsync(Path.Combine(receipts, $"receipt-{index}.html"), new byte[65536]);
            await File.WriteAllTextAsync(Path.Combine(directory, "printer-settings.json"), "{}");
            var resetDuration = Stopwatch.StartNew();
            stored.ResetLocalStorageIfRequired(path);
            resetDuration.Stop();
            output.WriteLine($"New enrollment reset: {resetDuration.Elapsed.TotalMilliseconds:F1} ms; database + 101 receipts (6.25 MiB); 0 HTTP requests.");
            Assert.Empty(recovery.ReadActiveDeviceIds());
            Assert.False(File.Exists(path));
            Assert.False(Directory.Exists(receipts));
            Assert.False(File.Exists(Path.Combine(directory, "printer-settings.json")));
            await PosStorageBootstrap.InitializeAsync(path);
            var identities = new PosLocalIdentityStore($"Data Source={path}", _secretPath,
                new Auraly.BuildingBlocks.Infrastructure.Identifiers.Uuid7AuralyIdGenerator(TimeProvider.System), TimeProvider.System);
            Assert.False(await identities.HasIdentitySnapshotAsync());
            Assert.Equal("Empty", (await new PosCatalogStore($"Data Source={path}").StatusAsync()).Status);
            // A normal restart of this same accepted enrollment never erases progress.
            stored.ResetLocalStorageIfRequired(path);
            Assert.True(File.Exists(path));
            var replay = await client.RedeemAsync(request);
            Assert.Equal(newDeviceId, replay.DeviceId);
            Assert.False(replay.RestartRequired);
            Assert.Equal(3, handler.ExistingDeviceIds.Count);
            stored.ResetLocalStorageIfRequired(path);
            Assert.True(File.Exists(path));
            Assert.Equal(newDeviceId, stored.Load()!.DeviceId);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".enrollment")) File.Delete(path + ".enrollment");
        }
    }

    [Fact]
    public async Task Legacy_protected_package_does_not_reset_local_storage()
    {
        var package = CreateEnrollmentPackage(Guid.NewGuid());
        var packagePath = _path + ".enrollment";
        try
        {
            await File.WriteAllTextAsync(packagePath, PosEdgeProtectedSecret.ProtectEnrollmentPackage(
                _secretPath, JsonSerializer.Serialize(package)));
            var store = new PosEdgeEnrollmentStore(packagePath, _secretPath, _path);
            Assert.Equal(package.DeviceId, store.Load()!.DeviceId);
            store.ResetLocalStorageIfRequired(_path);
            Assert.True(File.Exists(_path));
            Assert.Null(store.LoadResultForEnrollment(Guid.NewGuid()));
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

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
            "Negocio principal",
            "01",
            "Punto principal",
            true,
            Guid.NewGuid(),
            "Usuario",
            new PosEnrollmentDocumentSeries(
                Guid.NewGuid(), "SalesInvoice", "VTA", "01", 8, 1, 99999999),
            new PosEnrollmentFiscalSeries(
                Guid.NewGuid(), Guid.NewGuid(), "FV", "18760000001",
                1, 100, new DateOnly(2027, 12, 31), 2,
                "9001234567", "technical-key", "v1", "https://example.test/qr"),
            new PosEnrollmentDocumentSeries(
                Guid.NewGuid(), "SalesReceipt", "CVI", "01", 8, 1, 99999999),
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
    public async Task Cashier_with_synchronization_permission_can_read_events()
    {
        using var response = await Client.GetAsync(
            "/edge/v1/synchronization/events?take=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Manual_cash_drawer_requires_the_configurable_user_permission_offline()
    {
        using var denied = await Client.PostAsync(
            "/edge/v1/cash-drawer/open",
            content: null);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains(
            "no tiene permiso",
            await denied.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={_path};Mode=ReadWrite;Cache=Shared"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO PosOfflineUserPermissions(UserId,PermissionCode)
                SELECT UserId,$permission FROM PosOfflineUsers WHERE NormalizedUsername='CASHIER';
                """;
            command.Parameters.AddWithValue(
                "$permission",
                Auraly.Contracts.WorkSessions.WorkSessionPermissionCodes.OpenCashDrawer);
            await command.ExecuteNonQueryAsync();
        }

        using var permitted = await Client.PostAsync(
            "/edge/v1/cash-drawer/open",
            content: null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, permitted.StatusCode);
        Assert.Contains(
            "Configura la impresora",
            await permitted.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
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
            Assert.False(body.TryGetProperty("startupMode", out _));
            Assert.Null(factory.Services.GetService<PosWebPubSubConnection>());
            Assert.Null(factory.Services.GetService<PosSynchronizationSignal>());
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.PostAsync("/edge/v1/synchronization/refresh", null)).StatusCode);
            using var printers = await client.GetAsync(
                "/edge/v1/configuration/printers");
            printers.EnsureSuccessStatusCode();
            using var orderPrint = await client.PostAsJsonAsync(
                "/edge/v1/print/receipt?workflow=orders",
                new DirectPrintReceiptRequest(
                    Guid.NewGuid(),
                    PosSaleDocumentTypes.Receipt,
                    "CVI-1",
                    null,
                    DateTimeOffset.UtcNow,
                    "222222222222",
                    [new PosReceiptLine("P-1", "Producto", 1m, 10m, 0m, 0m, 10m)],
                    [new OfflineSalePayment("Cash", 10m)],
                    10m,
                    0m,
                    10m,
                    null,
                    null));
            Assert.Equal(HttpStatusCode.BadRequest, orderPrint.StatusCode);
            using var scale = await client.PostAsync("/edge/v1/scale/read", null);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, scale.StatusCode);
            var now = DateTimeOffset.UtcNow;
            using var cashMovement = await client.PostAsJsonAsync(
                "/edge/v1/print/cash-movement",
                new PosCashMovementTicket(
                    Guid.NewGuid(), "In", "Base", 10m, now,
                    null, null, "Cajero"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, cashMovement.StatusCode);
            using var closure = await client.PostAsJsonAsync(
                "/edge/v1/print/work-session-closure",
                new WorkSessionClosureView(
                    Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Sede",
                    Guid.NewGuid(), "Bodega", Guid.NewGuid(), "Cajero", null,
                    now.AddHours(-8), now, 10m, 0, 0, 10m, 10m, 10m, 0, null,
                    [new WorkSessionPaymentTotal(
                        "Cash", 10m, 0, 0, 10m, 10m, 0)]));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, closure.StatusCode);
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
        Assert.Contains("Idempotency-Key",
            accepted.Headers.GetValues("Access-Control-Allow-Headers").Single());

        using var rejected = new HttpRequestMessage(HttpMethod.Options, "/edge/v1/capture");
        rejected.Headers.Add("Origin", "https://malicious.example");
        rejected.Headers.Add("Access-Control-Request-Method", "POST");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await Client.SendAsync(rejected)).StatusCode);
    }


    [Fact]
    public async Task Capture_accepts_the_explicit_quantity_in_one_request()
    {
        using var response = await Client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", null, 3m));
        response.EnsureSuccessStatusCode();

        var captured = Assert.IsType<PosCaptureResult>(
            await response.Content.ReadFromJsonAsync<PosCaptureResult>());
        var line = Assert.Single(captured.Draft!.Lines);
        Assert.Equal(3m, line.Quantity);
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
    public async Task Order_proxy_requires_the_matching_local_order_permission()
    {
        using var page = await Client.GetAsync("/edge/v1/orders");
        using var recover = await Client.PostAsJsonAsync(
            $"/edge/v1/orders/{Guid.NewGuid():D}/recover",
            new { workSessionId = Guid.NewGuid(), userId = Guid.NewGuid() });
        using var invoice = await Client.PostAsJsonAsync(
            "/edge/v1/orders/invoice", new { orderIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.Forbidden, page.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, recover.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, invoice.StatusCode);
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
        Assert.NotNull(result.NextFiscalNumber);
        Assert.Equal("FV2", result.NextFiscalNumber.FullNumber);
        Assert.Empty(_printer.Receipts);
        Assert.Equal(result.IssuedSale.DocumentNumber, result.Receipt.DocumentNumber);
        Assert.Equal(result.IssuedSale.FiscalNumber, result.Receipt.FiscalNumber);
        Assert.Equal(result.IssuedSale.Cufe, result.Receipt.Cufe);
        Assert.NotNull(result.IssuedSale.Cufe);
        Assert.Contains(result.IssuedSale.Cufe, result.Receipt.QrPayload);
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
        Assert.Single(_printer.Receipts);
        var reprinted = _printer.Receipts[0];
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

        using var deleted = await Client.DeleteAsync(
            $"/edge/v1/temporaries/{paused!.DraftId.Value:D}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var remaining = await Client.GetFromJsonAsync<PosDraft[]>(
            "/edge/v1/temporaries");
        Assert.Empty(remaining!);
    }

    [Fact]
    public async Task Online_order_commit_can_clear_only_the_owned_local_draft()
    {
        using var client = AuthenticatedClient(_factory!);
        var capture = await client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", null));
        capture.EnsureSuccessStatusCode();
        var captured = await capture.Content.ReadFromJsonAsync<PosCaptureResult>();
        Assert.Single(captured!.Draft!.Lines);

        var completed = await client.PostAsync(
            $"/edge/v1/drafts/{captured.Draft.DraftId.Value:D}/clear-after-online-commit",
            null);

        completed.EnsureSuccessStatusCode();
        var next = Assert.IsType<PosDraft>(
            await completed.Content.ReadFromJsonAsync<PosDraft>());
        Assert.Empty(next.Lines);
        Assert.NotEqual(captured.Draft.DraftId, next.DraftId);
        Assert.Equal(PosDraftStatus.Deleted,
            await DraftStatusAsync(captured.Draft.DraftId.Value));
    }

    [Fact]
    public async Task Commercial_receipt_flows_through_the_local_http_api_without_fiscal_data()
    {
        var active = await Client.GetFromJsonAsync<PosDraft>("/edge/v1/drafts/active");
        Assert.NotNull(active);

        var capture = await Client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", null));
        capture.EnsureSuccessStatusCode();
        var captured = await capture.Content.ReadFromJsonAsync<PosCaptureResult>();
        Assert.NotNull(captured?.Draft);

        var completed = await Client.PostAsJsonAsync(
            $"/edge/v1/drafts/{captured.Draft.DraftId.Value:D}/complete",
            new CompleteDraftRequest(
                null,
                [new CompletePaymentRequest("Cash", captured.Draft.PayableAmount, null)],
                DocumentType: PosSaleDocumentTypes.Receipt));
        completed.EnsureSuccessStatusCode();
        var result = await completed.Content.ReadFromJsonAsync<CompletePosSaleResult>();

        Assert.NotNull(result);
        Assert.Equal("CVI03-00000001", result.IssuedSale.DocumentNumber);
        Assert.Null(result.IssuedSale.FiscalNumber);
        Assert.Null(result.IssuedSale.Cufe);
        Assert.Null(result.IssuedSale.QrPayload);
        Assert.Null(result.NextFiscalNumber);
        Assert.Equal("CVI03-00000002", result.NextDocumentNumber.FullNumber);
        Assert.Empty(_printer.Receipts);
        Assert.Equal(PosSaleDocumentTypes.Receipt, result.Receipt.DocumentType);
        Assert.Null(result.Receipt.FiscalNumber);
        Assert.Null(result.Receipt.Cufe);
        Assert.Null(result.Receipt.QrPayload);

        var sales = await Client.GetFromJsonAsync<SaleSearchPageContract>(
            $"/edge/v1/sales?search={result.IssuedSale.DocumentNumber}&skip=0&take=50");
        var found = Assert.Single(sales!.Items);
        Assert.Equal(result.IssuedSale.DocumentId, found.DocumentId);
        Assert.Equal(PosSaleDocumentTypes.Receipt, found.DocumentType);
        Assert.Equal(result.IssuedSale.DocumentNumber, found.DocumentNumber);
        Assert.Null(found.FiscalNumber);

        var reprint = await Client.PostAsync(
            $"/edge/v1/sales/{result.IssuedSale.DocumentId.Value:D}/reprint",
            null);
        Assert.Equal(HttpStatusCode.NoContent, reprint.StatusCode);
        Assert.Single(_printer.Receipts);
        Assert.Equal(PosSaleDocumentTypes.Receipt, _printer.Receipts[0].DocumentType);
    }

    [Fact]
    public async Task Offline_customer_withholding_is_collected_net_and_travels_in_the_sale_and_receipt()
    {
        var customers = await Client.GetFromJsonAsync<CustomerSearchPageContract>(
            "/edge/v1/customers?search=300&take=50");
        var customer = Assert.Single(customers!.Items);
        var active = await Client.GetFromJsonAsync<PosDraft>("/edge/v1/drafts/active");
        Assert.NotNull(active);

        var selectedResponse = await Client.PutAsJsonAsync(
            $"/edge/v1/drafts/{active!.DraftId.Value:D}/customer",
            new SelectCustomerRequest(customer.CustomerId, customer.PartySiteId));
        selectedResponse.EnsureSuccessStatusCode();
        var captureResponse = await Client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", null));
        captureResponse.EnsureSuccessStatusCode();
        var draft = (await captureResponse.Content.ReadFromJsonAsync<PosCaptureResult>())!.Draft!;

        var settlement = await Client.GetFromJsonAsync<WithholdingCalculationSnapshot>(
            $"/edge/v1/drafts/{draft.DraftId.Value:D}/settlement");
        Assert.NotNull(settlement);
        Assert.Equal(draft.PayableAmount, settlement!.GrossAmount);
        Assert.Equal(
            decimal.Round(draft.UntaxedAmount * 0.025m, 4, MidpointRounding.AwayFromZero),
            settlement.WithholdingTotal);
        Assert.Equal(settlement.GrossAmount - settlement.WithholdingTotal, settlement.NetAmount);
        Assert.Single(settlement.Lines);
        var roundingAdjustment = PosPaymentRoundingPolicy.Adjustment(settlement.NetAmount);

        var completedResponse = await Client.PostAsJsonAsync(
            $"/edge/v1/drafts/{draft.DraftId.Value:D}/complete",
            new CompleteDraftRequest(
                null,
                [new CompletePaymentRequest(
                    "Cash", settlement.NetAmount, null,
                    RoundingAdjustment: roundingAdjustment)]));
        completedResponse.EnsureSuccessStatusCode();
        var completed = await completedResponse.Content.ReadFromJsonAsync<CompletePosSaleResult>();
        Assert.NotNull(completed);
        Assert.Equal(settlement.WithholdingTotal, completed!.Receipt.WithholdingTotal);
        Assert.Equal(PosPaymentRoundingPolicy.RoundedTotal(settlement.NetAmount),
            completed.Receipt.NetPayableAmount);
        Assert.Equal(roundingAdjustment, completed.Receipt.PayableRoundingAmount);
        Assert.Single(completed.Receipt.Withholdings!);
        Assert.Equal(settlement.NetAmount, Assert.Single(completed.Receipt.Payments).Amount);

        Assert.Empty(_printer.Receipts);
        Assert.Equal(settlement.WithholdingTotal, completed.Receipt.WithholdingTotal);
        Assert.Equal(PosPaymentRoundingPolicy.RoundedTotal(settlement.NetAmount),
            completed.Receipt.NetPayableAmount);
        var rendered = Encoding.UTF8.GetString(new EscPosReceiptRenderer().Render(completed.Receipt));
        Assert.Contains("Total retenciones", rendered);
        Assert.Contains("Total", rendered);

        using var scope = _factory!.Services.CreateScope();
        var sales = scope.ServiceProvider.GetRequiredService<PosEdgeSaleStore>();
        var pending = Assert.Single(await sales.GetPendingOutboxAsync());
        var upload = PosSaleContractSerializer.Deserialize(pending.Payload);
        Assert.Equal(settlement.WithholdingTotal,
            upload.CommercialSnapshot.Withholding!.WithholdingTotal);
        Assert.Equal(PosPaymentRoundingPolicy.RoundedTotal(settlement.NetAmount),
            upload.CommercialSnapshot.NetPayableAmount);
        Assert.Equal(settlement.NetAmount, Assert.Single(upload.Payments).Amount);
        Assert.Equal(roundingAdjustment, Assert.Single(upload.Payments).RoundingAdjustment);
    }

    [Fact]
    public async Task Completion_returns_the_next_draft_without_waiting_for_direct_print()
    {
        var capture = await Client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", null));
        capture.EnsureSuccessStatusCode();
        var draft = (await capture.Content.ReadFromJsonAsync<PosCaptureResult>())!.Draft!;
        var completed = await Client.PostAsJsonAsync(
            $"/edge/v1/drafts/{draft.DraftId.Value:D}/complete",
            new CompleteDraftRequest(
                null,
                [new CompletePaymentRequest("Cash", draft.PayableAmount, null)],
                DocumentType: PosSaleDocumentTypes.Receipt));

        completed.EnsureSuccessStatusCode();
        var result = await completed.Content.ReadFromJsonAsync<CompletePosSaleResult>();
        Assert.NotNull(result);
        Assert.False(result.PrintedDirectly);
        Assert.Null(result.PrintError);
        Assert.Equal(result.IssuedSale.DocumentId, result.Receipt.DocumentId);
        Assert.Empty(result.NextDraft.Lines);
        Assert.Empty(_printer.Receipts);
        var active = await Client.GetFromJsonAsync<PosDraft>("/edge/v1/drafts/active");
        Assert.Equal(result.NextDraft.DraftId, active!.DraftId);
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
            new SelectCustomerRequest(customer.CustomerId, customer.PartySiteId));
        selectedResponse.EnsureSuccessStatusCode();

        var captureResponse = await Client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", null));
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
        var customerJson = await Client.GetStringAsync(
            "/edge/v1/customers?search=300&take=50");
        Assert.DoesNotContain("defaultDueDays", customerJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("defaultCreditDueDays", customerJson, StringComparison.OrdinalIgnoreCase);
        var customers = JsonSerializer.Deserialize<CustomerSearchPageContract>(
            customerJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var customer = Assert.Single(customers!.Items);
        Assert.Equal("Cliente POS", customer.Name);
        Assert.True(customer.IsCreditEnabled);
        Assert.Equal(500_000m, customer.AvailableCredit);

        var active = await Client.GetFromJsonAsync<PosDraft>("/edge/v1/drafts/active");
        var incompleteSelection = await Client.PutAsJsonAsync(
            $"/edge/v1/drafts/{active!.DraftId.Value:D}/customer",
            new SelectCustomerRequest(customer.CustomerId));
        Assert.Equal(HttpStatusCode.BadRequest, incompleteSelection.StatusCode);

        var selectedResponse = await Client.PutAsJsonAsync(
            $"/edge/v1/drafts/{active!.DraftId.Value:D}/customer",
            new SelectCustomerRequest(customer.CustomerId, customer.PartySiteId));
        selectedResponse.EnsureSuccessStatusCode();
        var selected = await selectedResponse.Content.ReadFromJsonAsync<PosCustomerSelectionView>();
        Assert.Equal(customer.CustomerId, selected!.Draft.CustomerId);
        Assert.Equal(500_000m, selected.Customer!.AvailableCredit);

        var capture = await Client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", null));
        capture.EnsureSuccessStatusCode();
        var captured = await capture.Content.ReadFromJsonAsync<PosCaptureResult>();
        var line = Assert.Single(captured!.Draft!.Lines);
        Assert.Equal(80m, line.UnitPrice);
        Assert.Equal("PriceChannel", line.PriceSource);

        var discountResponse = await Client.PutAsJsonAsync(
            $"/edge/v1/drafts/{captured.Draft.DraftId.Value:D}/lines",
            new UpdateDraftLinesRequest(
            [
                new UpdateSalesDraftLineRequest(
                    line.LineId,
                    line.Description,
                    line.PublicUnitPrice,
                    5m,
                    line.DocumentUnitCost)
            ]));
        discountResponse.EnsureSuccessStatusCode();
        var discounted = await discountResponse.Content.ReadFromJsonAsync<PosDraft>();
        Assert.Equal(5m, Assert.Single(discounted!.Lines).Discount);
        Assert.Equal(63.03m, discounted.UntaxedAmount);
        Assert.Equal(75m, discounted.PayableAmount);

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
    public async Task Product_verifier_keeps_public_price_even_when_the_selected_customer_has_a_channel()
    {
        var customers = await Client.GetFromJsonAsync<CustomerSearchPageContract>(
            "/edge/v1/customers?search=300&take=50");
        var customer = Assert.Single(customers!.Items);

        var regularJson = await Client.GetFromJsonAsync<JsonElement>(
            $"/edge/v1/catalog/products?search=770123&take=20&customerId={customer.CustomerId:D}");
        var regular = regularJson.GetProperty("items")[0];
        Assert.Equal(80m, regular.GetProperty("unitPrice").GetDecimal());
        Assert.Equal("PriceChannel", regular.GetProperty("priceSource").GetString());

        var verifierJson = await Client.GetFromJsonAsync<JsonElement>(
            $"/edge/v1/catalog/products?search=770123&take=20&customerId={customer.CustomerId:D}&publicPriceOnly=true");
        var verifier = verifierJson.GetProperty("items")[0];
        Assert.Equal(100m, verifier.GetProperty("unitPrice").GetDecimal());
        Assert.Equal("Base", verifier.GetProperty("priceSource").GetString());
    }

    [Fact]
    public async Task Selected_customer_channel_reprices_all_lines_when_accumulated_quantity_reaches_a_tier()
    {
        var customers = await Client.GetFromJsonAsync<CustomerSearchPageContract>(
            "/edge/v1/customers?search=400&take=50");
        var customer = Assert.Single(customers!.Items);
        Assert.Equal("Cliente canal escalonado POS", customer.Name);
        Assert.NotNull(customer.PriceChannelId);

        var active = await Client.GetFromJsonAsync<PosDraft>("/edge/v1/drafts/active");
        var selectedResponse = await Client.PutAsJsonAsync(
            $"/edge/v1/drafts/{active!.DraftId.Value:D}/customer",
            new SelectCustomerRequest(customer.CustomerId, customer.PartySiteId));
        selectedResponse.EnsureSuccessStatusCode();

        var firstResponse = await Client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", null));
        firstResponse.EnsureSuccessStatusCode();
        var first = await firstResponse.Content.ReadFromJsonAsync<PosCaptureResult>();
        var firstLine = Assert.Single(first!.Draft!.Lines);
        Assert.Equal(1m, firstLine.Quantity);
        Assert.Equal(90m, firstLine.UnitPrice);
        Assert.Equal("PriceChannel", firstLine.PriceSource);

        var secondResponse = await Client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", null));
        secondResponse.EnsureSuccessStatusCode();
        var second = await secondResponse.Content.ReadFromJsonAsync<PosCaptureResult>();
        Assert.Equal(2, second!.Draft!.Lines.Count);
        Assert.All(second.Draft.Lines, line => Assert.Equal(1m, line.Quantity));
        Assert.All(second.Draft.Lines, line => Assert.Equal(90m, line.UnitPrice));
        Assert.Equal(2, second.Draft.Lines.Select(line => line.LineId).Distinct().Count());

        var quantityResponse = await Client.PutAsJsonAsync(
            $"/edge/v1/drafts/{second.Draft.DraftId.Value:D}/lines/{firstLine.LineId:D}/quantity",
            new QuantityRequest(2m));
        quantityResponse.EnsureSuccessStatusCode();
        var changed = await quantityResponse.Content.ReadFromJsonAsync<PosCaptureResult>();
        Assert.Equal(new[] { 1m, 2m }, changed!.Draft!.Lines.Select(line => line.Quantity).Order().ToArray());
        Assert.All(changed.Draft.Lines, line => Assert.Equal(70m, line.UnitPrice));
        Assert.All(changed.Draft.Lines, line => Assert.Equal("PriceChannel", line.PriceSource));
    }

    [Fact]
    public async Task Channel_exclusion_keeps_product_sellable_at_public_price()
    {
        var customers = await Client.GetFromJsonAsync<CustomerSearchPageContract>(
            "/edge/v1/customers?search=500&take=50");
        var customer = Assert.Single(customers!.Items);
        Assert.NotNull(customer.PriceChannelId);

        var active = await Client.GetFromJsonAsync<PosDraft>("/edge/v1/drafts/active");
        var selectedResponse = await Client.PutAsJsonAsync(
            $"/edge/v1/drafts/{active!.DraftId.Value:D}/customer",
            new SelectCustomerRequest(customer.CustomerId, customer.PartySiteId));
        selectedResponse.EnsureSuccessStatusCode();

        var captureResponse = await Client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", null));
        captureResponse.EnsureSuccessStatusCode();
        var captured = await captureResponse.Content.ReadFromJsonAsync<PosCaptureResult>();
        var line = Assert.Single(captured!.Draft!.Lines);
        Assert.Equal(100m, line.UnitPrice);
        Assert.Equal("Base", line.PriceSource);
    }

    [Fact]
    public async Task Enrolled_cashier_can_log_in_locally_while_Auraly_Server_is_offline()
    {
        // The test host uses UnavailableServerHandler for every server request.
        // Authentication must therefore be resolved exclusively from the
        // durable protected local identity snapshot.
        using var loginClient = _factory!.CreateClient();
        loginClient.DefaultRequestHeaders.Add("X-Auraly-Edge-Session", Token);

        using var response = await loginClient.PostAsJsonAsync(
            "/edge/v1/auth/login",
            new PosLocalLoginRequest("cashier", "Cashier-Password-1"));

        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<PosLocalUserSession>();
        Assert.NotNull(session);
        Assert.Equal("Cajera de prueba", session.DisplayName);
        Assert.Equal(Guid.Empty, session.WorkSessionId);
        loginClient.DefaultRequestHeaders.Add(
            "X-Auraly-User-Session", session.Token);
        using var open = await loginClient.PostAsync(
            "/edge/v1/work-sessions/current", null);
        open.EnsureSuccessStatusCode();
        var operational = await open.Content.ReadFromJsonAsync<PosLocalUserSession>();
        Assert.NotEqual(Guid.Empty, operational!.WorkSessionId);
    }

    [Fact]
    public async Task Cashier_can_close_and_print_the_work_session_while_server_is_offline()
    {
        using var currentSessionResponse = await Client.PostAsync(
            "/edge/v1/work-sessions/current", null);
        currentSessionResponse.EnsureSuccessStatusCode();
        var currentSession = await currentSessionResponse.Content
            .ReadFromJsonAsync<PosLocalUserSession>();
        Assert.NotNull(currentSession);
        using (var scope = _factory!.Services.CreateScope())
        {
            var runtime = scope.ServiceProvider.GetRequiredService<PosEdgeRuntimeContext>();
            var cashStore = scope.ServiceProvider.GetRequiredService<PosCashMovementStore>();
            var closureStore = scope.ServiceProvider
                .GetRequiredService<PosOfflineWorkSessionClosureStore>();
            var cashEntryReasonId = Guid.NewGuid();
            var cashExitReasonId = Guid.NewGuid();
            await cashStore.ReplaceReasonsAsync(
                runtime.BusinessId.Value,
                [
                    new CashMovementReasonView(
                        cashEntryReasonId, runtime.BusinessId.Value, "BASE", "Base de caja",
                        CashMovementDirections.In, "CashOverShort", null, null,
                        "110505", "Caja general", true, true, true),
                    new CashMovementReasonView(
                        cashExitReasonId, runtime.BusinessId.Value, "GASTO", "Gasto de caja",
                        CashMovementDirections.Out, "CashOverShort", null, null,
                        "110505", "Caja general", true, true, true)
                ]);

            using var entry = await Client.PostAsJsonAsync(
                "/edge/v1/cash-movements",
                new QueueLocalCashMovementRequest(
                    Guid.NewGuid(), cashEntryReasonId, 5_000m, DateTimeOffset.UtcNow,
                    "BASE-LOCAL", "Entrada local", null));
            entry.EnsureSuccessStatusCode();
            using var exit = await Client.PostAsJsonAsync(
                "/edge/v1/cash-movements",
                new QueueLocalCashMovementRequest(
                    Guid.NewGuid(), cashExitReasonId, 1_250m, DateTimeOffset.UtcNow,
                    "GASTO-LOCAL", "Salida local", null));
            exit.EnsureSuccessStatusCode();
            await closureStore.RecordRefundAsync(new PosLocalWorkSessionRefund(
                Guid.NewGuid(), currentSession.WorkSessionId, "Cash", .50m));
            await closureStore.RecordRefundAsync(new PosLocalWorkSessionRefund(
                Guid.NewGuid(), currentSession.WorkSessionId, "CreditCard", .25m));
            await closureStore.RecordRefundAsync(new PosLocalWorkSessionRefund(
                Guid.NewGuid(), currentSession.WorkSessionId, "Transfer", .125m));
            await closureStore.RecordRefundAsync(new PosLocalWorkSessionRefund(
                Guid.NewGuid(), currentSession.WorkSessionId, "CustomerCredit", .375m));
        }

        var capture = await Client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", null));
        capture.EnsureSuccessStatusCode();
        var captured = await capture.Content.ReadFromJsonAsync<PosCaptureResult>();
        var total = captured!.Draft!.PayableAmount;
        var cashAmount = decimal.Round(total * 0.50m, 2, MidpointRounding.AwayFromZero);
        var cardAmount = decimal.Round(total * 0.25m, 2, MidpointRounding.AwayFromZero);
        var transferAmount = total - cashAmount - cardAmount;
        var completed = await Client.PostAsJsonAsync(
            $"/edge/v1/drafts/{captured.Draft.DraftId.Value:D}/complete",
            new CompleteDraftRequest(
                null,
                [
                    new CompletePaymentRequest("Cash", cashAmount, null),
                    new CompletePaymentRequest("Card", cardAmount, null, "Visa", "APPROVED-1"),
                    new CompletePaymentRequest("Transfer", transferAmount, "TRX-EDGE-1")
                ],
                DocumentType: PosSaleDocumentTypes.Receipt));
        Assert.True(
            completed.IsSuccessStatusCode,
            await completed.Content.ReadAsStringAsync());
        var nextDraft = (await completed.Content.ReadFromJsonAsync<CompletePosSaleResult>())!
            .NextDraft;

        var previewResponse = await Client.PostAsJsonAsync(
            "/edge/v1/work-sessions/current/closure-preview",
            new PreviewLocalWorkSessionClosureRequest(nextDraft.DraftId.Value));
        previewResponse.EnsureSuccessStatusCode();
        var preview = await previewResponse.Content
            .ReadFromJsonAsync<AuthorizedWorkSessionClosurePreview>();
        Assert.NotNull(preview);
        Assert.Equal(total, preview.Preview.TotalSales);
        Assert.Equal(3_750m, preview.Preview.TotalOther);
        Assert.Equal(1.25m, preview.Preview.TotalRefunds);
        Assert.Equal(4, preview.Preview.ReturnCount);
        Assert.Equal(total + 3_750m - 1.25m, preview.Preview.NetAmount);
        Assert.Equal(cashAmount + 3_749.50m, preview.Preview.ExpectedCash);
        var cashTotal = preview.Preview.PaymentTotals
            .Single(value => value.PaymentMethodCode == "Cash");
        Assert.Equal(.50m, cashTotal.RefundAmount);
        Assert.Equal(cashAmount + 3_749.50m, cashTotal.NetAmount);
        Assert.Equal(5_000m, cashTotal.CashEntryAmount);
        Assert.Equal(1_250m, cashTotal.CashExitAmount);
        var cashMovementDetails = Assert.IsAssignableFrom<
            IReadOnlyList<WorkSessionCashMovementDetail>>(preview.Preview.CashMovements);
        Assert.Equal(2, cashMovementDetails.Count);
        Assert.Equal(
            5_000m,
            cashMovementDetails
                .Where(value => value.Direction == CashMovementDirections.In)
                .Sum(value => value.Amount));
        Assert.Equal(
            1_250m,
            cashMovementDetails
                .Where(value => value.Direction == CashMovementDirections.Out)
                .Sum(value => value.Amount));
        Assert.Equal(
            cardAmount - .25m,
            preview.Preview.PaymentTotals.Single(value => value.PaymentMethodCode == "Card").NetAmount);
        Assert.Equal(
            .25m,
            preview.Preview.PaymentTotals.Single(value => value.PaymentMethodCode == "Card").RefundAmount);
        Assert.Equal(
            transferAmount - .125m,
            preview.Preview.PaymentTotals.Single(value => value.PaymentMethodCode == "Transfer").NetAmount);
        Assert.Equal(
            new[] { "Cash", "Card", "Transfer" },
            preview.Preview.PaymentTotals
                .Select(value => value.PaymentMethodCode));

        var operationId = Guid.NewGuid();
        var closeRequest = new CloseLocalWorkSessionRequest(
            operationId,
            preview.AuthorizationToken,
            cashAmount + 3_749.50m,
            [
                new WorkSessionPaymentCount("Cash", cashAmount + 3_749.50m),
                new WorkSessionPaymentCount("Card", cardAmount - .25m),
                new WorkSessionPaymentCount("Transfer", transferAmount - .125m)
            ],
            null);
        _closurePrinter.FailuresRemaining = 1;
        var closeResponse = await Client.PostAsJsonAsync(
            "/edge/v1/work-sessions/current/close",
            closeRequest);
        closeResponse.EnsureSuccessStatusCode();
        var closeResult = await closeResponse.Content.ReadFromJsonAsync<CloseLocalWorkSessionResult>();
        Assert.NotNull(closeResult);
        Assert.False(closeResult.PrintedDirectly);
        Assert.Contains("impresora", closeResult.PrintError, StringComparison.OrdinalIgnoreCase);
        var closure = closeResult.Closure;
        Assert.Equal(operationId, closure.WorkSessionClosureId);
        Assert.Equal(0, closure.CashDifference);
        Assert.Empty(_closurePrinter.Closures);

        await using var database = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={_path}");
        await database.OpenAsync();
        await using var command = database.CreateCommand();
        command.CommandText = """
            SELECT Status FROM Outbox
            WHERE DocumentId=$operation AND Type='work-session.closed';
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        Assert.Contains(
            (string)(await command.ExecuteScalarAsync())!,
            new[] { "Pending", "RetryScheduled", "Uploading" });

        using var healthClient = _factory!.CreateClient();
        healthClient.DefaultRequestHeaders.Add("X-Auraly-Edge-Session", Token);
        var health = await healthClient.GetFromJsonAsync<JsonElement>("/edge/v1/health");
        Assert.True(health.GetProperty("pendingSynchronizationCount").GetInt32() >= 2);
        Assert.NotEqual(
            JsonValueKind.Null,
            health.GetProperty("oldestPendingSynchronizationAt").ValueKind);

        // Closing the cash session must not close authentication. Re-entering POS
        // with the same token opens the next local operational stream.
        using var openNext = await Client.PostAsync(
            "/edge/v1/work-sessions/current", null);
        openNext.EnsureSuccessStatusCode();
        var nextSession = await openNext.Content.ReadFromJsonAsync<PosLocalUserSession>();
        Assert.NotEqual(closure.WorkSessionId, nextSession!.WorkSessionId);
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

        using var previousSessionResponse = await Client.GetAsync("/edge/v1/drafts/active");
        Assert.Equal(HttpStatusCode.Unauthorized, previousSessionResponse.StatusCode);
        var previousSessionProblem = await previousSessionResponse.Content
            .ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "LoginReplaced",
            previousSessionProblem.GetProperty("code").GetString());

        using var replacementClient = _factory.CreateClient();
        replacementClient.DefaultRequestHeaders.Add("X-Auraly-Edge-Session", Token);
        replacementClient.DefaultRequestHeaders.Add(
            "X-Auraly-User-Session", replacement!.Token);
        using var open = await replacementClient.PostAsync(
            "/edge/v1/work-sessions/current", null);
        open.EnsureSuccessStatusCode();
        var restored = await replacementClient.GetFromJsonAsync<PosDraft>(
            "/edge/v1/drafts/active");
        Assert.Equal(captured!.Draft!.DraftId, restored!.DraftId);
        Assert.Single(restored.Lines);
    }

    [Fact]
    public async Task Local_login_and_each_POS_bootstrap_query_complete_in_under_one_second()
    {
        using var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Auraly-Edge-Session", Token);

        var login = await MeasureAsync(
            "login local",
            () => client.PostAsJsonAsync(
                "/edge/v1/auth/login",
                new PosLocalLoginRequest("cashier", "Cashier-Password-1")));
        login.Value.EnsureSuccessStatusCode();
        var session = await login.Value.Content.ReadFromJsonAsync<PosLocalUserSession>();
        client.DefaultRequestHeaders.Add(
            "X-Auraly-User-Session",
            Assert.IsType<string>(session!.Token));

        var open = await MeasureAsync(
            "abrir o recuperar sesión de caja",
            () => client.PostAsync("/edge/v1/work-sessions/current", null));
        open.Value.EnsureSuccessStatusCode();

        foreach (var request in new[]
        {
            (Name: "estado local", Path: "/edge/v1/health"),
            (Name: "borrador activo", Path: "/edge/v1/drafts/active"),
            (Name: "ventas pausadas", Path: "/edge/v1/temporaries"),
            (Name: "siguiente numeración", Path: "/edge/v1/sales/next-number?documentType=SalesInvoice"),
            (Name: "búsqueda local de producto", Path: "/edge/v1/catalog/products?search=770123&skip=0&take=50")
        })
        {
            var measured = await MeasureAsync(request.Name, () => client.GetAsync(request.Path));
            measured.Value.EnsureSuccessStatusCode();
        }

        var capture = await MeasureAsync(
            "agregar producto local",
            () => client.PostAsJsonAsync(
                "/edge/v1/capture",
                new { value = "770123" }));
        capture.Value.EnsureSuccessStatusCode();

        static async Task<(T Value, TimeSpan Elapsed)> MeasureAsync<T>(
            string operation,
            Func<Task<T>> execute)
        {
            var stopwatch = Stopwatch.StartNew();
            var value = await execute();
            stopwatch.Stop();
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(1),
                $"{operation} tardó {stopwatch.Elapsed.TotalMilliseconds:N0} ms.");
            return (value, stopwatch.Elapsed);
        }
    }

    [Fact]
    public async Task A_reenrolled_device_does_not_revoke_its_new_login_from_a_historical_lease()
    {
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO PosOfflineAuthenticationLeases(
                  LeaseId,TenantId,UserId,DeviceId,KeyId,Algorithm,SignedPayload,Signature,
                  IssuedAt,NotBefore,ExpiresAt,LastObservedAt,Status,UpdatedAt)
                SELECT $lease,$tenant,UserId,$oldDevice,'key','ES256','payload','signature',
                  $issued,$issued,$expires,$issued,'Active',$issued
                FROM PosOfflineUsers WHERE NormalizedUsername='CASHIER';
                """;
            command.Parameters.AddWithValue("$lease", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$tenant", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$oldDevice", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$issued", DateTimeOffset.UtcNow.AddDays(-2).ToString("O"));
            command.Parameters.AddWithValue("$expires", DateTimeOffset.UtcNow.AddDays(2).ToString("O"));
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        var handler = new RejectingHistoricalLeaseHandler();
        using var factory = _factory!.WithWebHostBuilder(webHost =>
            webHost.ConfigureServices(services =>
            {
                services.RemoveAll<HttpClient>();
                services.AddSingleton(new HttpClient(handler)
                    { BaseAddress = new Uri("http://127.0.0.1:59999") });
            }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Auraly-Edge-Session", Token);
        using var login = await client.PostAsJsonAsync(
            "/edge/v1/auth/login",
            new PosLocalLoginRequest("cashier", "Cashier-Password-1"));
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<PosLocalUserSession>();
        client.DefaultRequestHeaders.Add("X-Auraly-User-Session", session!.Token);

        using var open = await client.PostAsync("/edge/v1/work-sessions/current", null);

        open.EnsureSuccessStatusCode();
        Assert.Equal(0, handler.ActiveLeaseChecks);
    }

    [Fact]
    public async Task Complete_enrollment_reaches_its_endpoint_without_an_existing_local_login()
    {
        using var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Auraly-Edge-Session", Token);

        using var response = await client.PostAsync("/edge/v1/auth/complete-enrollment", null);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual("LocalLoginRequired", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Health_exposes_a_durable_initial_enrollment_session_for_ui_recovery()
    {
        var store = _factory!.Services.GetRequiredService<PosEdgeEnrollmentStore>();
        var original = store.Load();
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var access = new OfflineAuthenticationLeaseAcquireResponse(
            new SignedOfflineAuthenticationLease("test", "PS256", "payload", "signature"),
            new OfflineAuthenticationLeaseUser(
                userId, "admin", "Administrador", [CommercePermissionCodes.SalesCreate],
                [1], [2], 1, now));
        try
        {
            store.Save(CreateEnrollmentPackage(Guid.NewGuid()) with
            {
                InitialOfflineAccess = access
            });
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Auraly-Edge-Session", Token);

            var health = await client.GetFromJsonAsync<JsonElement>("/edge/v1/health");

            Assert.True(health.GetProperty("initialEnrollmentSessionAvailable").GetBoolean());
            Assert.Equal(2, health.GetProperty("preparationCompletedSteps").GetInt32());
            Assert.Equal(3, health.GetProperty("preparationTotalSteps").GetInt32());
            Assert.False(health.TryGetProperty("printingReady", out _));
            Assert.False(health.TryGetProperty("printerValidationErrors", out _));
        }
        finally
        {
            if (original is null) store.Clear();
            else store.Save(original);
        }
    }

    public async Task InitializeAsync()
    {
        var customerId = Guid.NewGuid();
        var priceChannelId = Guid.NewGuid();
        var tierCustomerId = Guid.NewGuid();
        var tierChannelId = Guid.NewGuid();
        var excludedCustomerId = Guid.NewGuid();
        var excludedChannelId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
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
            ["PosEdge:UserId"] = userId.ToString("D"),
            ["PosEdge:DeviceSeriesCode"] = "03",
            ["PosEdge:Documents:SalesInvoice:Prefix"] = "VTA",
            ["PosEdge:Documents:SalesInvoice:SeriesCode"] = "03",
            ["PosEdge:WarehouseAllowsNegativeStock"] = "true",
            ["PosEdge:TenantId"] = tenantId.ToString("D"),
            ["PosEdge:SupplierTaxId"] = "9001234567",
            ["PosEdge:DefaultCustomerIdentification"] = "222222222",
            ["PosEdge:PrinterName"] = "Test printer",
            ["PosEdge:PaperWidthMillimeters"] = "80",
            ["PosEdge:Documents:SalesInvoice:SeriesId"] = Guid.NewGuid().ToString("D"),
            ["PosEdge:Documents:SalesInvoice:Padding"] = "8",
            ["PosEdge:Documents:SalesInvoice:RangeStart"] = "1",
            ["PosEdge:Documents:SalesInvoice:RangeEnd"] = "99999999",
            ["PosEdge:Documents:SalesReceipt:SeriesId"] = Guid.NewGuid().ToString("D"),
            ["PosEdge:Documents:SalesReceipt:Prefix"] = "CVI",
            ["PosEdge:Documents:SalesReceipt:SeriesCode"] = "03",
            ["PosEdge:Documents:SalesReceipt:Padding"] = "8",
            ["PosEdge:Documents:SalesReceipt:RangeStart"] = "1",
            ["PosEdge:Documents:SalesReceipt:RangeEnd"] = "99999999",
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
            ["PosEdge:Fiscal:ValidFrom"] = "2025-01-01",
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
                services.RemoveAll<IPosWorkSessionClosurePrinter>();
                services.AddSingleton<IPosWorkSessionClosurePrinter>(_closurePrinter);
                services.RemoveAll<HttpClient>();
                services.AddSingleton(new HttpClient(_serverHandler)
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
                    ["sales.create", "sales.change-price", "sales.lines.cost-margin.read", "sales.reprint", "sales.void", "orders.create",
                        "sales.returns.create", "receivables.payments.create", "payables.payments.create",
                        CommercePermissionCodes.SalesRemoveLine,
                        CommercePermissionCodes.SalesRestartDraft,
                        CommercePermissionCodes.SalesDeletePausedDraft,
                        Auraly.Contracts.WorkSessions.WorkSessionPermissionCodes.Close],
                    password)
            ]));
        var loginResponse = await _client.PostAsJsonAsync(
            "/edge/v1/auth/login",
            new PosLocalLoginRequest("cashier", "Cashier-Password-1"));
        if (!loginResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(await loginResponse.Content.ReadAsStringAsync());
        var localSession = await loginResponse.Content.ReadFromJsonAsync<PosLocalUserSession>();
        _userSessionToken = Assert.IsType<string>(localSession!.Token);
        _client.DefaultRequestHeaders.Add("X-Auraly-User-Session", _userSessionToken);
        var openWorkSession = await _client.PostAsync(
            "/edge/v1/work-sessions/current", null);
        if (!openWorkSession.IsSuccessStatusCode)
            throw new InvalidOperationException(await openWorkSession.Content.ReadAsStringAsync());

        var store = scope.ServiceProvider.GetRequiredService<PosCatalogStore>();
        var product = new PosCatalogItem(
            Guid.NewGuid(), "P-1", "REF-1", "Product", "EA", "VAT19", 19m,
            100m, "COP", true, null, ["770123"], []);
        var sessionId = Guid.NewGuid();
        var items = new[] { product };
        var hash = CatalogBootstrapIntegrity.Compute(items);
        await store.BeginBootstrapAsync(
            new CatalogSyncSessionResponse(sessionId, 0, 1, DateTimeOffset.UtcNow.AddHours(1)));
        await store.ApplyBootstrapPageAsync(
            new CatalogBootstrapPage(sessionId, 0, null, false, hash, items));
        await store.PromoteBootstrapAsync();
        await store.ApplyPricingSnapshotAsync(new PosPricingSnapshot(
            [
                new(tierChannelId,"TIER","Escalonado","TieredProductPrice",null),
                new(priceChannelId,"PRICE","Precio","TieredProductPrice",null),
                new(excludedChannelId,"EXCLUDED","Excluido","TieredProductPrice",null)
            ],
            [
                new PosPriceChannelTier(
                    tierChannelId,
                    product.ProductId,
                    1m,
                    90m,
                    "COP"),
                new PosPriceChannelTier(
                    tierChannelId,
                    product.ProductId,
                    3m,
                    70m,
                    "COP"),
                new PosPriceChannelTier(
                    priceChannelId,
                    product.ProductId,
                    1m,
                    80m,
                    "COP"),
                new PosPriceChannelTier(
                    excludedChannelId,
                    product.ProductId,
                    1m,
                    60m,
                    "COP")
            ],
            [new(excludedChannelId,"Product",product.ProductId,null,null)],
            [
                new PosCustomerPricing(
                    customerId,
                    "3001234567",
                    "Cliente POS",
                    priceChannelId,
                    true,
                    AppliesWithholding: true,
                    TaxResponsibilities: ["O-23"],
                    IsCreditEnabled: true,
                    CreditLimit: 500_000m,
                    AvailableCredit: 500_000m,
                    DefaultDueDays: 30,
                    Sites: [new(Guid.NewGuid(), "PRINCIPAL", "Principal", "Calle 1", null, true)]),
                new PosCustomerPricing(
                    tierCustomerId,
                    "4001234567",
                    "Cliente canal escalonado POS",
                    tierChannelId,
                    true,
                    Sites: [new(Guid.NewGuid(), "PRINCIPAL", "Principal", "Calle 2", null, true)]),
                new PosCustomerPricing(
                    excludedCustomerId,
                    "5001234567",
                    "Cliente canal excluido",
                    excludedChannelId,
                    true,
                    Sites: [new(Guid.NewGuid(), "PRINCIPAL", "Principal", "Calle 3", null, true)])
            ],
            [
                new PosWithholdingRule(
                    Guid.NewGuid(), 1, "RF-OFFLINE", "Retefuente venta offline",
                    "IncomeTax", "Sale", "Accrual", "TaxExclusiveAmount",
                    null, null, 2.5m, 0m, ["O-23"],
                    new DateOnly(2026, 1, 1), null, true)
            ]));
    }

    private HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Auraly-Edge-Session", Token);
        client.DefaultRequestHeaders.Add("X-Auraly-User-Session", _userSessionToken);
        return client;
    }

    private async Task<string> DraftStatusAsync(Guid draftId)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={_path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status FROM PosDrafts WHERE DraftId=$id;";
        command.Parameters.AddWithValue("$id", draftId.ToString("D"));
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The local draft does not exist."));
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
        IReadOnlyList<PosCustomerView> Items,
        bool HasMore,
        int? NextOffset);

    private sealed record SaleSearchPageContract(
        IReadOnlyList<PosIssuedSaleSummary> Items,
        bool HasMore,
        int? NextOffset);

    private sealed class RecordingPrinter : IPosReceiptPrinter
    {
        public List<PosReceipt> Receipts { get; } = [];
        public int FailuresRemaining { get; set; }

        public Task PrintAsync(
            PosReceipt receipt,
            CancellationToken cancellationToken = default)
        {
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new IOException("La impresora de prueba no respondió.");
            }
            Receipts.Add(receipt);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingClosurePrinter : IPosWorkSessionClosurePrinter
    {
        public List<WorkSessionClosureView> Closures { get; } = [];
        public int FailuresRemaining { get; set; }

        public Task PrintAsync(
            WorkSessionClosureView closure,
            CancellationToken cancellationToken)
        {
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new IOException("La impresora de prueba no respondió.");
            }
            Closures.Add(closure);
            return Task.CompletedTask;
        }
    }

    private sealed class UnavailableServerHandler : HttpMessageHandler
    {
        public int Count { get; private set; }
        public Func<HttpRequestMessage, Task>? BeforeSend { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage?>? Reply { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Count++;
            if (BeforeSend is not null) await BeforeSend(request);
            if (Reply?.Invoke(request) is { } response) return response;
            throw new HttpRequestException("Auraly Server is offline.");
        }
    }

    private sealed class EnrollmentConflictHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(
                    """
                    {"title":"PosEnrollmentConflict","detail":"La organización alcanzó el máximo de cajas enroladas."}
                    """,
                    Encoding.UTF8,
                    "application/problem+json")
            });
    }

    private sealed class RejectingHistoricalLeaseHandler : HttpMessageHandler
    {
        public int ActiveLeaseChecks { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/active", StringComparison.Ordinal) == true)
            {
                ActiveLeaseChecks++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { active = false })
                });
            }
            return Task.FromException<HttpResponseMessage>(
                new HttpRequestException("Auraly Server is offline."));
        }
    }

    private sealed class RetiredEnrollmentHandler(
        Guid retiredDeviceId,
        PosEnrollmentPackage package) : HttpMessageHandler
    {
        public List<Guid?> ExistingDeviceIds { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var payload = await request.Content!.ReadFromJsonAsync<RedeemPosEnrollmentRequest>(
                cancellationToken: cancellationToken);
            Assert.NotNull(payload);
            ExistingDeviceIds.Add(payload.ExistingDeviceId);
            if (ExistingDeviceIds.Count == 1)
            {
                Assert.Equal(retiredDeviceId, payload.ExistingDeviceId);
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        """
                        {"title":"Bad Request","detail":"El equipo indicado no está enrolado o su serie operativa ya no está activa."}
                        """,
                        Encoding.UTF8,
                        "application/problem+json")
                };
            }

            Assert.Null(payload.ExistingDeviceId);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(package)
            };
        }
    }

    private sealed class MultipleRetiredEnrollmentsHandler(
        IReadOnlyCollection<Guid> retiredDeviceIds,
        PosEnrollmentPackage package) : HttpMessageHandler
    {
        public List<Guid?> ExistingDeviceIds { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var payload = await request.Content!.ReadFromJsonAsync<RedeemPosEnrollmentRequest>(
                cancellationToken: cancellationToken);
            Assert.NotNull(payload);
            ExistingDeviceIds.Add(payload.ExistingDeviceId);
            if (payload.ExistingDeviceId.HasValue &&
                retiredDeviceIds.Contains(payload.ExistingDeviceId.Value))
                return new HttpResponseMessage(HttpStatusCode.Gone)
                {
                    Content = JsonContent.Create(new
                    {
                        title = "PosEnrollmentRetired",
                        detail = "El enrolamiento anterior fue retirado."
                    })
                };

            Assert.Null(payload.ExistingDeviceId);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(package)
            };
        }
    }

    private static async Task SeedRecoverableNumberingAsync(string path, params Guid[] deviceIds)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var schema = connection.CreateCommand();
        schema.CommandText = """
            CREATE TABLE DocumentSeriesCursors(
                SeriesId TEXT PRIMARY KEY,DeviceId TEXT NOT NULL,DocumentType TEXT NOT NULL,
                NextConsecutive INTEGER NOT NULL,RangeEnd INTEGER NOT NULL,IsActive INTEGER NOT NULL);
            CREATE TABLE FiscalSeriesCursors(
                DeviceId TEXT NOT NULL,NextConsecutive INTEGER NOT NULL,
                RangeStart INTEGER NOT NULL,RangeEnd INTEGER NOT NULL,IsActive INTEGER NOT NULL);
            """;
        await schema.ExecuteNonQueryAsync();
        foreach (var deviceId in deviceIds)
            foreach (var documentType in new[] { "SalesInvoice", "SalesReceipt" })
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO DocumentSeriesCursors
                      (SeriesId,DeviceId,DocumentType,NextConsecutive,RangeEnd,IsActive)
                    VALUES($series,$device,$type,1,99999999,1);
                    """;
                insert.Parameters.AddWithValue("$series", Guid.NewGuid().ToString("D"));
                insert.Parameters.AddWithValue("$device", deviceId.ToString("D"));
                insert.Parameters.AddWithValue("$type", documentType);
                await insert.ExecuteNonQueryAsync();
            }
    }

    private static PosEnrollmentPackage CreateEnrollmentPackage(Guid deviceId)
    {
        var seriesCode = "07";
        return new PosEnrollmentPackage(
            deviceId,
            "device-secret",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Negocio principal",
            "01",
            "Bodega principal",
            false,
            Guid.NewGuid(),
            "Administrador",
            new PosEnrollmentDocumentSeries(
                Guid.NewGuid(), "SalesInvoice", "VTA", seriesCode, 8, 1, 99_999_999),
            null,
            new PosEnrollmentDocumentSeries(
                Guid.NewGuid(), "SalesReceipt", "CVI", seriesCode, 8, 1, 99_999_999),
            null,
            DateTimeOffset.UtcNow, ReusesDevice: false, InitialWorkSessions: []);
    }
}
