using System.Text.Json;
using System.Threading.Channels;
using Auraly.BuildingBlocks.Application.Synchronization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPosSynchronizationOutboxDispatcher(
    SqlServerConnectionFactory connections,
    IPosSynchronizationPushGateway gateway,
    TimeProvider timeProvider,
    ILogger<SqlPosSynchronizationOutboxDispatcher> logger,
    bool publishInline = false)
    : IPosSynchronizationOutboxDispatcher
{
    private readonly Channel<Scope> signals =
        Channel.CreateUnbounded<Scope>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public Task DispatchPendingAsync(
        Guid tenantId,
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (publishInline)
            return PublishPendingAsync(new Scope(tenantId, businessId), cancellationToken);
        signals.Writer.TryWrite(new Scope(tenantId, businessId));
        return Task.CompletedTask;
    }

    public async Task DispatchTenantPendingAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        foreach (var scope in await ReadPersistedScopesAsync(cancellationToken, tenantId))
            await DispatchPendingAsync(tenantId, scope.BusinessId, cancellationToken);
    }

    public Task PublishBusinessAsync(Guid businessId, CancellationToken cancellationToken) =>
        PublishPendingAsync(new Scope(null, businessId), cancellationToken);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var scope in await ReadPersistedScopesAsync(cancellationToken))
            signals.Writer.TryWrite(scope);

        while (!cancellationToken.IsCancellationRequested)
        {
            var scope = await signals.Reader.ReadAsync(cancellationToken);
            try
            {
                await PublishPendingAsync(scope, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "POS synchronization outbox publication failed for business {BusinessId}; it remains durable.",
                    scope.BusinessId);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                signals.Writer.TryWrite(scope);
            }
        }
    }

    private async Task PublishPendingAsync(
        Scope scope,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        var resource = $"pos-sync-publish:{scope.BusinessId:D}";
        await using (var claim = new SqlCommand("""
            DECLARE @Result int;
            EXEC @Result=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',
              @LockOwner=N'Session',@LockTimeout=1000;
            SELECT @Result;
            """, connection))
        {
            claim.Parameters.Add(Parameter("@Resource", resource));
            if ((int)(await claim.ExecuteScalarAsync(token))! < 0)
                throw new InvalidOperationException("El ámbito de publicación del POS está ocupado.");
        }

        try
        {
            const int pageSize = 64;
            while (true)
            {
                var pending = await ReadPendingAsync(connection, scope, pageSize, token);
                if (pending.Count == 0)
                    return;

                var sent = new List<PosSynchronizationInvalidation>(pending.Count);
                foreach (var invalidation in pending)
                {
                    try
                    {
                        await gateway.SendAsync(invalidation, token);
                        sent.Add(invalidation);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        await MarkPublishedAsync(connection, scope.BusinessId, sent, token);
                        await RecordFailureAsync(connection, invalidation, exception.Message, token);
                        throw;
                    }
                }
                await MarkPublishedAsync(connection, scope.BusinessId, sent, token);
                if (pending.Count < pageSize)
                    return;
            }
        }
        finally
        {
            await using var release = new SqlCommand("""
                EXEC sys.sp_releaseapplock @Resource=@Resource,@LockOwner=N'Session';
                """, connection) { CommandTimeout = 3 };
            release.Parameters.Add(Parameter("@Resource", resource));
            if (connection.State == System.Data.ConnectionState.Open)
                await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private async Task<IReadOnlyCollection<Scope>> ReadPersistedScopesAsync(
        CancellationToken cancellationToken,
        Guid? tenantId = null)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = tenantId is null
            ? """
              SELECT DISTINCT b.TenantId,o.BusinessId
              FROM dbo.PosSynchronizationOutboxMessages o
              JOIN dbo.Businesses b ON b.BusinessId=o.BusinessId
              WHERE o.PublishedAt IS NULL;
              """
            : """
              SELECT b.TenantId,b.BusinessId
              FROM dbo.Businesses b
              WHERE b.TenantId=@TenantId AND b.IsActive=1
                AND EXISTS (
                  SELECT 1 FROM dbo.PosSynchronizationOutboxMessages o
                  WHERE o.BusinessId=b.BusinessId AND o.PublishedAt IS NULL);
              """;
        if (tenantId is not null)
            command.Parameters.Add(Parameter("@TenantId", tenantId.Value));
        var scopes = new List<Scope>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            scopes.Add(new Scope(reader.GetGuid(0), reader.GetGuid(1)));
        return scopes;
    }

    private static async Task<IReadOnlyCollection<PosSynchronizationInvalidation>> ReadPendingAsync(
        SqlConnection connection,
        Scope scope,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH Pending AS
            (
                SELECT
                    o.NotificationId,
                    b.TenantId,
                    o.BusinessId,
                    o.Stream,
                    o.AvailableThroughCursor,
                    o.OccurredAt,
                    o.TargetDeviceId,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY o.Stream,o.TargetDeviceId
                        ORDER BY o.AvailableThroughCursor DESC
                    ) AS Position
                FROM dbo.PosSynchronizationOutboxMessages o
                JOIN dbo.Businesses b ON b.BusinessId=o.BusinessId
                WHERE o.BusinessId=@BusinessId AND b.IsActive=1
                  AND (@TenantId IS NULL OR b.TenantId=@TenantId)
                  AND o.PublishedAt IS NULL
            )
            SELECT TOP(@Take) NotificationId,TenantId,BusinessId,Stream,
                   AvailableThroughCursor,OccurredAt,TargetDeviceId
            FROM Pending
            WHERE Position=1
            ORDER BY Stream,TargetDeviceId;
            """;
        command.Parameters.AddRange([
            new SqlParameter("@TenantId", System.Data.SqlDbType.UniqueIdentifier)
                { Value = (object?)scope.TenantId ?? DBNull.Value },
            Parameter("@BusinessId", scope.BusinessId),
            Parameter("@Take", pageSize)
        ]);
        var values = new List<PosSynchronizationInvalidation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new PosSynchronizationInvalidation(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6)));
        }
        return values;
    }

    private async Task MarkPublishedAsync(
        SqlConnection connection,
        Guid businessId,
        IReadOnlyCollection<PosSynchronizationInvalidation> sent,
        CancellationToken cancellationToken)
    {
        if (sent.Count == 0)
            return;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pending
            SET PublishedAt=@Now,LastAttemptAt=@Now,AttemptCount=AttemptCount+1,
                LastError=NULL
            FROM dbo.PosSynchronizationOutboxMessages pending
            INNER JOIN OPENJSON(@Sent) WITH
              (Stream nvarchar(32), AvailableThroughCursor bigint, TargetDeviceId uniqueidentifier) sent
              ON sent.Stream=pending.Stream
              AND pending.AvailableThroughCursor<=sent.AvailableThroughCursor
              AND (pending.TargetDeviceId=sent.TargetDeviceId
                OR pending.TargetDeviceId IS NULL AND sent.TargetDeviceId IS NULL)
            WHERE pending.BusinessId=@BusinessId AND pending.PublishedAt IS NULL;
            """;
        command.Parameters.AddRange([
            Parameter("@Now", timeProvider.GetUtcNow()),
            Parameter("@BusinessId", businessId),
            new SqlParameter("@Sent", System.Data.SqlDbType.NVarChar, -1)
                { Value = JsonSerializer.Serialize(sent.Select(value =>
                    new { value.Stream, value.AvailableThroughCursor, value.TargetDeviceId })) }
        ]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task RecordFailureAsync(
        SqlConnection connection,
        PosSynchronizationInvalidation invalidation,
        string error,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.PosSynchronizationOutboxMessages
            SET LastAttemptAt=@Now,AttemptCount=AttemptCount+1,LastError=@Error
            WHERE NotificationId=@NotificationId AND PublishedAt IS NULL;
            """;
        command.Parameters.AddRange([
            Parameter("@Now", timeProvider.GetUtcNow()),
            Parameter("@Error", error.Length <= 1000 ? error : error[..1000]),
            Parameter("@NotificationId", invalidation.NotificationId)
        ]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqlParameter Parameter(string name, object value) =>
        new(name, value);

    private sealed record Scope(Guid? TenantId, Guid BusinessId);
}
