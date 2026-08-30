using Auraly.Contracts.Catalog;
using Auraly.Contracts.Authorization;
using Auraly.Pos.Edge.Host;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosSynchronizationEventLogTests
{
    [Fact]
    public void Product_price_event_preserves_previous_and_new_values()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 27, 14, 30, 0, TimeSpan.Zero));
        var log = new PosSynchronizationEventLog(clock);
        var productId = Guid.NewGuid();
        var previous = Product(productId, 10_000m);
        var current = Product(productId, 12_500m);

        log.ProductReceived(current, previous, bootstrap: false);

        var value = Assert.Single(log.Read());
        Assert.Equal(clock.GetUtcNow(), value.OccurredAt);
        Assert.Equal(productId, value.ProductId);
        Assert.Equal(10_000m, value.PreviousPrice);
        Assert.Equal(12_500m, value.NewPrice);
        Assert.Contains("Precio actualizado", value.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Customer_event_identifies_the_customer_that_was_received()
    {
        var log = new PosSynchronizationEventLog(TimeProvider.System);
        var customer = new PosCustomerPricing(
            Guid.NewGuid(), "900123456", "Cliente sincronizado", null, true);

        log.CustomerReceived(customer, previous: null);

        var value = Assert.Single(log.Read());
        Assert.Equal("Cliente", value.Category);
        Assert.Contains(customer.Name, value.Title, StringComparison.Ordinal);
        Assert.Contains(customer.Identification, value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Channel_price_event_preserves_product_and_price_detail()
    {
        var log = new PosSynchronizationEventLog(TimeProvider.System);
        var productId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var previous = new PosPriceChannelItem(channelId, productId, 5m, 10_000m, "COP", false);
        var current = previous with { Amount = 9_500m };

        log.ChannelPriceReceived(current, previous, "Producto sincronizado");

        var value = Assert.Single(log.Read());
        Assert.Equal("Precio", value.Category);
        Assert.Equal(productId, value.ProductId);
        Assert.Equal(previous.Amount, value.PreviousPrice);
        Assert.Equal(current.Amount, value.NewPrice);
        Assert.Contains("Producto sincronizado", value.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void User_event_identifies_the_user_and_local_credential_change()
    {
        var now = new DateTimeOffset(2026, 8, 29, 21, 0, 0, TimeSpan.Zero);
        var log = new PosSynchronizationEventLog(new FixedTimeProvider(now));
        var userId = Guid.NewGuid();
        var previous = new PosLocalIdentitySummary(
            userId, "cajero", "Cajero anterior", now.AddDays(-1), ["sales.create"]);
        var current = new PosOfflineUserProjection(
            userId, "cajero", "Cajero actualizado", ["sales.create"],
            new PosOfflinePasswordVerifier([1], [2], 10, now));

        log.UserReceived(current, previous);

        var value = Assert.Single(log.Read());
        Assert.Equal("Usuario", value.Category);
        Assert.Contains(current.DisplayName, value.Title, StringComparison.Ordinal);
        Assert.Contains("credencial local actualizada", value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Event_history_is_newest_first_and_bounded()
    {
        var log = new PosSynchronizationEventLog(TimeProvider.System);

        for (var index = 1; index <= 300; index++)
            log.Record("Info", "Test", $"Evento {index}");

        var values = log.Read(300);
        Assert.Equal(250, values.Count);
        Assert.Equal("Evento 300", values[0].Title);
        Assert.Equal("Evento 51", values[^1].Title);
    }

    [Fact]
    public void Every_new_event_notifies_the_local_user_interface()
    {
        var signal = new PosUiStateSignal();
        var subscription = signal.Subscribe();
        var log = new PosSynchronizationEventLog(TimeProvider.System, signal);

        log.Record("Info", "Cliente", "Cliente recibido");

        Assert.True(subscription.Reader.TryRead(out var message));
        Assert.Equal("state", message);
        signal.Unsubscribe(subscription.SubscriptionId);
    }

    [Fact]
    public async Task Unified_outbox_preserves_session_dependencies_without_blocking_other_cashiers()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-unified-outbox-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        var now = new DateTimeOffset(2026, 8, 27, 15, 0, 0, TimeSpan.Zero);
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();
        try
        {
            await PosUnifiedOutboxSchema.EnsureCreatedAsync(connectionString);
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await InsertAsync(connection, Guid.NewGuid(), firstSession,
                "sales.receipt.confirmed", "RetryScheduled", now, now.AddMinutes(1));
            await InsertAsync(connection, Guid.NewGuid(), firstSession,
                PosOutboxMessageTypes.WorkSessionClosure, "Pending", now.AddSeconds(1), null);
            await InsertAsync(connection, Guid.NewGuid(), secondSession,
                PosOutboxMessageTypes.CashMovement, "Pending", now.AddSeconds(2), null);
            var dispatcher = new PosUnifiedOutboxDispatcher(
                connectionString, new FixedTimeProvider(now));

            Assert.Equal(PosUnifiedOutboxRoute.CashMovement, await dispatcher.NextAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Unified_outbox_upgrades_an_existing_install_without_losing_pending_documents()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-outbox-upgrade-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        var messageId = Guid.NewGuid().ToString("D");
        try
        {
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE Outbox(
                      MessageId TEXT NOT NULL PRIMARY KEY, DocumentId TEXT NOT NULL,
                      Type TEXT NOT NULL, Payload TEXT NOT NULL, Status TEXT NOT NULL,
                      AttemptCount INTEGER NOT NULL DEFAULT 0, CreatedAt TEXT NOT NULL,
                      UploadedAt TEXT NULL);
                    INSERT INTO Outbox(MessageId,DocumentId,Type,Payload,Status,CreatedAt)
                    VALUES($id,$id,'sales.receipt.confirmed','{}','Pending','2026-08-28T12:00:00Z');
                    """;
                command.Parameters.AddWithValue("$id", messageId);
                await command.ExecuteNonQueryAsync();
            }

            await PosUnifiedOutboxSchema.EnsureCreatedAsync(connectionString);
            await PosUnifiedOutboxSchema.EnsureCreatedAsync(connectionString);

            await using var upgraded = new SqliteConnection(connectionString);
            await upgraded.OpenAsync();
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = upgraded.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('Outbox');";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            }
            Assert.Contains("WorkSessionId", columns);
            Assert.Contains("NextAttemptAt", columns);
            await using (var command = upgraded.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM Outbox WHERE MessageId=$id AND Status='Pending';";
                command.Parameters.AddWithValue("$id", messageId);
                Assert.Equal(1L, await command.ExecuteScalarAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static async Task InsertAsync(
        SqliteConnection connection,
        Guid id,
        Guid workSessionId,
        string type,
        string status,
        DateTimeOffset createdAt,
        DateTimeOffset? nextAttemptAt)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Outbox(
              MessageId,DocumentId,WorkSessionId,Type,Payload,Status,
              AttemptCount,CreatedAt,NextAttemptAt)
            VALUES($id,$id,$session,$type,'{}',$status,0,$created,$next);
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$session", workSessionId.ToString("D"));
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$created", createdAt.ToString("O"));
        command.Parameters.AddWithValue("$next", (object?)nextAttemptAt?.ToString("O") ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static PosCatalogItem Product(Guid productId, decimal price) => new(
        productId, "P-1", "REF-1", "Producto de prueba", "EA", "VAT19", 19m,
        price, "COP", true, null, ["770123"], []);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
