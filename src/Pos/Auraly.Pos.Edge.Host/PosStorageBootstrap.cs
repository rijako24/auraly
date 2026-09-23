using Auraly.Application.Authorization;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Extensions.Options;
using Auraly.Contracts.Organization;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Host;

/// <summary>
/// Initializes the local POS store without requiring an enrolled device.
/// </summary>
public static class PosStorageBootstrap
{
    // Called before composing the new host: no local store or synchronizer may
    // use the old database while a new enrollment is being installed.
    public static bool ValidateEnrollmentContinuity(string databasePath, PosEnrollmentPackage package)
    {
        if (package.ReusesDevice is null || package.InitialWorkSessions is null)
            throw new PosEnrollmentServerException(
                "Actualiza Auraly Server antes de preparar nuevamente esta caja. La versión actual no confirma su numeración y sus sesiones.",
                409, "PosEnrollmentUpdateRequired");
        if (!File.Exists(databasePath))
        {
            if (package.ReusesDevice == true) throw MissingNumbering();
            return false;
        }
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        connection.Open();
        using var tables = connection.CreateCommand();
        tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DocumentSeriesCursors';";
        if (Convert.ToInt32(tables.ExecuteScalar()) == 0)
        {
            if (package.ReusesDevice == true) throw MissingNumbering();
            return false;
        }
        using var existing = connection.CreateCommand();
        existing.CommandText = "SELECT COUNT(*) FROM DocumentSeriesCursors WHERE DeviceId=$device COLLATE NOCASE;";
        existing.Parameters.AddWithValue("$device", package.DeviceId.ToString("D"));
        if (Convert.ToInt32(existing.ExecuteScalar()) == 0 && package.ReusesDevice == false) return false;
        tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FiscalSeriesCursors';";
        if (Convert.ToInt32(tables.ExecuteScalar()) == 0) throw MissingNumbering();
        using var numbers = connection.CreateCommand();
        numbers.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM DocumentSeriesCursors
               WHERE DeviceId=$device COLLATE NOCASE AND IsActive=1
                 AND ((SeriesId=$invoice COLLATE NOCASE AND NextConsecutive BETWEEN $invoiceStart AND $invoiceEnd)
                   OR (SeriesId=$receipt COLLATE NOCASE AND NextConsecutive BETWEEN $receiptStart AND $receiptEnd)))
              + (SELECT COUNT(*) FROM FiscalSeriesCursors
                 WHERE DeviceId=$device COLLATE NOCASE AND SeriesId=$fiscal COLLATE NOCASE
                   AND NextConsecutive BETWEEN $fiscalStart AND $fiscalEnd);
            """;
        numbers.Parameters.AddWithValue("$device", package.DeviceId.ToString("D"));
        numbers.Parameters.AddWithValue("$invoice", package.DocumentSeries.SeriesId.ToString("D"));
        numbers.Parameters.AddWithValue("$invoiceStart", package.DocumentSeries.RangeStart);
        numbers.Parameters.AddWithValue("$invoiceEnd", checked(package.DocumentSeries.RangeEnd + 1));
        numbers.Parameters.AddWithValue("$receipt", package.ReceiptDocumentSeries.SeriesId.ToString("D"));
        numbers.Parameters.AddWithValue("$receiptStart", package.ReceiptDocumentSeries.RangeStart);
        numbers.Parameters.AddWithValue("$receiptEnd", checked(package.ReceiptDocumentSeries.RangeEnd + 1));
        numbers.Parameters.AddWithValue("$fiscal", package.FiscalSeries?.SeriesId.ToString("D") ?? "");
        numbers.Parameters.AddWithValue("$fiscalStart", package.FiscalSeries?.RangeStart ?? 0);
        numbers.Parameters.AddWithValue("$fiscalEnd", checked((package.FiscalSeries?.RangeEnd ?? 0) + 1));
        if (Convert.ToInt32(numbers.ExecuteScalar()) != (package.FiscalSeries is null ? 2 : 3))
            throw MissingNumbering();
        return true;
    }

    private static PosEnrollmentServerException MissingNumbering() => new(
        "No se puede reutilizar esta caja porque falta su último consecutivo local. Su numeración no se reiniciará. Revisa la instalación o desenrola el dispositivo antes de preparar uno nuevo.",
        409, "PosEnrollmentNumberingUnavailable");

    public static void ValidateRecoveredNumbering(string databasePath, IReadOnlyList<Guid> deviceIds)
    {
        if (deviceIds.Count == 0) return;
        if (!File.Exists(databasePath)) throw MissingNumbering();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        connection.Open();
        using var tables = connection.CreateCommand();
        tables.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type='table' AND name IN ('DocumentSeriesCursors','FiscalSeriesCursors');
            """;
        if (Convert.ToInt32(tables.ExecuteScalar()) != 2) throw MissingNumbering();
        using var numbers = connection.CreateCommand();
        numbers.CommandText = """
            SELECT DeviceId,COUNT(DISTINCT DocumentType)
            FROM DocumentSeriesCursors
            WHERE DeviceId COLLATE NOCASE IN (SELECT value FROM json_each($devices))
              AND IsActive=1
              AND DocumentType IN ('SalesInvoice','SalesReceipt')
              AND NextConsecutive>=1 AND NextConsecutive<=RangeEnd+1
            GROUP BY DeviceId;
            """;
        numbers.Parameters.AddWithValue("$devices", System.Text.Json.JsonSerializer.Serialize(
            deviceIds.Select(deviceId => deviceId.ToString("D"))));
        try
        {
            var valid = new HashSet<Guid>();
            using (var reader = numbers.ExecuteReader())
                while (reader.Read())
                    if (reader.GetInt32(1) == 2 && Guid.TryParse(reader.GetString(0), out var deviceId))
                        valid.Add(deviceId);
            if (deviceIds.Any(deviceId => !valid.Contains(deviceId))) throw MissingNumbering();
            numbers.CommandText = """
                SELECT COUNT(*) FROM FiscalSeriesCursors
                WHERE DeviceId COLLATE NOCASE IN (SELECT value FROM json_each($devices))
                  AND IsActive=1
                  AND (NextConsecutive<RangeStart OR NextConsecutive>RangeEnd+1);
                """;
            if (Convert.ToInt32(numbers.ExecuteScalar()) != 0) throw MissingNumbering();
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1)
        {
            throw MissingNumbering();
        }
    }

    public static void ResetForNewEnrollment(string databasePath, PosEnrollmentPackage package)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var preserveNumbering = ValidateEnrollmentContinuity(databasePath, package);
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath)!;
        using var pool = new SqliteConnection($"Data Source={fullPath}");
        SqliteConnection.ClearPool(pool);
        if (preserveNumbering)
            ResetOperationalRows(fullPath, package);
        else
            foreach (var file in new[] { fullPath, fullPath + "-wal", fullPath + "-shm", fullPath + "-journal" })
                if (File.Exists(file)) File.Delete(file);
        foreach (var file in new[] { Path.Combine(directory, "printer-settings.json"), Path.Combine(directory, "printer-settings.json.new") })
            if (File.Exists(file)) File.Delete(file);
        var receipts = Path.GetFullPath(Path.Combine(directory, "receipts"));
        if (!string.Equals(Path.GetDirectoryName(receipts), directory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The receipt directory is outside the local POS storage.");
        if (Directory.Exists(receipts)) Directory.Delete(receipts, recursive: true);
    }

    private static void ResetOperationalRows(string databasePath, PosEnrollmentPackage package)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false, ForeignKeys = false
        }.ToString());
        connection.Open();
        using var security = connection.CreateCommand();
        security.CommandText = "PRAGMA secure_delete=ON;";
        security.ExecuteNonQuery();
        using var transaction = connection.BeginTransaction();
        using var tables = connection.CreateCommand();
        tables.Transaction = transaction;
        tables.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
        var deletes = new List<string>();
        using (var reader = tables.ExecuteReader())
            while (reader.Read())
            {
                var name = reader.GetString(0);
                if (name.StartsWith("sqlite_", StringComparison.Ordinal) ||
                    name is "DocumentSeriesCursors" or "FiscalSeriesCursors" or "PosOutboxSequence" or "__EFMigrationsHistory") continue;
                deletes.Add($"DELETE FROM \"{name.Replace("\"", "\"\"")}\";");
            }
        using var reset = connection.CreateCommand();
        reset.Transaction = transaction;
        reset.CommandText = string.Join('\n', deletes) + """

            DELETE FROM DocumentSeriesCursors WHERE DeviceId<>$device COLLATE NOCASE
              OR (SeriesId<>$invoice COLLATE NOCASE AND SeriesId<>$receipt COLLATE NOCASE);
            DELETE FROM FiscalSeriesCursors WHERE DeviceId<>$device COLLATE NOCASE OR SeriesId<>$fiscal COLLATE NOCASE;
            """;
        reset.Parameters.AddWithValue("$device", package.DeviceId.ToString("D"));
        reset.Parameters.AddWithValue("$invoice", package.DocumentSeries.SeriesId.ToString("D"));
        reset.Parameters.AddWithValue("$receipt", package.ReceiptDocumentSeries.SeriesId.ToString("D"));
        reset.Parameters.AddWithValue("$fiscal", package.FiscalSeries?.SeriesId.ToString("D") ?? "");
        reset.ExecuteNonQuery();
        PosLocalWorkSessionStore.RestoreEnrollmentSessions(connection, transaction, package);
        transaction.Commit();
    }

    public static async Task InitializeAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var connectionString = $"Data Source={fullPath}";
        var keyDirectory = Path.Combine(directory ?? AppContext.BaseDirectory, "keys");
        Directory.CreateDirectory(keyDirectory);
        var clock = TimeProvider.System;
        var ids = new Uuid7AuralyIdGenerator(clock);
        var identities = new PosLocalIdentityStore(connectionString, keyDirectory, ids, clock);
        var leaseVerifier = new PosOfflineLeaseVerifier(Options.Create(new PosOfflineLeaseTrustOptions()));
        var leases = new PosOfflineLeaseStore(connectionString, Guid.Empty, Guid.Empty, leaseVerifier, clock);
        var catalog = new PosCatalogStore(connectionString);
        var drafts = new PosDraftStore(connectionString, ids, clock);
        var sales = new PosEdgeSaleStore(
            connectionString,
            new ConfirmOfflineSaleService(
                new PermissionAuthorizer(new PosLocalPermissionProvider(new PosLocalSessionAccessor()))));
        var issuance = new PosDraftIssuanceStore(connectionString, ids, clock);

        // EF EnsureCreated must run before the hand-written stores create their
        // tables; otherwise an existing SQLite file makes EF skip its model.
        await sales.InitializeAsync(cancellationToken);
        await issuance.InitializeAsync(cancellationToken);
        await identities.InitializeAsync(cancellationToken);
        await leases.InitializeAsync(cancellationToken);
        await catalog.InitializeAsync(cancellationToken);
        await drafts.InitializeAsync(cancellationToken);
    }
}
