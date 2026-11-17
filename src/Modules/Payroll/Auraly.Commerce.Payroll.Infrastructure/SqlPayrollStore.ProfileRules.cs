using System.Text.Json;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;
using Auraly.Commerce.Payroll.Domain;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Payroll.Infrastructure;

public sealed partial class SqlPayrollStore
{
    private static async Task<Dictionary<Guid,(Guid RuleSetId,IReadOnlyDictionary<string,decimal> Rules)>> ReadProfileRulesAsync(
        SqlConnection connection,PayrollUserIdentity user,IReadOnlyList<EmployeeSeed> employees,
        PayrollWorkPeriod period,CancellationToken ct)
    {
        var result=new Dictionary<Guid,(Guid,IReadOnlyDictionary<string,decimal>)>();
        if(employees.Count==0)return result;
        await using var command=new SqlCommand("""
            SELECT e.EmploymentId,e.ContractNumber,configuration.RuleSetId,
              CONVERT(bit,CASE WHEN configuration.Issue IS NULL THEN 1 ELSE 0 END) Valid
            INTO #ProfileRules FROM OPENJSON(@Employees) WITH(EmploymentId uniqueidentifier) selected
            JOIN payroll.Employments e ON e.EmploymentId=selected.EmploymentId AND e.TenantId=@Tenant AND e.BusinessId=@Business
            CROSS APPLY payroll.ProfileRuleConfiguration(@Tenant,e.AccountingProfileId,@Start,@End) configuration;
            SELECT EmploymentId,ContractNumber,RuleSetId,Valid FROM #ProfileRules ORDER BY EmploymentId;
            SELECT p.RuleSetId,p.Code,p.NumericValue FROM (SELECT DISTINCT RuleSetId FROM #ProfileRules WHERE Valid=1) chosen
              JOIN payroll.RuleParameters p ON p.RuleSetId=chosen.RuleSetId ORDER BY p.RuleSetId,p.Code;
            """,connection);
        command.Parameters.AddWithValue("@Tenant",user.TenantId);command.Parameters.AddWithValue("@Business",user.BusinessId);
        command.Parameters.AddWithValue("@Start",period.Start.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@End",period.End.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@Employees",JsonSerializer.Serialize(employees.Select(employee=>new{employee.EmploymentId})));
        await using var reader=await command.ExecuteReaderAsync(ct);var assignments=new Dictionary<Guid,Guid>();
        while(await reader.ReadAsync(ct))
        {
            if(!reader.GetBoolean(3))throw new PayrollValidationException(
                $"Configura el parámetro de nómina del contrato {reader.GetString(1)} con un perfil activo y porcentajes aprobados vigentes para todo el período.");
            assignments.Add(reader.GetGuid(0),reader.GetGuid(2));
        }
        if(assignments.Count!=employees.Count)throw new PayrollConflictException("La configuración de un contrato cambió durante el cálculo.");
        await reader.NextResultAsync(ct);var rulesById=new Dictionary<Guid,Dictionary<string,decimal>>();
        while(await reader.ReadAsync(ct))
        {
            var id=reader.GetGuid(0);if(!rulesById.TryGetValue(id,out var rules))rulesById[id]=rules=new(StringComparer.Ordinal);
            rules.Add(reader.GetString(1),reader.GetDecimal(2));
        }
        foreach(var rules in rulesById.Values)
        {
            try{PayrollCalculator.ValidateRuleParameters(rules);}
            catch(PayrollCalculationException error){throw new PayrollValidationException(error.Message);}
        }
        foreach(var assignment in assignments)
        {
            if(!rulesById.TryGetValue(assignment.Value,out var rules))throw new PayrollValidationException("El perfil no tiene parámetros legales completos.");
            result.Add(assignment.Key,(assignment.Value,rules));
        }
        return result;
    }
}
