using System.Data;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Api;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Dispatching;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Fact]
    public async Task Previously_recorded_dispatch_collections_keep_all_four_native_forms_without_collecting_twice()
    {
        using var admin=fixture.CreateAdminClient(AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate,CatalogPermissionCodes.Read,CatalogPermissionCodes.Update,DispatchPermissionCodes.Settle);
        using(var defaults=await admin.PutAsync("/api/commerce/v1/accounting/defaults",null))defaults.EnsureSuccessStatusCode();
        using(var activation=await admin.PostAsJsonAsync("/api/commerce/v1/accounting/activate",new ActivateAccountingRequest(new(2026,1,1),"COP","ZeroDeclared")))activation.EnsureSuccessStatusCode();
        var types=(await admin.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!;
        var franchises=(await admin.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/card-franchise"))!;
        var forms=new List<PaymentMethodView>();
        foreach(var method in new[]{"Cash","DebitCard","CreditCard","Transfer"})
        {
            Guid? bankId=null;
            if(method=="Transfer")
            {
                bankId=Guid.NewGuid();
                var bankTypes=(await admin.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/bank-account-type"))!;
                using var bank=await admin.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bankId}",
                    new SaveBankAccountRequest(bankId.Value,null,bankTypes.Single(type=>type.Code=="Checking").Id,"Banco de despacho",bankId.Value.ToString("N"),"Banco de despacho",false,true,null));
                Assert.True(bank.IsSuccessStatusCode,await bank.Content.ReadAsStringAsync());
            }
            var account=method=="Cash"?await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture,admin):
                await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,admin,method=="Transfer"?"1118":"1398","Asset",method=="Transfer"?"Bank":"CardClearing","Debit",bankId);
            var request=new SavePaymentMethodRequest(Guid.NewGuid(),$"DSP-{Guid.NewGuid():N}",method,types.Single(type=>type.Code==method).Id,account.AccountId,true,true,false,false,true,10,BankAccountId:bankId);
            using var saved=await admin.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{request.PaymentMethodId}",request);
            Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());forms.Add((await saved.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method);
        }
        var(customer,site)=await CreateCustomerAsync();
        var sale=WithUblSnapshot(fixture.CreateValidRequest(9897) with{CustomerId=customer,CustomerPartySiteId=site,Payments=[],
            Credit=new PosSaleCreditContract(customer,11900,new DateTimeOffset(2026,8,31,0,0,0,TimeSpan.FromHours(-5)),PartySiteId:site)});
        sale=sale with{UblSnapshot=sale.UblSnapshot! with{PaymentFormCode="2",DueDate=new(2026,8,31)}};
        using(var upload=fixture.CreateUploadMessage(sale))using(var response=await fixture.CreateClient().SendAsync(upload))
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        await AssertBalancedAsync(sale.DocumentId);
        var dispatch=Guid.NewGuid();var source=Guid.NewGuid();var settlement=Guid.NewGuid();
        var previous=await SeedDispatchSettlementAsync(dispatch,source,settlement,sale.DocumentId,11900);
        try
        {
            await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
            await using var seed=new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.DocumentSeries WHERE BusinessId=@Business AND DocumentType=N'ReceivablePayment' AND IsActive=1)
                  INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
                    Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                  VALUES(NEWID(),@Business,NULL,N'ReceivablePayment',N'RCC',N'00',8,1,99999999,0,1,SYSUTCDATETIME());
                DELETE dbo.DispatchDeliveryPayments WHERE DispatchId=@Dispatch;
                INSERT dbo.DispatchDeliveryPayments(DispatchDeliveryPaymentId,BusinessId,DispatchId,DispatchSourceDocumentId,
                  ApplicationType,PaymentMethod,Amount,RecordedBy,OccurredAt,CreatedAt,PaymentMethodId,PaymentMethodVersion,
                  Reference,CardFranchiseCode,ApprovalNumber,VerifiedBy,VerifiedAt)
                SELECT NEWID(),@Business,@Dispatch,@Source,N'CreditAdvance',form.MethodCode,2975,@User,SYSUTCDATETIME(),SYSUTCDATETIME(),
                  form.PaymentMethodId,form.Version,CASE WHEN form.MethodCode=N'Transfer' THEN N'TRF-DSP' END,
                  CASE WHEN form.MethodCode IN(N'DebitCard',N'CreditCard') THEN @Franchise END,
                  CASE WHEN form.MethodCode IN(N'DebitCard',N'CreditCard') THEN N'APR-DSP' END,@User,SYSUTCDATETIME()
                FROM OPENJSON(@Forms) WITH(PaymentMethodId uniqueidentifier '$.PaymentMethodId',Version int '$.Version',MethodCode nvarchar(24) '$.MethodCode') form;
                INSERT dbo.DispatchDeliveryPayments(DispatchDeliveryPaymentId,BusinessId,DispatchId,DispatchSourceDocumentId,
                  ApplicationType,PaymentMethod,Amount,RecordedBy,OccurredAt,CreatedAt,EvidenceUrl)
                VALUES(NEWID(),@Business,@Dispatch,@Source,N'CreditDocument',NULL,0,@User,SYSUTCDATETIME(),SYSUTCDATETIME(),N'https://qa.invalid/firma-despacho.png');
                """,connection);
            seed.Parameters.AddWithValue("@Dispatch",dispatch);seed.Parameters.AddWithValue("@Source",source);seed.Parameters.AddWithValue("@Business",fixture.BusinessId);
            seed.Parameters.AddWithValue("@User",fixture.UserId);seed.Parameters.AddWithValue("@Franchise",franchises.First().Code);seed.Parameters.AddWithValue("@Forms",JsonSerializer.Serialize(forms));await seed.ExecuteNonQueryAsync();
            var worker=ActivatorUtilities.CreateInstance<DispatchSettlementHostedService>(fixture.Services);await worker.StartAsync(default);
            try
            {
                var request=new SettleDispatchRequest(2975,null,$"settle-{dispatch:N}");
                for(var attempt=0;attempt<2;attempt++)
                {
                    using var accepted=await admin.PostAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/settle",request);
                    Assert.True(accepted.IsSuccessStatusCode,await accepted.Content.ReadAsStringAsync());
                }
                var deadline=DateTimeOffset.UtcNow.AddSeconds(30);
                while(await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE SourceDocumentId=@Id",sale.DocumentId)!=0&&DateTimeOffset.UtcNow<deadline)await Task.Delay(50);
                Assert.Equal(0,await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE SourceDocumentId=@Id",sale.DocumentId));
                await using var verify=new SqlCommand("""
                    SELECT tender.MethodCode,tender.PaymentMethodId,tender.CardFranchiseCode,tender.ApprovalNumber,tender.Reference
                    FROM dbo.CustomerPayments payment JOIN dbo.CustomerPaymentTenders tender ON tender.PaymentId=payment.PaymentId
                    WHERE payment.IdempotencyKey LIKE @Key;
                    SELECT COUNT(*) FROM dbo.CustomerPayments WHERE IdempotencyKey LIKE @Key;
                    SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE SourceKey=@MovementKey;
                    """,connection);
                verify.Parameters.AddWithValue("@Key",$"dispatch-settlement:{dispatch:N}:payment:%");verify.Parameters.AddWithValue("@MovementKey",$"dispatch-settlement:settle-{dispatch:N}");
                await using var reader=await verify.ExecuteReaderAsync();var count=0;
                while(await reader.ReadAsync()){count++;var form=Assert.Single(forms,item=>item.MethodCode==reader.GetString(0));Assert.Equal(form.PaymentMethodId,reader.GetGuid(1));if(form.MethodCode is "DebitCard" or "CreditCard"){Assert.Equal(franchises.First().Code,reader.GetString(2));Assert.Equal("APR-DSP",reader.GetString(3));}if(form.MethodCode=="Transfer")Assert.Equal("TRF-DSP",reader.GetString(4));}
                Assert.Equal(4,count);await reader.NextResultAsync();Assert.True(await reader.ReadAsync());Assert.Equal(4,reader.GetInt32(0));await reader.NextResultAsync();Assert.True(await reader.ReadAsync());Assert.Equal(0,reader.GetInt32(0)); // Receiving the dispatch does not create a second cashier income.
                await reader.CloseAsync();
                foreach(var form in forms){await using var journal=new SqlCommand("SELECT payment.PaymentId FROM dbo.CustomerPayments payment JOIN dbo.CustomerPaymentTenders tender ON tender.PaymentId=payment.PaymentId WHERE payment.IdempotencyKey LIKE @Key AND tender.PaymentMethodId=@Form",connection);journal.Parameters.AddWithValue("@Key",$"dispatch-settlement:{dispatch:N}:payment:%");journal.Parameters.AddWithValue("@Form",form.PaymentMethodId);await AssertBalancedAsync((Guid)(await journal.ExecuteScalarAsync())!);}
            }
            finally{await worker.StopAsync(default);worker.Dispose();}
        }
        finally{await RestoreShortageAccountAsync(previous);}
    }
}
