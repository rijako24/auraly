using System.Net;
using System.Net.Http.Json;
using Auraly.Application.Expenses;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Commerce.Taxation.Application;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Contracts.Expenses;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class ExpenseCaptureTests
{
    [Fact]
    public async Task An_unavailable_manual_withholding_account_blocks_all_financial_effects_until_explicit_retry()
    {
        using var client=fixture.CreateAdminClient(AccountingPermissionCodes.Configure,
            TaxationPermissionCodes.ManageWithholdingRules,TaxationPermissionCodes.ViewWithholdingRules);
        var expenseAccount=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"5294","Expense","General","Debit");
        var manualAccount=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"2394","Liability","General","Credit");
        using var scope=fixture.CreateScope();
        var service=scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var processor=scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        var actor=new ExpenseUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,
            new HashSet<string>{ExpensePermissionCodes.Create,TaxationPermissionCodes.ManageWithholdingRules});
        using var profileResponse=await client.GetAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}");
        var previous=profileResponse.StatusCode==HttpStatusCode.NotFound?null:
            await profileResponse.Content.ReadFromJsonAsync<CounterpartyTaxProfileView>();
        var date=new DateTimeOffset(2026,9,30,12,0,0,TimeSpan.FromHours(-5));
        var request=new ConfirmExpenseRequest(Guid.NewGuid(),fixture.BusinessId,fixture.SupplierId,null,null,
            Guid.NewGuid().ToString("N"),date,date.AddDays(30),"COP","Retención puntual",0m,0m,null,null,
            Lines:[new ExpenseLineInput(expenseAccount.AccountId,null,null,"Servicio",100m,null,"CapitalizedCost",null)],
            WithholdingAdjustments:[new(Guid.Empty,"Manual",100m,1m,"Retención revisada",Guid.NewGuid(),
                WithholdingKinds.IncomeTax,"Retención puntual",1m,null,manualAccount.AccountId)]);
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_",connection.Database);
        Guid? periodId=null;
        try
        {
            using(var profile=await client.PutAsJsonAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}",
                new SaveCounterpartyTaxProfileRequest(fixture.BusinessId,fixture.SupplierId,false,[],null)))
                profile.EnsureSuccessStatusCode();
            var preview=await service.PreviewAsync(actor,request);
            Assert.Equal(1m,preview.Withholding.WithholdingTotal);
            request=request with {CalculationHash=preview.CalculationHash};
            await using(var close=new SqlCommand("""
                IF (SELECT COUNT(*) FROM dbo.AccountingPeriods WHERE TenantId=@Tenant AND Status=N'Open'
                  AND StartsOn<=@Day AND EndsOn>=@Day)<>1 THROW 51500,'The scenario requires one open period.',1;
                UPDATE dbo.AccountingPeriods SET Status=N'Closed' OUTPUT inserted.PeriodId
                  WHERE TenantId=@Tenant AND Status=N'Open' AND StartsOn<=@Day AND EndsOn>=@Day;
                """,connection))
            {
                close.Parameters.AddWithValue("@Tenant",fixture.TenantId);close.Parameters.AddWithValue("@Day",date.Date);
                periodId=(Guid)(await close.ExecuteScalarAsync())!;
            }
            await service.ConfirmAsync(actor,request.ExpenseId.ToString("N"),request);
            var captured=await ScalarAsync("SELECT PayloadJson FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id");
            Assert.Equal("OpenPeriodMissing",await ScalarAsync("SELECT LastErrorCode FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id"));
            await SetActiveAsync(false);
            await RestorePeriodAsync();
            await processor.ProcessAsync(request.ExpenseId,ExpenseDocumentTypes.Expense,fixture.BusinessId,default);
            Assert.Equal("AccountMappingMissing",await ScalarAsync("SELECT LastErrorCode FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id"));
            Assert.Equal(0,await ScalarAsync("SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id"));
            Assert.Equal(0,await ScalarAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id"));
            Assert.Equal(captured,await ScalarAsync("SELECT PayloadJson FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id"));
            await SetActiveAsync(true);
            await processor.ProcessAsync(request.ExpenseId,ExpenseDocumentTypes.Expense,fixture.BusinessId,default);
            await SetActiveAsync(false);
            await processor.ProcessAsync(request.ExpenseId,ExpenseDocumentTypes.Expense,fixture.BusinessId,default);
            Assert.True((await service.ConfirmAsync(actor,request.ExpenseId.ToString("N"),request)).IdempotentReplay);
            Assert.Equal(1,await ScalarAsync("SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id AND OriginalAmount=99"));
            Assert.Equal(1,await ScalarAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id AND DebitTotal=100 AND CreditTotal=100"));
            await using var applied=new SqlCommand("""
                SELECT line.Credit FROM dbo.AccountingEntryLines line
                JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
                WHERE entry.SourceDocumentId=@Id AND line.AccountId=@Account;
                """,connection);
            applied.Parameters.AddWithValue("@Id",request.ExpenseId);applied.Parameters.AddWithValue("@Account",manualAccount.AccountId);
            Assert.Equal(1m,await applied.ExecuteScalarAsync());
        }
        finally
        {
            await RestorePeriodAsync();
            await SetActiveAsync(true);
            using var restore=await client.PutAsJsonAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}",
                new SaveCounterpartyTaxProfileRequest(fixture.BusinessId,fixture.SupplierId,
                    previous?.AppliesWithholding??false,previous?.Responsibilities??[],previous?.JurisdictionCode));
            restore.EnsureSuccessStatusCode();
        }
        async Task<object?> ScalarAsync(string sql)
        {
            await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@Id",request.ExpenseId);
            return await command.ExecuteScalarAsync();
        }
        async Task RestorePeriodAsync()
        {
            if(periodId is not { } id)return;
            await using var command=new SqlCommand("UPDATE dbo.AccountingPeriods SET Status=N'Open' WHERE PeriodId=@Id",connection);
            command.Parameters.AddWithValue("@Id",id);Assert.Equal(1,await command.ExecuteNonQueryAsync());periodId=null;
        }
        async Task SetActiveAsync(bool active)
        {
            await using var command=new SqlCommand("UPDATE dbo.AccountingAccounts SET IsActive=@Active WHERE AccountId=@Account AND TenantId=@Tenant",connection);
            command.Parameters.AddWithValue("@Account",manualAccount.AccountId);command.Parameters.AddWithValue("@Tenant",fixture.TenantId);
            command.Parameters.AddWithValue("@Active",active);Assert.Equal(1,await command.ExecuteNonQueryAsync());
        }
    }

    [Fact]
    public async Task Manual_withholding_accounts_use_the_canonical_purpose_and_active_shared_accounts_in_one_batch()
    {
        using var client=fixture.CreateAdminClient(AccountingPermissionCodes.Configure);
        var valid=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"2396","Liability","General","Credit");
        var control=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"2896","Liability","CustomerAdvance","Credit");
        var inactive=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"2397","Liability","General","Credit");
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            Assert.StartsWith("AuralyServerSlice_",connection.Database);
            await using var command=new SqlCommand("""
                UPDATE dbo.AccountingAccounts SET IsActive=0 WHERE AccountId=@Account AND TenantId=@Tenant;
                """,connection);
            command.Parameters.AddWithValue("@Tenant",fixture.TenantId);command.Parameters.AddWithValue("@Account",inactive.AccountId);
            Assert.Equal(1,await command.ExecuteNonQueryAsync());
        }
        using var scope=fixture.CreateScope();
        var service=scope.ServiceProvider.GetRequiredService<WithholdingService>();
        using(var commands=new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter:command=>command.CommandText.Contains("OPENJSON(@AccountIds)",StringComparison.Ordinal)))
        {
            var plan=await service.PrepareCalculationPlanAsync(fixture.TenantId,fixture.BusinessId,[],default,
                [valid.AccountId,control.AccountId,inactive.AccountId]);
            Assert.Equal(1,commands.Count);
            Assert.Contains(valid.AccountId,plan.ValidManualAccountIds);
            Assert.DoesNotContain(control.AccountId,plan.ValidManualAccountIds);
            Assert.DoesNotContain(inactive.AccountId,plan.ValidManualAccountIds);
        }
        var actor=new TaxationUserIdentity(fixture.TenantId,fixture.BusinessId,fixture.UserId,
            new HashSet<string>{TaxationPermissionCodes.ManageWithholdingRules});
        var request=new SaveWithholdingRuleRequest(fixture.BusinessId,"TAX-"+Guid.NewGuid().ToString("N")[..12],
            "Retención puntual con auxiliar validado","IncomeTax","Purchase","Accrual","TaxExclusiveAmount",
            null,null,1m,0m,[],new(2026,1,1),null,true,false,valid.AccountId);
        Assert.Equal(valid.AccountId,(await service.SaveAsync(actor,null,request)).DefaultAccountId);
        foreach(var accountId in new[]{control.AccountId,inactive.AccountId})
            await Assert.ThrowsAsync<TaxationValidationException>(()=>service.SaveAsync(actor,null,
                request with {Code="BAD-"+Guid.NewGuid().ToString("N")[..12],DefaultAccountId=accountId}));
    }
}
