using System.Data;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlRestaurantAccountStore
{
    public async Task<RestaurantPrebill> PrintPrebillAsync(
        RestaurantAccountActor actor, Guid accountId,
        long expectedVersion, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var account = await FindAsync(connection, transaction, actor,
            accountId, true, ct);
        if (account is null || account.Status != "Open")
            throw new RestaurantAccountConflictException(
                "La cuenta ya no está abierta para imprimir la precuenta.");
        var draft = await LoadDraftAsync(connection, transaction, actor,
            accountId, ct) ?? throw new RestaurantAccountConflictException(
                "La cuenta ya no está disponible.");
        var lines = draft.Additions.Where(item => item.Status is "Pending" or "Sent" or "Direct")
            .Select(item => new RestaurantPrebillLine(item.Description,
                item.Quantity, item.PublicUnitPrice - item.PromotionDiscountPerUnit,
                decimal.Round(item.Quantity *
                    (item.PublicUnitPrice - item.PromotionDiscountPerUnit), 4),
                item.AddedByName))
            .ToArray();
        if (lines.Length == 0)
            throw new RestaurantAccountValidationException(
                "La mesa no tiene productos para imprimir.");
        await using var table = new SqlCommand("""
            SELECT restaurantTable.Code,business.RestaurantSuggestedTipPercent
            FROM sales.RestaurantTables restaurantTable
            JOIN dbo.Businesses business
              ON business.BusinessId=restaurantTable.BusinessId
             AND business.TenantId=restaurantTable.TenantId
            WHERE restaurantTable.TableId=@TableId
              AND restaurantTable.BusinessId=@BusinessId
              AND restaurantTable.TenantId=@TenantId;
            """, connection, transaction);
        AddScope(table, actor);
        table.Parameters.AddWithValue("@TableId", account.TableId);
        await using var tableReader = await table.ExecuteReaderAsync(ct);
        if (!await tableReader.ReadAsync(ct))
            throw new RestaurantAccountConflictException("La mesa no está disponible.");
        var tableCode = tableReader.GetString(0);
        var suggestedTipPercent = tableReader.GetDecimal(1);
        await tableReader.DisposeAsync();
        var total = lines.Sum(line => line.Total);
        var prebill = new RestaurantPrebill(accountId, tableCode,
            account.Version, now, lines, total, suggestedTipPercent,
            decimal.Round(total * suggestedTipPercent / 100, 0,
                MidpointRounding.AwayFromZero));
        await using var audit = new SqlCommand("""
            INSERT dbo.AuditLogs
              (AuditLogId,UserId,TenantId,BusinessId,Action,EntityType,
               EntityId,NewValues,Timestamp)
            VALUES(NEWID(),@UserId,@TenantId,@BusinessId,
                   N'RestaurantPrebillPrepared',N'RestaurantAccount',
                   CONVERT(nvarchar(100),@AccountId),@Snapshot,@Now);
            """, connection, transaction);
        AddScope(audit, actor);
        audit.Parameters.AddWithValue("@UserId", actor.UserId);
        audit.Parameters.AddWithValue("@AccountId", accountId);
        audit.Parameters.Add("@Snapshot", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(new {
                account.TableId, TableCode = tableCode,
                AccountVersion = account.Version,
                prebill.Total, prebill.SuggestedTipPercent,
                prebill.SuggestedTipAmount, LineCount = lines.Length,
                actor.ConnectedUserId, actor.CredentialVerified
            });
        audit.Parameters.AddWithValue("@Now", now.UtcDateTime);
        await audit.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return prebill;
    }
}
