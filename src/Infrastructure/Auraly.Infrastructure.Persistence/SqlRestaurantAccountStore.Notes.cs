using System.Data;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlRestaurantAccountStore
{
    public async Task<RestaurantAccountDraft> ChangeNoteAsync(
        RestaurantAccountActor actor, Guid accountId, Guid additionId,
        ChangeRestaurantAccountNoteRequest request, DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var account = await FindAsync(connection, transaction, actor,
            accountId, true, ct) ?? throw new RestaurantAccountConflictException(
                "La cuenta ya no está disponible.");
        if (account.Status != "Open")
            throw new RestaurantAccountConflictException(
                "La cuenta ya no admite observaciones.");

        await using var read = new SqlCommand("""
            SELECT PreparationNote,Status
            FROM sales.RestaurantAccountAdditions WITH(UPDLOCK,HOLDLOCK)
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId
              AND AccountId=@AccountId AND AdditionId=@AdditionId;
            """, connection, transaction);
        AddScope(read, actor);
        read.Parameters.AddWithValue("@AccountId", accountId);
        read.Parameters.AddWithValue("@AdditionId", additionId);
        string? oldNote;
        string status;
        await using (var reader = await read.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                throw new RestaurantAccountConflictException(
                    "El producto ya no pertenece a la cuenta.");
            oldNote = reader.IsDBNull(0) ? null : reader.GetString(0);
            status = reader.GetString(1);
        }
        if (status is not ("Pending" or "Direct"))
            throw new RestaurantAccountConflictException(
                "La observación ya se envió a comanda. Agrega una nueva indicación autorizada.");
        if (oldNote != request.PreparationNote)
        {
            if (account.Version != request.ExpectedVersion)
                throw new RestaurantAccountConflictException(
                    "La cuenta cambió. Revisa la observación antes de guardarla.");
            await using var update = new SqlCommand("""
                UPDATE sales.RestaurantAccountAdditions
                SET PreparationNote=@Note,UpdatedAt=@Now
                WHERE TenantId=@TenantId AND BusinessId=@BusinessId
                  AND AccountId=@AccountId AND AdditionId=@AdditionId
                  AND Status IN(N'Pending',N'Direct');
                IF @@ROWCOUNT<>1
                    THROW 51085,N'La incorporación cambió antes de guardar la observación.',1;
                UPDATE sales.RestaurantAccounts
                SET Version=Version+1,UpdatedAt=@Now
                WHERE TenantId=@TenantId AND BusinessId=@BusinessId
                  AND AccountId=@AccountId AND Status=N'Open'
                  AND Version=@Version;
                IF @@ROWCOUNT<>1
                    THROW 51085,N'La cuenta cambió antes de guardar la observación.',1;
                """, connection, transaction);
            AddScope(update, actor);
            update.Parameters.AddWithValue("@AccountId", accountId);
            update.Parameters.AddWithValue("@AdditionId", additionId);
            update.Parameters.Add("@Note", SqlDbType.NVarChar, 500).Value =
                (object?)request.PreparationNote ?? DBNull.Value;
            update.Parameters.AddWithValue("@Version", request.ExpectedVersion);
            update.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            try { await update.ExecuteNonQueryAsync(ct); }
            catch (SqlException error) when (error.Number == 51085)
            { throw new RestaurantAccountConflictException(
                "La cuenta cambió antes de guardar la observación."); }

            await using var audit = new SqlCommand("""
                INSERT dbo.AuditLogs
                  (AuditLogId,UserId,TenantId,BusinessId,Action,EntityType,
                   EntityId,OldValues,NewValues,Timestamp)
                VALUES(NEWID(),@UserId,@TenantId,@BusinessId,
                       N'RestaurantAdditionNoteChanged',N'RestaurantAccountAddition',
                       CONVERT(NVARCHAR(100),@AdditionId),@Old,@New,@AuditTime);
                """, connection, transaction);
            AddScope(audit, actor);
            audit.Parameters.AddWithValue("@UserId", actor.UserId);
            audit.Parameters.AddWithValue("@AdditionId", additionId);
            audit.Parameters.Add("@Old", SqlDbType.NVarChar, -1).Value =
                JsonSerializer.Serialize(new { PreparationNote = oldNote });
            audit.Parameters.Add("@New", SqlDbType.NVarChar, -1).Value =
                JsonSerializer.Serialize(new { request.PreparationNote,
                    actor.ConnectedUserId, actor.CredentialVerified });
            audit.Parameters.Add("@AuditTime", SqlDbType.DateTime2).Value = now.UtcDateTime;
            await audit.ExecuteNonQueryAsync(ct);
        }
        var draft = await LoadDraftAsync(connection, transaction, actor,
            accountId, ct) ?? throw new InvalidDataException(
                "La cuenta desapareció durante el cambio de observación.");
        await transaction.CommitAsync(ct);
        return draft;
    }
}
