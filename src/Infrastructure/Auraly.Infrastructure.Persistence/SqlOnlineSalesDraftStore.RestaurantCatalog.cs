using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore
{
    public async Task<IReadOnlyList<RestaurantMenuCategory>> CategoriesAsync(
        RestaurantAccountActor actor, Guid accountId,
        Guid warehouseId, CancellationToken ct)
    {
        const string sql = """
            SELECT account.AccountId
            FROM sales.RestaurantAccounts account
            JOIN dbo.AppUsers [user]
              ON [user].UserId=@UserId AND [user].TenantId=@TenantId
             AND [user].IsActive=1
            LEFT JOIN parties.Waiters waiter
              ON waiter.TenantId=[user].TenantId
             AND waiter.PartyId=[user].PartyId AND waiter.IsActive=1
            WHERE account.AccountId=@AccountId
              AND account.TenantId=@TenantId
              AND account.BusinessId=@BusinessId
              AND account.WarehouseId=@WarehouseId
              AND account.Status=N'Open'
              AND (@CanOpenWithoutWaiter=1 OR waiter.PartyId IS NOT NULL);

            SELECT ProductCategoryId,Name,ProductCount
            FROM catalog.ActiveProductAreas(@TenantId)
            ORDER BY DisplayOrder,Name,ProductCategoryId;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", actor.TenantId);
        command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", warehouseId);
        command.Parameters.AddWithValue("@AccountId", accountId);
        command.Parameters.AddWithValue("@UserId", actor.UserId);
        command.Parameters.Add("@CanOpenWithoutWaiter", System.Data.SqlDbType.Bit).Value =
            actor.Permissions.Contains("restaurant.tables.open_without_waiter");
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new RestaurantAccountForbiddenException(
                "La cuenta o el mesero ya no está disponible para ver el menú.");
        await reader.NextResultAsync(ct);
        var categories = new List<RestaurantMenuCategory>();
        while (await reader.ReadAsync(ct))
            categories.Add(new(reader.GetGuid(0), reader.GetString(1),
                reader.GetInt32(2)));
        return categories;
    }
}
