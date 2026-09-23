using System.Diagnostics;
using Auraly.Application.Authorization;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Organization;
using Auraly.Contracts.WorkSessions;
using Auraly.Pos.Edge.Host;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosEnrollmentResetTests(Xunit.Abstractions.ITestOutputHelper output) : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"auraly-reset-{Guid.NewGuid():N}");
    private string Database => Path.Combine(directory, "auraly-pos.db");
    private string Keys => Path.Combine(directory, "keys");
    private PosEdgeEnrollmentStore Enrollments => new(Path.Combine(directory, "enrollment.protected"), Keys, Database);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reenrollment_clears_operational_data_but_keeps_numbering_and_server_session(bool reusesDevice)
    {
        var package = Package() with { ReusesDevice = reusesDevice };
        var remoteSession = new DeviceWorkSessionSnapshot(Guid.NewGuid(), package.InitialUserId, DateTimeOffset.UtcNow.AddHours(-2));
        package = package with { InitialWorkSessions = [remoteSession] };
        await PosStorageBootstrap.InitializeAsync(Database);
        var sales = new PosEdgeSaleStore($"Data Source={Database}",
            new ConfirmOfflineSaleService(new PermissionAuthorizer(new PosLocalPermissionProvider(new PosLocalSessionAccessor()))));
        foreach (var series in new[] { package.DocumentSeries, package.ReceiptDocumentSeries })
            await sales.ProvisionDocumentSeriesAsync(new(series.SeriesId, new(package.DeviceId), series.DocumentType,
                series.Prefix, series.SeriesCode, series.Padding, series.RangeStart, series.RangeEnd));
        var fiscal = package.FiscalSeries!;
        await sales.ProvisionSeriesAsync(new(fiscal.SeriesId, new(package.DeviceId), fiscal.Prefix,
            fiscal.AuthorizationNumber, fiscal.RangeStart, fiscal.RangeEnd, fiscal.ValidUntil, fiscal.FiscalAuthorizationId));
        var ids = new Uuid7AuralyIdGenerator(TimeProvider.System);
        var identity = new PosLocalIdentityStore($"Data Source={Database}", Keys, ids, TimeProvider.System);
        var password = PosOfflinePasswordHasher.Hash("Test-Cashier-Password", DateTimeOffset.UtcNow);
        await identity.ApplySnapshotAsync(new("old", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1),
            [new(Guid.NewGuid(), "cashier", "Old cashier", ["sales.create"], password)]));
        var sessions = new PosLocalWorkSessionStore($"Data Source={Database}", ids, TimeProvider.System,
            new(new(package.TenantId), new(package.BusinessId), new(package.WarehouseId), new(package.DeviceId), false));
        await sessions.InitializeAsync();
        await sessions.OpenOrResumeAsync(Guid.NewGuid());
        await Sql("""
            UPDATE DocumentSeriesCursors SET NextConsecutive=51;
            UPDATE FiscalSeriesCursors SET NextConsecutive=501;
            CREATE TABLE LegacySensitiveRows(Payload BLOB NOT NULL);
            WITH RECURSIVE n(value) AS (SELECT 1 UNION ALL SELECT value+1 FROM n WHERE value<1000)
            INSERT INTO LegacySensitiveRows SELECT randomblob(4096) FROM n;
            """);
        var enrollmentId = Guid.NewGuid();
        Enrollments.SaveForNewEnrollment(package, enrollmentId);
        var elapsed = Stopwatch.StartNew();
        Enrollments.ResetLocalStorageIfRequired(Database);
        elapsed.Stop();
        output.WriteLine($"Operational reset: {elapsed.Elapsed.TotalMilliseconds:F1} ms; 1000 rows/4 MiB; 0 HTTP requests.");

        Assert.Equal(0L, await Sql("SELECT COUNT(*) FROM LegacySensitiveRows;"));
        Assert.Equal(51L, await Sql("SELECT MIN(NextConsecutive) FROM DocumentSeriesCursors;"));
        Assert.Equal(501L, await Sql("SELECT NextConsecutive FROM FiscalSeriesCursors;"));
        Assert.False(await identity.HasIdentitySnapshotAsync());
        // The old username may belong to a different server user after a fresh preparation.
        await identity.ApplySnapshotAsync(new("new", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1),
            [new(package.InitialUserId, "cashier", "Current cashier", ["sales.create"], password)]));
        await sessions.InitializeAsync();
        Assert.Equal(remoteSession.WorkSessionId, (await sessions.OpenOrResumeAsync(package.InitialUserId)).WorkSessionId);
        Assert.Equal(1L, await Sql("SELECT COUNT(*) FROM Outbox;"));
        Assert.Equal("Uploaded", await Sql("SELECT Status FROM Outbox;"));
        Assert.Equal(package.DeviceId, Enrollments.Load()!.DeviceId);
        Assert.False(Enrollments.LoadResultForEnrollment(enrollmentId)!.RestartRequired);
        await Sql("INSERT INTO LegacySensitiveRows VALUES(X'01');");
        Enrollments.ResetLocalStorageIfRequired(Database);
        Assert.Equal(1L, await Sql("SELECT COUNT(*) FROM LegacySensitiveRows;"));
    }

    [Fact]
    public async Task Missing_numbering_rejects_reuse_before_marking_or_clearing_local_data()
    {
        await PosStorageBootstrap.InitializeAsync(Database);
        await Sql("CREATE TABLE KeepUntilAccepted(Value INTEGER); INSERT INTO KeepUntilAccepted VALUES(1);");
        var error = Assert.Throws<PosEnrollmentServerException>(() =>
            Enrollments.SaveForNewEnrollment(Package(), Guid.NewGuid()));
        Assert.Equal("PosEnrollmentNumberingUnavailable", error.Title);
        Assert.Null(Enrollments.Load());
        Assert.Equal(1L, await Sql("SELECT COUNT(*) FROM KeepUntilAccepted;"));
    }

    [Fact]
    public async Task Missing_local_database_rejects_existing_identity_before_contacting_server()
    {
        Enrollments.Save(Package() with { ReusesDevice = false });
        var handler = new UnexpectedEnrollmentRequestHandler();
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var client = new PosEdgeEnrollmentClient(
            http, Enrollments, new PosLocalDeviceIdentityRecovery(Database));

        var error = await Assert.ThrowsAsync<PosEnrollmentServerException>(() =>
            client.RedeemAsync(new LocalPosEnrollmentRequest(Guid.NewGuid(), "code")));

        Assert.Equal("PosEnrollmentNumberingUnavailable", error.Title);
        Assert.Equal(0, handler.RequestCount);
        Assert.False(File.Exists(Database));
    }

    [Fact]
    public async Task Incomplete_recovered_numbering_rejects_reuse_before_contacting_server()
    {
        var package = Package();
        await PosStorageBootstrap.InitializeAsync(Database);
        var sales = new PosEdgeSaleStore($"Data Source={Database}",
            new ConfirmOfflineSaleService(new PermissionAuthorizer(
                new PosLocalPermissionProvider(new PosLocalSessionAccessor()))));
        await sales.ProvisionDocumentSeriesAsync(new(
            package.DocumentSeries.SeriesId, new(package.DeviceId),
            package.DocumentSeries.DocumentType, package.DocumentSeries.Prefix,
            package.DocumentSeries.SeriesCode, package.DocumentSeries.Padding,
            package.DocumentSeries.RangeStart, package.DocumentSeries.RangeEnd));
        var handler = new UnexpectedEnrollmentRequestHandler();
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var client = new PosEdgeEnrollmentClient(
            http, Enrollments, new PosLocalDeviceIdentityRecovery(Database));

        var error = await Assert.ThrowsAsync<PosEnrollmentServerException>(() =>
            client.RedeemAsync(new LocalPosEnrollmentRequest(Guid.NewGuid(), "code")));

        Assert.Equal("PosEnrollmentNumberingUnavailable", error.Title);
        Assert.Equal(0, handler.RequestCount);
        Assert.Null(Enrollments.Load());
    }

    [Fact]
    public async Task Logo_failure_after_server_redemption_preserves_identity_for_retry()
    {
        var package = Package() with
        {
            ReusesDevice = false,
            CompanyLogoSource = "http://invalid-logo.example.test/logo.png"
        };
        var handler = new EnrollmentPackageHandler(package);
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var client = new PosEdgeEnrollmentClient(
            http, Enrollments, new PosLocalDeviceIdentityRecovery(Database));
        var request = new LocalPosEnrollmentRequest(Guid.NewGuid(), "code");

        await Assert.ThrowsAsync<InvalidDataException>(() => client.RedeemAsync(request));
        Assert.Equal(package.DeviceId, Enrollments.Load()!.DeviceId);
        Assert.True(Enrollments.LoadResultForEnrollment(request.EnrollmentSessionId)!.RestartRequired);
        Assert.True((await client.RedeemAsync(request)).RestartRequired);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public void Old_server_cannot_trigger_a_reset_without_a_continuity_contract()
    {
        var error = Assert.Throws<PosEnrollmentServerException>(() => Enrollments.SaveForNewEnrollment(
            Package() with { ReusesDevice = null, InitialWorkSessions = null }, Guid.NewGuid()));
        Assert.Equal("PosEnrollmentUpdateRequired", error.Title);
        Assert.Null(Enrollments.Load());
    }

    private async Task<object?> Sql(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static PosEnrollmentPackage Package() => new(
        Guid.NewGuid(), "test-secret", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "Business", "01", "Warehouse", false, Guid.NewGuid(), "Cashier",
        new(Guid.NewGuid(), "SalesInvoice", "VTA", "07", 8, 1, 99999999),
        new(Guid.NewGuid(), Guid.NewGuid(), "FV", "Authorization", 1, 1000,
            new DateOnly(2028, 1, 1), 2, "900123456", "test-key", "v1", "https://example.test"),
        new(Guid.NewGuid(), "SalesReceipt", "CVI", "07", 8, 1, 99999999),
        null, DateTimeOffset.UtcNow, ReusesDevice: true, InitialWorkSessions: []);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private sealed class UnexpectedEnrollmentRequestHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new InvalidOperationException("The server must not be contacted without local numbering.");
        }
    }

    private sealed class EnrollmentPackageHandler(PosEnrollmentPackage package) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = System.Net.Http.Json.JsonContent.Create(package)
            });
        }
    }
}
