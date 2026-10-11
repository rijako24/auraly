using System.Data;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlRestaurantAccountStore(
    SqlServerConnectionFactory connections,
    IPosSynchronizationOutboxDispatcher synchronization,
    SqlInventoryOperationStore inventoryOperations)
    : IRestaurantAccountStore
{
    public async Task<RestaurantAccount> OpenAsync(RestaurantAccountActor actor,
        OpenRestaurantAccountRequest request, DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        try
        {
            await using var transaction = (SqlTransaction)await connection
                .BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var previous = await FindAsync(connection, transaction, actor,
                request.AccountId, true, ct);
            if (previous is not null)
            {
                if (previous.TableId != request.TableId ||
                    previous.WarehouseId != request.WarehouseId ||
                    previous.ResponsibleUserId != actor.UserId)
                    throw new RestaurantAccountConflictException(
                        "La identidad de cuenta ya pertenece a otra apertura.");
                await transaction.CommitAsync(ct);
                return previous;
            }

            const string insert = """
                INSERT sales.RestaurantAccounts
                    (AccountId,TenantId,BusinessId,TableId,WarehouseId,
                     ResponsibleUserId,Status,OpenedAt,UpdatedAt)
                OUTPUT inserted.AccountId,inserted.BusinessId,inserted.TableId,
                       inserted.WarehouseId,inserted.ResponsibleUserId,
                       inserted.Status,inserted.Version,inserted.OpenedAt,
                       inserted.UpdatedAt
                SELECT @AccountId,@TenantId,@BusinessId,[table].TableId,
                       warehouse.WarehouseId,[user].UserId,N'Open',@Now,@Now
                FROM dbo.Businesses business
                JOIN sales.RestaurantTables [table] WITH (UPDLOCK,HOLDLOCK)
                  ON [table].BusinessId=business.BusinessId
                 AND [table].TenantId=business.TenantId
                 AND [table].TableId=@TableId AND [table].IsActive=1
                JOIN dbo.Warehouses warehouse
                  ON warehouse.BusinessId=business.BusinessId
                 AND warehouse.WarehouseId=@WarehouseId
                 AND warehouse.IsActive=1 AND warehouse.UseForSales=1
                JOIN dbo.AppUsers [user]
                  ON [user].TenantId=business.TenantId
                 AND [user].UserId=@UserId AND [user].IsActive=1
                LEFT JOIN parties.Waiters waiter
                  ON waiter.TenantId=business.TenantId
                 AND waiter.PartyId=[user].PartyId AND waiter.IsActive=1
                WHERE business.BusinessId=@BusinessId
                  AND business.TenantId=@TenantId
                  AND business.IsActive=1
                  AND business.PointOfSaleMode=N'RestaurantBar'
                  AND (@CanOpenWithoutWaiter=1 OR waiter.PartyId IS NOT NULL);
                """;
            await using var command = new SqlCommand(insert, connection, transaction);
            AddScope(command, actor);
            command.Parameters.AddWithValue("@AccountId", request.AccountId);
            command.Parameters.AddWithValue("@TableId", request.TableId);
            command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
            command.Parameters.AddWithValue("@UserId", actor.UserId);
            command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            RestaurantAccount account;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                    throw new RestaurantAccountConflictException(
                        "La mesa, bodega o sede no está disponible para atención, o el usuario no es mesero activo.");
                account = Read(reader);
            }
            await AuditAsync(connection, transaction, actor, account, now, ct);
            await transaction.CommitAsync(ct);
            return account;
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            // The table's unique occupied-account index remains the final arbiter
            // when two independent clients open the same table concurrently.
            var previous = await FindAsync(connection, null, actor,
                request.AccountId, false, ct);
            if (previous is not null && previous.TableId == request.TableId &&
                previous.WarehouseId == request.WarehouseId &&
                previous.ResponsibleUserId == actor.UserId)
                return previous;
            throw new RestaurantAccountConflictException(
                "Esta mesa ya tiene una cuenta abierta.");
        }
    }

    public async Task<RestaurantAccount?> GetAsync(RestaurantAccountActor actor,
        Guid accountId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        return await FindAsync(connection, null, actor, accountId, false, ct);
    }

    public async Task<IReadOnlyList<RestaurantTableOccupancy>> OccupancyAsync(
        RestaurantAccountActor actor, CancellationToken ct)
    {
        const string sql = """
            SELECT [table].TableId,account.AccountId,account.ResponsibleUserId,
                   account.OpenedAt,account.Version,
                   CASE WHEN account.AccountId IS NULL OR
                             (account.Status=N'Open' AND NOT EXISTS (
                                 SELECT 1 FROM sales.RestaurantAccountAdditions addition
                                 WHERE addition.AccountId=account.AccountId
                                   AND addition.Status IN (N'Pending',N'Sent',N'Direct')))
                        THEN N'Free' ELSE account.Status END
            FROM sales.RestaurantTables [table]
            LEFT JOIN sales.RestaurantAccounts account
              ON account.BusinessId=[table].BusinessId
             AND account.TableId=[table].TableId
             AND account.ReleasedAt IS NULL
            WHERE [table].TenantId=@TenantId AND [table].BusinessId=@BusinessId
              AND [table].IsActive=1
            ORDER BY [table].Code,[table].TableId;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        AddScope(command, actor);
        var items = new List<RestaurantTableOccupancy>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(new RestaurantTableOccupancy(
                reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetString(5),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetDateTimeOffset(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4)));
        return items;
    }

    private static async Task<RestaurantAccount?> FindAsync(
        SqlConnection connection, SqlTransaction? transaction,
        RestaurantAccountActor actor, Guid accountId, bool lockRow,
        CancellationToken ct)
    {
        var sql = $"""
            SELECT account.AccountId,account.BusinessId,account.TableId,
                   account.WarehouseId,account.ResponsibleUserId,
                   account.Status,account.Version,account.OpenedAt,account.UpdatedAt,
                   COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(responsible.FirstName,N' ',responsible.LastName))),N''),
                            responsible.Username)
            FROM sales.RestaurantAccounts account {(lockRow ? "WITH (UPDLOCK,HOLDLOCK)" : "")}
            LEFT JOIN dbo.AppUsers responsible
              ON responsible.UserId=account.ResponsibleUserId
             AND responsible.TenantId=account.TenantId
            WHERE account.AccountId=@AccountId AND account.TenantId=@TenantId
              AND account.BusinessId=@BusinessId
              AND (@InterventionTableId IS NULL OR account.TableId=@InterventionTableId)
              AND (@EnforceResponsibility=0 OR account.ResponsibleUserId=@UserId
                   OR EXISTS (SELECT 1 FROM dbo.Businesses
                     WHERE BusinessId=@BusinessId AND TenantId=@TenantId
                       AND RestaurantRestrictTableToWaiter=0));
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@AccountId", accountId);
        command.Parameters.Add("@InterventionTableId", SqlDbType.UniqueIdentifier)
            .Value = actor.InterventionTableId is { } tableId ? tableId : DBNull.Value;
        command.Parameters.AddWithValue("@EnforceResponsibility",
            actor.EnforceWaiterRestriction);
        command.Parameters.AddWithValue("@UserId", actor.UserId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    private static RestaurantAccount Read(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
        reader.GetGuid(3), reader.GetGuid(4), reader.GetString(5),
        reader.GetInt64(6), reader.GetDateTimeOffset(7),
        reader.GetDateTimeOffset(8), reader.FieldCount > 9 && !reader.IsDBNull(9)
            ? reader.GetString(9) : null);

    private static void AddScope(SqlCommand command, RestaurantAccountActor actor)
    {
        command.Parameters.AddWithValue("@TenantId", actor.TenantId);
        command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
        command.Parameters.Add("@CanOpenWithoutWaiter", SqlDbType.Bit).Value =
            actor.Permissions.Contains("restaurant.tables.open_without_waiter");
    }

    private static async Task AuditAsync(SqlConnection connection,
        SqlTransaction transaction, RestaurantAccountActor actor,
        RestaurantAccount account, DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            INSERT dbo.AuditLogs
                (AuditLogId,UserId,TenantId,BusinessId,Action,EntityType,
                 EntityId,NewValues,Timestamp)
            VALUES(NEWID(),@UserId,@TenantId,@BusinessId,
                   N'RestaurantAccountOpened',N'RestaurantAccount',
                   @EntityId,@NewValues,@Now);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@UserId", actor.UserId);
        command.Parameters.Add("@EntityId", SqlDbType.NVarChar, 100)
            .Value = account.AccountId.ToString();
        command.Parameters.Add("@NewValues", SqlDbType.NVarChar, -1)
            .Value = JsonSerializer.Serialize(new {
                Account = account,
                actor.ConnectedUserId,
                actor.CredentialVerified
            });
        command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = now.UtcDateTime;
        await command.ExecuteNonQueryAsync(ct);
    }
}
