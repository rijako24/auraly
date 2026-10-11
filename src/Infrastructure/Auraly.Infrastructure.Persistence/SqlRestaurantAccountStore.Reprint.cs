using System.Data;
using Auraly.Application.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlRestaurantAccountStore
{
    public async Task ReprintCommandAsync(RestaurantAccountActor actor,
        Guid cardId, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var command = new SqlCommand("""
            DECLARE @Changed TABLE(DeviceId UNIQUEIDENTIFIER,
                PreviousState NVARCHAR(16),CopyNumber INT);
            UPDATE job SET State=N'Pending',LeaseToken=NULL,ClaimedAt=NULL,
                SubmittedAt=NULL,LastError=NULL,CopyNumber=CopyNumber+1,
                UpdatedAt=@Now
            OUTPUT inserted.DeviceId,deleted.State,inserted.CopyNumber
              INTO @Changed
            FROM sales.RestaurantCommandPrintJobs job
            WHERE job.CardId=@CardId AND job.TenantId=@TenantId
              AND job.BusinessId=@BusinessId
              AND (job.State IN (N'Submitted',N'Failed',N'Uncertain')
                OR (job.State=N'Claimed' AND
                    job.ClaimedAt<DATEADD(MINUTE,-5,@Now)));
            IF @@ROWCOUNT<>1
              THROW 51086,N'La tirilla está pendiente o no admite copia.',1;

            INSERT dbo.AuditLogs
              (AuditLogId,UserId,TenantId,BusinessId,Action,
               EntityType,EntityId,OldValues,NewValues,Timestamp)
            SELECT NEWID(),@UserId,@TenantId,@BusinessId,
                   N'RestaurantCommandReprintRequested',
                   N'RestaurantCommandPrintJob',CONVERT(NVARCHAR(100),@CardId),
                   PreviousState,CONCAT(N'Copia ',CopyNumber),@AuditTime
            FROM @Changed;

            DECLARE @Cursor BIGINT;
            SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
            FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND Stream=N'RestaurantCommandPrint';
            INSERT dbo.PosSynchronizationOutboxMessages
              (NotificationId,BusinessId,Stream,AvailableThroughCursor,
               OccurredAt,EntityType,EntityId,ChangeKind,TargetDeviceId)
            SELECT NEWID(),@BusinessId,N'RestaurantCommandPrint',@Cursor,
                   @Now,N'RestaurantCommandPrintJob',@CardId,N'Upsert',DeviceId
            FROM @Changed;
            """, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@CardId", cardId);
        command.Parameters.AddWithValue("@UserId", actor.UserId);
        command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
        command.Parameters.Add("@AuditTime", SqlDbType.DateTime2).Value =
            now.UtcDateTime;
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException error) when (error.Number == 51086)
        { throw new RestaurantAccountConflictException(error.Message); }
        await transaction.CommitAsync(ct);
        await synchronization.DispatchPendingAsync(actor.TenantId,
            actor.BusinessId, CancellationToken.None);
    }
}
