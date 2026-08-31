using System.Threading.Channels;
using Auraly.BuildingBlocks.Application.Synchronization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPosSynchronizationOutboxDispatcher(
    SqlServerConnectionFactory connections,
    IPosSynchronizationPushGateway gateway,
    TimeProvider timeProvider,
    ILogger<SqlPosSynchronizationOutboxDispatcher> logger)
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
        signals.Writer.TryWrite(new Scope(tenantId, businessId));
        return Task.CompletedTask;
    }

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
        var pending = await ReadPendingAsync(
            scope.TenantId, scope.BusinessId, cancellationToken);
        foreach (var invalidation in pending)
        {
            try
            {
                await gateway.SendAsync(invalidation, cancellationToken);
                await MarkPublishedAsync(invalidation, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await RecordFailureAsync(
                    invalidation,
                    exception.Message,
                    cancellationToken);
                throw;
            }
        }
    }

    private async Task<IReadOnlyCollection<Scope>> ReadPersistedScopesAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT b.TenantId,o.BusinessId
            FROM dbo.PosSynchronizationOutboxMessages o
            JOIN dbo.Businesses b ON b.BusinessId=o.BusinessId
            WHERE o.PublishedAt IS NULL;
            """;
        var scopes = new List<Scope>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            scopes.Add(new Scope(reader.GetGuid(0), reader.GetGuid(1)));
        return scopes;
    }

    private async Task<IReadOnlyCollection<PosSynchronizationInvalidation>> ReadPendingAsync(
        Guid tenantId,
        Guid businessId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
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
                WHERE o.BusinessId=@BusinessId
                  AND b.TenantId=@TenantId
                  AND o.PublishedAt IS NULL
            )
            SELECT NotificationId,TenantId,BusinessId,Stream,
                   AvailableThroughCursor,OccurredAt,TargetDeviceId
            FROM Pending
            WHERE Position=1;
            """;
        command.Parameters.AddRange([
            Parameter("@TenantId", tenantId),
            Parameter("@BusinessId", businessId)
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
        PosSynchronizationInvalidation invalidation,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.PosSynchronizationOutboxMessages
            SET PublishedAt=@Now,LastAttemptAt=@Now,AttemptCount=AttemptCount+1,
                LastError=NULL
            WHERE BusinessId=@BusinessId AND Stream=@Stream
              AND AvailableThroughCursor<=@Cursor AND PublishedAt IS NULL
              AND ((@TargetDeviceId IS NULL AND TargetDeviceId IS NULL)
                   OR TargetDeviceId=@TargetDeviceId);
            """;
        command.Parameters.AddRange([
            Parameter("@Now", timeProvider.GetUtcNow()),
            Parameter("@BusinessId", invalidation.BusinessId),
            Parameter("@Stream", invalidation.Stream),
            Parameter("@Cursor", invalidation.AvailableThroughCursor),
            new SqlParameter("@TargetDeviceId",
                (object?)invalidation.TargetDeviceId ?? DBNull.Value)
        ]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task RecordFailureAsync(
        PosSynchronizationInvalidation invalidation,
        string error,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
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

    private sealed record Scope(Guid TenantId, Guid BusinessId);
}
