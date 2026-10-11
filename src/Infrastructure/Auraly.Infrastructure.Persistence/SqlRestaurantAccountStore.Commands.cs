using System.Data;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlRestaurantAccountStore
{
    public async Task<SendRestaurantPendingResult> SendPendingAsync(
        RestaurantAccountActor actor, Guid accountId,
        SendRestaurantPendingRequest request, DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var replay = await FindRoundAsync(connection, transaction, actor,
            request.OperationId, ct);
        if (replay is { } previousRound)
        {
            ValidateReplay(previousRound, actor, accountId, request);
            var existing = previousRound.ResultJson is { } json
                ? JsonSerializer.Deserialize<SendRestaurantPendingResult>(json)
                : null;
            if (existing is null)
                throw new InvalidDataException("El recibo de envío no está disponible.");
            await transaction.CommitAsync(ct);
            return existing;
        }

        var account = await FindAsync(connection, transaction, actor, accountId,
            true, ct) ?? throw new RestaurantAccountConflictException(
                "La cuenta ya no está disponible para enviar comandas.");
        if (account.Status != "Open" || account.Version != request.ExpectedVersion)
            throw new RestaurantAccountConflictException(
                "La cuenta cambió. Revisa los pendientes antes de enviar.");

        var (total, unrouted) = await InspectPendingAsync(connection, transaction,
            actor, accountId, request.AdditionId, ct);
        if (total == 0)
            throw new RestaurantAccountConflictException(
                "No hay productos pendientes para enviar.");
        if (unrouted > 0)
            throw new RestaurantAccountConflictException(
                "Hay productos sin estación o comanda activa. Corrige su destino antes de enviar.");

        var roundId = Guid.NewGuid();
        await PersistRoundAsync(connection, transaction, actor, account,
            roundId, request, now, ct);
        var result = await ReadResultAsync(connection, transaction, actor,
            accountId, roundId, ct);
        await SaveReceiptAsync(connection, transaction, actor, roundId,
            result, ct);
        await transaction.CommitAsync(ct);
        // The round is already committed. A cancelled HTTP request must not
        // turn a successful send into a misleading failure for the waiter.
        await synchronization.DispatchPendingAsync(actor.TenantId,
            actor.BusinessId, CancellationToken.None);
        return result;
    }

    private static async Task<(int Total, int Unrouted)> InspectPendingAsync(
        SqlConnection connection, SqlTransaction transaction,
        RestaurantAccountActor actor, Guid accountId, Guid? additionId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(*),COALESCE(SUM(CASE WHEN station.StationId IS NULL
              OR configuration.ConfigurationId IS NULL THEN 1 ELSE 0 END),0)
            FROM sales.RestaurantAccountAdditions addition WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN sales.RestaurantProductRoutes route
              ON route.BusinessId=addition.BusinessId
             AND route.ProductId=addition.ProductId
            LEFT JOIN sales.RestaurantStations station
              ON station.BusinessId=route.BusinessId
             AND station.StationId=route.StationId AND station.IsActive=1
            LEFT JOIN sales.RestaurantCommandConfigurations configuration
              ON configuration.BusinessId=station.BusinessId
             AND configuration.StationId=station.StationId
             AND configuration.IsActive=1
            WHERE addition.TenantId=@TenantId
              AND addition.BusinessId=@BusinessId
              AND addition.AccountId=@AccountId
              AND addition.Status=N'Pending'
              AND (@AdditionId IS NULL OR addition.AdditionId=@AdditionId);

            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@AccountId", accountId);
        command.Parameters.Add("@AdditionId", SqlDbType.UniqueIdentifier)
            .Value = (object?)additionId ?? DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private static async Task PersistRoundAsync(
        SqlConnection connection, SqlTransaction transaction,
        RestaurantAccountActor actor, RestaurantAccount account,
        Guid roundId, SendRestaurantPendingRequest request,
        DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            INSERT sales.RestaurantCommandRounds
              (RoundId,TenantId,BusinessId,AccountId,OperationId,
               ExpectedAccountVersion,SelectedAdditionId,SentByUserId,
               ConnectedUserId,SentAt)
            VALUES(@RoundId,@TenantId,@BusinessId,@AccountId,@OperationId,
                   @ExpectedVersion,@AdditionId,@UserId,@ConnectedUserId,@Now);

            INSERT sales.RestaurantCommandCards
              (CardId,TenantId,BusinessId,RoundId,AccountId,StationId,
               ConfigurationId,StationName,TableCode,State,ReceivedAt,UpdatedAt)
            SELECT NEWID(),@TenantId,@BusinessId,@RoundId,@AccountId,
                   station.StationId,configuration.ConfigurationId,
                   station.Name,[table].Code,
                   CASE WHEN station.Kind=N'Pickup' THEN N'ToDeliver'
                        ELSE N'Pending' END,@Now,@Now
            FROM sales.RestaurantAccountAdditions addition
            JOIN sales.RestaurantProductRoutes route
              ON route.BusinessId=addition.BusinessId
             AND route.ProductId=addition.ProductId
            JOIN sales.RestaurantStations station
              ON station.BusinessId=route.BusinessId
             AND station.StationId=route.StationId AND station.IsActive=1
            JOIN sales.RestaurantCommandConfigurations configuration
              ON configuration.BusinessId=station.BusinessId
             AND configuration.StationId=station.StationId
             AND configuration.IsActive=1
            JOIN sales.RestaurantAccounts account
              ON account.AccountId=addition.AccountId
             AND account.BusinessId=addition.BusinessId
            JOIN sales.RestaurantTables [table]
              ON [table].BusinessId=account.BusinessId
             AND [table].TableId=account.TableId
            WHERE addition.TenantId=@TenantId
              AND addition.BusinessId=@BusinessId
              AND addition.AccountId=@AccountId
              AND addition.Status=N'Pending'
              AND (@AdditionId IS NULL OR addition.AdditionId=@AdditionId)
            GROUP BY station.StationId,station.Name,station.Kind,
                     configuration.ConfigurationId,[table].Code;

            INSERT sales.RestaurantCommandLines
              (CardId,AdditionId,AccountId,Quantity,ProductName,PreparationNote,
               AddedByUserId,AddedByName,AddedAt)
            SELECT card.CardId,addition.AdditionId,@AccountId,
                   addition.Quantity,COALESCE(route.ShortName,addition.Description),
                   addition.PreparationNote,
                   addition.AddedByUserId,addition.AddedByName,addition.AddedAt
            FROM sales.RestaurantAccountAdditions addition
            JOIN sales.RestaurantProductRoutes route
              ON route.BusinessId=addition.BusinessId
             AND route.ProductId=addition.ProductId
            JOIN sales.RestaurantCommandCards card
              ON card.RoundId=@RoundId AND card.BusinessId=@BusinessId
             AND card.StationId=route.StationId
            WHERE addition.TenantId=@TenantId
              AND addition.BusinessId=@BusinessId
              AND addition.AccountId=@AccountId
              AND addition.Status=N'Pending'
              AND (@AdditionId IS NULL OR addition.AdditionId=@AdditionId);

            INSERT sales.RestaurantCommandPrintJobs
              (CardId,TenantId,BusinessId,DeviceId,ProfileKey,
               State,CreatedAt,UpdatedAt)
            SELECT card.CardId,@TenantId,@BusinessId,
                   configuration.PrinterDeviceId,configuration.PrinterProfileKey,
                   N'Pending',@Now,@Now
            FROM sales.RestaurantCommandCards card
            JOIN sales.RestaurantCommandConfigurations configuration
              ON configuration.ConfigurationId=card.ConfigurationId
             AND configuration.BusinessId=card.BusinessId
            WHERE card.RoundId=@RoundId AND card.BusinessId=@BusinessId
              AND configuration.Mode IN (N'Physical',N'Both');

            UPDATE addition
            SET Status=N'Sent',UpdatedAt=@Now,
                ManageStockAtSend=product.ManageStock,
                ProductRecipeVersionId=CASE WHEN product.ManageStock=0 THEN (
                  SELECT TOP(1) versionRow.ProductRecipeVersionId
                  FROM dbo.ProductRecipeVersions versionRow
                  WHERE versionRow.TenantId=addition.TenantId
                    AND versionRow.ProductId=addition.ProductId
                    AND versionRow.IsEnabled=1
                    AND versionRow.[Version]=(
                      SELECT MAX(latest.[Version])
                      FROM dbo.ProductRecipeVersions latest
                      WHERE latest.TenantId=versionRow.TenantId
                        AND latest.ProductId=versionRow.ProductId)
                  ) ELSE NULL END
            FROM sales.RestaurantAccountAdditions addition
            JOIN dbo.Products product ON product.TenantId=addition.TenantId
              AND product.ProductId=addition.ProductId
            WHERE addition.TenantId=@TenantId AND addition.BusinessId=@BusinessId
              AND addition.AccountId=@AccountId AND addition.Status=N'Pending'
              AND (@AdditionId IS NULL OR addition.AdditionId=@AdditionId);

            UPDATE sales.RestaurantAccounts
            SET Version=Version+1,UpdatedAt=@Now
            WHERE AccountId=@AccountId AND TenantId=@TenantId
              AND BusinessId=@BusinessId AND Version=@ExpectedVersion
              AND Status=N'Open';
            IF @@ROWCOUNT<>1
              THROW 51082,N'La cuenta cambió durante el envío.',1;

            INSERT dbo.AuditLogs
              (AuditLogId,UserId,TenantId,BusinessId,Action,
               EntityType,EntityId,NewValues,CorrelationId,Timestamp)
            VALUES(NEWID(),@UserId,@TenantId,@BusinessId,
                   N'RestaurantCommandSent',N'RestaurantAccount',
                   CONVERT(NVARCHAR(100),@AccountId),@Audit,
                   CONVERT(NVARCHAR(100),@OperationId),@AuditTime);

            DECLARE @Cursor BIGINT;
            SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
            FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND Stream=N'RestaurantCommands';
            INSERT dbo.PosSynchronizationOutboxMessages
              (NotificationId,BusinessId,Stream,AvailableThroughCursor,
               OccurredAt,EntityType,EntityId,ChangeKind)
            VALUES(NEWID(),@BusinessId,N'RestaurantCommands',@Cursor,
                   @Now,N'RestaurantCommandRound',@RoundId,N'Upsert');

            SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)
            FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND Stream=N'RestaurantCommandPrint';
            INSERT dbo.PosSynchronizationOutboxMessages
              (NotificationId,BusinessId,Stream,AvailableThroughCursor,
               OccurredAt,EntityType,EntityId,ChangeKind,TargetDeviceId)
            SELECT NEWID(),@BusinessId,N'RestaurantCommandPrint',
                   @Cursor+ROW_NUMBER() OVER(ORDER BY job.CardId),
                   @Now,N'RestaurantCommandPrintJob',job.CardId,N'Upsert',
                   job.DeviceId
            FROM sales.RestaurantCommandPrintJobs job
            JOIN sales.RestaurantCommandCards card ON card.CardId=job.CardId
            WHERE card.RoundId=@RoundId AND job.BusinessId=@BusinessId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@AccountId", account.AccountId);
        command.Parameters.AddWithValue("@RoundId", roundId);
        command.Parameters.AddWithValue("@OperationId", request.OperationId);
        command.Parameters.AddWithValue("@ExpectedVersion", request.ExpectedVersion);
        command.Parameters.AddWithValue("@UserId", actor.UserId);
        command.Parameters.Add("@ConnectedUserId", SqlDbType.UniqueIdentifier)
            .Value = (object?)actor.ConnectedUserId ?? DBNull.Value;
        command.Parameters.Add("@AdditionId", SqlDbType.UniqueIdentifier)
            .Value = (object?)request.AdditionId ?? DBNull.Value;
        command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
        command.Parameters.Add("@AuditTime", SqlDbType.DateTime2).Value = now.UtcDateTime;
        command.Parameters.Add("@Audit", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(new { RoundId = roundId,
                request.AdditionId, actor.ConnectedUserId,
                actor.CredentialVerified,
                AccountVersion = request.ExpectedVersion + 1 });
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException error) when (error.Number == 51082)
        { throw new RestaurantAccountConflictException(
            "La cuenta cambió durante el envío. Revisa los pendientes."); }
    }

    private static async Task<RoundIdentity?> FindRoundAsync(
        SqlConnection connection, SqlTransaction transaction,
        RestaurantAccountActor actor, Guid operationId, CancellationToken ct)
    {
        const string sql = """
            SELECT RoundId,AccountId,ExpectedAccountVersion,SelectedAdditionId,
                   SentByUserId,ConnectedUserId,ResultJson
            FROM sales.RestaurantCommandRounds WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId
              AND OperationId=@OperationId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@OperationId", operationId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.IsDBNull(6) ? null : reader.GetString(6)) : null;
    }

    private static void ValidateReplay(RoundIdentity round,
        RestaurantAccountActor actor, Guid accountId,
        SendRestaurantPendingRequest request)
    {
        if (round.AccountId != accountId ||
            round.ExpectedVersion != request.ExpectedVersion ||
            round.SelectedAdditionId != request.AdditionId ||
            round.SentByUserId != actor.UserId ||
            round.ConnectedUserId != actor.ConnectedUserId)
            throw new RestaurantAccountConflictException(
                "La identidad de envío ya corresponde a otra comanda.");
    }

    private static async Task<SendRestaurantPendingResult> ReadResultAsync(
        SqlConnection connection, SqlTransaction transaction,
        RestaurantAccountActor actor, Guid accountId, Guid roundId,
        CancellationToken ct)
    {
        var draft = await LoadDraftAsync(connection, transaction, actor,
            accountId, ct) ?? throw new RestaurantAccountConflictException(
                "La cuenta ya no está disponible.");
        const string sql = """
            SELECT card.CardId,card.StationId,card.StationName,card.State,
                   COUNT(line.AdditionId)
            FROM sales.RestaurantCommandCards card
            JOIN sales.RestaurantCommandLines line ON line.CardId=card.CardId
            WHERE card.TenantId=@TenantId AND card.BusinessId=@BusinessId
              AND card.RoundId=@RoundId AND card.AccountId=@AccountId
            GROUP BY card.CardId,card.StationId,card.StationName,card.State
            ORDER BY card.StationName,card.CardId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@RoundId", roundId);
        command.Parameters.AddWithValue("@AccountId", accountId);
        var cards = new List<RestaurantCommandCardSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            cards.Add(new(reader.GetGuid(0), reader.GetGuid(1),
                reader.GetString(2), reader.GetString(3), reader.GetInt32(4)));
        if (cards.Count == 0)
            throw new InvalidDataException("El envío no creó tarjetas de comanda.");
        return new(roundId, draft, cards);
    }

    private static async Task SaveReceiptAsync(SqlConnection connection,
        SqlTransaction transaction, RestaurantAccountActor actor,
        Guid roundId, SendRestaurantPendingResult result,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            UPDATE sales.RestaurantCommandRounds
            SET ResultJson=@Receipt
            WHERE RoundId=@RoundId AND TenantId=@TenantId
              AND BusinessId=@BusinessId AND ResultJson IS NULL;
            """, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@RoundId", roundId);
        command.Parameters.Add("@Receipt", SqlDbType.NVarChar, -1)
            .Value = JsonSerializer.Serialize(result);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidDataException("El recibo de envío no se pudo confirmar.");
    }

    private sealed record RoundIdentity(Guid RoundId, Guid AccountId,
        long ExpectedVersion, Guid? SelectedAdditionId,
        Guid SentByUserId, Guid? ConnectedUserId, string? ResultJson);
}
