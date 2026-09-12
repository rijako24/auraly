using Auraly.Contracts.Catalog;
using Auraly.Contracts.Authorization;
using Auraly.Pos.Edge.Host;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosSynchronizationEventLogTests
{
    [Fact]
    public void Outbox_ordering_is_local_to_each_work_session()
    {
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();

        Assert.False(PosOutboxOrdering.Blocks(
            PosOutboxStatus.Pending,
            10,
            PosOutboxMessageTypes.WorkSessionClosure,
            firstSession,
            11,
            PosOutboxMessageTypes.CashMovement,
            secondSession));
        Assert.True(PosOutboxOrdering.Blocks(
            PosOutboxStatus.Pending,
            11,
            PosOutboxMessageTypes.CashMovement,
            firstSession,
            10,
            PosOutboxMessageTypes.WorkSessionClosure,
            firstSession));
        Assert.False(PosOutboxOrdering.Blocks(
            PosOutboxStatus.Pending,
            10,
            PosOutboxMessageTypes.WorkSessionClosure,
            firstSession,
            11,
            PosOutboxMessageTypes.CashMovement,
            firstSession));
    }

    [Fact]
    public async Task Failed_upload_lane_does_not_block_catalog_download_lane()
    {
        var events = new PosSynchronizationEventLog(TimeProvider.System);
        var executor = new PosSynchronizationLaneExecutor(
            events,
            NullLogger<PosSynchronizationLaneExecutor>.Instance);
        var catalogExecuted = false;
        var catalogStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        var result = await executor.ExecuteAllAsync(
            [
                new PosSynchronizationLane(
                    PosSynchronizationTrigger.LocalOutbox,
                    "subida",
                    async () =>
                    {
                        await catalogStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                        throw new HttpRequestException(
                            "Host desconocido. (api-auraly-dev-w5usmo6w.azurewebsites.net:443)");
                    }),
                new PosSynchronizationLane(
                    PosSynchronizationTrigger.Catalog,
                    "catálogo",
                    () =>
                    {
                        catalogExecuted = true;
                        catalogStarted.TrySetResult();
                        return Task.CompletedTask;
                    })
            ],
            cancellation.Token);

        cancellation.Cancel();
        Assert.False(result.Succeeded);
        Assert.True(result.HasRetryableFailure);
        Assert.False(result.HasPermanentFailure);
        Assert.Equal(PosSynchronizationTrigger.LocalOutbox, result.RetryableTriggers);
        Assert.True(catalogExecuted);
        var failedEvent = Assert.Single(events.Read(),
            item => item.Title.Contains("subida", StringComparison.Ordinal));
        Assert.DoesNotContain("azurewebsites", failedEvent.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":443", failedEvent.Detail, StringComparison.Ordinal);
        Assert.Contains("automáticamente", failedEvent.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Permanent_synchronization_failure_is_not_scheduled_as_a_retryable_failure()
    {
        var executor = new PosSynchronizationLaneExecutor(
            new PosSynchronizationEventLog(TimeProvider.System),
            NullLogger<PosSynchronizationLaneExecutor>.Instance);

        var result = await executor.ExecuteAllAsync(
            [
                new PosSynchronizationLane(
                    PosSynchronizationTrigger.Security,
                    "usuarios",
                    () => throw new HttpRequestException(
                        "Forbidden at https://internal.example.test",
                        null,
                        System.Net.HttpStatusCode.Forbidden))
            ],
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.HasRetryableFailure);
        Assert.True(result.HasPermanentFailure);
        Assert.Equal(PosSynchronizationTrigger.None, result.RetryableTriggers);
    }

    [Fact]
    public void Stored_transport_error_is_never_exposed_to_the_cashier()
    {
        var detail = PosSynchronizationFailurePresenter.StoredError(
            "Host desconocido. (api-auraly-dev-w5usmo6w.azurewebsites.net:443)");

        Assert.NotNull(detail);
        Assert.DoesNotContain("azurewebsites", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":443", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Transient_synchronization_retries_exactly_three_times()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), PosSynchronizationRetryPolicy.NextDelay(0));
        Assert.Equal(TimeSpan.FromSeconds(10), PosSynchronizationRetryPolicy.NextDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(20), PosSynchronizationRetryPolicy.NextDelay(2));
        Assert.Null(PosSynchronizationRetryPolicy.NextDelay(3));
    }

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
    public void Initial_catalog_download_is_not_presented_as_a_live_change()
    {
        var log = new PosSynchronizationEventLog(TimeProvider.System);

        log.ProductReceived(Product(Guid.NewGuid(), 10_000m), null, bootstrap: true);

        Assert.Empty(log.Read());
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
    public void Channel_tier_event_preserves_product_and_price_detail()
    {
        var log = new PosSynchronizationEventLog(TimeProvider.System);
        var productId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var previous = new PosPriceChannelTier(channelId, productId, 5m, 10_000m, "COP");
        var current = previous with { Amount = 9_500m };

        log.ChannelTierReceived(current, previous, "Producto sincronizado");

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
    public void Local_user_interface_notifications_are_coalesced_until_consumed()
    {
        var signal = new PosUiStateSignal();
        var subscription = signal.Subscribe();

        for (var index = 0; index < 100; index++) signal.Publish();

        Assert.True(subscription.Reader.TryRead(out var message));
        Assert.Equal("state", message);
        Assert.False(subscription.Reader.TryRead(out _));
        signal.Unsubscribe(subscription.SubscriptionId);
    }

    [Fact]
    public async Task Unified_outbox_isolates_retry_barriers_between_sessions()
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
            dispatcher = new PosUnifiedOutboxDispatcher(
                connectionString, new FixedTimeProvider(now.AddMinutes(1)));
            Assert.Equal(PosUnifiedOutboxRoute.Sale, await dispatcher.NextAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Unified_outbox_recovers_a_same_session_document_queued_after_its_closure()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-unified-outbox-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        var now = new DateTimeOffset(2026, 9, 10, 15, 0, 0, TimeSpan.Zero);
        var closedSession = Guid.NewGuid();
        var nextSession = Guid.NewGuid();
        var closureId = Guid.NewGuid();
        var movementId = Guid.NewGuid();
        var nextOpeningId = Guid.NewGuid();
        var nextMovementId = Guid.NewGuid();
        try
        {
            await PosUnifiedOutboxSchema.EnsureCreatedAsync(connectionString);
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await InsertAsync(connection, closureId, closedSession,
                PosOutboxMessageTypes.WorkSessionClosure, "RetryScheduled", now,
                now.AddMinutes(5));
            await InsertAsync(connection, movementId, closedSession,
                PosOutboxMessageTypes.CashMovement, "Pending", now.AddSeconds(1), null);
            await InsertAsync(connection, nextOpeningId, nextSession,
                PosOutboxMessageTypes.WorkSessionOpened, "Pending", now.AddSeconds(2), null);
            await InsertAsync(connection, nextMovementId, nextSession,
                PosOutboxMessageTypes.CashMovement, "Pending", now.AddSeconds(3), null);

            var dispatcher = new PosUnifiedOutboxDispatcher(
                connectionString, new FixedTimeProvider(now));

            Assert.Equal(PosUnifiedOutboxRoute.CashMovement, await dispatcher.NextAsync());

            var cashStore = new PosCashMovementStore(
                connectionString, new FixedTimeProvider(now));
            var claimed = await cashStore.ClaimAsync();
            Assert.NotNull(claimed);
            Assert.Equal(movementId, claimed.Value.DocumentId);

            await SetStatusAsync(connection, movementId, PosOutboxStatus.Uploaded);
            Assert.Equal(PosUnifiedOutboxRoute.WorkSessionOpened, await dispatcher.NextAsync());

            await SetStatusAsync(connection, nextOpeningId, PosOutboxStatus.Uploaded);
            Assert.Equal(PosUnifiedOutboxRoute.CashMovement, await dispatcher.NextAsync());

            await SetStatusAsync(connection, nextMovementId, PosOutboxStatus.Uploaded);
            Assert.Null(await dispatcher.NextAsync());

            await SetNextAttemptAsync(connection, closureId, now);
            Assert.Equal(PosUnifiedOutboxRoute.WorkSessionClosure, await dispatcher.NextAsync());

            await SetStatusAsync(connection, closureId, PosOutboxStatus.Uploaded);
            Assert.Null(await dispatcher.NextAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Unified_outbox_treats_work_session_guids_case_insensitively()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"auraly-unified-outbox-case-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        var now = new DateTimeOffset(2026, 9, 11, 19, 0, 0, TimeSpan.Zero);
        var blockedSession = Guid.NewGuid();
        var activeSession = Guid.NewGuid();
        try
        {
            await PosUnifiedOutboxSchema.EnsureCreatedAsync(connectionString);
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            var blockedOpening = Guid.NewGuid();
            await InsertAsync(connection, blockedOpening, blockedSession,
                PosOutboxMessageTypes.WorkSessionOpened, "RetryScheduled", now,
                now.AddMinutes(5));
            await InsertAsync(connection, Guid.NewGuid(), blockedSession,
                "sales.receipt.confirmed", "Pending", now.AddSeconds(1), null);
            await InsertAsync(connection, Guid.NewGuid(), activeSession,
                PosOutboxMessageTypes.WorkSessionOpened, "Pending", now.AddSeconds(2), null);

            await using (var upperCase = connection.CreateCommand())
            {
                upperCase.CommandText = """
                    UPDATE Outbox SET WorkSessionId=upper(WorkSessionId)
                    WHERE Type='sales.receipt.confirmed';
                    """;
                await upperCase.ExecuteNonQueryAsync();
            }

            var dispatcher = new PosUnifiedOutboxDispatcher(
                connectionString, new FixedTimeProvider(now));

            Assert.Equal(PosUnifiedOutboxRoute.WorkSessionOpened, await dispatcher.NextAsync());
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
            Assert.Contains("LocalSequence", columns);
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

    private static async Task SetStatusAsync(
        SqliteConnection connection,
        Guid documentId,
        string status)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Outbox SET Status=$status WHERE DocumentId=$id;";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$id", documentId.ToString("D"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetNextAttemptAsync(
        SqliteConnection connection,
        Guid documentId,
        DateTimeOffset nextAttemptAt)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Outbox SET NextAttemptAt=$next WHERE DocumentId=$id;";
        command.Parameters.AddWithValue("$next", nextAttemptAt.ToString("O"));
        command.Parameters.AddWithValue("$id", documentId.ToString("D"));
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
