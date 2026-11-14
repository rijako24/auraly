using System.Net;
using System.Net.Http.Json;
using Auraly.Application.Expenses;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Contracts.Expenses;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class ExpenseCaptureTests
{
    [Theory]
    [InlineData(1, false, false)]
    [InlineData(100, false, false)]
    [InlineData(1, true, false)]
    [InlineData(1, false, true)]
    public async Task Concept_account_is_selected_at_posting_without_rewriting_capture_and_cancellation_uses_the_original_journal(
        int count, bool legacyHeader, bool direct)
    {
        using var client=fixture.CreateAdminClient(AccountingPermissionCodes.Configure,AccountingPermissionCodes.Read,
            TaxationPermissionCodes.ManageWithholdingRules,TaxationPermissionCodes.ViewWithholdingRules);
        var before=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"5293","Expense","General","Debit");
        var current=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"6293","Cost","General","Debit");
        using var scope=fixture.CreateScope();
        var service=scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var processor=scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        var actor=new ExpenseUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,
            new HashSet<string>{ExpensePermissionCodes.Configure,ExpensePermissionCodes.Create,ExpensePermissionCodes.Cancel});
        var configuration=new SaveExpenseConceptRequest(Guid.NewGuid(),fixture.BusinessId,"Concepto con cuenta vigente",before.AccountId,null,null,true);
        await service.SaveConceptAsync(actor,configuration);
        using var profileResponse=await client.GetAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}");
        var previous=profileResponse.StatusCode==HttpStatusCode.NotFound?null:await profileResponse.Content.ReadFromJsonAsync<CounterpartyTaxProfileView>();
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_",connection.Database);
        var date=new DateTimeOffset(2026,9,30,12,0,0,TimeSpan.FromHours(-5));
        var request=new ConfirmExpenseRequest(Guid.NewGuid(),fixture.BusinessId,fixture.SupplierId,
            legacyHeader?configuration.ConceptId:null,null,Guid.NewGuid().ToString("N"),date,date.AddDays(30),"COP",
            "Cuenta automática de concepto",legacyHeader?100m:0m,0m,null,null,
            Lines:legacyHeader?null:Enumerable.Range(0,count).Select(_=>new ExpenseLineInput(direct?before.AccountId:null,direct?null:configuration.ConceptId,null,
                "Concepto",100m,null,"CapitalizedCost",null)).ToArray());
        Guid? periodId=null;
        try
        {
            using(var profile=await client.PutAsJsonAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}",
                new SaveCounterpartyTaxProfileRequest(fixture.BusinessId,fixture.SupplierId,false,[],null)))
                profile.EnsureSuccessStatusCode();
            if(!legacyHeader)
            {
                var preview=await service.PreviewAsync(actor,request);
                request=request with {CalculationHash=preview.CalculationHash};
            }
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
            var accepted=await SourceAsync();
            Assert.Equal("OpenPeriodMissing",await ScalarAsync("SELECT LastErrorCode FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",request.ExpenseId));
            var protectedAt=await ScalarAsync("SELECT ConfigurationLockedAt FROM dbo.AccountingAccounts WHERE AccountId=@Id",before.AccountId);
            if(direct) Assert.IsType<DateTimeOffset>(protectedAt); else Assert.Equal(DBNull.Value,protectedAt);
            await service.SaveConceptAsync(actor,configuration with {ExpenseAccountId=current.AccountId});
            await SetCurrentActiveAsync(false);
            await RestorePeriodAsync();
            await processor.ProcessAsync(request.ExpenseId,ExpenseDocumentTypes.Expense,fixture.BusinessId,default);
            Assert.Equal("AccountMappingMissing",await ScalarAsync("SELECT LastErrorCode FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",request.ExpenseId));
            Assert.Equal(0,await ScalarAsync("SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id",request.ExpenseId));
            Assert.Equal(0,await ScalarAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id",request.ExpenseId));
            Assert.Equal(accepted,await SourceAsync());
            await SetCurrentActiveAsync(true);
            using(var reads=new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                commandFilter:command=>command.CommandText.Contains("SELECT input.ConceptId,ready.AccountId",StringComparison.Ordinal)))
            {
                await processor.ProcessAsync(request.ExpenseId,ExpenseDocumentTypes.Expense,fixture.BusinessId,default);
                Assert.Equal(1,reads.Count);
            }
            await AssertAccountAsync(request.ExpenseId,count*100m,0m);
            await service.SaveConceptAsync(actor,configuration);
            await processor.ProcessAsync(request.ExpenseId,ExpenseDocumentTypes.Expense,fixture.BusinessId,default);
            Assert.True((await service.ConfirmAsync(actor,request.ExpenseId.ToString("N"),request)).IdempotentReplay);
            Assert.Equal(accepted,await SourceAsync());
            await AssertAccountAsync(request.ExpenseId,count*100m,0m);
            var cancelRequest=new CancelExpenseRequest(Guid.NewGuid(),"Servicio cancelado");
            var cancellation=await service.CancelAsync(actor,request.ExpenseId,cancelRequest);
            Assert.True((await service.CancelAsync(actor,request.ExpenseId,cancelRequest)).IdempotentReplay);
            await processor.ProcessAsync(cancellation.CancellationId,ExpenseDocumentTypes.Cancellation,fixture.BusinessId,default);
            await AssertAccountAsync(cancellation.CancellationId,0m,count*100m);
            Assert.Equal(0m,await ScalarAsync("SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id",request.ExpenseId));
        }
        finally
        {
            await RestorePeriodAsync();
            await SetCurrentActiveAsync(true);
            using var restore=await client.PutAsJsonAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}",
                new SaveCounterpartyTaxProfileRequest(fixture.BusinessId,fixture.SupplierId,
                    previous?.AppliesWithholding??false,previous?.Responsibilities??[],previous?.JurisdictionCode));
            restore.EnsureSuccessStatusCode();
        }

        async Task<object?> ScalarAsync(string sql,Guid id)
        {
            await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@Id",id);
            return await command.ExecuteScalarAsync();
        }
        async Task<string> SourceAsync()=>(string)(await ScalarAsync("SELECT PayloadJson FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id",request.ExpenseId))!;
        async Task RestorePeriodAsync()
        {
            if(periodId is not { } id)return;
            await ScalarAsync("UPDATE dbo.AccountingPeriods SET Status=N'Open' WHERE PeriodId=@Id",id);
            periodId=null;
        }
        async Task SetCurrentActiveAsync(bool active)
        {
            await using var command=new SqlCommand("UPDATE dbo.AccountingAccounts SET IsActive=@Active WHERE AccountId=@Id AND TenantId=@Tenant",connection);
            command.Parameters.AddWithValue("@Id",direct?before.AccountId:current.AccountId);command.Parameters.AddWithValue("@Tenant",fixture.TenantId);
            command.Parameters.AddWithValue("@Active",active);Assert.Equal(1,await command.ExecuteNonQueryAsync());
        }
        async Task AssertAccountAsync(Guid sourceId,decimal debit,decimal credit)
        {
            await using var command=new SqlCommand("""
                SELECT entry.DebitTotal,entry.CreditTotal,
                  (SELECT COALESCE(SUM(line.Debit),0) FROM dbo.AccountingEntryLines line WHERE line.EntryId=entry.EntryId AND line.AccountId=@Account),
                  (SELECT COALESCE(SUM(line.Credit),0) FROM dbo.AccountingEntryLines line WHERE line.EntryId=entry.EntryId AND line.AccountId=@Account),
                  (SELECT COUNT(*) FROM dbo.AccountingEntryLines line WHERE line.EntryId=entry.EntryId AND line.AccountId=@Before)
                FROM dbo.AccountingEntries entry WHERE entry.SourceDocumentId=@Id AND entry.BusinessId=@Business;
                """,connection);
            command.Parameters.AddWithValue("@Id",sourceId);command.Parameters.AddWithValue("@Business",fixture.BusinessId);
            command.Parameters.AddWithValue("@Account",direct?before.AccountId:current.AccountId);command.Parameters.AddWithValue("@Before",direct?current.AccountId:before.AccountId);
            await using var row=await command.ExecuteReaderAsync();Assert.True(await row.ReadAsync());
            Assert.Equal(row.GetDecimal(0),row.GetDecimal(1));Assert.Equal(debit,row.GetDecimal(2));
            Assert.Equal(credit,row.GetDecimal(3));Assert.Equal(0,row.GetInt32(4));Assert.False(await row.ReadAsync());
        }
    }
}
