using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Infrastructure;

public static class PosUnifiedOutboxSchema
{
    public static async Task EnsureCreatedAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
            CREATE TABLE IF NOT EXISTS Outbox(
              MessageId TEXT NOT NULL PRIMARY KEY,
              DocumentId TEXT NOT NULL,
              WorkSessionId TEXT NULL,
              Type TEXT NOT NULL,
              Payload TEXT NOT NULL,
              Status TEXT NOT NULL,
              AttemptCount INTEGER NOT NULL DEFAULT 0,
              CreatedAt TEXT NOT NULL,
              UploadedAt TEXT NULL,
              NextAttemptAt TEXT NULL,
              LeaseAcquiredAt TEXT NULL,
              LastAttemptAt TEXT NULL,
              LastError TEXT NULL,
              RemoteStatus TEXT NULL,
              ServerReceiptId TEXT NULL);
            """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('Outbox');";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                columns.Add(reader.GetString(1));
        }

        // Outbox is the sole local coordination table. Keep upgrades additive so an
        // installed cashier preserves pending documents when a newer client starts.
        var additions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WorkSessionId"] = "TEXT NULL",
            ["NextAttemptAt"] = "TEXT NULL",
            ["LeaseAcquiredAt"] = "TEXT NULL",
            ["LastAttemptAt"] = "TEXT NULL",
            ["LastError"] = "TEXT NULL",
            ["RemoteStatus"] = "TEXT NULL",
            ["ServerReceiptId"] = "TEXT NULL"
        };
        foreach (var addition in additions.Where(item => !columns.Contains(item.Key)))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER TABLE Outbox ADD COLUMN {addition.Key} {addition.Value};";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Outbox_DocumentId
              ON Outbox(DocumentId);
            CREATE INDEX IF NOT EXISTS IX_Outbox_WorkSessionId_CreatedAt
              ON Outbox(WorkSessionId,CreatedAt);
            CREATE INDEX IF NOT EXISTS IX_Outbox_Dispatch
              ON Outbox(Status,NextAttemptAt,CreatedAt);
            """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
