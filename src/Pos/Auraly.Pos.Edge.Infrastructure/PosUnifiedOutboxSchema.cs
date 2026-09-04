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
              LocalSequence INTEGER NULL,
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
            ["LocalSequence"] = "INTEGER NULL",
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
            CREATE TABLE IF NOT EXISTS PosOutboxSequence(
              Singleton INTEGER NOT NULL PRIMARY KEY CHECK(Singleton=1),
              NextValue INTEGER NOT NULL);
            UPDATE Outbox
            SET LocalSequence=(SELECT COUNT(1) FROM Outbox prior WHERE prior.rowid<=Outbox.rowid)
            WHERE LocalSequence IS NULL;
            INSERT INTO PosOutboxSequence(Singleton,NextValue)
            VALUES(1,COALESCE((SELECT MAX(LocalSequence) FROM Outbox),0))
            ON CONFLICT(Singleton) DO UPDATE SET
              NextValue=MAX(NextValue,excluded.NextValue);
            CREATE TRIGGER IF NOT EXISTS TR_Outbox_AssignLocalSequence
            AFTER INSERT ON Outbox
            WHEN NEW.LocalSequence IS NULL
            BEGIN
              UPDATE PosOutboxSequence SET NextValue=NextValue+1 WHERE Singleton=1;
              UPDATE Outbox SET LocalSequence=(
                SELECT NextValue FROM PosOutboxSequence WHERE Singleton=1)
              WHERE rowid=NEW.rowid;
            END;
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Outbox_DocumentId
              ON Outbox(DocumentId);
            CREATE UNIQUE INDEX IF NOT EXISTS UX_Outbox_LocalSequence
              ON Outbox(LocalSequence);
            CREATE INDEX IF NOT EXISTS IX_Outbox_WorkSessionId_CreatedAt
              ON Outbox(WorkSessionId,CreatedAt);
            CREATE INDEX IF NOT EXISTS IX_Outbox_Dispatch
              ON Outbox(Status,NextAttemptAt,LocalSequence);
            """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
