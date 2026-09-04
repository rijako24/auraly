using System.Net.Http.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Contracts.WorkSessions;
using Auraly.Pos.Edge.Host;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosLocalWorkSessionTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(
        Path.GetTempPath(), $"auraly-work-session-{Guid.NewGuid():N}.db");
    private readonly FixedTimeProvider time = new(
        new DateTimeOffset(2026, 9, 3, 10, 0, 0, TimeSpan.Zero));
    private readonly TenantId tenantId = new(Guid.NewGuid());
    private readonly BusinessId businessId = new(Guid.NewGuid());
    private readonly DeviceId deviceId = new(Guid.NewGuid());
    private PosLocalWorkSessionStore store = null!;

    public async Task InitializeAsync()
    {
        var runtime = new PosEdgeRuntimeContext(
            tenantId,
            businessId,
            new WarehouseId(Guid.NewGuid()),
            deviceId,
            false);
        store = new PosLocalWorkSessionStore(
            $"Data Source={path}", new Uuid7AuralyIdGenerator(time), time, runtime);
        await store.InitializeAsync();
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var schema = connection.CreateCommand();
        schema.CommandText = """
            CREATE TABLE IF NOT EXISTS PosClosedWorkSessions(
                WorkSessionId TEXT NOT NULL PRIMARY KEY,
                UserId TEXT NOT NULL,
                ClosedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS PosLocalUserSessions(
                SessionId TEXT NOT NULL PRIMARY KEY,
                WorkSessionId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                EndedAt TEXT NULL);
            """;
        await schema.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Opening_is_local_idempotent_and_closing_creates_a_new_exact_stream()
    {
        var userId = Guid.NewGuid();
        var first = await store.OpenOrResumeAsync(userId);
        var resumed = await store.OpenOrResumeAsync(userId);
        Assert.Equal(first.WorkSessionId, resumed.WorkSessionId);

        time.Advance(TimeSpan.FromHours(2));
        await store.MarkClosedAsync(first.WorkSessionId, userId, time.GetUtcNow());
        var second = await store.OpenOrResumeAsync(userId);
        Assert.NotEqual(first.WorkSessionId, second.WorkSessionId);

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DocumentId,LocalSequence,Type
            FROM Outbox ORDER BY LocalSequence;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(first.WorkSessionId, Guid.Parse(reader.GetString(0)));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal("work-session.opened", reader.GetString(2));
        Assert.True(await reader.ReadAsync());
        Assert.Equal(second.WorkSessionId, Guid.Parse(reader.GetString(0)));
        Assert.Equal(2L, reader.GetInt64(1));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task Different_users_on_the_same_enrolled_device_never_share_a_session()
    {
        var first = await store.OpenOrResumeAsync(Guid.NewGuid());
        var second = await store.OpenOrResumeAsync(Guid.NewGuid());
        Assert.NotEqual(first.WorkSessionId, second.WorkSessionId);
    }

    [Fact]
    public async Task Synchronization_registers_the_locally_created_identifier_without_replacing_it()
    {
        var local = await store.OpenOrResumeAsync(Guid.NewGuid());
        await using (var database = new SqliteConnection($"Data Source={path}"))
        {
            await database.OpenAsync();
            await using var movement = database.CreateCommand();
            movement.CommandText = """
                INSERT INTO Outbox(
                    MessageId,DocumentId,WorkSessionId,Type,Payload,Status,
                    AttemptCount,CreatedAt)
                VALUES($id,$id,$session,$type,'{}','Pending',0,$created);
                """;
            movement.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            movement.Parameters.AddWithValue("$session", local.WorkSessionId.ToString("D"));
            movement.Parameters.AddWithValue("$type", PosOutboxMessageTypes.CashMovement);
            movement.Parameters.AddWithValue("$created", local.OpenedAt.AddMinutes(1).ToString("O"));
            await movement.ExecuteNonQueryAsync();
        }
        var dispatcher = new PosUnifiedOutboxDispatcher($"Data Source={path}", time);
        Assert.Equal(PosUnifiedOutboxRoute.WorkSessionOpened, await dispatcher.NextAsync());

        RegisterDeviceWorkSessionRequest? uploaded = null;
        var handler = new StubHandler(async request =>
        {
            uploaded = await request.Content!.ReadFromJsonAsync<RegisterDeviceWorkSessionRequest>();
            var view = new WorkSessionView(
                local.WorkSessionId, businessId.Value, "Sede", null, null,
                local.UserId, "Usuario", deviceId.Value, local.OpenedAt,
                local.OpenedAt, "Open", tenantId.Value);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = JsonContent.Create(view)
            };
        });
        var uploader = new PosWorkSessionOpenUploader(
            $"Data Source={path}", new HttpClient(handler)
            {
                BaseAddress = new Uri("https://server.test")
            }, new PosDeviceCredentials(deviceId.Value, "secret"), time,
            new PosSynchronizationEventLog(time));

        Assert.True(await uploader.UploadNextAsync());
        Assert.NotNull(uploaded);
        Assert.Equal(local.WorkSessionId, uploaded!.WorkSessionId);
        Assert.Equal(PosUnifiedOutboxRoute.CashMovement, await dispatcher.NextAsync());

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status FROM Outbox WHERE DocumentId=$id;";
        command.Parameters.AddWithValue("$id", local.WorkSessionId.ToString("D"));
        Assert.Equal("Uploaded", Convert.ToString(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Existing_local_session_schema_is_backfilled_with_the_enrolled_tenant()
    {
        var legacyPath = Path.Combine(
            Path.GetTempPath(), $"auraly-work-session-legacy-{Guid.NewGuid():N}.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={legacyPath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE PosLocalWorkSessions(
                        WorkSessionId TEXT NOT NULL PRIMARY KEY,
                        BusinessId TEXT NOT NULL,DeviceId TEXT NOT NULL,UserId TEXT NOT NULL,
                        OpenedAt TEXT NOT NULL,ClosedAt TEXT NULL);
                    """;
                await command.ExecuteNonQueryAsync();
            }
            var runtime = new PosEdgeRuntimeContext(
                tenantId, businessId, new WarehouseId(Guid.NewGuid()), deviceId, false);
            var legacyStore = new PosLocalWorkSessionStore(
                $"Data Source={legacyPath}", new Uuid7AuralyIdGenerator(time), time, runtime);

            await legacyStore.InitializeAsync();
            await legacyStore.OpenOrResumeAsync(Guid.NewGuid());

            await using (var verification = new SqliteConnection($"Data Source={legacyPath}"))
            {
                await verification.OpenAsync();
                await using var tenant = verification.CreateCommand();
                tenant.CommandText = "SELECT DISTINCT TenantId FROM PosLocalWorkSessions;";
                Assert.Equal(tenantId.Value.ToString("D"), await tenant.ExecuteScalarAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
        }
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var candidate in new[] { path, $"{path}-wal", $"{path}-shm" })
            if (File.Exists(candidate)) File.Delete(candidate);
        return Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan value) => current += value;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
