using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingConfigurationReadinessTests
{
    [Fact]
    public async Task Legacy_defaults_do_not_reclassify_an_unused_customer_account_by_code_and_name()
    {
        await using var connection=new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync();
        var tenant=Guid.NewGuid(); var account=Guid.NewGuid();
        try
        {
            await using(var seed=new SqlCommand("""
                INSERT dbo.Tenants(TenantId,TenantKey,Name,Email)
                VALUES(@Tenant,@Key,N'Configuración propia de prueba',N'vat-test@example.invalid');
                INSERT dbo.AccountingAccounts(AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,CreatedAt)
                VALUES(@Account,@Tenant,N'240810',N'IVA descontable',N'Asset',1,0,1,SYSDATETIMEOFFSET());
                """,connection,transaction))
            {
                seed.Parameters.AddWithValue("@Tenant",tenant); seed.Parameters.AddWithValue("@Key",$"@vat-{tenant:N}");
                seed.Parameters.AddWithValue("@Account",account); await seed.ExecuteNonQueryAsync();
            }
            for(var attempt=0;attempt<2;attempt++)
                await fixture.ApplyMigrationAsync("20260928_CorrectDefaultPucMappings.sql",connection,transaction);
            await using var verify=new SqlCommand("SELECT AccountType FROM dbo.AccountingAccounts WHERE TenantId=@Tenant AND AccountId=@Account",connection,transaction);
            verify.Parameters.AddWithValue("@Tenant",tenant); verify.Parameters.AddWithValue("@Account",account);
            Assert.Equal("Asset",await verify.ExecuteScalarAsync());
        }
        finally { await transaction.RollbackAsync(); }
    }

    [Fact]
    public async Task An_already_valid_source_is_preserved_when_the_reviewed_plan_is_outdated()
    {
        using var scope=fixture.CreateScope(); var store=scope.ServiceProvider.GetRequiredService<IAccountingStore>();
        var original=await ReadPostingMappingAsync(); var targetId=Guid.NewGuid();
        var accountBefore=await ReadAccountEvidenceAsync(original.AccountId);
        await ApplyReviewedMappingUpgradeAsync(InputVatPlan(original,targetId,"240813",original.EffectiveFrom.AddDays(1)));
        Assert.Equal(original,await ReadPostingMappingAsync());
        Assert.Equal(accountBefore,await ReadAccountEvidenceAsync(original.AccountId));
        Assert.DoesNotContain(await store.ListAccountsAsync(User,default),account=>account.AccountId==targetId);
    }

    [Fact]
    public async Task Reviewed_input_vat_upgrade_preserves_used_accounts_and_pending_receipts_and_posts_a_new_backdated_purchase_once()
    {
        using var scope=fixture.CreateScope(); var store=scope.ServiceProvider.GetRequiredService<IAccountingStore>();
        var original=await ReadPostingMappingAsync();
        var accounts=await store.ListAccountsAsync(User,default);
        var source=Assert.Single(accounts,account=>account.AccountId==original.AccountId);
        var targetId=accounts.SingleOrDefault(account=>account.Code=="240811")?.AccountId ?? Guid.NewGuid();
        using var client=fixture.CreateAdminClient(AccountingPermissionCodes.ManualCreate,AccountingPermissionCodes.ManualSend,
            PurchasingPermissionCodes.CreateGoodsReceipts,PurchasingPermissionCodes.ConfirmGoodsReceipts,
            PurchasingPermissionCodes.CreatePurchaseReturns,PurchasingPermissionCodes.ConfirmPurchaseReturns);
        var historical=VatReceipt(new(2026,8,1));
        await ConfirmAsync(historical);
        await SetInputVatTypeAsync("Asset",source.AccountId);
        try
        {
            var pending=VatReceipt(new(2026,8,2));
            await ConfirmAsync(pending);
            var pendingBefore=await ReceiptEvidenceAsync(pending.DocumentId);
            Assert.Contains("AccountingPendingConfiguration",pendingBefore);
            Assert.Contains("AccountMappingMissing",pendingBefore);
            Assert.Contains("\"receiptStatus\":\"Processed\"",pendingBefore);
            Assert.Contains("\"payables\":0",pendingBefore);
            Assert.Contains("\"entries\":0",pendingBefore);
            Assert.Contains("\"movements\":1",pendingBefore);
            var accountBefore=await ReadAccountEvidenceAsync(source.AccountId);
            var historyBefore=await ReceiptEvidenceAsync(historical.DocumentId);
            var balanceBefore=await store.GetBalanceSheetAsync(User,new(2026,8,2),default);
            Assert.Contains(balanceBefore,row=>row.AccountCode==source.Code && row.Section=="Asset");
            var plan=InputVatPlan(original,targetId,"240811");

            await ApplyReviewedMappingUpgradeAsync(plan);
            Assert.Equal(accountBefore,await ReadAccountEvidenceAsync(source.AccountId));
            Assert.Equal(historyBefore,await ReceiptEvidenceAsync(historical.DocumentId));
            Assert.Equal(pendingBefore,await ReceiptEvidenceAsync(pending.DocumentId));
            Assert.Equal(balanceBefore,await store.GetBalanceSheetAsync(User,new(2026,8,2),default));
            var after=await ReadPostingMappingAsync();
            Assert.Equal(targetId,after.AccountId); Assert.Equal(original.MappingId,after.MappingId);
            Assert.Equal(original.EffectiveFrom,after.EffectiveFrom); Assert.Equal(original.EffectiveTo,after.EffectiveTo);
            Assert.True((await store.EnsureDefaultsAsync(User,default)).IsReady);
            Assert.Empty((await store.GetReadinessAsync(User,new(2026,8,3),null,default)).BlockingIssues);

            await ApplyReviewedMappingUpgradeAsync(plan);
            Assert.Equal(after,await ReadPostingMappingAsync()); // Includes the unchanged mapping rowversion.
            // The full published DACPAC also keeps this used account and the
            // explicitly selected mapping. Publishing is outside timed reads.
            await fixture.ReapplySchemaAsync();
            Assert.Equal(accountBefore,await ReadAccountEvidenceAsync(source.AccountId));
            Assert.Equal(after,await ReadPostingMappingAsync());
            Assert.Equal(pendingBefore,await ReceiptEvidenceAsync(pending.DocumentId));

            // Record the first complete operation after publishing separately.
            // The purchasing budget is explicitly for the warm path; this
            // first operation must still produce its single financial result.
            var first=VatReceipt(new(2026,8,3));
            var firstTimer=Stopwatch.StartNew();
            await ConfirmAsync(first);
            firstTimer.Stop();
            output.WriteLine($"Primera compra tras publicar, recorrido completo: {firstTimer.Elapsed.TotalMilliseconds:F1}ms.");
            var firstEvidence=await ReceiptEvidenceAsync(first.DocumentId);
            Assert.Contains("\"status\":\"Posted\"",firstEvidence);
            Assert.Contains("\"payables\":1",firstEvidence); Assert.Contains("\"entries\":1",firstEvidence);
            Assert.Contains("\"movements\":1",firstEvidence);
            using (var evidence = JsonDocument.Parse(firstEvidence))
            {
                Assert.Equal(1,evidence.RootElement.GetArrayLength());
                var lines=evidence.RootElement[0].GetProperty("journal").EnumerateArray().ToArray();
                Assert.Equal(11_900m,lines.Sum(line=>line.GetProperty("Debit").GetDecimal()));
                Assert.Equal(11_900m,lines.Sum(line=>line.GetProperty("Credit").GetDecimal()));
                Assert.Single(lines,line=>line.GetProperty("AccountId").GetGuid()==targetId &&
                    line.GetProperty("Debit").GetDecimal()==1_900m);
            }
            var next=VatReceipt(new(2026,8,3)); // Earlier than this release, still within the original mapping range.
            using (var activity = new Activity("input-vat-upgraded-purchase-warm-budget")
                .SetIdFormat(ActivityIdFormat.W3C).Start())
            {
                var elapsedByCommand = new System.Collections.Concurrent.ConcurrentDictionary<string,double>();
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(
                    new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog, activity.TraceId,
                    afterExecute: (command, milliseconds) => elapsedByCommand.AddOrUpdate(
                        command.CommandText, milliseconds, (_, elapsed) => elapsed + milliseconds));
                var timer=Stopwatch.StartNew();
                await ConfirmAsync(next, activity.Id);
                timer.Stop();
                output.WriteLine($"Compra retrodatada en caliente, recorrido completo: {timer.Elapsed.TotalMilliseconds:F1}ms, {counter.Count} comandos SQL.");
                if (timer.Elapsed >= TimeSpan.FromSeconds(2))
                    foreach (var sample in elapsedByCommand.OrderByDescending(item => item.Value).Take(5))
                        output.WriteLine($"SQL {sample.Value:F1}ms: {new string(sample.Key.ReplaceLineEndings(" ").Take(180).ToArray())}");
                Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2),
                    $"La compra en caliente tras actualizar superó 2 s: {timer.Elapsed.TotalMilliseconds:F1}ms.");
            }
            var newBeforeReplay=await ReceiptEvidenceAsync(next.DocumentId);
            Assert.Contains("\"status\":\"Posted\"",newBeforeReplay);
            Assert.Contains("\"payables\":1",newBeforeReplay); Assert.Contains("\"entries\":1",newBeforeReplay);
            Assert.Contains("\"movements\":1",newBeforeReplay);
            await using(var connection=new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var verify=new SqlCommand("""
                    SELECT (SELECT OriginalAmount FROM dbo.Payables WHERE SourceDocumentId=@Source),
                      (SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Source),
                      entry.DebitTotal,entry.CreditTotal,
                      (SELECT SUM(line.Debit) FROM dbo.AccountingEntryLines line WHERE line.EntryId=entry.EntryId AND line.AccountId=@Target),
                      (SELECT COUNT(*) FROM dbo.AccountingEntryLines line WHERE line.EntryId=entry.EntryId AND line.AccountId=@Old)
                    FROM dbo.AccountingEntries entry WHERE entry.BusinessId=@Business AND entry.SourceDocumentId=@Source;
                    """,connection);
                verify.Parameters.AddWithValue("@Business",fixture.BusinessId); verify.Parameters.AddWithValue("@Source",next.DocumentId);
                verify.Parameters.AddWithValue("@Target",targetId); verify.Parameters.AddWithValue("@Old",source.AccountId);
                await using var reader=await verify.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
                Assert.Equal(11_900m,reader.GetDecimal(0)); Assert.Equal(11_900m,reader.GetDecimal(1));
                Assert.Equal(reader.GetDecimal(2),reader.GetDecimal(3)); Assert.Equal(1_900m,reader.GetDecimal(4));
                Assert.Equal(0,reader.GetInt32(5)); Assert.False(await reader.ReadAsync());
            }
            await ConfirmAsync(next);
            await scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>().ProcessAsync(
                next.DocumentId,"GoodsReceipt",fixture.BusinessId,default);
            await ApplyReviewedMappingUpgradeAsync(plan);
            Assert.Equal(newBeforeReplay,await ReceiptEvidenceAsync(next.DocumentId));
            Assert.Equal(pendingBefore,await ReceiptEvidenceAsync(pending.DocumentId));
            Assert.Equal(historyBefore,await ReceiptEvidenceAsync(historical.DocumentId));
            Assert.Equal(accountBefore,await ReadAccountEvidenceAsync(source.AccountId));

            var service=scope.ServiceProvider.GetRequiredService<AccountingService>();
            var own=(await store.ListAccountsAsync(User,default)).SingleOrDefault(account=>account.Code=="240812")
                ?? await service.CreateAccountAsync(User,new(Guid.NewGuid(),fixture.TenantId,"240812",
                    "IVA descontable alterno del cliente","Liability",true,false,
                    new(null,"General","Debit",null,null,null)));
            await service.SetMappingAsync(User,new(fixture.TenantId,null,"InputVat",own.AccountId,
                original.EffectiveFrom,original.EffectiveTo));
            var custom=await ReadPostingMappingAsync();
            await ApplyReviewedMappingUpgradeAsync(plan);
            Assert.Equal(custom,await ReadPostingMappingAsync());
            Assert.Equal(own.AccountId,custom.AccountId);

            // A new return is a new operation, not a repair/replay of the old
            // receipt. It must cancel the original VAT auxiliary even though
            // today's profile and the customer's mapping use Liability.
            var returned = new ConfirmPurchaseReturnRequest(Guid.NewGuid(),fixture.BusinessId,historical.DocumentId,
                new(2026,8,4,9,0,0,TimeSpan.FromHours(-5)),"QualityIssue","Devolución después de migrar IVA",[new(1,1m)]);
            for (var attempt=0;attempt<2;attempt++)
            {
                using var message=new HttpRequestMessage(HttpMethod.Post,"/api/commerce/v1/purchase-returns/confirm")
                    {Content=JsonContent.Create(returned)};
                message.Headers.Add("Idempotency-Key",returned.ReturnId.ToString("N"));
                using var response=await client.SendAsync(message);
                Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
                await using var connection=new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
                await using var verify=new SqlCommand("""
                    SELECT entry.DebitTotal,entry.CreditTotal,
                      (SELECT SUM(line.Credit) FROM dbo.AccountingEntryLines line WHERE line.EntryId=entry.EntryId AND line.AccountId=@OriginalAccount),
                      (SELECT COUNT(*) FROM dbo.AccountingEntryLines line WHERE line.EntryId=entry.EntryId AND line.AccountId IN(@Target,@Custom)),
                      (SELECT AccountType FROM dbo.AccountingAccounts WHERE AccountId=@OriginalAccount),
                      (SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Receipt),
                      (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Receipt),
                      (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Return)
                    FROM dbo.AccountingEntries entry WHERE entry.SourceDocumentId=@Return AND entry.BusinessId=@Business;
                    """,connection);
                verify.Parameters.AddWithValue("@OriginalAccount",source.AccountId);
                verify.Parameters.AddWithValue("@Target",targetId); verify.Parameters.AddWithValue("@Custom",own.AccountId);
                verify.Parameters.AddWithValue("@Receipt",historical.DocumentId); verify.Parameters.AddWithValue("@Return",returned.ReturnId);
                verify.Parameters.AddWithValue("@Business",fixture.BusinessId);
                await using var reader=await verify.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
                Assert.Equal(11_900m,reader.GetDecimal(0)); Assert.Equal(11_900m,reader.GetDecimal(1));
                Assert.Equal(1_900m,reader.GetDecimal(2)); Assert.Equal(0,reader.GetInt32(3));
                Assert.Equal("Asset",reader.GetString(4)); Assert.Equal(0m,reader.GetDecimal(5));
                Assert.Equal(1,reader.GetInt32(6)); Assert.Equal(1,reader.GetInt32(7));
                Assert.False(await reader.ReadAsync());
            }
            Assert.Equal(pendingBefore,await ReceiptEvidenceAsync(pending.DocumentId));
        }
        finally { await RestoreInputVatAsync(original,source); }

        async Task ConfirmAsync(ConfirmGoodsReceiptRequest receipt, string? traceParent = null)
        {
            using var message=new HttpRequestMessage(HttpMethod.Post,"/api/commerce/v1/goods-receipts/confirm") {Content=JsonContent.Create(receipt)};
            message.Headers.Add("Idempotency-Key",receipt.DocumentId.ToString("N"));
            if (traceParent is not null) message.Headers.Add("traceparent", traceParent);
            using var response=await client.SendAsync(message);
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData("unreviewed")]
    [InlineData("changed-range")]
    [InlineData("collision")]
    [InlineData("other-invalid-category")]
    [InlineData("compliance-mapping")]
    public async Task Input_vat_upgrade_rolls_back_ambiguity_collisions_and_unreviewed_configuration(string fault)
    {
        using var scope=fixture.CreateScope(); var store=scope.ServiceProvider.GetRequiredService<IAccountingStore>();
        var original=await ReadPostingMappingAsync();
        var source=Assert.Single(await store.ListAccountsAsync(User,default),account=>account.AccountId==original.AccountId);
        var targetId=Guid.NewGuid(); var targetCode="240813"; Guid? extraOverride=null; Guid? extraCompliance=null;
        await SetInputVatTypeAsync("Asset",source.AccountId);
        try
        {
            if(fault=="collision")
            {
                var collision=(await store.ListAccountsAsync(User,default)).SingleOrDefault(account=>account.Code=="240814");
                if(collision is null)
                    await scope.ServiceProvider.GetRequiredService<AccountingService>().CreateAccountAsync(User,
                        new(Guid.NewGuid(),fixture.TenantId,"240814","Configuración propia", "Liability",true,false));
                targetCode="240814";
            }
            if(fault=="other-invalid-category")
            {
                extraOverride=Guid.NewGuid();
                await using var connection=new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
                await using var wrong=new SqlCommand("""
                    INSERT dbo.AccountingAccountMappings(MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,CreatedAt)
                    SELECT @Mapping,@Tenant,@Business,N'Inventory',AccountId,'20000101',SYSDATETIMEOFFSET()
                    FROM dbo.AccountingAccounts WHERE TenantId=@Tenant AND Code=N'240805';
                    """,connection);
                wrong.Parameters.AddWithValue("@Mapping",extraOverride.Value); wrong.Parameters.AddWithValue("@Tenant",fixture.TenantId);
                wrong.Parameters.AddWithValue("@Business",fixture.BusinessId); await wrong.ExecuteNonQueryAsync();
            }
            if(fault=="compliance-mapping")
            {
                extraCompliance=Guid.NewGuid();
                await using var connection=new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
                await using var fiscal=new SqlCommand("""
                    INSERT compliance.ComplianceConceptMappings(MappingId,TenantId,AuthorityCode,TaxYear,FormatCode,
                      FormatVersion,AccountId,ConceptCode,TargetField,CreatedAt,UpdatedAt)
                    VALUES(@Id,@Tenant,N'DIAN',2026,N'1005',9,@Account,N'VAT',N'vatDeductible',SYSUTCDATETIME(),SYSUTCDATETIME());
                    """,connection);
                fiscal.Parameters.AddWithValue("@Id",extraCompliance.Value); fiscal.Parameters.AddWithValue("@Tenant",fixture.TenantId);
                fiscal.Parameters.AddWithValue("@Account",source.AccountId); await fiscal.ExecuteNonQueryAsync();
            }
            var accountBefore=await ReadAccountEvidenceAsync(source.AccountId);
            var plan=fault=="unreviewed" ? "[]" : InputVatPlan(original,targetId,targetCode,
                fault=="changed-range" ? original.EffectiveFrom.AddDays(1) : null);
            var error=await Assert.ThrowsAsync<SqlException>(()=>ApplyReviewedMappingUpgradeAsync(plan));
            Assert.Equal(fault switch {"changed-range"=>51931,"collision"=>51933,"compliance-mapping"=>51937,_=>51935},error.Number);
            Assert.Equal(original,await ReadPostingMappingAsync());
            Assert.Equal(accountBefore,await ReadAccountEvidenceAsync(source.AccountId));
            Assert.DoesNotContain(await store.ListAccountsAsync(User,default),account=>account.AccountId==targetId);
        }
        finally
        {
            if(extraCompliance is { } fiscalId)
            {
                await using var connection=new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
                await using var remove=new SqlCommand("DELETE compliance.ComplianceConceptMappings WHERE MappingId=@Id AND TenantId=@Tenant",connection);
                remove.Parameters.AddWithValue("@Id",fiscalId); remove.Parameters.AddWithValue("@Tenant",fixture.TenantId); await remove.ExecuteNonQueryAsync();
            }
            if(extraOverride is { } id)
            {
                await using var connection=new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
                await using var remove=new SqlCommand("DELETE dbo.AccountingAccountMappings WHERE MappingId=@Id AND TenantId=@Tenant",connection);
                remove.Parameters.AddWithValue("@Id",id); remove.Parameters.AddWithValue("@Tenant",fixture.TenantId); await remove.ExecuteNonQueryAsync();
            }
            await RestoreInputVatAsync(original,source);
        }
    }

    [Fact]
    public async Task Purchase_return_without_original_VAT_evidence_preserves_the_payable_and_does_not_use_current_mappings()
    {
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts, PurchasingPermissionCodes.CreatePurchaseReturns,
            PurchasingPermissionCodes.ConfirmPurchaseReturns);
        var receipt = VatReceipt(new(2026,8,5));
        using (var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
            { Content = JsonContent.Create(receipt) })
        {
            message.Headers.Add("Idempotency-Key", receipt.DocumentId.ToString("N"));
            using var accepted = await client.SendAsync(message);
            Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());
        }
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        string evidence;
        await using (var removeProof = new SqlCommand("""
            UPDATE dbo.AccountingPostingJobs SET ResolvedAccountMappingsJson=NULL
            OUTPUT deleted.ResolvedAccountMappingsJson
            WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt' AND Status=N'Posted';
            """, connection))
        {
            removeProof.Parameters.AddWithValue("@Id", receipt.DocumentId);
            evidence = Assert.IsType<string>(await removeProof.ExecuteScalarAsync());
        }
        try
        {
            var returned = new ConfirmPurchaseReturnRequest(Guid.NewGuid(), fixture.BusinessId, receipt.DocumentId,
                receipt.ReceivedAt.AddDays(1), "QualityIssue", "Prueba sin vínculo histórico de IVA", [new(1,1m)]);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/purchase-returns/confirm")
                    { Content = JsonContent.Create(returned) };
                message.Headers.Add("Idempotency-Key", returned.ReturnId.ToString("N"));
                using var accepted = await client.SendAsync(message);
                Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());
            }
            await using var verify = new SqlCommand("""
                SELECT job.Status,job.LastErrorCode,
                  (SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Receipt),
                  (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Return),
                  (SELECT COUNT(*) FROM dbo.PayableTransactions WHERE SourceDocumentId=@Return),
                  (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Return),
                  (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Receipt)
                FROM dbo.AccountingPostingJobs job WHERE SourceDocumentId=@Return AND BusinessId=@Business;
                """, connection);
            verify.Parameters.AddWithValue("@Return", returned.ReturnId); verify.Parameters.AddWithValue("@Receipt", receipt.DocumentId);
            verify.Parameters.AddWithValue("@Business", fixture.BusinessId);
            await using var reader = await verify.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
            Assert.Equal("AccountingPendingConfiguration", reader.GetString(0));
            Assert.Equal("OriginalInputVatAccountMissing", reader.GetString(1)); Assert.Equal(11_900m, reader.GetDecimal(2));
            Assert.Equal(0, reader.GetInt32(3)); Assert.Equal(0, reader.GetInt32(4));
            Assert.Equal(1, reader.GetInt32(5)); Assert.Equal(1, reader.GetInt32(6)); Assert.False(await reader.ReadAsync());
        }
        finally
        {
            // Restore only synthetic fixture metadata; production never
            // invents or backfills this evidence to unblock a pending return.
            await using var restore = new SqlCommand(
                "UPDATE dbo.AccountingPostingJobs SET ResolvedAccountMappingsJson=@Evidence WHERE SourceDocumentId=@Id", connection);
            restore.Parameters.AddWithValue("@Evidence", evidence); restore.Parameters.AddWithValue("@Id", receipt.DocumentId);
            Assert.Equal(1, await restore.ExecuteNonQueryAsync());
        }
    }

    private ConfirmGoodsReceiptRequest VatReceipt(DateOnly date) => new(Guid.NewGuid(),fixture.BusinessId,fixture.WarehouseId,
        fixture.SupplierId,$"VAT-UPGRADE-{Guid.NewGuid():N}",new(date.ToDateTime(new(9,0)),TimeSpan.FromHours(-5)),
        new(date.ToDateTime(new(9,0)),TimeSpan.FromHours(-5)),true,
        new(date.AddDays(30).ToDateTime(new(9,0)),TimeSpan.FromHours(-5)),"COP",null,
        [new(1,fixture.ProductId,"Compra para migración de IVA",1m,10_000m,0m,"01",19m,PurchasingTaxTreatments.DeductibleInputVat)]);

    private string InputVatPlan(PostingMapping original,Guid targetId,string code,DateOnly? from=null) => JsonSerializer.Serialize(new[] {
        new { category="InputVat", sourceCode="240810", sourceName="IVA descontable", sourceType="Asset",
            sourceFunction=(string?)null,sourceSide=(string?)null,sourceBankAccountId=(Guid?)null,
            targetType="Liability",targetFunction="General",targetSide="Debit",
            tenantId=fixture.TenantId,mappingId=original.MappingId,sourceAccountId=original.AccountId,
            effectiveFrom=from ?? original.EffectiveFrom,effectiveTo=original.EffectiveTo,targetAccountId=targetId,
            targetCode=code,targetName="IVA descontable en compras" } });

    private async Task ApplyReviewedMappingUpgradeAsync(string plan)
    {
        await using var connection=new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await fixture.ApplyMigrationAsync("20261115_ApplyReviewedPostingMappingUpgrade.sql",connection,
            parameters:new Dictionary<string,object>{{"@PostingMappingUpgradePlanJson",plan}});
    }

    private async Task<PostingMapping> ReadPostingMappingAsync(string category="InputVat")
    {
        await using var connection=new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using var command=new SqlCommand("""
            SELECT TOP(1) MappingId,AccountId,EffectiveFrom,EffectiveTo,RowVersion FROM dbo.AccountingAccountMappings
            WHERE TenantId=@Tenant AND Category=@Category AND BusinessId IS NULL ORDER BY EffectiveFrom DESC;
            """,connection); command.Parameters.AddWithValue("@Tenant",fixture.TenantId);
        command.Parameters.AddWithValue("@Category",category);
        await using var reader=await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        return new(reader.GetGuid(0),reader.GetGuid(1),DateOnly.FromDateTime(reader.GetDateTime(2)),
            reader.IsDBNull(3) ? null : DateOnly.FromDateTime(reader.GetDateTime(3)),Convert.ToBase64String(reader.GetFieldValue<byte[]>(4)));
    }

    private async Task SetInputVatTypeAsync(string type,Guid accountId,string? function=null,string? side=null)
    {
        await using var connection=new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_",connection.Database);
        // Reproduce the old template explicitly. Fresh defaults now carry
        // financial semantics; this historical fixture predates those fields.
        await using var command=new SqlCommand("UPDATE dbo.AccountingAccounts SET AccountType=@Type,FinancialFunction=@Function,BalanceSide=@Side WHERE AccountId=@Id AND TenantId=@Tenant",connection);
        command.Parameters.AddWithValue("@Type",type); command.Parameters.AddWithValue("@Id",accountId);
        command.Parameters.AddWithValue("@Function",(object?)function??DBNull.Value);
        command.Parameters.AddWithValue("@Side",(object?)side??DBNull.Value);
        command.Parameters.AddWithValue("@Tenant",fixture.TenantId); Assert.Equal(1,await command.ExecuteNonQueryAsync());
    }

    private async Task RestoreInputVatAsync(PostingMapping original,AccountingAccountView source)
    {
        await SetInputVatTypeAsync(source.AccountType,original.AccountId,source.Semantics?.FinancialFunction,source.Semantics?.BalanceSide);
        await using var connection=new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        // The canonical editor replaces the mapping identity on a user edit.
        // Restore only the fixture's unique natural key after that scenario.
        await using var command=new SqlCommand("""
            UPDATE dbo.AccountingAccountMappings SET AccountId=@Account
            WHERE TenantId=@Tenant AND BusinessId IS NULL AND Category=N'InputVat' AND EffectiveFrom=@From;
            """,connection);
        command.Parameters.AddWithValue("@Account",original.AccountId); command.Parameters.AddWithValue("@Tenant",fixture.TenantId);
        command.Parameters.AddWithValue("@From",original.EffectiveFrom.ToDateTime(TimeOnly.MinValue)); Assert.Equal(1,await command.ExecuteNonQueryAsync());
    }

    private async Task<string> ReadAccountEvidenceAsync(Guid id)
    {
        await using var connection=new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using var command=new SqlCommand("SELECT (SELECT * FROM dbo.AccountingAccounts WHERE AccountId=@Id AND TenantId=@Tenant FOR JSON PATH)",connection);
        command.Parameters.AddWithValue("@Id",id); command.Parameters.AddWithValue("@Tenant",fixture.TenantId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string> ReceiptEvidenceAsync(Guid id)
    {
        await using var connection=new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using var command=new SqlCommand("""
            SELECT (SELECT job.Status status,job.AttemptCount attempts,job.LastErrorCode error,job.ResolvedAccountMappingsJson frozen,
              job.ResolvedBankAccountsJson banks,job.RowVersion jobVersion,
              (SELECT Status FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id) receiptStatus,
              (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id) entries,
              (SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id) payables,
              (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id) movements,
              JSON_QUERY((SELECT QuantityChange,ValueChange,RecognizedUnitCost FROM dbo.InventoryMovements
                WHERE DocumentId=@Id ORDER BY LineNumber FOR JSON PATH)) inventory,
              JSON_QUERY((SELECT OriginalAmount,OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id FOR JSON PATH)) debts,
              JSON_QUERY((SELECT AccountId,Debit,Credit FROM dbo.AccountingEntries entry
                JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId WHERE entry.SourceDocumentId=@Id
                ORDER BY line.LineNumber FOR JSON PATH)) journal
            FROM dbo.AccountingPostingJobs job WHERE job.SourceDocumentId=@Id AND job.BusinessId=@Business FOR JSON PATH);
            """,connection); command.Parameters.AddWithValue("@Id",id); command.Parameters.AddWithValue("@Business",fixture.BusinessId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private sealed record PostingMapping(Guid MappingId,Guid AccountId,DateOnly EffectiveFrom,DateOnly? EffectiveTo,string Version);
}
