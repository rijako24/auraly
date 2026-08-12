using System.Data;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Commerce;

public sealed record ExternalCustomerOutboxDispatchOutcome(
    int Published,
    int Failed,
    bool HasImmediateWork,
    DateTimeOffset? NextAttemptAt);

public sealed class SqlExternalCustomerReconciliationOutboxDispatcher(
    ApplicationDbContext context,
    IExternalCustomerReconciliationSignalPublisher publisher,
    TimeProvider timeProvider,
    ILogger<SqlExternalCustomerReconciliationOutboxDispatcher> logger)
{
    private const int BatchSize = 100;
    private static readonly int[] RetrySeconds = [2, 5, 15, 30, 120, 300];

    public async Task<ExternalCustomerOutboxDispatchOutcome> DispatchAvailableAsync(
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var leaseId = Guid.NewGuid();
        var messages = await ClaimAsync(leaseId, now, cancellationToken);
        var published = 0;
        var failed = 0;
        foreach (var message in messages)
        {
            try
            {
                await publisher.PublishAsync(
                    new ExternalCustomerReconciliationSignal(
                        message.MessageId,
                        message.ExternalCommerceCustomerId,
                        message.BusinessId,
                        message.OccurredAt),
                    cancellationToken);
                await MarkPublishedAsync(
                    message.MessageId,
                    leaseId,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                published++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var availableAt = timeProvider.GetUtcNow().AddSeconds(
                    RetrySeconds[Math.Min(message.AttemptCount - 1, RetrySeconds.Length - 1)]);
                await ReleaseForRetryAsync(
                    message.MessageId,
                    leaseId,
                    availableAt,
                    Error(exception),
                    cancellationToken);
                failed++;
                logger.LogWarning(
                    exception,
                    "External-customer reconciliation message {MessageId} remains durable for retry.",
                    message.MessageId);
            }
        }

        var next = await NextAttemptAtAsync(timeProvider.GetUtcNow(), cancellationToken);
        return new ExternalCustomerOutboxDispatchOutcome(
            published,
            failed,
            next is not null && next <= timeProvider.GetUtcNow(),
            next);
    }

    private async Task<IReadOnlyCollection<ClaimedMessage>> ClaimAsync(
        Guid leaseId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        var connection = (SqlConnection)context.Database.GetDbConnection();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                ;WITH pending AS
                (
                    SELECT TOP (@BatchSize) *
                    FROM dbo.ExternalCustomerReconciliationOutboxMessages
                         WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE PublishedAt IS NULL
                      AND AvailableAt<=@Now
                      AND (LeaseExpiresAt IS NULL OR LeaseExpiresAt<=@Now)
                    ORDER BY OccurredAt,MessageId
                )
                UPDATE pending
                SET LeaseId=@LeaseId,LeaseExpiresAt=@LeaseExpiresAt,
                    AttemptCount=AttemptCount+1,LastAttemptAt=@Now,LastError=NULL
                OUTPUT inserted.MessageId,inserted.ExternalCommerceCustomerId,
                       inserted.BusinessId,inserted.OccurredAt,inserted.AttemptCount;
                """;
            command.Parameters.AddRange([
                P("@BatchSize", BatchSize),
                P("@Now", now),
                P("@LeaseId", leaseId),
                P("@LeaseExpiresAt", now.AddMinutes(2))
            ]);
            var result = new List<ClaimedMessage>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                result.Add(new ClaimedMessage(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetDateTimeOffset(3),
                    reader.GetInt32(4)));
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private Task MarkPublishedAsync(
        Guid messageId,
        Guid leaseId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            UPDATE dbo.ExternalCustomerReconciliationOutboxMessages
            SET PublishedAt=@Now,LeaseId=NULL,LeaseExpiresAt=NULL,LastError=NULL
            WHERE MessageId=@MessageId AND LeaseId=@LeaseId AND PublishedAt IS NULL;
            """,
            [P("@Now", now), P("@MessageId", messageId), P("@LeaseId", leaseId)],
            cancellationToken);

    private Task ReleaseForRetryAsync(
        Guid messageId,
        Guid leaseId,
        DateTimeOffset availableAt,
        string error,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            UPDATE dbo.ExternalCustomerReconciliationOutboxMessages
            SET AvailableAt=@AvailableAt,LeaseId=NULL,LeaseExpiresAt=NULL,LastError=@Error
            WHERE MessageId=@MessageId AND LeaseId=@LeaseId AND PublishedAt IS NULL;
            """,
            [
                P("@AvailableAt", availableAt),
                P("@Error", error),
                P("@MessageId", messageId),
                P("@LeaseId", leaseId)
            ],
            cancellationToken);

    private async Task<DateTimeOffset?> NextAttemptAtAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        var connection = (SqlConnection)context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MIN(CASE
                WHEN LeaseExpiresAt IS NOT NULL AND LeaseExpiresAt>@Now
                    THEN LeaseExpiresAt
                ELSE AvailableAt END)
            FROM dbo.ExternalCustomerReconciliationOutboxMessages
            WHERE PublishedAt IS NULL;
            """;
        command.Parameters.Add(P("@Now", now));
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DateTimeOffset next ? next : null;
    }

    private async Task ExecuteAsync(
        string sql,
        SqlParameter[] parameters,
        CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        var connection = (SqlConnection)context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqlParameter P(string name, object value) => new(name, value);

    private static string Error(Exception exception)
    {
        var value = exception.GetBaseException().Message;
        return value.Length <= 1000 ? value : value[..1000];
    }

    private sealed record ClaimedMessage(
        Guid MessageId,
        Guid ExternalCommerceCustomerId,
        Guid BusinessId,
        DateTimeOffset OccurredAt,
        int AttemptCount);
}
