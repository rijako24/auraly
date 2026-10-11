using System.Data;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlRestaurantAccountStore
{
    public async Task<RestaurantAccountDraft> ChangeQuantityAsync(
        RestaurantAccountActor actor, Guid accountId, Guid additionId,
        ChangeRestaurantAccountQuantityRequest request,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        var existing = await FindQuantityChangeAsync(connection, null,
            actor, request.ChangeId, ct);
        if (existing is not null)
            return await ReplayQuantityChangeAsync(connection, null, actor,
                accountId, additionId, request, existing, ct);

        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var account = await FindAsync(connection, transaction, actor,
            accountId, true, ct) ?? throw new RestaurantAccountConflictException(
                "La cuenta ya no está disponible.");
        existing = await FindQuantityChangeAsync(connection, transaction,
            actor, request.ChangeId, ct);
        if (existing is not null)
        {
            var replay = await ReplayQuantityChangeAsync(connection,
                transaction, actor, accountId, additionId, request,
                existing, ct);
            await transaction.CommitAsync(ct);
            return replay;
        }
        if (account.Status != "Open" || account.Version != request.ExpectedVersion)
            throw new RestaurantAccountConflictException(
                "La cuenta cambió. Revisa las cantidades antes de continuar.");

        const string readSql = """
            SELECT Quantity,AllowsFractionalSale,Status
            FROM sales.RestaurantAccountAdditions WITH (UPDLOCK,HOLDLOCK)
            WHERE AdditionId=@AdditionId AND AccountId=@AccountId
              AND TenantId=@TenantId AND BusinessId=@BusinessId;
            """;
        decimal oldQuantity;
        bool allowsFractional;
        string status;
        await using (var read = new SqlCommand(readSql, connection, transaction))
        {
            AddScope(read, actor);
            read.Parameters.AddWithValue("@AccountId", accountId);
            read.Parameters.AddWithValue("@AdditionId", additionId);
            await using var reader = await read.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new RestaurantAccountConflictException(
                    "El producto ya no pertenece a esta cuenta.");
            oldQuantity = reader.GetDecimal(0);
            allowsFractional = reader.GetBoolean(1);
            status = reader.GetString(2);
        }
        if (status is not ("Pending" or "Direct"))
            throw new RestaurantAccountConflictException(
                "Un producto enviado a comanda requiere una corrección autorizada.");
        if (request.Quantity >= oldQuantity ||
            (!allowsFractional &&
             request.Quantity != decimal.Truncate(request.Quantity)))
            throw new RestaurantAccountValidationException(
                "Solo puedes reducir pendientes; agregar unidades crea otra incorporación con su propio autor.");

        const string updateSql = """
            UPDATE sales.RestaurantAccountAdditions
            SET Quantity=CASE WHEN @Quantity=0 THEN Quantity ELSE @Quantity END,
                Status=CASE WHEN @Quantity=0 THEN N'Cancelled' ELSE Status END,
                CancelledAt=CASE WHEN @Quantity=0 THEN @Now ELSE NULL END,
                UpdatedAt=@Now
            WHERE AdditionId=@AdditionId AND AccountId=@AccountId
              AND TenantId=@TenantId AND BusinessId=@BusinessId
              AND Status IN(N'Pending',N'Direct');
            """;
        await using (var update = new SqlCommand(updateSql, connection, transaction))
        {
            AddScope(update, actor);
            update.Parameters.AddWithValue("@AccountId", accountId);
            update.Parameters.AddWithValue("@AdditionId", additionId);
            update.Parameters.Add("@Quantity", SqlDbType.Decimal).Value = request.Quantity;
            update.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            if (await update.ExecuteNonQueryAsync(ct) != 1)
                throw new RestaurantAccountConflictException(
                    "El producto cambió durante la corrección.");
        }
        const string advanceSql = """
            UPDATE sales.RestaurantAccounts
            SET Version=Version+1,UpdatedAt=@Now
            WHERE AccountId=@AccountId AND TenantId=@TenantId
              AND BusinessId=@BusinessId AND Status=N'Open'
              AND Version=@Version;
            """;
        await using (var advance = new SqlCommand(advanceSql, connection, transaction))
        {
            AddScope(advance, actor);
            advance.Parameters.AddWithValue("@AccountId", accountId);
            advance.Parameters.AddWithValue("@Version", request.ExpectedVersion);
            advance.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            if (await advance.ExecuteNonQueryAsync(ct) != 1)
                throw new RestaurantAccountConflictException(
                    "La cuenta cambió durante la corrección.");
        }
        const string receiptSql = """
            INSERT sales.RestaurantAccountQuantityChanges
                (ChangeId,TenantId,BusinessId,AccountId,AdditionId,
                 PreviousQuantity,NewQuantity,ExpectedVersion,
                 AccountVersionAfter,ActorUserId,ChangedAt)
            SELECT @ChangeId,@TenantId,@BusinessId,@AccountId,@AdditionId,
                   @PreviousQuantity,@Quantity,@Version,@Version+1,
                   [user].UserId,@Now
            FROM dbo.AppUsers [user]
            LEFT JOIN parties.Waiters waiter
              ON waiter.TenantId=[user].TenantId
             AND waiter.PartyId=[user].PartyId AND waiter.IsActive=1
            WHERE [user].UserId=@UserId AND [user].TenantId=@TenantId
              AND [user].IsActive=1
              AND (@CanOpenWithoutWaiter=1 OR waiter.PartyId IS NOT NULL);
            """;
        await using (var receipt = new SqlCommand(receiptSql, connection, transaction))
        {
            AddScope(receipt, actor);
            receipt.Parameters.AddWithValue("@ChangeId", request.ChangeId);
            receipt.Parameters.AddWithValue("@AccountId", accountId);
            receipt.Parameters.AddWithValue("@AdditionId", additionId);
            receipt.Parameters.AddWithValue("@UserId", actor.UserId);
            receipt.Parameters.Add("@PreviousQuantity", SqlDbType.Decimal).Value = oldQuantity;
            receipt.Parameters.Add("@Quantity", SqlDbType.Decimal).Value = request.Quantity;
            receipt.Parameters.AddWithValue("@Version", request.ExpectedVersion);
            receipt.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            if (await receipt.ExecuteNonQueryAsync(ct) != 1)
                throw new RestaurantAccountForbiddenException(
                    "El mesero ya no está habilitado para corregir.");
        }
        await AuditQuantityChangeAsync(connection, transaction, actor,
            accountId, additionId, request.ChangeId, oldQuantity,
            request.Quantity, request.ExpectedVersion + 1, now, ct);
        var draft = await LoadDraftAsync(connection, transaction,
            actor, accountId, ct) ?? throw new InvalidDataException(
                "La cuenta desapareció durante la corrección.");
        await transaction.CommitAsync(ct);
        return draft;
    }

    private sealed record QuantityChangeReceipt(
        Guid AccountId, Guid AdditionId, decimal Quantity,
        long ExpectedVersion, Guid ActorUserId);

    private static async Task<QuantityChangeReceipt?> FindQuantityChangeAsync(
        SqlConnection connection, SqlTransaction? transaction,
        RestaurantAccountActor actor, Guid changeId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT change.AccountId,change.AdditionId,change.NewQuantity,
                   change.ExpectedVersion,change.ActorUserId
            FROM sales.RestaurantAccountQuantityChanges change
            WHERE change.ChangeId=@ChangeId AND change.TenantId=@TenantId
              AND change.BusinessId=@BusinessId
              AND EXISTS(
                SELECT 1 FROM dbo.AppUsers [user]
                LEFT JOIN parties.Waiters waiter
                  ON waiter.TenantId=[user].TenantId
                 AND waiter.PartyId=[user].PartyId AND waiter.IsActive=1
                WHERE [user].UserId=@UserId AND [user].TenantId=@TenantId
                  AND [user].IsActive=1
                  AND (@CanOpenWithoutWaiter=1 OR waiter.PartyId IS NOT NULL));
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@ChangeId", changeId);
        command.Parameters.AddWithValue("@UserId", actor.UserId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(reader.GetGuid(0), reader.GetGuid(1),
                reader.GetDecimal(2), reader.GetInt64(3), reader.GetGuid(4))
            : null;
    }

    private static async Task<RestaurantAccountDraft> ReplayQuantityChangeAsync(
        SqlConnection connection, SqlTransaction? transaction,
        RestaurantAccountActor actor, Guid accountId, Guid additionId,
        ChangeRestaurantAccountQuantityRequest request,
        QuantityChangeReceipt existing, CancellationToken ct)
    {
        if (existing.AccountId != accountId ||
            existing.AdditionId != additionId ||
            existing.Quantity != request.Quantity ||
            existing.ExpectedVersion != request.ExpectedVersion ||
            existing.ActorUserId != actor.UserId)
            throw new RestaurantAccountConflictException(
                "Esta operación ya corresponde a otra corrección.");
        return await LoadDraftAsync(connection, transaction,
            actor, accountId, ct) ?? throw new RestaurantAccountConflictException(
                "La cuenta ya no está disponible.");
    }

    private static async Task AuditQuantityChangeAsync(
        SqlConnection connection, SqlTransaction transaction,
        RestaurantAccountActor actor, Guid accountId, Guid additionId,
        Guid changeId, decimal before, decimal after,
        long version, DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            INSERT dbo.AuditLogs
                (AuditLogId,UserId,TenantId,BusinessId,Action,EntityType,
                 EntityId,OldValues,NewValues,CorrelationId,Timestamp)
            VALUES(NEWID(),@UserId,@TenantId,@BusinessId,
                   N'RestaurantAccountQuantityChanged',N'RestaurantAccount',
                   @EntityId,@OldValues,@NewValues,@ChangeId,@Now);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@UserId", actor.UserId);
        command.Parameters.Add("@EntityId", SqlDbType.NVarChar, 100)
            .Value = accountId.ToString();
        command.Parameters.Add("@OldValues", SqlDbType.NVarChar, -1)
            .Value = JsonSerializer.Serialize(new { AdditionId = additionId,
                Quantity = before });
        command.Parameters.Add("@NewValues", SqlDbType.NVarChar, -1)
            .Value = JsonSerializer.Serialize(new { AdditionId = additionId,
                Quantity = after, Status = after == 0 ? "Cancelled" : "Pending",
                Version = version, actor.ConnectedUserId,
                actor.CredentialVerified });
        command.Parameters.Add("@ChangeId", SqlDbType.NVarChar, 100)
            .Value = changeId.ToString();
        command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = now.UtcDateTime;
        await command.ExecuteNonQueryAsync(ct);
    }
}
