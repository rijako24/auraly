using System.Text.Json;
using Auraly.Commerce.Payroll.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingPostingProcessor
{
    private static async Task<FinancialFactsResult> LoadPayrollFactsAsync(SqlConnection connection,SqlTransaction transaction,
        SourceEnvelope source,CancellationToken token)
    {
        var payload=LoadPayrollPayload(source);
        var sourceLines=payload.Lines.Select((line,index)=>new{line,index,employee=ReadPayrollEmployeeContext(payload,line),
            conceptId=line.ConceptIndex is int concept?payload.Concepts![concept].ConceptId:null}).ToArray();
        var lines=sourceLines.Select(item=>new CategoryLineSpec(item.line.Category,item.line.Debit,item.line.Credit,item.line.PartyId,
            item.line.Description!,CostCenterId:item.employee?.CostCenterId)).ToArray();
        if(payload.Employees is null)
            return FinancialFactsResult.Ready(FinancialFacts.Payroll(payload.Description,lines));
        // Resolve each configuration once, then apply it to the original lines.
        // Hundreds of workers commonly share the same profile and concepts.
        var configurations=sourceLines
            .GroupBy(item=>new{ProfileId=item.employee?.ProfileId,ConceptId=item.conceptId,CostCenterId=item.employee?.CostCenterId,
                item.line.Category,IsDebit=item.line.Debit>0}).ToArray();
        await using var command=new SqlCommand("""
            SELECT input.Ordinal,chosen.AccountId,
              CASE WHEN input.ProfileId IS NOT NULL AND (profile.ProfileId IS NULL OR profile.IsActive=0) THEN N'Profile'
                WHEN input.CostCenterId IS NOT NULL AND center.CostCenterId IS NULL THEN N'CostCenter'
                WHEN chosen.AccountId IS NOT NULL AND ready.AccountId IS NULL THEN N'Account' END Issue
            FROM OPENJSON(@Lines) WITH(Ordinal int,ProfileId uniqueidentifier,ConceptId uniqueidentifier,
              CostCenterId uniqueidentifier,Category nvarchar(128),IsDebit bit) input
            LEFT JOIN payroll.AccountingProfiles profile ON profile.ProfileId=input.ProfileId AND profile.TenantId=@Tenant
            LEFT JOIN payroll.ProfileAccountMappings mapping ON mapping.ProfileId=profile.ProfileId AND mapping.TenantId=@Tenant
              AND (mapping.ConceptId=input.ConceptId OR mapping.ConceptId IS NULL AND input.ConceptId IS NULL)
              AND mapping.EffectiveFrom<=@On AND (mapping.EffectiveTo IS NULL OR mapping.EffectiveTo>=@On)
            CROSS APPLY(SELECT CASE WHEN input.IsDebit=1 THEN mapping.DebitAccountId ELSE mapping.CreditAccountId END AccountId) chosen
            OUTER APPLY accounting.PostingAccountReadiness(@Tenant,input.Category,chosen.AccountId) ready
            LEFT JOIN dbo.AccountingCostCenters center ON center.CostCenterId=input.CostCenterId AND center.TenantId=@Tenant AND center.IsActive=1
            ORDER BY input.Ordinal;
            """,connection,transaction);
        command.Parameters.AddWithValue("@Tenant",source.TenantId);command.Parameters.AddWithValue("@On",source.OccurredAt.Date);
        command.Parameters.AddWithValue("@Lines",JsonSerializer.Serialize(configurations.Select((group,index)=>new{
            Ordinal=index,group.Key.ProfileId,group.Key.ConceptId,group.Key.CostCenterId,group.Key.Category,group.Key.IsDebit})));
        await using var reader=await command.ExecuteReaderAsync(token);var seen=new HashSet<int>();
        while(await reader.ReadAsync(token))
        {
            var index=reader.GetInt32(0);
            if(!seen.Add(index))return FinancialFactsResult.Pending("PayrollProfileMappingAmbiguous","Hay vigencias superpuestas en las cuentas del perfil de nómina.");
            if(!reader.IsDBNull(2))return FinancialFactsResult.Pending("PayrollProfileConfigurationInvalid",
                "El perfil, centro o una cuenta explícita de nómina ya no está disponible o no es válida. Corrige su configuración y reintenta contabilizar.");
            if(!reader.IsDBNull(1))
                foreach(var item in configurations[index])lines[item.index]=lines[item.index] with{AccountId=reader.GetGuid(1)};
        }
        if(seen.Count!=configurations.Length)throw new InvalidOperationException("The payroll profile projection is incomplete.");
        // Preserve source ordinals: payments prove the original net-payable
        // account through that ordinal and party, including different profiles.
        return FinancialFactsResult.Ready(FinancialFacts.Payroll(payload.Description,lines));
    }
}
