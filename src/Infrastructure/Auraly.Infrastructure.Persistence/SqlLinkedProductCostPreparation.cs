using System.Data;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal static class SqlLinkedProductCostPreparation
{
    public static Task PrepareAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid parentProductId,
        Guid childProductId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct) =>
        PrepareFamilyAsync(
            connection, transaction, businessId, parentProductId, null,
            childProductId, userId, now, ct);

    public static async Task PrepareFamilyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid parentProductId,
        decimal? parentCost,
        Guid? childProductId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "dbo.ProductLinkedCostsPrepare", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@ParentProductId", parentProductId);
        var cost = command.Parameters.Add("@ParentCost", SqlDbType.Decimal);
        cost.Precision = 19;
        cost.Scale = 6;
        cost.Value = (object?)parentCost ?? DBNull.Value;
        command.Parameters.Add("@ChildProductId", SqlDbType.UniqueIdentifier).Value =
            (object?)childProductId ?? DBNull.Value;
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(ct);
    }
}
