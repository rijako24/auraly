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
        await using var command = connection.CreateCommand();
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
