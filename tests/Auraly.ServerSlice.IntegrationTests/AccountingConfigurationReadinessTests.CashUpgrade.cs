using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingConfigurationReadinessTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("changed-source")]
    [InlineData("collision")]
    [InlineData("changed-range")]
    public async Task Reviewed_cash_upgrade_preserves_bank_history_and_uses_a_distinct_cash_auxiliary(string scenario)
    {
        using var scope=fixture.CreateScope();
        var store=scope.ServiceProvider.GetRequiredService<IAccountingStore>();
        var original=await ReadPostingMappingAsync("Cash");
        var source=Assert.Single(await store.ListAccountsAsync(User,default),account=>account.AccountId==original.AccountId);
        var target=Guid.NewGuid();
        var targetCode=scenario switch {"valid"=>"110506","changed-source"=>"110507","collision"=>"110508",_=>"110509"};
        using var client=fixture.CreateAdminClient(AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.ManualCreate,AccountingPermissionCodes.ManualSend,WorkSessionPermissionCodes.ManageCash);
        Guid? cashSession=null;
        string? cashDraftVersion=null;
        PostingMapping? vatOriginal=null;
        AccountingAccountView? vatOriginalAccount=null;
        var bank=Guid.NewGuid();
        var types=(await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/bank-account-type"))!;
        using(var saved=await client.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bank}",
            new SaveBankAccountRequest(bank,null,types.Single(type=>type.Code=="Checking").Id,
                "Banco histórico de prueba",bank.ToString("N"),"Banco histórico",false,true,null)))
            Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
        var counterpart=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,
            "4198","Revenue","General","Credit");
        var historical=Guid.NewGuid();
        await using var connection=new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_",connection.Database);
        await using var command=new SqlCommand("",connection);
        command.Parameters.AddWithValue("@Tenant",fixture.TenantId); command.Parameters.AddWithValue("@Account",source.AccountId);
        command.Parameters.AddWithValue("@Bank",bank);
        try
        {
            // The used historical auxiliary existed before financial semantics were
            // introduced. Keep that fixture distinct from today's typed defaults.
            await using (var legacy=new SqlCommand("UPDATE dbo.AccountingAccounts SET FinancialFunction=NULL,BalanceSide=NULL WHERE TenantId=@Tenant AND AccountId=@Account",connection))
            {
                legacy.Parameters.AddWithValue("@Tenant",fixture.TenantId);
                legacy.Parameters.AddWithValue("@Account",source.AccountId);
                Assert.Equal(1,await legacy.ExecuteNonQueryAsync());
            }
            await PostAsync(historical,source.AccountId);
            command.CommandText="""
                UPDATE dbo.AccountingAccounts SET FinancialFunction=N'Bank',BalanceSide=N'Debit',BankAccountId=@Bank
                WHERE TenantId=@Tenant AND AccountId=@Account;
                """;
            Assert.Equal(1,await command.ExecuteNonQueryAsync());
            var before=await ReadAccountEvidenceAsync(source.AccountId);
            var history=await JournalAsync(historical);
            if(scenario=="collision")
            {
                command.CommandText="""
                    INSERT dbo.AccountingAccounts(AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,CreatedAt)
                    VALUES(@Target,@Tenant,@Code,N'Cuenta propia del cliente',N'Asset',1,0,1,SYSDATETIMEOFFSET());
                    """;
                command.Parameters.AddWithValue("@Target",target); command.Parameters.AddWithValue("@Code",targetCode);
                await command.ExecuteNonQueryAsync();
            }
            var plan=JsonSerializer.Serialize(new[]{new {
                category="Cash",tenantId=fixture.TenantId,mappingId=original.MappingId,sourceAccountId=source.AccountId,
                sourceCode=source.Code,sourceName=scenario=="changed-source"?"Nombre no revisado":source.Name,
                sourceType="Asset",sourceFunction="Bank",sourceSide="Debit",sourceBankAccountId=bank,
                effectiveFrom=scenario=="changed-range"?original.EffectiveFrom.AddDays(1):original.EffectiveFrom,
                effectiveTo=original.EffectiveTo,targetAccountId=target,targetCode,targetName="Caja operativa",
                targetType="Asset",targetFunction="Cash",targetSide="Debit" }});
            if(scenario!="valid")
            {
                var error=await Assert.ThrowsAsync<SqlException>(()=>ApplyReviewedMappingUpgradeAsync(plan));
                Assert.Equal(scenario switch {"changed-source"=>51932,"collision"=>51933,_=>51931},error.Number);
                Assert.Equal(original,await ReadPostingMappingAsync("Cash"));
                if(scenario!="collision")
                    Assert.DoesNotContain(await store.ListAccountsAsync(User,default),account=>account.AccountId==target);
            }
            else
            {
                // Both incompatibilities must be resolved in the same batch
                // before its shared Ready coverage check, as in DEV.
                vatOriginal=await ReadPostingMappingAsync();
                vatOriginalAccount=Assert.Single(await store.ListAccountsAsync(User,default),
                    account=>account.AccountId==vatOriginal.AccountId);
                await SetInputVatTypeAsync("Asset",vatOriginal.AccountId);
                var vatBefore=await ReadAccountEvidenceAsync(vatOriginal.AccountId);
                var vatTarget=Guid.NewGuid();
                plan=JsonSerializer.Serialize(new[]{JsonSerializer.Deserialize<JsonElement>(plan)[0],
                    JsonSerializer.Deserialize<JsonElement>(InputVatPlan(vatOriginal,vatTarget,"240815"))[0]});
                var balance=await store.GetBalanceSheetAsync(User,new(2026,8,2),default);
                await ApplyReviewedMappingUpgradeAsync(plan);
                Assert.Equal(vatTarget,(await ReadPostingMappingAsync()).AccountId);
                Assert.Equal(vatBefore,await ReadAccountEvidenceAsync(vatOriginal.AccountId));
                var after=await ReadPostingMappingAsync("Cash");
                Assert.Equal(target,after.AccountId); Assert.Equal(original.MappingId,after.MappingId);
                Assert.Equal(original.EffectiveFrom,after.EffectiveFrom); Assert.Equal(original.EffectiveTo,after.EffectiveTo);
                Assert.Equal(balance,await store.GetBalanceSheetAsync(User,new(2026,8,2),default));
                await ApplyReviewedMappingUpgradeAsync(plan);
                Assert.Equal(after,await ReadPostingMappingAsync("Cash")); // Including rowversion.
                await fixture.ApplyMigrationAsync("20261109_PaymentMethodCatalog.sql",connection);
                command.CommandText="""
                    SELECT COUNT(*) FROM reference.PaymentMethods method
                    JOIN reference.Options type ON type.OptionId=method.PaymentTypeId
                    WHERE method.TenantId=@Tenant AND method.AccountId=@Target AND type.Code=N'Cash'
                      AND method.UseForReceipts=1 AND method.UseForSupplierPayments=1 AND method.IsActive=1;
                    """;
                command.Parameters.AddWithValue("@Target",target);
                Assert.Equal(1,Convert.ToInt32(await command.ExecuteScalarAsync()));
                var next=Guid.NewGuid();
                await PostAsync(next,target); var posted=await JournalAsync(next);
                await PostAsync(next,target); Assert.Equal(posted,await JournalAsync(next));
                using var journal=JsonDocument.Parse(posted);
                var lines=journal.RootElement.EnumerateArray().ToArray();
                Assert.Equal(2,lines.Length);
                Assert.Equal(25m,lines.Sum(line=>line.GetProperty("Debit").GetDecimal()));
                Assert.Equal(25m,lines.Sum(line=>line.GetProperty("Credit").GetDecimal()));
                Assert.Single(lines,line=>line.GetProperty("AccountId").GetGuid()==target);
                Assert.DoesNotContain(lines,line=>line.GetProperty("AccountId").GetGuid()==source.AccountId);
                await ApplyReviewedMappingUpgradeAsync(plan);
                Assert.Equal(posted,await JournalAsync(next));
                Assert.Equal(after,await ReadPostingMappingAsync("Cash"));
                command.CommandText="""
                    SELECT COUNT(*),SUM(Amount) FROM dbo.WorkSessionMovements
                    WHERE WorkSessionId=@Session AND DocumentId=@Document;
                    """;
                command.Parameters.AddWithValue("@Session",cashSession!.Value);
                command.Parameters.AddWithValue("@Document",next);
                await using var money=await command.ExecuteReaderAsync();
                Assert.True(await money.ReadAsync());Assert.Equal(1,money.GetInt32(0));Assert.Equal(25m,money.GetDecimal(1));
            }
            Assert.Equal(before,await ReadAccountEvidenceAsync(source.AccountId));
            Assert.Equal(history,await JournalAsync(historical));
        }
        finally
        {
            command.CommandText="""
                UPDATE dbo.AccountingAccountMappings SET AccountId=@Account
                WHERE TenantId=@Tenant AND MappingId=@Mapping AND AccountId<>@Account;
                UPDATE dbo.AccountingAccounts SET FinancialFunction=@Function,BalanceSide=@Side,BankAccountId=@OriginalBank
                WHERE TenantId=@Tenant AND AccountId=@Account;
                """;
            command.Parameters.AddWithValue("@Mapping",original.MappingId);
            command.Parameters.AddWithValue("@Function",(object?)source.Semantics?.FinancialFunction??DBNull.Value);
            command.Parameters.AddWithValue("@Side",(object?)source.Semantics?.BalanceSide??DBNull.Value);
            command.Parameters.AddWithValue("@OriginalBank",(object?)source.Semantics?.BankAccountId??DBNull.Value);
            await command.ExecuteNonQueryAsync();
            if(vatOriginal is not null) await RestoreInputVatAsync(vatOriginal,vatOriginalAccount!);
            // Remove only the unreferenced synthetic collision, so other theory
            // cases can reuse its code. Posted auxiliaries remain immutable.
            if(scenario=="collision")
            {
                command.CommandText="DELETE dbo.AccountingAccounts WHERE TenantId=@Tenant AND AccountId=@Target";
                await command.ExecuteNonQueryAsync();
            }
        }

        async Task PostAsync(Guid id,Guid cash)
        {
            if(cash==target)
            {
                cashSession??=await fixture.OpenWebWorkSessionAsync();
                var path=$"/api/commerce/v1/accounting/manual/drafts/{id}";
                if(cashDraftVersion is null)
                {
                    using var saved=await client.PutAsJsonAsync(path,new SaveVoucherDraftRequest(id,
                        AccountingManualDocumentTypes.ManualVoucher,new(2026,8,2,12,0,0,TimeSpan.Zero),
                        "MANUAL_VOUCHER","Recibo tras migrar caja",null,[
                            new(cash,fixture.SupplierPartyId,null,"Ingreso",25m,0m,LineId:target,FinancialGroupId:id),
                            new(counterpart.AccountId,fixture.SupplierPartyId,null,"Contrapartida",0m,25m,
                                LineId:counterpart.AccountId,FinancialGroupId:id)],null,null,
                        MainPartyId:fixture.SupplierPartyId,PayloadVersion:4,VoucherTypeCode:"CashReceipt",WorkSessionId:cashSession));
                    Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
                    cashDraftVersion=(await saved.Content.ReadFromJsonAsync<VoucherDraftView>())!.RowVersion;
                }
                using var sent=await client.PostAsJsonAsync(path+"/send",new SendVoucherDraftRequest(cashDraftVersion));
                Assert.True(sent.IsSuccessStatusCode,await sent.Content.ReadAsStringAsync());
                Assert.Equal("Posted",(await sent.Content.ReadFromJsonAsync<VoucherDraftView>())!.Status);
                return;
            }
            using var posted=await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers",
                new ConfirmManualAccountingVoucherRequest(id,fixture.BusinessId,new(2026,8,2,12,0,0,TimeSpan.Zero),
                    "MANUAL_VOUCHER","Prueba de caja revisada",[
                        new(cash,null,null,"Ingreso",25m,0m),
                        new(counterpart.AccountId,null,null,"Contrapartida",0m,25m)]));
            Assert.True(posted.StatusCode==HttpStatusCode.Accepted,await posted.Content.ReadAsStringAsync());
        }
        async Task<string> JournalAsync(Guid id)
        {
            await using var read=new SqlCommand("""
                SELECT (SELECT entry.EntryId,line.LineNumber,line.AccountId,line.Debit,line.Credit
                  FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
                  WHERE entry.TenantId=@Tenant AND entry.BusinessId=@Business AND entry.SourceDocumentId=@Id
                  ORDER BY line.LineNumber FOR JSON PATH);
                """,connection);
            read.Parameters.AddWithValue("@Tenant",fixture.TenantId);read.Parameters.AddWithValue("@Business",fixture.BusinessId);
            read.Parameters.AddWithValue("@Id",id);return (string)(await read.ExecuteScalarAsync())!;
        }
    }
}
