using Auraly.Contracts.Expenses;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlExpenseStore
{
    public async Task<ExpenseConceptOptionPage> ConceptOptionsAsync(ExpenseUserIdentity user, int page, int pageSize, string? search, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.ExpenseConcepts concept
            JOIN dbo.Businesses business ON business.BusinessId=concept.BusinessId AND business.TenantId=@Tenant
            WHERE concept.BusinessId=@Business AND concept.IsActive=1
              AND (@Search IS NULL OR concept.Code LIKE @Search OR concept.Name LIKE @Search);
            SELECT concept.ExpenseConceptId,concept.Code,concept.Name FROM dbo.ExpenseConcepts concept
            JOIN dbo.Businesses business ON business.BusinessId=concept.BusinessId AND business.TenantId=@Tenant
            WHERE concept.BusinessId=@Business AND concept.IsActive=1
              AND (@Search IS NULL OR concept.Code LIKE @Search OR concept.Name LIKE @Search)
            ORDER BY concept.Code,concept.ExpenseConceptId OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
            """, connection);
        command.Parameters.AddWithValue("@Tenant", user.TenantId); command.Parameters.AddWithValue("@Business", user.BusinessId);
        command.Parameters.AddWithValue("@Search", search is null ? DBNull.Value : $"%{search}%");
        command.Parameters.AddWithValue("@Offset", (page-1)*pageSize); command.Parameters.AddWithValue("@Limit", pageSize);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct); var total = reader.GetInt32(0);
        await reader.NextResultAsync(ct); var items = new List<ExpenseConceptOption>();
        while (await reader.ReadAsync(ct)) items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        return new(items, page, pageSize, total);
    }
}
