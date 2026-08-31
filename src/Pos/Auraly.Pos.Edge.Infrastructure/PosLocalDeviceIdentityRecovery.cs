using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed class PosLocalDeviceIdentityRecovery(string databasePath)
{
    public Guid? ReadSingleDeviceId()
    {
        if (!File.Exists(databasePath)) return null;
        using var connection = new SqliteConnection(
            $"Data Source={databasePath};Mode=ReadOnly;Cache=Private;Pooling=False");
        connection.Open();
        using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText = """
            SELECT COUNT(1)
            FROM sqlite_master
            WHERE type='table' AND name='DocumentSeriesCursors';
            """;
        if (Convert.ToInt64(tableCommand.ExecuteScalar()) == 0) return null;

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT DeviceId
            FROM DocumentSeriesCursors
            WHERE IsActive=1
            ORDER BY DeviceId;
            """;
        using var reader = command.ExecuteReader();
        Guid? recovered = null;
        while (reader.Read())
        {
            if (!Guid.TryParse(reader.GetString(0), out var candidate) ||
                candidate == Guid.Empty)
                throw new InvalidDataException(
                    "La identidad local de esta caja no es válida.");
            if (recovered.HasValue && recovered.Value != candidate)
                throw new InvalidDataException(
                    "La base local contiene más de una identidad de caja y no puede recuperarse automáticamente.");
            recovered = candidate;
        }
        return recovered;
    }

    public void Retire(Guid deviceId)
    {
        if (deviceId == Guid.Empty || !File.Exists(databasePath)) return;
        using var connection = new SqliteConnection(
            $"Data Source={databasePath};Mode=ReadWrite;Cache=Private;Pooling=False");
        connection.Open();
        using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText = """
            SELECT COUNT(1)
            FROM sqlite_master
            WHERE type='table' AND name='DocumentSeriesCursors';
            """;
        if (Convert.ToInt64(tableCommand.ExecuteScalar()) == 0) return;

        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DocumentSeriesCursors
            SET IsActive=0
            WHERE DeviceId=$deviceId AND IsActive=1;
            """;
        command.Parameters.AddWithValue("$deviceId", deviceId.ToString("D"));
        command.ExecuteNonQuery();
    }
}
