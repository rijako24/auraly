using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Theory]
    [InlineData("None")]
    [InlineData("Balance")]
    [InlineData("Hash")]
    public async Task Historical_return_credit_migration_preserves_the_journal_and_rejects_unproven_history(string fault)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, SalesReturnPermissionCodes.Create, SalesReturnPermissionCodes.Read,
            ReceivablesPermissionCodes.Read);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activation = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026,1,1),"COP","ZeroDeclared"))) activation.EnsureSuccessStatusCode();
        var (customerId,siteId) = await CreateCustomerAsync();
        var sale = WithUblSnapshot(fixture.CreateUniqueValidRequest())
            with {CustomerId=customerId,CustomerPartySiteId=siteId};
        sale = sale with { Payments=[], Credit=new(customerId,11_900m,sale.FiscalSnapshot!.IssuedAt.AddDays(30),PartySiteId:siteId),
            UblSnapshot=sale.UblSnapshot! with {PaymentFormCode="2",DueDate=DateOnly.FromDateTime(sale.FiscalSnapshot.IssuedAt.AddDays(30).Date)} };
        await SetWarehouseNegativeSalesPolicyAsync(true);
        using (var upload = fixture.CreateUploadMessage(sale))
        using (var response = await fixture.CreateClient().SendAsync(upload))
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        var returned = new ConfirmSalesReturnRequest(Guid.NewGuid(),fixture.BusinessId,fixture.WarehouseId,sale.DocumentId,
            DateTimeOffset.UtcNow,ReturnEconomicResolutions.CustomerCredit,null,"Crédito histórico con evidencia",
            [new(1,1m,ReturnInventoryDispositions.Sellable)],null,null,"Other");
        using (var message = CreateSalesReturnMessage(returned,returned.ReturnId.ToString("N")))
        using (var response = await client.SendAsync(message))
            Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        using var scope = fixture.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        await processor.ProcessAsync(returned.ReturnId,"SalesReturn",fixture.BusinessId,default);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var buffers=new SqlCommand("""
            SELECT TOP(0) CustomerCreditId,CurrencyCode,CreditNature,OpeningEntryId,OpeningEntryLineNumber,AvailableAmount,PartySiteId
              INTO #RetainedCredit FROM dbo.CustomerCredits;
            SELECT TOP(0) * INTO #RetainedHistory FROM dbo.CustomerCreditTransactions;
            """,connection)) await buffers.ExecuteNonQueryAsync();
        // Reproduce the archived return-credit shape. Today's return command
        // reduces the invoice instead, so it cannot generate this older shape.
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using (var archived = new SqlCommand("""
            DECLARE @Credit uniqueidentifier=NEWID(),@Entry uniqueidentifier,@Line int,@Party uniqueidentifier,@Account uniqueidentifier;
            SELECT @Party=PartyId FROM dbo.Customers WHERE CustomerId=@Customer;
            SELECT @Account=account.AccountId FROM dbo.AccountingAccounts account
              JOIN dbo.AccountingAccountMappings mapping ON mapping.AccountId=account.AccountId
              WHERE mapping.TenantId=@Tenant AND mapping.BusinessId IS NULL AND mapping.Category=N'CustomerCreditsPayable' AND mapping.EffectiveTo IS NULL;
            SELECT @Entry=entry.EntryId,@Line=line.LineNumber FROM dbo.AccountingEntries entry
              JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
              WHERE entry.SourceDocumentId=@Id AND entry.SourceDocumentType=N'SalesReturn' AND line.Credit=11900 AND line.PartyId=@Party;
            UPDATE dbo.AccountingEntryLines SET AccountId=@Account WHERE EntryId=@Entry AND LineNumber=@Line;
            DELETE dbo.SalesReturnReceivableApplications WHERE ReturnId=@Id;
            INSERT dbo.CustomerCredits(CustomerCreditId,BusinessId,CustomerId,SourceReturnId,OriginalAmount,AvailableAmount,
              Status,CreatedAt,CurrencyCode,CreditNature,OpeningEntryId,OpeningEntryLineNumber,PartySiteId)
            SELECT @Credit,BusinessId,CustomerId,ReturnId,TotalAmount,TotalAmount,N'Open',ProcessedAt,N'COP',N'Credit',@Entry,@Line,@Site
              FROM dbo.SalesReturns WHERE ReturnId=@Id;
            INSERT dbo.CustomerCreditTransactions(CustomerCreditTransactionId,CustomerCreditId,TransactionType,Amount,
              SourceDocumentId,SourceDocumentType,OccurredAt,CreatedAt)
            SELECT NEWID(),@Credit,N'Opening',TotalAmount,ReturnId,N'SalesReturn',ReturnedAt,ProcessedAt FROM dbo.SalesReturns WHERE ReturnId=@Id;
            """,connection))
        {
            archived.Parameters.AddWithValue("@Id",returned.ReturnId); archived.Parameters.AddWithValue("@Tenant",fixture.TenantId);
            archived.Parameters.AddWithValue("@Customer",customerId); archived.Parameters.AddWithValue("@Site",siteId);
            await archived.ExecuteNonQueryAsync();
        }
        await PortfolioOriginalAccountScenario.AssertCustomerCreditOpeningAsync(fixture,returned.ReturnId,"SalesReturn");
        const string journalSnapshotSql="""
            SELECT entry.EntryId,entry.SourcePayloadHash,entry.OccurredAt,entry.DebitTotal,entry.CreditTotal,
              line.LineNumber,line.AccountId,line.PartyId,line.Debit,line.Credit
            FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
            WHERE entry.SourceDocumentId=@Id AND entry.SourceDocumentType=N'SalesReturn'
            ORDER BY line.LineNumber FOR JSON PATH;
            """;
        var journalBefore=await ScalarAsync<string>(journalSnapshotSql,returned.ReturnId);
        await using (var legacy = new SqlCommand("""
            INSERT #RetainedCredit SELECT CustomerCreditId,CurrencyCode,CreditNature,OpeningEntryId,OpeningEntryLineNumber,AvailableAmount,PartySiteId
              FROM dbo.CustomerCredits WHERE SourceReturnId=@Id;
            INSERT #RetainedHistory SELECT history.* FROM dbo.CustomerCreditTransactions history
              JOIN #RetainedCredit credit ON credit.CustomerCreditId=history.CustomerCreditId;
            DELETE history FROM dbo.CustomerCreditTransactions history JOIN #RetainedCredit credit ON credit.CustomerCreditId=history.CustomerCreditId;
            UPDATE dbo.CustomerCredits SET CurrencyCode=NULL,CreditNature=NULL,OpeningEntryId=NULL,OpeningEntryLineNumber=NULL,PartySiteId=NULL,
              AvailableAmount=CASE WHEN @Fault=N'Balance' THEN OriginalAmount-1 ELSE OriginalAmount END WHERE SourceReturnId=@Id;
            IF @Fault=N'Hash' UPDATE dbo.AccountingPostingJobs SET SourcePayloadHash=HASHBYTES('SHA2_256',N'Unproven source')
              WHERE SourceDocumentId=@Id AND SourceDocumentType=N'SalesReturn';
            """,connection))
        {
            legacy.Parameters.AddWithValue("@Id",returned.ReturnId); legacy.Parameters.AddWithValue("@Fault",fault);
            await legacy.ExecuteNonQueryAsync();
        }
        try
        {
            var path=$"/api/commerce/v1/receivables/report?templateVersion=4&dateRangeVersion=2&page=1&pageSize=50&consolidated=false&cutoff=2030-12-31&customerId={customerId:D}";
            using (var before = await client.GetAsync(path))
            {
                Assert.Equal(HttpStatusCode.BadRequest,before.StatusCode);
                Assert.Contains("sin apertura histórica",await before.Content.ReadAsStringAsync());
            }
            if (fault!="None")
            {
                var error=await Assert.ThrowsAsync<SqlException>(()=>fixture.ApplyMigrationAsync("20261115_LinkHistoricalCustomerCredits.sql",connection));
                Assert.Equal(51872,error.Number);
                Assert.Equal(0,await CountAsync("CustomerCreditTransactions","SourceDocumentId",returned.ReturnId));
                Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CustomerCredits WHERE SourceReturnId=@Id AND CurrencyCode IS NOT NULL",returned.ReturnId));
            }
            else
            {
                await fixture.ApplyMigrationAsync("20261115_LinkHistoricalCustomerCredits.sql",connection);
                var version=await ScalarAsync<long>("SELECT CONVERT(bigint,RowVersion) FROM dbo.CustomerCredits WHERE SourceReturnId=@Id",returned.ReturnId);
                await fixture.ApplyMigrationAsync("20261115_LinkHistoricalCustomerCredits.sql",connection);
                Assert.Equal(version,await ScalarAsync<long>("SELECT CONVERT(bigint,RowVersion) FROM dbo.CustomerCredits WHERE SourceReturnId=@Id",returned.ReturnId));
                await PortfolioOriginalAccountScenario.AssertCustomerCreditOpeningAsync(fixture,returned.ReturnId,"SalesReturn");
                var report=(await client.GetFromJsonAsync<ReceivablesReportPage>(path))!;
                Assert.Equal(11_900m,report.TotalAvailableCredits);
                Assert.Equal(-11_900m,report.TotalNetAmount);
                Assert.Equal(1,await CountAsync("CustomerCreditTransactions","SourceDocumentId",returned.ReturnId));
                Assert.Equal(1,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CustomerCredits WHERE SourceReturnId=@Id AND PartySiteId IS NULL",returned.ReturnId));
            }
            Assert.Equal(1,await CountAsync("AccountingEntries","SourceDocumentId",returned.ReturnId));
            Assert.Equal(1,await CountAsync("InventoryMovements","DocumentId",returned.ReturnId));
            Assert.Equal(journalBefore,await ScalarAsync<string>(journalSnapshotSql,returned.ReturnId));
        }
        finally
        {
            await using var restore = new SqlCommand("""
                UPDATE credit SET CurrencyCode=retained.CurrencyCode,CreditNature=retained.CreditNature,
                  OpeningEntryId=retained.OpeningEntryId,OpeningEntryLineNumber=retained.OpeningEntryLineNumber,
                  AvailableAmount=retained.AvailableAmount,PartySiteId=retained.PartySiteId
                FROM dbo.CustomerCredits credit JOIN #RetainedCredit retained ON retained.CustomerCreditId=credit.CustomerCreditId;
                DELETE history FROM dbo.CustomerCreditTransactions history JOIN #RetainedCredit credit ON credit.CustomerCreditId=history.CustomerCreditId;
                INSERT dbo.CustomerCreditTransactions(CustomerCreditTransactionId,CustomerCreditId,TransactionType,Amount,
                  SourceDocumentId,SourceDocumentType,SourceLineId,AppliedPositionTransactionId,ReversesTransactionId,OccurredAt,CreatedAt)
                SELECT CustomerCreditTransactionId,CustomerCreditId,TransactionType,Amount,SourceDocumentId,SourceDocumentType,
                  SourceLineId,AppliedPositionTransactionId,ReversesTransactionId,OccurredAt,CreatedAt FROM #RetainedHistory;
                UPDATE job SET SourcePayloadHash=entry.SourcePayloadHash FROM dbo.AccountingPostingJobs job
                  JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=job.SourceDocumentId AND entry.SourceDocumentType=job.SourceDocumentType
                WHERE job.SourceDocumentId=@Id AND job.SourceDocumentType=N'SalesReturn';
                """,connection);
            restore.Parameters.AddWithValue("@Id",returned.ReturnId); await restore.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Return_settlement_preserves_receivables_and_binds_customer_credit_to_its_actual_source(bool correctedRefund)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read,
            AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate,
            SalesReturnPermissionCodes.Create, SalesReturnPermissionCodes.Read);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null))
            defaults.EnsureSuccessStatusCode();
        using (var activate = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared")))
            activate.EnsureSuccessStatusCode();
        var (customerId, siteId) = await CreateCustomerAsync();
        var sale = WithUblSnapshot(fixture.CreateUniqueValidRequest()) with
        {
            CustomerId = customerId, CustomerPartySiteId = siteId
        };
        if (!correctedRefund)
            sale = sale with { Payments = [], Credit = new(customerId, 11_900m, sale.FiscalSnapshot!.IssuedAt.AddDays(30), PartySiteId: siteId),
                UblSnapshot = sale.UblSnapshot! with { PaymentFormCode = "2", DueDate = DateOnly.FromDateTime(sale.FiscalSnapshot.IssuedAt.AddDays(30).Date) } };
        await SetWarehouseNegativeSalesPolicyAsync(true);
        using (var upload = fixture.CreateUploadMessage(sale))
        using (var response = await fixture.CreateClient().SendAsync(upload))
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var returned = new ConfirmSalesReturnRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            sale.DocumentId, DateTimeOffset.UtcNow,
            correctedRefund ? ReturnEconomicResolutions.Refund : ReturnEconomicResolutions.CustomerCredit,
            correctedRefund ? SalesReturnRefundMethods.Cash : null, "Saldo a favor con origen de cuenta",
            [new(1, 1m, ReturnInventoryDispositions.Sellable)],
            correctedRefund ? await fixture.OpenWebWorkSessionAsync() : null, null, "Other");
        var alternate = !correctedRefund ? await PortfolioOriginalAccountScenario.RemapAsync(fixture, client, true) : ((Guid AccountId, string Previous)?)null;
        try
        {
            using (var message = CreateSalesReturnMessage(returned, returned.ReturnId.ToString("N")))
            using (var response = await client.SendAsync(message))
                Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
            if (!correctedRefund)
                await PortfolioOriginalAccountScenario.AssertReturnOriginalLineAsync(fixture, true, sale.DocumentId, returned.ReturnId, 11_900m);
        }
        finally
        {
            if (alternate is { } mapping) await PortfolioOriginalAccountScenario.RestoreAsync(fixture, mapping);
        }

        using var scope = fixture.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        var sourceId = returned.ReturnId;
        var sourceType = "SalesReturn";
        if (!correctedRefund)
        {
            Assert.Equal(0, await CountAsync("CustomerCredits", "SourceReturnId", returned.ReturnId));
            Assert.Equal(0m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE SourceDocumentId=@Id", sale.DocumentId));
            await processor.ProcessAsync(sourceId, sourceType, fixture.BusinessId, default);
            Assert.Equal(1, await CountAsync("SalesReturnReceivableApplications", "ReturnId", returned.ReturnId));
            return;
        }
        if (correctedRefund)
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            var partyId = await ScalarAsync<Guid>("SELECT PartyId FROM dbo.Customers WHERE CustomerId=@Id", customerId);
            var correction = new WorkSessionClosureReconciliationPayload(Guid.NewGuid(), Guid.NewGuid(),
                fixture.TenantId, fixture.BusinessId, fixture.UserId, returned.ReturnedAt,
                [new("Cash", "Cash", 10_000m, 12_000m, 12_000m, 2_000m, "CORRECTION")], [],
                [new("refund", "Refund", returned.ReturnId, 1, "Cash", -11_900m, "Cash", -9_900m,
                    "CORRECTION", partyId, customerId, null, "CustomerCreditsPayable")]);
            sourceId = correction.ReconciliationId;
            sourceType = WorkSessionAccountingDocumentTypes.ClosureReconciliation;
            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction,
                    [new(fixture.TenantId, fixture.BusinessId, sourceId, sourceType,
                        JsonSerializer.Serialize(correction, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                        correction.ReconciledAt, Guid.NewGuid())], correction.ReconciledAt,
                    AccountingJobRequirement.PreserveCommercialEffects, default);
                await transaction.CommitAsync();
            }
            // Force the late journal-link guard: no credit/history may survive a failed posting.
            using (var fault = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                commandFilter: command => command.CommandText.Contains("DECLARE @OpeningLines TABLE", StringComparison.Ordinal),
                beforeExecute: command =>
                {
                    var lines = JsonNode.Parse((string)command.Parameters["@Lines"].Value)!.AsArray();
                    lines.First(line => line!["OpeningDocumentId"] is not null)!["OpeningDocumentId"] = Guid.NewGuid();
                    command.Parameters["@Lines"].Value = lines.ToJsonString();
                }))
            {
                var error = await Assert.ThrowsAsync<SqlException>(() => processor.ProcessAsync(sourceId,
                    sourceType, fixture.BusinessId, default));
                Assert.Contains(error.Errors.Cast<SqlError>(), item => item.Number == 51844);
                Assert.Equal(1, fault.Count);
            }
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CustomerCredits WHERE SourceReturnId=@Id", returned.ReturnId));
            Assert.Equal(0, await CountAsync("CustomerCreditTransactions", "SourceDocumentId", sourceId));
            await processor.ProcessAsync(sourceId, sourceType, fixture.BusinessId, default);
        }
        await PortfolioOriginalAccountScenario.AssertCustomerCreditOpeningAsync(fixture, sourceId, sourceType);
        Assert.Equal(correctedRefund ? 2_000m : 11_900m, await ScalarAsync<decimal>(
            "SELECT AvailableAmount FROM dbo.CustomerCredits WHERE SourceReturnId=@Id", returned.ReturnId));
        await processor.ProcessAsync(sourceId, sourceType, fixture.BusinessId, default);
        await PortfolioOriginalAccountScenario.AssertCustomerCreditOpeningAsync(fixture, sourceId, sourceType);
        Assert.Equal(1, await CountAsync("CustomerCredits", "SourceReturnId", returned.ReturnId));
        Assert.Equal(1, await CountAsync("CustomerCreditTransactions", "SourceDocumentId", sourceId));
    }
}
