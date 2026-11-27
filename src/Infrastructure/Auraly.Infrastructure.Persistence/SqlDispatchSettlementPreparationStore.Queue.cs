using System.Data;
using System.Text.Json;
using Auraly.Application.Dispatching;
using Auraly.Contracts.Dispatching;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlDispatchSettlementPreparationStore
{
    public async Task<DispatchSettlementWork?> ClaimAsync(DispatchSettlementSignal signal, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SET NOCOUNT ON;
            DECLARE @Claimed table(Id uniqueidentifier);
            UPDATE operation
            SET Status=N'Processing',PreparationLeaseId=@Lease,
                NextAttemptAt=DATEADD(minute,5,SYSUTCDATETIME()),LastError=NULL,
                ActivationLeaseId=NULL,ActivationAvailableAt=DATEADD(minute,11,SYSUTCDATETIME())
            OUTPUT inserted.DispatchSettlementOperationId INTO @Claimed
            FROM dbo.DispatchSettlementOperations operation
            WHERE operation.DispatchSettlementOperationId=@Id AND operation.BusinessId=@Business
              AND operation.ActivationVersion=@Version AND operation.PreparationPhase<3
              AND operation.Status IN(N'Pending',N'Processing')
              AND operation.ActivationAvailableAt IS NOT NULL
              AND (operation.PreparationLeaseId IS NULL OR operation.NextAttemptAt<=SYSUTCDATETIME());

            SELECT operation.DispatchSettlementOperationId,operation.BusinessId,operation.DispatchId,
              operation.WorkSessionId,operation.RequestedBy,operation.RequestedAt,operation.Attempts,
              business.TenantId,dispatch.DispatchNumber,settlement.DispatchSettlementId,dispatch.DriverName,
              operation.PreparationPhase,operation.PreparationPlanJson
            FROM @Claimed claimed
            JOIN dbo.DispatchSettlementOperations operation ON operation.DispatchSettlementOperationId=claimed.Id
            JOIN dbo.Businesses business ON business.BusinessId=operation.BusinessId
            JOIN dbo.Dispatches dispatch ON dispatch.DispatchId=operation.DispatchId AND dispatch.BusinessId=operation.BusinessId
            JOIN dispatch.Closures settlement ON settlement.DispatchId=operation.DispatchId AND settlement.BusinessId=operation.BusinessId;
            """, connection);
        var lease = Guid.NewGuid();
        command.Parameters.AddWithValue("@Id", signal.OperationId);
        command.Parameters.AddWithValue("@Business", signal.BusinessId);
        command.Parameters.AddWithValue("@Version", signal.Version);
        command.Parameters.AddWithValue("@Lease", lease);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token)
            ? new DispatchSettlementWork(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetGuid(3), reader.GetGuid(4), reader.GetDateTimeOffset(5), reader.GetInt32(6),
                reader.GetGuid(7), reader.GetString(8), reader.GetGuid(9), reader.GetString(10),
                (DispatchPreparationPhase)reader.GetByte(11), lease, reader.IsDBNull(12) ? null : reader.GetString(12))
            : null;
    }

    public Task<IReadOnlyList<DispatchReturnWork>> LoadReturnsAsync(DispatchSettlementWork operation, CancellationToken token) =>
        LoadPlanAsync(operation, ReadReturnsAsync, token);

    public Task<IReadOnlyList<DispatchPaymentWork>> LoadPaymentsAsync(DispatchSettlementWork operation, CancellationToken token) =>
        LoadPlanAsync(operation, ReadPaymentsAsync, token);

    private async Task<IReadOnlyList<T>> LoadPlanAsync<T>(DispatchSettlementWork operation,
        Func<DispatchSettlementWork, CancellationToken, Task<IReadOnlyList<T>>> read, CancellationToken token)
    {
        if (operation.PlanJson is { } json)
            return JsonSerializer.Deserialize<T[]>(json)
                ?? throw new InvalidOperationException("El plan congelado de conciliación no es válido.");
        var items = await read(operation, token);
        if (operation.LeaseId is null)
            return items;

        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            UPDATE dbo.DispatchSettlementOperations SET PreparationPlanJson=@Plan
            WHERE DispatchSettlementOperationId=@Id AND BusinessId=@Business AND PreparationLeaseId=@Lease
              AND Status=N'Processing' AND PreparationPlanJson IS NULL;
            """, connection);
        BindOperation(command, operation);
        command.Parameters.Add("@Plan", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(items);
        if (await command.ExecuteNonQueryAsync(token) != 1)
            throw new DBConcurrencyException("La conciliación cambió de propietario antes de guardar su plan.");
        return items;
    }

    public async Task<bool> AdvanceAsync(DispatchSettlementWork operation,
        IReadOnlyList<DispatchSettlementPrerequisite> prerequisites, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SET DEADLOCK_PRIORITY LOW;
            UPDATE operation
            SET Status=CASE WHEN @Submitted=0 AND ready.AttentionCount>0 THEN N'NeedsAttention' ELSE N'Processing' END,
                LastError=CASE WHEN @Submitted=0 AND ready.AttentionCount>0
                    THEN N'Un documento de la conciliación requiere atención contable o de inventario.' END,
                PreparationPhase=PreparationPhase+1,PreparationPlanJson=NULL,
                PrerequisitesJson=CASE WHEN @Submitted=0 THEN @Expected ELSE PrerequisitesJson END,
                PreparationLeaseId=NULL,ActivationLeaseId=NULL,ActivationVersion=ActivationVersion+1,
                Attempts=0,NextAttemptAt=SYSUTCDATETIME(),
                ActivationAvailableAt=CASE WHEN @Submitted=0 AND ready.PendingCount=0 AND ready.AttentionCount=0 THEN SYSUTCDATETIME() END
            OUTPUT inserted.ActivationAvailableAt
            FROM dbo.DispatchSettlementOperations operation
            CROSS APPLY dispatch.SettlementPrerequisites(@Business,@Expected) ready
            WHERE operation.DispatchSettlementOperationId=@Id AND operation.BusinessId=@Business
              AND operation.PreparationLeaseId=@Lease AND operation.Status=N'Processing';
            """, connection);
        BindOperation(command, operation);
        command.Parameters.Add("@Expected", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(prerequisites);
        command.Parameters.AddWithValue("@Submitted", operation.Phase == DispatchPreparationPhase.CashDifference);
        return await command.ExecuteScalarAsync(token) is DateTimeOffset;
    }

    public async Task RescheduleAsync(DispatchSettlementWork operation, string error, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            UPDATE dbo.DispatchSettlementOperations
            SET Attempts=Attempts+1,
                Status=CASE WHEN Attempts>=2 THEN N'NeedsAttention' ELSE N'Processing' END,
                LastError=@Error,PreparationLeaseId=NULL,ActivationLeaseId=NULL,
                ActivationVersion=ActivationVersion+1,NextAttemptAt=SYSUTCDATETIME(),
                ActivationAvailableAt=CASE WHEN Attempts<2 THEN DATEADD(second,15,SYSUTCDATETIME()) END
            WHERE DispatchSettlementOperationId=@Id AND BusinessId=@Business AND PreparationLeaseId=@Lease
              AND Status=N'Processing';
            """, connection);
        BindOperation(command, operation);
        command.Parameters.Add("@Error", SqlDbType.NVarChar, 2000).Value = error[..Math.Min(error.Length, 2000)];
        await command.ExecuteNonQueryAsync(token);
    }

    private static void BindOperation(SqlCommand command, DispatchSettlementWork operation)
    {
        command.Parameters.AddWithValue("@Id", operation.Id);
        command.Parameters.AddWithValue("@Business", operation.BusinessId);
        command.Parameters.Add("@Lease", SqlDbType.UniqueIdentifier).Value = operation.LeaseId
            ?? throw new InvalidOperationException("La conciliación no tiene un reclamo vigente.");
    }
}
