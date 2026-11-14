using System.Data;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Payroll.Infrastructure;

public sealed partial class SqlPayrollStore
{
    public async Task<PayrollRunEmploymentPage> PageRunEmploymentsAsync(PayrollUserIdentity user,
        Guid runId, int page, int pageSize, string? search, CancellationToken ct,
        PayrollRunEmploymentFilter? filter = null)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT RowVersion FROM payroll.Runs
            WHERE PayrollRunId=@RunId AND TenantId=@TenantId AND BusinessId=@BusinessId
              AND Status IN(N'Draft',N'Calculated');
            SELECT COUNT(*),COALESCE(SUM(CONVERT(int,e.IsSelected)),0),
              COALESCE(SUM(CASE WHEN (@Search IS NULL OR p.Identification LIKE @Search ESCAPE N'~'
                OR e.ContractNumber LIKE @Search ESCAPE N'~'
                OR COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName)) LIKE @Search ESCAPE N'~'
                ) AND (@Contract IS NULL OR e.ContractNumber LIKE @Contract ESCAPE N'~') THEN 1 ELSE 0 END),0)
            FROM payroll.EligibleRunEmployments(@TenantId,@BusinessId,@RunId) e
            JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=@TenantId;
            WITH Page AS
            (
                SELECT e.EmploymentId,e.ContractNumber,e.StartDate,e.EndDate,e.PayFrequencyOptionId,e.IsSelected,
                  COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),N'Trabajador') AS EmployeeName,
                  COALESCE(p.Identification,N'') AS Identification,
                  ROW_NUMBER() OVER(ORDER BY
                  CASE WHEN @Sort=N'employeeName' AND @Descending=0 THEN COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName)) END,
                  CASE WHEN @Sort=N'employeeName' AND @Descending=1 THEN COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName)) END DESC,
                  CASE WHEN @Sort=N'identification' AND @Descending=0 THEN p.Identification END,
                  CASE WHEN @Sort=N'identification' AND @Descending=1 THEN p.Identification END DESC,
                  CASE WHEN @Sort=N'contractNumber' AND @Descending=0 THEN e.ContractNumber END,
                  CASE WHEN @Sort=N'contractNumber' AND @Descending=1 THEN e.ContractNumber END DESC,
                  CASE WHEN @Sort=N'startDate' AND @Descending=0 THEN e.StartDate END,
                  CASE WHEN @Sort=N'startDate' AND @Descending=1 THEN e.StartDate END DESC,
                  CASE WHEN @Sort=N'endDate' AND @Descending=0 THEN e.EndDate END,
                  CASE WHEN @Sort=N'endDate' AND @Descending=1 THEN e.EndDate END DESC,
                  e.EmploymentId
                  ) AS SortOrdinal
                FROM payroll.EligibleRunEmployments(@TenantId,@BusinessId,@RunId) e
                JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=@TenantId
                WHERE (@Search IS NULL OR p.Identification LIKE @Search ESCAPE N'~'
                  OR e.ContractNumber LIKE @Search ESCAPE N'~'
                  OR COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName)) LIKE @Search ESCAPE N'~'
                ) AND (@Contract IS NULL OR e.ContractNumber LIKE @Contract ESCAPE N'~')
                ORDER BY SortOrdinal OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            )
            SELECT e.EmploymentId,e.EmployeeName,e.Identification,e.ContractNumber,e.StartDate,e.EndDate,
              f.Label,e.IsSelected
            FROM Page e JOIN payroll.CatalogOptions f ON f.OptionId=e.PayFrequencyOptionId
            ORDER BY e.SortOrdinal;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@PageSize", pageSize);
        filter ??= new();
        command.Parameters.Add("@Search", SqlDbType.NVarChar, 324).Value = (object?)RunSelectionPattern(search) ?? DBNull.Value;
        command.Parameters.Add("@Contract", SqlDbType.NVarChar, 132).Value = (object?)RunSelectionPattern(filter.ContractNumber) ?? DBNull.Value;
        command.Parameters.AddWithValue("@Sort", filter.SortBy);
        command.Parameters.AddWithValue("@Descending", filter.SortDirection == "desc");
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new PayrollNotFoundException("La liquidación no existe o ya no admite selección.");
        var version = (byte[])reader[0];
        await reader.NextResultAsync(ct);
        await reader.ReadAsync(ct);
        var eligible = reader.GetInt32(0);
        var selected = reader.GetInt32(1);
        var total = reader.GetInt32(2);
        await reader.NextResultAsync(ct);
        var items = new List<PayrollRunEmploymentCandidate>();
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.GetString(1).Trim(), reader.GetString(2),
                reader.GetString(3), DateOnly.FromDateTime(reader.GetDateTime(4)),
                reader.IsDBNull(5) ? null : DateOnly.FromDateTime(reader.GetDateTime(5)),
                reader.GetString(6), reader.GetBoolean(7)));
        return new(items, page, pageSize, total, eligible, selected, version);
    }

    private static string? RunSelectionPattern(string? value) => string.IsNullOrEmpty(value) ? null
        : "%" + value.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
}
