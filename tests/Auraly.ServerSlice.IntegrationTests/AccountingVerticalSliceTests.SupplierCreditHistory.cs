using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Payables;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Theory]
    [InlineData("None")]
    [InlineData("Hash")]
    [InlineData("Balance")]
    [InlineData("Party")]
    [InlineData("Currency")]
    [InlineData("Nature")]
    [InlineData("Pending")]
    public async Task Historical_expense_credit_links_only_its_proven_journal_without_replaying_money(string fault)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, ExpensePermissionCodes.Create, ExpensePermissionCodes.Read,
            ExpensePermissionCodes.Cancel, PayablesPermissionCodes.RegisterPayment,
            TaxationPermissionCodes.ViewWithholdingRules,TaxationPermissionCodes.ManageWithholdingRules);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activation = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026,1,1),"COP","ZeroDeclared"))) activation.EnsureSuccessStatusCode();
        var profilePath=$"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}";
        using var priorProfile=await client.GetAsync(profilePath);
        var createdProfile=priorProfile.StatusCode==HttpStatusCode.NotFound;
        if(createdProfile)
        {
            using var profile=await client.PutAsJsonAsync(profilePath,
                new SaveCounterpartyTaxProfileRequest(fixture.BusinessId,fixture.SupplierId,false,[],null));
            profile.EnsureSuccessStatusCode();
        }
        else priorProfile.EnsureSuccessStatusCode();
        try
        {
        var options = (await client.GetFromJsonAsync<ExpenseWorkspaceOptions>("/api/commerce/v1/expenses/options"))!;
        var date = new DateTimeOffset(2026,10,2,12,0,0,TimeSpan.FromHours(-5));
        var expense = new ConfirmExpenseRequest(Guid.NewGuid(),fixture.BusinessId,fixture.SupplierId,null,null,
            $"HISTORY-{Guid.NewGuid():N}",date,date.AddDays(30),"COP","Crédito histórico de gasto",0,0,null,null,
            Lines:[new(options.ExpenseAccounts.First().AccountId,null,null,"Servicio",6_000m,null,"CapitalizedCost",null)]);
        using(var preview=await client.PostAsJsonAsync("/api/commerce/v1/expenses/preview",expense))
        {
            Assert.True(preview.IsSuccessStatusCode,await preview.Content.ReadAsStringAsync());
            var calculation=(await preview.Content.ReadFromJsonAsync<ExpensePreview>())!;
            Assert.True(calculation.CanConfirm);
            expense=expense with {CalculationHash=calculation.CalculationHash};
        }
        using (var message = new HttpRequestMessage(HttpMethod.Post,"/api/commerce/v1/expenses/confirm")
            { Content=JsonContent.Create(expense) })
        {
            message.Headers.Add("Idempotency-Key",expense.ExpenseId.ToString("N"));
            using var accepted=await client.SendAsync(message);
            Assert.True(accepted.StatusCode==HttpStatusCode.Accepted,await accepted.Content.ReadAsStringAsync());
        }
        var payable=await ScalarAsync<Guid>("SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id",expense.ExpenseId);
        var amount=await ScalarAsync<decimal>("SELECT OriginalAmount FROM dbo.Payables WHERE PayableId=@Id",payable);
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await fixture.EnsureSupplierPaymentSeriesAsync(connection);
        var configuration=(await client.GetFromJsonAsync<PosAccountingSettlementConfiguration>(
            "/api/commerce/v1/accounting/settlement-configuration"))!;
        var cash=Assert.Single(configuration.PaymentMethods!.Forms.Where(form=>form.MethodCode=="Cash"&&form.UseForSupplierPayments));
        var payment=new ConfirmSupplierPaymentRequest(Guid.NewGuid(),fixture.BusinessId,fixture.SupplierId,date.AddDays(1),"COP",null,
            [new(payable,amount)],[new(cash.MethodCode,amount,amount,PaymentMethodId:cash.PaymentMethodId,PaymentMethodVersion:cash.Version)]);
        using (var message=new HttpRequestMessage(HttpMethod.Post,"/api/commerce/v1/payable-payments/confirm")
            {Content=JsonContent.Create(payment)})
        {
            message.Headers.Add("Idempotency-Key",payment.PaymentId.ToString("N"));
            using var accepted=await client.SendAsync(message);
            Assert.True(accepted.StatusCode==HttpStatusCode.Accepted,await accepted.Content.ReadAsStringAsync());
        }
        var cancellation=new CancelExpenseRequest(Guid.NewGuid(),"Servicio pagado no realizado");
        using (var accepted=await client.PostAsJsonAsync($"/api/commerce/v1/expenses/{expense.ExpenseId}/cancel",cancellation))
            Assert.True(accepted.StatusCode==HttpStatusCode.Accepted,await accepted.Content.ReadAsStringAsync());
        await PortfolioOriginalAccountScenario.AssertSupplierCreditOpeningAsync(fixture,cancellation.CancellationId,"ExpenseCancellation");
        const string snapshot="""
            SELECT entry.EntryId,entry.SourcePayloadHash,entry.DebitTotal,entry.CreditTotal,
              line.LineNumber,line.AccountId,line.PartyId,line.Debit,line.Credit
            FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
            WHERE entry.SourceDocumentId=@Id AND entry.SourceDocumentType=N'ExpenseCancellation'
            ORDER BY line.LineNumber FOR JSON PATH;
            """;
        async Task Execute(string sql)
        {
            await using var command=new SqlCommand(sql,connection);
            command.Parameters.AddWithValue("@Id",cancellation.CancellationId);
            command.Parameters.AddWithValue("@Fault",fault);
            await command.ExecuteNonQueryAsync();
        }
        // A native opening already has evidence and must be a strict no-op.
        var nativeVersion=await ScalarAsync<long>("SELECT CONVERT(bigint,RowVersion) FROM dbo.SupplierCredits WHERE SourceDocumentId=@Id",cancellation.CancellationId);
        await fixture.ApplyMigrationAsync("20261116_LinkHistoricalSupplierCredits.sql",connection);
        Assert.Equal(nativeVersion,await ScalarAsync<long>("SELECT CONVERT(bigint,RowVersion) FROM dbo.SupplierCredits WHERE SourceDocumentId=@Id",cancellation.CancellationId));
        Assert.StartsWith("AuralyServerSlice_",connection.Database);
        // Parameterized commands execute in a nested SQL scope. Create buffers
        // in the connection scope so the later restore can still see them.
        await using(var buffers=new SqlCommand("""
            SELECT TOP(0) CurrencyCode,CreditNature,OpeningEntryId,OpeningEntryLineNumber,AvailableAmount
              INTO #RetainedSupplierCredit FROM dbo.SupplierCredits;
            SELECT TOP(0) * INTO #RetainedSupplierHistory FROM dbo.SupplierCreditTransactions;
            SELECT TOP(0) SourcePayloadHash,Status INTO #RetainedSupplierJob FROM dbo.AccountingPostingJobs;
            SELECT TOP(0) EntryId,LineNumber,PartyId INTO #RetainedSupplierParties FROM dbo.AccountingEntryLines;
            """,connection)) await buffers.ExecuteNonQueryAsync();
        try
        {
        await Execute("""
            INSERT #RetainedSupplierCredit SELECT CurrencyCode,CreditNature,OpeningEntryId,OpeningEntryLineNumber,AvailableAmount
              FROM dbo.SupplierCredits WHERE SourceDocumentId=@Id;
            INSERT #RetainedSupplierHistory SELECT history.* FROM dbo.SupplierCreditTransactions history
              JOIN dbo.SupplierCredits credit ON credit.SupplierCreditId=history.SupplierCreditId WHERE credit.SourceDocumentId=@Id;
            INSERT #RetainedSupplierJob SELECT SourcePayloadHash,Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id;
            INSERT #RetainedSupplierParties SELECT line.EntryId,line.LineNumber,line.PartyId FROM dbo.AccountingEntryLines line
              JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId WHERE entry.SourceDocumentId=@Id;
            DELETE history FROM dbo.SupplierCreditTransactions history JOIN dbo.SupplierCredits credit
              ON credit.SupplierCreditId=history.SupplierCreditId WHERE credit.SourceDocumentId=@Id;
            UPDATE dbo.SupplierCredits SET CurrencyCode=CASE WHEN @Fault=N'Currency' THEN N'USD' ELSE NULL END,
              CreditNature=CASE WHEN @Fault=N'Nature' THEN N'Advance' ELSE NULL END,OpeningEntryId=NULL,OpeningEntryLineNumber=NULL,
              AvailableAmount=CASE WHEN @Fault=N'Balance' THEN OriginalAmount-1 ELSE OriginalAmount END WHERE SourceDocumentId=@Id;
            IF @Fault=N'Hash' UPDATE dbo.AccountingPostingJobs SET SourcePayloadHash=HASHBYTES('SHA2_256',N'Unproven source') WHERE SourceDocumentId=@Id;
            IF @Fault=N'Pending' UPDATE dbo.AccountingPostingJobs SET Status=N'Pending' WHERE SourceDocumentId=@Id;
            IF @Fault=N'Party' UPDATE line SET PartyId=NULL FROM dbo.AccountingEntryLines line JOIN dbo.AccountingEntries entry
              ON entry.EntryId=line.EntryId WHERE entry.SourceDocumentId=@Id AND line.Debit>0;
            """);
        var journal=await ScalarAsync<string>(snapshot,cancellation.CancellationId);
        var version=await ScalarAsync<long>("SELECT CONVERT(bigint,RowVersion) FROM dbo.SupplierCredits WHERE SourceDocumentId=@Id",cancellation.CancellationId);
            if(fault is not ("None" or "Pending"))
            {
                var error=await Assert.ThrowsAsync<SqlException>(()=>fixture.ApplyMigrationAsync("20261116_LinkHistoricalSupplierCredits.sql",connection));
                Assert.Equal(51985,error.Number);
                Assert.Equal(version,await ScalarAsync<long>("SELECT CONVERT(bigint,RowVersion) FROM dbo.SupplierCredits WHERE SourceDocumentId=@Id",cancellation.CancellationId));
                Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SupplierCreditTransactions WHERE SourceDocumentId=@Id",cancellation.CancellationId));
            }
            else
            {
                using var counter=new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog);
                var watch=System.Diagnostics.Stopwatch.StartNew();
                await fixture.ApplyMigrationAsync("20261116_LinkHistoricalSupplierCredits.sql",connection);
                watch.Stop();
                Assert.Equal(1,counter.Count);
                output.WriteLine("Historical supplier-credit link: {0:F1} ms, one SQL command.",watch.Elapsed.TotalMilliseconds);
                version=await ScalarAsync<long>("SELECT CONVERT(bigint,RowVersion) FROM dbo.SupplierCredits WHERE SourceDocumentId=@Id",cancellation.CancellationId);
                await fixture.ApplyMigrationAsync("20261116_LinkHistoricalSupplierCredits.sql",connection);
                Assert.Equal(version,await ScalarAsync<long>("SELECT CONVERT(bigint,RowVersion) FROM dbo.SupplierCredits WHERE SourceDocumentId=@Id",cancellation.CancellationId));
                if(fault=="Pending")
                    Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SupplierCreditTransactions WHERE SourceDocumentId=@Id",cancellation.CancellationId));
                else
                {
                    await PortfolioOriginalAccountScenario.AssertSupplierCreditOpeningAsync(fixture,cancellation.CancellationId,"ExpenseCancellation");
                    using var replay=await client.PostAsJsonAsync($"/api/commerce/v1/expenses/{expense.ExpenseId}/cancel",cancellation);
                    Assert.True((await replay.Content.ReadFromJsonAsync<ExpenseCancellationAcceptance>())!.IdempotentReplay);
                    Assert.Equal(1,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SupplierCreditTransactions WHERE SourceDocumentId=@Id",cancellation.CancellationId));
                    Assert.Equal(amount,await ScalarAsync<decimal>("SELECT AvailableAmount FROM dbo.SupplierCredits WHERE SourceDocumentId=@Id",cancellation.CancellationId));
                }
            }
            Assert.Equal(journal,await ScalarAsync<string>(snapshot,cancellation.CancellationId));
            Assert.Equal(1,await CountAsync("AccountingEntries","SourceDocumentId",cancellation.CancellationId));
            Assert.Equal(0,await CountAsync("InventoryMovements","DocumentId",cancellation.CancellationId));
            Assert.Equal(0m,await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id",payable));
        }
        finally
        {
            await Execute("""
                UPDATE credit SET CurrencyCode=retained.CurrencyCode,CreditNature=retained.CreditNature,
                  OpeningEntryId=retained.OpeningEntryId,OpeningEntryLineNumber=retained.OpeningEntryLineNumber,AvailableAmount=retained.AvailableAmount
                FROM dbo.SupplierCredits credit CROSS JOIN #RetainedSupplierCredit retained WHERE credit.SourceDocumentId=@Id;
                DELETE history FROM dbo.SupplierCreditTransactions history JOIN dbo.SupplierCredits credit
                  ON credit.SupplierCreditId=history.SupplierCreditId WHERE credit.SourceDocumentId=@Id;
                INSERT dbo.SupplierCreditTransactions(SupplierCreditTransactionId,SupplierCreditId,TransactionType,Amount,
                  SourceDocumentId,SourceDocumentType,SourceLineId,AppliedPositionTransactionId,ReversesTransactionId,OccurredAt,CreatedAt)
                SELECT SupplierCreditTransactionId,SupplierCreditId,TransactionType,Amount,SourceDocumentId,SourceDocumentType,
                  SourceLineId,AppliedPositionTransactionId,ReversesTransactionId,OccurredAt,CreatedAt FROM #RetainedSupplierHistory;
                UPDATE job SET SourcePayloadHash=retained.SourcePayloadHash,Status=retained.Status
                FROM dbo.AccountingPostingJobs job CROSS JOIN #RetainedSupplierJob retained WHERE job.SourceDocumentId=@Id;
                UPDATE line SET PartyId=retained.PartyId FROM dbo.AccountingEntryLines line JOIN #RetainedSupplierParties retained
                  ON retained.EntryId=line.EntryId AND retained.LineNumber=line.LineNumber;
                """);
        }
        }
        finally
        {
            if(createdProfile)
            {
                await using var cleanup=new SqlConnection(fixture.ConnectionString);
                await cleanup.OpenAsync();
                Assert.StartsWith("AuralyServerSlice_",cleanup.Database);
                await using var command=new SqlCommand("DELETE dbo.CounterpartyTaxProfiles WHERE TenantId=@Tenant AND CounterpartyId=@Supplier;",cleanup);
                command.Parameters.AddWithValue("@Tenant",fixture.TenantId);
                command.Parameters.AddWithValue("@Supplier",fixture.SupplierId);
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
