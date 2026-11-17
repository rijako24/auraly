using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Payroll.Infrastructure;

public sealed partial class SqlPayrollStore
{
    private static async Task<PayrollRuleSetView> ReadRuleSetAsync(SqlConnection connection,SqlTransaction? transaction,
        Guid tenantId,Guid id,CancellationToken ct)
    {
        await using var command=new SqlCommand("""
            SELECT RuleSetId,CountryCode,Code,Name,EffectiveFrom,EffectiveTo,SourceReference,Status,RowVersion,
              CONVERT(bit,CASE WHEN TenantId IS NULL THEN 1 ELSE 0 END)
              FROM payroll.RuleSets WHERE RuleSetId=@Id AND (TenantId=@Tenant OR TenantId IS NULL);
            SELECT p.Code,p.NumericValue,p.UnitCode,p.Description FROM payroll.RuleParameters p
              JOIN payroll.RuleSets rules ON rules.RuleSetId=p.RuleSetId
              WHERE rules.RuleSetId=@Id AND (rules.TenantId=@Tenant OR rules.TenantId IS NULL) ORDER BY p.Code;
            """,connection,transaction);
        command.Parameters.AddWithValue("@Tenant",tenantId);command.Parameters.AddWithValue("@Id",id);
        await using var reader=await command.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct))throw new PayrollNotFoundException("La configuración de porcentajes no existe en esta empresa.");
        var result=new PayrollRuleSetView(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),
            DateOnly.FromDateTime(reader.GetDateTime(4)),reader.IsDBNull(5)?null:DateOnly.FromDateTime(reader.GetDateTime(5)),
            reader.GetString(6),reader.GetString(7),[],(byte[])reader[8]){IsGlobal=reader.GetBoolean(9)};
        await reader.NextResultAsync(ct);var parameters=new List<PayrollRuleParameterView>();
        while(await reader.ReadAsync(ct))parameters.Add(new(reader.GetString(0),reader.GetDecimal(1),reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3)));
        return result with{Parameters=parameters};
    }
}
