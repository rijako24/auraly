using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PayablesVerticalSliceTests
{
    [Fact]
    public async Task Accepted_bank_auxiliary_survives_more_specific_configuration_and_ambiguous_payments_remain_unpaid()
    {
        var productId = Guid.NewGuid();
        await ConfigureSeriesAndAccountingAsync(productId);
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts,PayablesPermissionCodes.RegisterPayment,
            AccountingPermissionCodes.Configure,AccountingPermissionCodes.Read,AccountingPermissionCodes.Retry);
        var date = new DateTimeOffset(2026,8,2,9,0,0,TimeSpan.FromHours(-5));
        var receipt = new ConfirmGoodsReceiptRequest(Guid.NewGuid(),fixture.BusinessId,fixture.WarehouseId,
            fixture.SupplierId,$"FROZEN-{Guid.NewGuid():N}",date,date,true,date.AddDays(30),"COP",null,
            [new(1,productId,"Compra para verificar el banco aceptado",1m,10_000m,0m,"00",0m,PurchasingTaxTreatments.NotApplicable)]);
        using(var response=await SendAsync(client,"/api/commerce/v1/goods-receipts/confirm",receipt,receipt.DocumentId.ToString("N")))
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        var positionId=await ScalarAsync<Guid>("SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id",receipt.DocumentId);
        var positions=await client.GetFromJsonAsync<AccountingOpenPositionPage>($"/api/commerce/v1/accounting/open-positions?subledgerKind=Payable&subledgerId={positionId}&page=1&pageSize=10");
        var position=Assert.Single(positions!.Items);
        var bankId=Guid.NewGuid();
        var typeId=await ScalarAsync<Guid>("SELECT OptionId FROM reference.Options WHERE CatalogCode=N'bank-account-type' AND Code=N'Checking'",Guid.Empty);
        BankAccountView savedBank;
        using(var bank=await client.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bankId}",
            new SaveBankAccountRequest(bankId,null,typeId,"Banco con auxiliares",bankId.ToString("N"),"Banco para congelación",false,true,null)))
        {
            bank.EnsureSuccessStatusCode();
            savedBank=(await bank.Content.ReadFromJsonAsync<BankAccountView>())!;
        }
        var parentId=Guid.NewGuid();
        using(var parent=await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
            new CreateAccountingAccountRequest(parentId,fixture.TenantId,"111992","Auxiliares de prueba de aceptación","Asset",false,false)))
            parent.EnsureSuccessStatusCode();
        async Task<AccountingAccountView> CreateAuxiliaryAsync(int number,Guid? business)
        {
            using var response=await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
                new CreateAccountingAccountRequest(Guid.NewGuid(),fixture.TenantId,$"1119920{number}",
                    $"Banco auxiliar {number}","Asset",true,false,new(parentId,"Bank","Debit",business,bankId,null)));
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<AccountingAccountView>())!;
        }
        var shared=await CreateAuxiliaryAsync(1,null);
        var payment=new ConfirmSupplierPaymentRequest(Guid.NewGuid(),fixture.BusinessId,fixture.SupplierId,
            date.AddDays(1),"COP","Cuenta aceptada antes de cambiar la configuración",[new(positionId,100m)],
            [new(SupplierPaymentMethods.Transfer,100m,BankAccountId:bankId,Reference:"FREEZE-1")]);
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var binding=new SqlCommand("UPDATE dbo.Payables SET OpeningEntryId=NULL,OpeningEntryLineNumber=NULL WHERE PayableId=@Id",connection);
        binding.Parameters.AddWithValue("@Id",positionId);
        Assert.Equal(1,await binding.ExecuteNonQueryAsync());
        try
        {
            using var accepted=await SendAsync(client,"/api/commerce/v1/payable-payments/confirm",payment,payment.PaymentId.ToString("N"));
            Assert.True(accepted.IsSuccessStatusCode,await accepted.Content.ReadAsStringAsync());
            Assert.Equal("AccountingPendingConfiguration",await ScalarAsync<string>("SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",payment.PaymentId));
            var frozenJson=await ScalarAsync<string>("SELECT ResolvedBankAccountsJson FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",payment.PaymentId);
            using var frozen=JsonDocument.Parse(frozenJson);
            Assert.Equal(shared.AccountId,Assert.Single(frozen.RootElement.EnumerateArray()).GetProperty("AccountId").GetGuid());
            Assert.Equal(10_000m,await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id",positionId));
        }
        finally
        {
            binding.CommandText="UPDATE dbo.Payables SET OpeningEntryId=@EntryId,OpeningEntryLineNumber=@Line WHERE PayableId=@Id";
            binding.Parameters.AddWithValue("@EntryId",position.OpeningEntryId);
            binding.Parameters.AddWithValue("@Line",position.OpeningEntryLineNumber);
            Assert.Equal(1,await binding.ExecuteNonQueryAsync());
        }
        var specific=await CreateAuxiliaryAsync(2,fixture.BusinessId);
        async Task SetBankActiveAsync(bool active)
        {
            using var response=await client.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bankId}",
                new SaveBankAccountRequest(bankId,null,typeId,savedBank.BankName,savedBank.AccountNumber,
                    savedBank.DisplayName,false,active,savedBank.RowVersion));
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
            savedBank=(await response.Content.ReadFromJsonAsync<BankAccountView>())!;
        }
        async Task RetryAsync(Guid id)
        {
            using var retry=await client.PostAsync($"/api/commerce/v1/accounting/postings/{id}/retry",null);
            Assert.True(retry.IsSuccessStatusCode,await retry.Content.ReadAsStringAsync());
        }
        await SetBankActiveAsync(false);
        try
        {
            await RetryAsync(payment.PaymentId);
            await RetryAsync(payment.PaymentId);
        }
        finally { await SetBankActiveAsync(true); }
        Assert.Equal(100m,await AccountAmountAsync(payment.PaymentId,shared.Code,false));
        Assert.Equal(0m,await AccountAmountAsync(payment.PaymentId,specific.Code,false));
        var second=payment with {PaymentId=Guid.NewGuid()};
        using(var response=await SendAsync(client,"/api/commerce/v1/payable-payments/confirm",second,second.PaymentId.ToString("N")))
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        Assert.Equal(100m,await AccountAmountAsync(second.PaymentId,specific.Code,false));

        var ambiguous=await CreateAuxiliaryAsync(3,fixture.BusinessId);
        var third=payment with {PaymentId=Guid.NewGuid()};
        using(var response=await SendAsync(client,"/api/commerce/v1/payable-payments/confirm",third,third.PaymentId.ToString("N")))
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        Assert.Equal("BankAccountConfigurationMissing",await ScalarAsync<string>("SELECT LastErrorCode FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",third.PaymentId));
        Assert.Contains("No se aplicó el pago",await ScalarAsync<string>("SELECT LastErrorMessage FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",third.PaymentId));
        Assert.Equal(9_800m,await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id",positionId));
        Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id",third.PaymentId));
        Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PayableTransactions WHERE SourceDocumentId=@Id",third.PaymentId));
        using(var resolved=await client.PutAsJsonAsync($"/api/commerce/v1/accounting/accounts/{ambiguous.AccountId}",
            new UpdateAccountingAccountRequest(ambiguous.Name,false,ambiguous.RowVersion!,
                ambiguous.Semantics! with {FinancialFunction="General",BankAccountId=null})))
            resolved.EnsureSuccessStatusCode();
        await RetryAsync(third.PaymentId);
        await RetryAsync(third.PaymentId);
        Assert.Equal(100m,await AccountAmountAsync(third.PaymentId,specific.Code,false));
        Assert.Equal(9_700m,await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id",positionId));
        Assert.Equal(1,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id",third.PaymentId));
        await AssertLegacyBankMigrationAsync(bankId,shared.AccountId,payment.PaymentId);
    }

    private async Task AssertLegacyBankMigrationAsync(Guid bankId,Guid accountId,Guid paymentId)
    {
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            // Reconstruct only this test payment's pre-transition metadata. Its
            // original request, journal, applications and amounts remain intact.
            await using var legacy=new SqlCommand("""
                UPDATE accounting.BankAccounts SET AccountingAccountId=@AccountId WHERE BankAccountId=@BankId;
                UPDATE dbo.AccountingAccounts SET BankAccountId=NULL,FinancialFunction=NULL,BalanceSide=NULL,
                  ParentAccountId=NULL,BusinessId=NULL,PaymentMethodOptionId=NULL WHERE AccountId=@AccountId;
                UPDATE dbo.AccountingPostingJobs SET ResolvedBankAccountsJson=NULL
                  WHERE SourceDocumentId=@PaymentId AND SourceDocumentType=N'PayablePayment';
                SELECT CONCAT(CONVERT(varchar(64),entry.SourcePayloadHash,2),N'|',entry.EntryId,N'|',line.LineNumber,N'|',
                  line.AccountId,N'|',line.Debit,N'|',line.Credit)
                FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
                WHERE entry.SourceDocumentId=@PaymentId AND line.AccountId=@AccountId;
                """,connection,transaction);
            legacy.Parameters.AddWithValue("@BankId",bankId);
            legacy.Parameters.AddWithValue("@AccountId",accountId);
            legacy.Parameters.AddWithValue("@PaymentId",paymentId);
            var original=(string)(await legacy.ExecuteScalarAsync())!;
            Assert.False(string.IsNullOrEmpty(original));
            for(var attempt=0;attempt<2;attempt++)
                await fixture.ApplyMigrationAsync("20261023_BankAuxiliariesAndAcceptedAccounts.sql",connection,transaction);
            legacy.CommandText="""
                SELECT job.ResolvedBankAccountsJson,account.BankAccountId,account.FinancialFunction,
                  CONCAT(CONVERT(varchar(64),entry.SourcePayloadHash,2),N'|',entry.EntryId,N'|',line.LineNumber,N'|',
                    line.AccountId,N'|',line.Debit,N'|',line.Credit)
                FROM dbo.AccountingPostingJobs job JOIN dbo.AccountingEntries entry
                  ON entry.SourceDocumentId=job.SourceDocumentId AND entry.SourceDocumentType=job.SourceDocumentType
                JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId AND line.AccountId=@AccountId
                JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId
                WHERE job.SourceDocumentId=@PaymentId AND job.SourceDocumentType=N'PayablePayment';
                """;
            await using(var reader=await legacy.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                using var frozen=JsonDocument.Parse(reader.GetString(0));
                Assert.Equal(accountId,Assert.Single(frozen.RootElement.EnumerateArray()).GetProperty("AccountId").GetGuid());
                Assert.Equal(bankId,reader.GetGuid(1));
                Assert.Equal("Bank",reader.GetString(2));
                Assert.Equal(original,reader.GetString(3));
                Assert.False(await reader.ReadAsync());
            }
            legacy.CommandText="""
                UPDATE accounting.BankAccounts SET AccountingAccountId=@AccountId WHERE BankAccountId=@BankId;
                INSERT accounting.BankAccounts(BankAccountId,TenantId,AccountingAccountId,AccountTypeOptionId,
                  BankName,AccountNumber,DisplayName,CurrencyCode,IsPrimary,IsActive,CreatedByUserId,CreatedAt,UpdatedByUserId,UpdatedAt)
                SELECT NEWID(),TenantId,@AccountId,AccountTypeOptionId,BankName,CONVERT(nvarchar(36),NEWID()),
                  N'Vínculo histórico ambiguo de prueba',CurrencyCode,0,IsActive,CreatedByUserId,CreatedAt,UpdatedByUserId,UpdatedAt
                FROM accounting.BankAccounts WHERE BankAccountId=@BankId;
                """;
            Assert.Equal(2,await legacy.ExecuteNonQueryAsync());
            var rejected=await Assert.ThrowsAsync<SqlException>(()=>fixture.ApplyMigrationAsync(
                "20261023_BankAuxiliariesAndAcceptedAccounts.sql",connection,transaction));
            Assert.Equal(51734,rejected.Number);
            Assert.Contains("comparten",rejected.Message);
        }
        finally { if(transaction.Connection is not null)await transaction.RollbackAsync(); }
    }
}
