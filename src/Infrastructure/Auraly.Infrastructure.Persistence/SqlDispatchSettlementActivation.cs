using System.Collections.Concurrent;
using System.Data;
using System.Text.Json;
using Auraly.Contracts.Dispatching;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlDispatchSettlementActivation(
    SqlServerConnectionFactory connections,
    IDispatchSettlementSignalPublisher publisher,
    ILogger<SqlDispatchSettlementActivation> logger)
{
    public Task RecoverAsync(CancellationToken token) => PublishDueAsync(null, null, null, token);

    public Task PublishAcceptedAsync(Guid businessId, Guid dispatchId, string key, CancellationToken token) =>
        PublishDueAsync(businessId, dispatchId, key, token);

    private async Task PublishDueAsync(Guid? business, Guid? dispatch, string? key, CancellationToken token)
    {
        var lease = Guid.NewGuid();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(business is null ? TimeSpan.FromSeconds(90) : TimeSpan.FromSeconds(2));
        try
        {
            var signals = await ClaimAsync(lease, business, dispatch, key, budget.Token);
            if (signals.Count == 0)
                return;
            var accepted = new ConcurrentBag<Guid>();
            await Parallel.ForEachAsync(signals.GroupBy(signal => signal.BusinessId), new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = budget.Token
            }, async (session, sessionToken) =>
            {
                foreach (var signal in session)
                {
                    using var send = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
                    send.CancelAfter(TimeSpan.FromSeconds(2));
                    try
                    {
                        await publisher.PublishAsync(signal, send.Token);
                        accepted.Add(signal.OperationId);
                    }
                    catch (Exception error) when (!sessionToken.IsCancellationRequested)
                    {
                        logger.LogWarning(error, "Dispatch settlement activation {OperationId} remains available for recovery.",
                            signal.OperationId);
                        break;
                    }
                }
            });
            if (!accepted.IsEmpty)
                await AcknowledgeAsync(lease, accepted.ToArray(), budget.Token);
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            logger.LogWarning(error, "Dispatch settlement activation could not be confirmed for scope {BusinessId}/{DispatchId}.",
                business, dispatch);
        }
    }

    private async Task<IReadOnlyList<DispatchSettlementSignal>> ClaimAsync(Guid lease, Guid? business,
        Guid? dispatch, string? key, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        var scope = business is null ? "" : "AND BusinessId=@Business AND DispatchId=@Dispatch AND IdempotencyKey=@Key";
        await using var command = new SqlCommand($"""
            SET NOCOUNT ON;
            SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
            DECLARE @Now datetimeoffset(7)=SYSUTCDATETIME();
            DECLARE @Claimed TABLE(Id uniqueidentifier,BusinessId uniqueidentifier,Version int,AvailableAt datetimeoffset(7));
            ;WITH due AS
            (
                SELECT TOP(@Take) DispatchSettlementOperationId,BusinessId,ActivationVersion,ActivationAvailableAt,ActivationLeaseId
                FROM dbo.DispatchSettlementOperations WITH(UPDLOCK,READPAST,READCOMMITTEDLOCK)
                WHERE Status IN(N'Pending',N'Processing') AND PreparationPhase<3
                  AND ActivationVersion IS NOT NULL AND ActivationAvailableAt<=@Now
                  AND (PreparationLeaseId IS NULL OR NextAttemptAt<=@Now) {scope}
                ORDER BY ActivationAvailableAt,DispatchSettlementOperationId
            )
            UPDATE due SET ActivationLeaseId=@Lease,ActivationAvailableAt=DATEADD(minute,2,@Now)
            OUTPUT inserted.DispatchSettlementOperationId,inserted.BusinessId,inserted.ActivationVersion,
              deleted.ActivationAvailableAt INTO @Claimed;
            SELECT Id,BusinessId,Version FROM @Claimed ORDER BY AvailableAt,Id;
            """, connection);
        command.Parameters.AddWithValue("@Lease", lease);
        command.Parameters.AddWithValue("@Take", business is null ? 100 : 1);
        if (business is { } id)
        {
            command.Parameters.AddWithValue("@Business", id);
            command.Parameters.Add("@Dispatch", SqlDbType.UniqueIdentifier).Value = dispatch!;
            command.Parameters.Add("@Key", SqlDbType.NVarChar, 128).Value = key!;
        }
        var signals = new List<DispatchSettlementSignal>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            signals.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2)));
        return signals;
    }

    private async Task AcknowledgeAsync(Guid lease, IReadOnlyList<Guid> accepted, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            UPDATE operation SET ActivationLeaseId=NULL,ActivationAvailableAt=DATEADD(minute,11,SYSUTCDATETIME())
            FROM dbo.DispatchSettlementOperations operation
            JOIN OPENJSON(@Accepted) WITH(Id uniqueidentifier '$') confirmed ON confirmed.Id=operation.DispatchSettlementOperationId
            WHERE operation.ActivationLeaseId=@Lease;
            """, connection);
        command.Parameters.AddWithValue("@Lease", lease);
        command.Parameters.Add("@Accepted", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(accepted);
        await command.ExecuteNonQueryAsync(token);
    }
}
