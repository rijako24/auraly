using Auraly.Application.Authorization;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Authorization;
using Auraly.Domain.Authorization;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Auraly.Foundation.Tests;

public sealed class PosEdgeSchemaUpgradeTests
{
    [Fact]
    public async Task Initialize_upgrades_previous_series_schema_without_losing_the_cursor()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"auraly-pos-edge-upgrade-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();
        var seriesId = Guid.NewGuid();
        var deviceId = new DeviceId(Guid.NewGuid());
        var authorizationId = Guid.NewGuid();
        try
        {
            await CreatePreviousSchemaAsync(connectionString, seriesId, deviceId);
            var userId = new UserId(Guid.NewGuid());
            var permissionSet = new UserPermissionSet(
                new TenantId(Guid.NewGuid()),
                userId,
                [CommercePermissionCodes.SalesCreate]);
            var store = new PosEdgeSaleStore(
                connectionString,
                new ConfirmOfflineSaleService(
                    new PermissionAuthorizer(new FixedPermissionProvider(permissionSet))));

            await store.InitializeAsync();
            await store.ProvisionSeriesAsync(new PosEdgeSeriesProvision(
                seriesId,
                deviceId,
                "FV01",
                "18760000001",
                1,
                100,
                new DateOnly(2027, 7, 27),
                authorizationId));

            await using var verification = new SqliteConnection(connectionString);
            await verification.OpenAsync();
            await using var command = verification.CreateCommand();
            command.CommandText =
                """
                SELECT FiscalAuthorizationId, NextConsecutive,
                    EXISTS(SELECT 1 FROM pragma_table_info('IssuedSales') WHERE name='DocumentNumber'),
                    EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name='DocumentSeriesCursors'),
                    EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name='SalesDocumentTaxSummaries'),
                    EXISTS(SELECT 1 FROM pragma_table_info('IssuedSales') WHERE name='RemoteFiscalStatus'),
                    EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name='PosSyncState'),
                    EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name='PosPrintAudit')
                FROM FiscalSeriesCursors;
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(authorizationId, reader.GetGuid(0));
            Assert.Equal(7L, reader.GetInt64(1));
            Assert.Equal(1L, reader.GetInt64(2));
            Assert.Equal(1L, reader.GetInt64(3));
            Assert.Equal(1L, reader.GetInt64(5));
            Assert.Equal(1L, reader.GetInt64(6));
            Assert.Equal(1L, reader.GetInt64(7));
            Assert.Equal(0L, reader.GetInt64(4));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfPresent(databasePath);
            DeleteIfPresent($"{databasePath}-wal");
            DeleteIfPresent($"{databasePath}-shm");
        }
    }

    [Fact]
    public async Task Provisioning_the_same_durable_series_after_device_identity_refresh_preserves_local_cursors()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"auraly-pos-edge-series-refresh-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();
        var previousDevice = new DeviceId(Guid.NewGuid());
        var refreshedDevice = new DeviceId(Guid.NewGuid());
        var documentSeriesId = Guid.NewGuid();
        var fiscalSeriesId = Guid.NewGuid();
        var authorizationId = Guid.NewGuid();
        try
        {
            var userId = new UserId(Guid.NewGuid());
            var permissions = new UserPermissionSet(
                new TenantId(Guid.NewGuid()),
                userId,
                [CommercePermissionCodes.SalesCreate]);
            var authorization = new PermissionAuthorizer(new FixedPermissionProvider(permissions));
            var firstStore = new PosEdgeSaleStore(
                connectionString,
                new ConfirmOfflineSaleService(authorization));
            await firstStore.InitializeAsync();
            await firstStore.ProvisionDocumentSeriesAsync(new PosEdgeDocumentSeriesProvision(
                documentSeriesId,
                previousDevice,
                AuralyDocumentTypes.SalesInvoice,
                AuralyDocumentTypes.DefaultPrefix(AuralyDocumentTypes.SalesInvoice),
                "01",
                AuralyDocumentNumberAssignment.CanonicalPadding,
                1,
                100));
            await firstStore.ProvisionSeriesAsync(new PosEdgeSeriesProvision(
                fiscalSeriesId,
                previousDevice,
                "FE",
                "18760000001",
                1,
                100,
                new DateOnly(2027, 7, 27),
                authorizationId));

            var restartedStore = new PosEdgeSaleStore(
                connectionString,
                new ConfirmOfflineSaleService(authorization));
            await restartedStore.InitializeAsync();
            await restartedStore.ProvisionDocumentSeriesAsync(new PosEdgeDocumentSeriesProvision(
                documentSeriesId,
                refreshedDevice,
                AuralyDocumentTypes.SalesInvoice,
                AuralyDocumentTypes.DefaultPrefix(AuralyDocumentTypes.SalesInvoice),
                "01",
                AuralyDocumentNumberAssignment.CanonicalPadding,
                1,
                100));
            await restartedStore.ProvisionSeriesAsync(new PosEdgeSeriesProvision(
                fiscalSeriesId,
                refreshedDevice,
                "FE",
                "18760000001",
                1,
                100,
                new DateOnly(2027, 7, 27),
                authorizationId));

            var document = await restartedStore.PreviewNextDocumentNumberAsync(
                refreshedDevice,
                AuralyDocumentTypes.SalesInvoice);
            var fiscal = await restartedStore.PreviewNextFiscalNumberAsync(
                refreshedDevice,
                DateTimeOffset.UtcNow);

            Assert.Equal(documentSeriesId, document.SeriesId);
            Assert.Equal(1, document.Consecutive);
            Assert.Equal(fiscalSeriesId, fiscal.SeriesId);
            Assert.Equal(1, fiscal.Consecutive);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfPresent(databasePath);
            DeleteIfPresent($"{databasePath}-wal");
            DeleteIfPresent($"{databasePath}-shm");
        }
    }
    private static async Task CreatePreviousSchemaAsync(
        string connectionString,
        Guid seriesId,
        DeviceId deviceId)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE FiscalSeriesCursors (
                SeriesId TEXT NOT NULL PRIMARY KEY,
                DeviceId TEXT NOT NULL,
                Prefix TEXT NOT NULL,
                AuthorizationNumber TEXT NOT NULL,
                NextConsecutive INTEGER NOT NULL,
                RangeEnd INTEGER NOT NULL,
                ValidUntil TEXT NOT NULL,
                IsActive INTEGER NOT NULL
            );
            CREATE UNIQUE INDEX IX_FiscalSeriesCursors_DeviceId
                ON FiscalSeriesCursors(DeviceId);
            INSERT INTO FiscalSeriesCursors (
                SeriesId, DeviceId, Prefix, AuthorizationNumber,
                NextConsecutive, RangeEnd, ValidUntil, IsActive)
            VALUES (
                $seriesId, $deviceId, 'FV01', '18760000001',
                7, 100, '2027-07-27', 1);
            CREATE TABLE IssuedSales (
                DocumentId TEXT NOT NULL PRIMARY KEY,
                FiscalNumber TEXT NOT NULL,
                Consecutive INTEGER NOT NULL,
                IssuedAt TEXT NOT NULL,
                Cufe TEXT NOT NULL,
                FiscalSnapshot TEXT NOT NULL,
                Total TEXT NOT NULL,
                Status TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE Outbox (
                MessageId TEXT NOT NULL PRIMARY KEY,
                DocumentId TEXT NOT NULL,
                Type TEXT NOT NULL,
                Payload TEXT NOT NULL,
                Status TEXT NOT NULL,
                AttemptCount INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UploadedAt TEXT NULL
            );
            CREATE UNIQUE INDEX IX_Outbox_DocumentId
                ON Outbox(DocumentId);
            """;
        command.Parameters.AddWithValue("$seriesId", seriesId.ToString("D").ToUpperInvariant());
        command.Parameters.AddWithValue("$deviceId", deviceId.Value.ToString("D").ToUpperInvariant());
        await command.ExecuteNonQueryAsync();
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class FixedPermissionProvider(UserPermissionSet permissionSet)
        : IUserPermissionSetProvider
    {
        public UserPermissionSet Get(TenantId tenantId, UserId userId) => permissionSet;
    }
}
