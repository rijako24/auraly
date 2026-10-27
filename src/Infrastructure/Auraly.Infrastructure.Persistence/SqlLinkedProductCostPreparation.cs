using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal static class SqlLinkedProductCostPreparation
{
    internal sealed record Family(Guid ParentProductId, decimal? ParentCost, Guid? ChildProductId,
        Guid? SourceDocumentId = null, int? SourceLineNumber = null);
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

    public static Task PrepareFamilyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid parentProductId,
        decimal? parentCost,
        Guid? childProductId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct) =>
        PrepareFamiliesAsync(connection, transaction, businessId,
            [new(parentProductId, parentCost, childProductId)], userId, now, ct);

    public static async Task PrepareFamiliesAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, IReadOnlyCollection<Family> families, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        if (families.Count == 0) return;
        await using var command = new SqlCommand(
            "dbo.ProductLinkedCostsPrepare", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.Add("@FamiliesJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(families);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(ct);
    }
}
