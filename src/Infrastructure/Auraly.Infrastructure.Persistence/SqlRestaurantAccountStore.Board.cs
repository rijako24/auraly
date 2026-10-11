using System.Data;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlRestaurantAccountStore
{
    public async Task ChangeCommandStateAsync(RestaurantAccountActor actor,
        Guid cardId, string state, byte[] rowVersion, DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        const string sql = """
            UPDATE sales.RestaurantCommandCards
            SET State=@State,UpdatedAt=@Now,
                StartedAt=CASE WHEN @State=N'Preparing' THEN @Now ELSE StartedAt END,
                ReadyAt=CASE WHEN @State=N'Ready' THEN @Now ELSE ReadyAt END,
                DeliveredAt=CASE WHEN @State=N'Delivered' THEN @Now ELSE DeliveredAt END
            WHERE CardId=@CardId AND TenantId=@TenantId AND BusinessId=@BusinessId
              AND RowVersion=@Version
              AND NOT EXISTS(
                SELECT 1 FROM sales.RestaurantCancellationRequests cancellation
                WHERE cancellation.CardId=@CardId
                  AND cancellation.Status IN(N'Requested',N'Confirmed'))
              AND ((State=N'Pending' AND @State=N'Preparing')
                OR (State=N'Preparing' AND @State=N'Ready')
                OR (State=N'Ready' AND @State=N'ToDeliver')
                OR (State=N'ToDeliver' AND @State=N'Delivered'));
            IF @@ROWCOUNT<>1
              THROW 51083,N'La comanda cambió o no admite este estado.',1;

            INSERT dbo.AuditLogs
              (AuditLogId,UserId,TenantId,BusinessId,Action,
               EntityType,EntityId,NewValues,Timestamp)
            VALUES(NEWID(),@UserId,@TenantId,@BusinessId,
                   N'RestaurantCommandStateChanged',N'RestaurantCommandCard',
                   CONVERT(NVARCHAR(100),@CardId),@State,@AuditTime);

            DECLARE @Cursor BIGINT;
            SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
            FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND Stream=N'RestaurantCommands';
            INSERT dbo.PosSynchronizationOutboxMessages
              (NotificationId,BusinessId,Stream,AvailableThroughCursor,
               OccurredAt,EntityType,EntityId,ChangeKind)
            VALUES(NEWID(),@BusinessId,N'RestaurantCommands',@Cursor,
                   @Now,N'RestaurantCommandCard',@CardId,N'Upsert');
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@CardId", cardId);
        command.Parameters.AddWithValue("@UserId", actor.UserId);
        command.Parameters.AddWithValue("@State", state);
        command.Parameters.Add("@Version", SqlDbType.Binary, 8).Value = rowVersion;
        command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
        command.Parameters.Add("@AuditTime", SqlDbType.DateTime2).Value = now.UtcDateTime;
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException error) when (error.Number == 51083)
        { throw new RestaurantAccountConflictException(
            "La comanda cambió o no admite ese estado. Actualiza la vista."); }
        await transaction.CommitAsync(ct);
        await synchronization.DispatchPendingAsync(actor.TenantId,
            actor.BusinessId, CancellationToken.None);
    }

    public async Task<RestaurantCommandBoardPage> CommandCardsAsync(
        RestaurantAccountActor actor, Guid? stationId, string? state,
        string? tableCode, int page, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        const string sql = """
            SELECT card.CardId,card.StationId,card.StationName,card.TableCode,
                   card.State,card.ReceivedAt,card.RowVersion,
                   line.AdditionId,CASE WHEN card.State=N'Cancelled'
                     THEN line.Quantity ELSE addition.Quantity END,line.ProductName,
                   line.AddedByName,line.AddedAt,line.PreparationNote,
                   CASE WHEN printJob.State=N'Claimed' AND
                        printJob.ClaimedAt<DATEADD(MINUTE,-5,SYSUTCDATETIME())
                        THEN N'Uncertain' ELSE printJob.State END,
                   printJob.LastError,
                   cancellation.CancellationId,cancellation.Quantity,
                   cancellation.Status,cancellation.PreparationState,
                   cancellation.RowVersion
            FROM (
                SELECT CardId,StationId,StationName,TableCode,State,ReceivedAt,RowVersion
                FROM sales.RestaurantCommandCards
                WHERE TenantId=@TenantId AND BusinessId=@BusinessId
                  AND (@StationId IS NULL OR StationId=@StationId)
                  AND (@State IS NULL OR State=@State)
                  AND (@TableCode IS NULL OR TableCode=@TableCode)
                  AND State<>N'Delivered'
                  AND (@State=N'Cancelled' OR State<>N'Cancelled')
                ORDER BY ReceivedAt,CardId
                OFFSET @Offset ROWS FETCH NEXT 61 ROWS ONLY
            ) card
            JOIN sales.RestaurantCommandLines line ON line.CardId=card.CardId
            JOIN sales.RestaurantAccountAdditions addition
              ON addition.AdditionId=line.AdditionId
             AND addition.AccountId=line.AccountId
             AND (addition.Status=N'Sent' OR card.State=N'Cancelled')
            LEFT JOIN sales.RestaurantCommandPrintJobs printJob ON printJob.CardId=card.CardId
            LEFT JOIN sales.RestaurantCancellationRequests cancellation
              ON cancellation.AdditionId=line.AdditionId
             AND cancellation.Status IN(N'Requested',N'Confirmed')
            ORDER BY card.ReceivedAt,card.CardId,line.AddedAt,line.AdditionId;

            SELECT DISTINCT card.State
            FROM sales.RestaurantCommandCards card
            WHERE card.TenantId=@TenantId AND card.BusinessId=@BusinessId
              AND (@StationId IS NULL OR card.StationId=@StationId)
              AND (@TableCode IS NULL OR card.TableCode=@TableCode)
              AND card.State IN(N'Pending',N'Preparing',N'Ready',N'ToDeliver')
              AND EXISTS (
                SELECT 1 FROM sales.RestaurantCancellationRequests cancellation
                WHERE cancellation.CardId=card.CardId
                  AND cancellation.Status=N'Requested');
            """;
        await using var command = new SqlCommand(sql, connection);
        AddScope(command, actor);
        command.Parameters.Add("@StationId", SqlDbType.UniqueIdentifier).Value =
            (object?)stationId ?? DBNull.Value;
        command.Parameters.Add("@State", SqlDbType.NVarChar, 20).Value =
            (object?)state ?? DBNull.Value;
        command.Parameters.Add("@TableCode", SqlDbType.NVarChar, 32).Value =
            (object?)tableCode ?? DBNull.Value;
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = (page - 1) * 60;
        var cards = new List<RestaurantCommandCardView>();
        Guid lastCardId = Guid.Empty;
        List<RestaurantCommandLineView>? lines = null;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var cardId = reader.GetGuid(0);
            if (lastCardId != cardId)
            {
                lines = [];
                cards.Add(new(cardId, reader.GetGuid(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5),
                    Convert.ToBase64String((byte[])reader.GetValue(6)), lines,
                    reader.IsDBNull(13) ? null : reader.GetString(13),
                    reader.IsDBNull(14) ? null : reader.GetString(14)));
                lastCardId = cardId;
            }
            lines!.Add(new(reader.GetGuid(7), reader.GetDecimal(8),
                reader.GetString(9), reader.GetString(10),
                reader.GetFieldValue<DateTimeOffset>(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(15) ? null : reader.GetGuid(15),
                reader.IsDBNull(16) ? null : reader.GetDecimal(16),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.IsDBNull(19) ? null :
                    Convert.ToBase64String((byte[])reader.GetValue(19))));
        }
        var statesWithPendingCorrections = new List<string>();
        if (await reader.NextResultAsync(ct))
            while (await reader.ReadAsync(ct))
                statesWithPendingCorrections.Add(reader.GetString(0));
        var hasMore = cards.Count > 60;
        if (hasMore) cards.RemoveAt(cards.Count - 1);
        return new RestaurantCommandBoardPage(cards, page, hasMore,
            statesWithPendingCorrections);
    }
}
