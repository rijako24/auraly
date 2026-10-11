using System.Data;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed record RestaurantPrintClaim(Guid CardId, Guid LeaseToken,
    string ProfileKey, bool IsCopy, RestaurantCommandCardView Card,
    string PrintKind = "Original", decimal? RemovedQuantity = null,
    string? RemovedProductName = null);

public sealed class SqlRestaurantCommandPrintStore(
    SqlServerConnectionFactory connections,
    IPosSynchronizationOutboxDispatcher synchronization)
{
    public async Task<IReadOnlyList<RestaurantPrintClaim>> ClaimAsync(
        Guid tenantId, Guid deviceId, CancellationToken ct)
    {
        var leaseToken = Guid.NewGuid();
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await using var command = new SqlCommand("""
            DECLARE @Claimed TABLE(CardId UNIQUEIDENTIFIER,BusinessId UNIQUEIDENTIFIER);
            ;WITH picked AS (
              SELECT TOP (20) * FROM sales.RestaurantCommandPrintJobs WITH (UPDLOCK,READPAST,ROWLOCK)
              WHERE TenantId=@TenantId AND DeviceId=@DeviceId AND State=N'Pending'
                AND EXISTS(SELECT 1 FROM sales.RestaurantCommandCards card
                  WHERE card.CardId=RestaurantCommandPrintJobs.CardId
                    AND (card.State<>N'Cancelled' OR
                         RestaurantCommandPrintJobs.PrintKind=N'Correction'))
              ORDER BY CreatedAt,CardId
            )
            UPDATE picked SET State=N'Claimed',LeaseToken=@LeaseToken,
              ClaimedAt=SYSUTCDATETIME(),UpdatedAt=SYSUTCDATETIME()
            OUTPUT inserted.CardId,inserted.BusinessId INTO @Claimed;

            INSERT dbo.AuditLogs
              (AuditLogId,TenantId,BusinessId,Action,EntityType,
               EntityId,NewValues,CorrelationId,Timestamp)
            SELECT NEWID(),@TenantId,BusinessId,
                   N'RestaurantCommandPrintClaimed',N'RestaurantCommandPrintJob',
                   CONVERT(NVARCHAR(100),CardId),
                   CONVERT(NVARCHAR(100),@DeviceId),
                   CONVERT(NVARCHAR(100),@LeaseToken),SYSUTCDATETIME()
            FROM @Claimed;

            SELECT job.CardId,job.ProfileKey,job.CopyNumber,card.StationId,card.StationName,
                   card.TableCode,card.State,card.ReceivedAt,card.RowVersion,
                   line.AdditionId,addition.Quantity,line.ProductName,
                   line.AddedByName,line.AddedAt,line.PreparationNote,
                   job.PrintKind,cancellation.Quantity,correctedLine.ProductName
            FROM sales.RestaurantCommandPrintJobs job
            JOIN sales.RestaurantCommandCards card ON card.CardId=job.CardId
            JOIN sales.RestaurantCommandLines line ON line.CardId=card.CardId
            JOIN sales.RestaurantAccountAdditions addition
              ON addition.AdditionId=line.AdditionId
             AND addition.AccountId=line.AccountId
             AND (addition.Status=N'Sent' OR card.State=N'Cancelled')
            LEFT JOIN sales.RestaurantCancellationRequests cancellation
              ON cancellation.CancellationId=job.CorrectionCancellationId
            LEFT JOIN sales.RestaurantCommandLines correctedLine
              ON correctedLine.AdditionId=cancellation.AdditionId
            WHERE job.TenantId=@TenantId AND job.DeviceId=@DeviceId
              AND job.LeaseToken=@LeaseToken AND job.State=N'Claimed'
            ORDER BY card.ReceivedAt,card.CardId,line.AddedAt,line.AdditionId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@DeviceId", deviceId);
        command.Parameters.AddWithValue("@LeaseToken", leaseToken);
        var claims = new List<RestaurantPrintClaim>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var cardId = reader.GetGuid(0);
                if (claims.Count == 0 || claims[^1].CardId != cardId)
                {
                    var lines = new List<RestaurantCommandLineView>();
                    claims.Add(new(cardId, leaseToken, reader.GetString(1),
                        reader.GetInt32(2) > 0,
                        new(cardId, reader.GetGuid(3), reader.GetString(4),
                            reader.GetString(5), reader.GetString(6),
                            reader.GetFieldValue<DateTimeOffset>(7),
                            Convert.ToBase64String(reader.GetFieldValue<byte[]>(8)),
                            lines),reader.GetString(15),
                        reader.IsDBNull(16) ? null : reader.GetDecimal(16),
                        reader.IsDBNull(17) ? null : reader.GetString(17)));
                }
                ((List<RestaurantCommandLineView>)claims[^1].Card.Lines).Add(
                    new(reader.GetGuid(9), reader.GetDecimal(10),
                        reader.GetString(11), reader.GetString(12),
                        reader.GetFieldValue<DateTimeOffset>(13),
                        reader.IsDBNull(14) ? null : reader.GetString(14)));
            }
        }
        await transaction.CommitAsync(ct);
        return claims;
    }

    public async Task<bool> AcknowledgeAsync(Guid tenantId, Guid deviceId,
        Guid cardId, Guid leaseToken, string state, string? error,
        CancellationToken ct)
    {
        if (state is not ("Submitted" or "Failed" or "Uncertain"))
            throw new ArgumentException("Invalid print result.", nameof(state));
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var command = new SqlCommand("""
            DECLARE @Changed TABLE(BusinessId UNIQUEIDENTIFIER);
            UPDATE sales.RestaurantCommandPrintJobs
            SET State=@State,SubmittedAt=CASE WHEN @State=N'Submitted'
                  THEN SYSUTCDATETIME() ELSE NULL END,
                LastError=@Error,UpdatedAt=SYSUTCDATETIME()
            OUTPUT inserted.BusinessId INTO @Changed
            WHERE CardId=@CardId AND TenantId=@TenantId
              AND DeviceId=@DeviceId AND LeaseToken=@LeaseToken
              AND State=N'Claimed';
            DECLARE @BusinessId UNIQUEIDENTIFIER=(SELECT TOP(1) BusinessId FROM @Changed);
            IF @BusinessId IS NOT NULL
            BEGIN
              INSERT dbo.AuditLogs
                (AuditLogId,TenantId,BusinessId,Action,EntityType,
                 EntityId,NewValues,CorrelationId,Timestamp)
              VALUES(NEWID(),@TenantId,@BusinessId,
                     N'RestaurantCommandPrintAcknowledged',
                     N'RestaurantCommandPrintJob',
                     CONVERT(NVARCHAR(100),@CardId),
                     CONCAT(@State,N': ',COALESCE(@Error,N'')),
                     CONVERT(NVARCHAR(100),@LeaseToken),SYSUTCDATETIME());
              DECLARE @Cursor BIGINT;
              SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
              FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
              WHERE BusinessId=@BusinessId AND Stream=N'RestaurantCommands';
              INSERT dbo.PosSynchronizationOutboxMessages
                (NotificationId,BusinessId,Stream,AvailableThroughCursor,
                 OccurredAt,EntityType,EntityId,ChangeKind)
              VALUES(NEWID(),@BusinessId,N'RestaurantCommands',@Cursor,
                     SYSUTCDATETIME(),N'RestaurantCommandPrintJob',@CardId,N'Upsert');
            END
            SELECT @BusinessId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@DeviceId", deviceId);
        command.Parameters.AddWithValue("@CardId", cardId);
        command.Parameters.AddWithValue("@LeaseToken", leaseToken);
        command.Parameters.Add("@State", SqlDbType.NVarChar, 16).Value = state;
        command.Parameters.Add("@Error", SqlDbType.NVarChar, 500).Value =
            (object?)error?[..Math.Min(error.Length, 500)] ?? DBNull.Value;
        var value = await command.ExecuteScalarAsync(ct);
        await transaction.CommitAsync(ct);
        if (value is not Guid businessId) return false;
        await synchronization.DispatchPendingAsync(tenantId, businessId,
            CancellationToken.None);
        return true;
    }
}
