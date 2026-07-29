using Auraly.Application.Authorization;
using Auraly.Application.Sales;
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
        var seriesId = Guid.NewGuid();
        var registerId = new RegisterId(Guid.NewGuid());
        var authorizationId = Guid.NewGuid();
        try
        {
            await CreatePreviousSchemaAsync(databasePath, seriesId, registerId);
            var userId = new UserId(Guid.NewGuid());
            var permissionSet = new UserPermissionSet(
                new TenantId(Guid.NewGuid()),
                userId,
                [CommercePermissionCodes.SalesCreate]);
            var store = new PosEdgeSaleStore(
                $"Data Source={databasePath}",
                new ConfirmOfflineSaleService(
                    new PermissionAuthorizer(new FixedPermissionProvider(permissionSet))));

            await store.InitializeAsync();
            await store.ProvisionSeriesAsync(new PosEdgeSeriesProvision(
                seriesId,
                registerId,
                "FV01",
                "18760000001",
                1,
                100,
                new DateOnly(2027, 7, 27),
                authorizationId));

            await using var verification = new SqliteConnection($"Data Source={databasePath}");
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

    private static async Task CreatePreviousSchemaAsync(
        string databasePath,
        Guid seriesId,
        RegisterId registerId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE FiscalSeriesCursors (
                SeriesId TEXT NOT NULL PRIMARY KEY,
                RegisterId TEXT NOT NULL,
                Prefix TEXT NOT NULL,
                AuthorizationNumber TEXT NOT NULL,
                NextConsecutive INTEGER NOT NULL,
                RangeEnd INTEGER NOT NULL,
                ValidUntil TEXT NOT NULL,
                IsActive INTEGER NOT NULL
            );
            CREATE UNIQUE INDEX IX_FiscalSeriesCursors_RegisterId
                ON FiscalSeriesCursors(RegisterId);
            INSERT INTO FiscalSeriesCursors (
                SeriesId, RegisterId, Prefix, AuthorizationNumber,
                NextConsecutive, RangeEnd, ValidUntil, IsActive)
            VALUES (
                $seriesId, $registerId, 'FV01', '18760000001',
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
        command.Parameters.AddWithValue("$registerId", registerId.Value.ToString("D").ToUpperInvariant());
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
