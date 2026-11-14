using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PortfolioManualAdvanceTests
{
    [Fact]
    public async Task A_general_voucher_form_keeps_its_legacy_edited_usage_without_enabling_cash_receipts()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate,AccountingPermissionCodes.ManualCreate,AccountingPermissionCodes.ManualSend,
            CatalogPermissionCodes.Read,CatalogPermissionCodes.Update);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults",null))
            Assert.True(defaults.IsSuccessStatusCode,await defaults.Content.ReadAsStringAsync());
        using (var active = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",new ActivateAccountingRequest(new(2026,1,1),"COP","ZeroDeclared")))
            Assert.True(active.IsSuccessStatusCode,await active.Content.ReadAsStringAsync());
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture,client);
        var revenue = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"4192","Revenue","General","Credit");
        var party = await CreateCustomerPartyAsync(false);
        var types = (await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!;
        var configuration = new SavePaymentMethodRequest(Guid.NewGuid(),$"GENERAL-{Guid.NewGuid():N}","Caja contable",
            types.Single(type => type.Code=="Cash").Id,cash.AccountId,false,false,false,false,true,10,UseForVouchers:true);
        using var created = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{configuration.PaymentMethodId}",configuration);
        Assert.True(created.IsSuccessStatusCode,await created.Content.ReadAsStringAsync());
        var form = (await created.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        var legacyEdit = configuration with { Name="Caja editada",RowVersion=form.RowVersion,UseForVouchers=null };
        using var edited = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{form.PaymentMethodId}",legacyEdit);
        Assert.True(edited.IsSuccessStatusCode,await edited.Content.ReadAsStringAsync());
        form = (await edited.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        Assert.True(form.UseForVouchers);
        var eligible = (await client.GetFromJsonAsync<PaymentMethodPage>($"/api/commerce/v1/payment-methods/?usage=VoucherReceipts&paymentMethodId={form.PaymentMethodId}"))!;
        Assert.Equal(form.PaymentMethodId,Assert.Single(eligible.Items).PaymentMethodId);
        var ineligible = (await client.GetFromJsonAsync<PaymentMethodPage>($"/api/commerce/v1/payment-methods/?usage=Receipts&paymentMethodId={form.PaymentMethodId}"))!;
        Assert.Empty(ineligible.Items);
        var capture = new SaveVoucherDraftRequest(Guid.NewGuid(),AccountingManualDocumentTypes.ManualVoucher,
            new(2026,8,10,12,0,0,TimeSpan.Zero),"MANUAL_VOUCHER","Entrada en comprobante contable","GEN-FORMA",
            [new(cash.AccountId,party,null,"Dinero",100,0,LineId:Guid.NewGuid(),PaymentEvidence:VoucherFormEvidence(form)),
             new(revenue.AccountId,party,null,"Ingreso",0,100,LineId:Guid.NewGuid())],null,null,
            MainPartyId:party,PayloadVersion:4,VoucherTypeCode:"CashReceipt");
        var path=$"/api/commerce/v1/accounting/manual/drafts/{capture.DocumentId}";
        using var saved=await client.PutAsJsonAsync(path,capture);
        Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
        var draft=(await saved.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        using (var rejected=await client.PostAsJsonAsync(path+"/send",new SendVoucherDraftRequest(draft.RowVersion)))
            Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id",capture.DocumentId));
        var posted=await SendReceiptScenarioAsync(client,capture with { VoucherTypeCode="Accounting",RowVersion=draft.RowVersion });
        Assert.Equal("Posted",posted.Status);
        Assert.Equal(cash.AccountId,Assert.Single(posted.AppliedPaymentMethods!).AccountId);
        Assert.Empty(posted.FinancialEffects!.Payments);
        // This source has no native tender rows: the accepted draft identity
        // still prevents changing a used form's technical meaning.
        using var changedType=await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{form.PaymentMethodId}",
            legacyEdit with { RowVersion=form.RowVersion,PaymentTypeId=types.Single(type => type.Code=="Transfer").Id,IsActive=false });
        Assert.Equal(HttpStatusCode.Conflict,changedType.StatusCode);
    }

    [Fact]
    public async Task A_receipt_resolves_its_current_form_atomically_and_replay_keeps_the_completed_result()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate,AccountingPermissionCodes.ManualCreate,AccountingPermissionCodes.ManualSend,
            AccountingPermissionCodes.Retry,ReceivablesPermissionCodes.RegisterPayment,CatalogPermissionCodes.Read,CatalogPermissionCodes.Update);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults",null))
            Assert.True(defaults.IsSuccessStatusCode,await defaults.Content.ReadAsStringAsync());
        using (var active = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",new ActivateAccountingRequest(new(2026,1,1),"COP","ZeroDeclared")))
            Assert.True(active.IsSuccessStatusCode,await active.Content.ReadAsStringAsync());
        var before = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture,client);
        var failed = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture,client);
        var current = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture,client);
        var advance = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"2392","Liability","CustomerAdvance","Credit");
        var party = await CreateCustomerPartyAsync(false);
        var form = await CreateVoucherCashFormAsync(client,before,"Caja capturada");
        var moneyLine = Guid.NewGuid(); var advanceLine = Guid.NewGuid();
        var capture = new SaveVoucherDraftRequest(Guid.NewGuid(),AccountingManualDocumentTypes.ManualVoucher,
            new(2026,8,10,12,0,0,TimeSpan.Zero),"MANUAL_VOUCHER","Anticipo explícito con forma configurada","RC-FORMA",
            [new(before.AccountId,party,null,"Entrada de dinero",500,0,LineId:moneyLine,PaymentEvidence:VoucherFormEvidence(form) with { TenderedAmount=500m,Notes="Sólo la transacción original" }),
             new(advance.AccountId,party,null,"Anticipo elegido",0,500,LineId:advanceLine)],null,null,
            MainPartyId:party,PayloadVersion:4,VoucherTypeCode:"CashReceipt");
        var path = $"/api/commerce/v1/accounting/manual/drafts/{capture.DocumentId}";
        using var saved = await client.PutAsJsonAsync(path,capture);
        Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
        var draft = (await saved.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        Assert.Equal(form.PaymentMethodId,draft.Lines[0].Value.PaymentEvidence!.PaymentMethodId);

        await FailAfterFinancialWritesAsync(() => client.PostAsJsonAsync(path+"/send",new SendVoucherDraftRequest(draft.RowVersion)));
        var accepted = (await client.GetFromJsonAsync<VoucherDraftView>(path))!;
        Assert.NotNull(accepted.SentAt);
        Assert.Null(accepted.AppliedPaymentMethods);
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.CustomerCredits WHERE SourceDocumentId=@Id",capture.DocumentId));
        var types = (await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!;
        using (var semanticEdit = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{form.PaymentMethodId}",
            Configuration(form) with { PaymentTypeId=types.Single(type => type.Code=="Transfer").Id,IsActive=false }))
            Assert.Equal(HttpStatusCode.Conflict,semanticEdit.StatusCode);
        form = await ChangeFormAsync(form,failed.AccountId,"Intento fallido");
        await FailAfterFinancialWritesAsync(() => client.PostAsync($"/api/commerce/v1/accounting/postings/{capture.DocumentId}/retry",null));
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var rollback = new SqlCommand("""
            SELECT job.AppliedPaymentMethodsJson,tender.AccountId,account.ConfigurationLockedAt
            FROM dbo.AccountingPostingJobs job
            JOIN dbo.CustomerPayments payment ON payment.ParentSourceDocumentId=job.SourceDocumentId AND payment.BusinessId=job.BusinessId
            JOIN dbo.CustomerPaymentTenders tender ON tender.PaymentId=payment.PaymentId
            JOIN dbo.AccountingAccounts account ON account.AccountId=@Failed AND account.TenantId=job.TenantId
            WHERE job.SourceDocumentId=@Id AND job.TenantId=@Tenant AND job.BusinessId=@Business;
            """,connection))
        {
            rollback.Parameters.AddWithValue("@Failed",failed.AccountId); rollback.Parameters.AddWithValue("@Id",capture.DocumentId);
            rollback.Parameters.AddWithValue("@Tenant",fixture.TenantId); rollback.Parameters.AddWithValue("@Business",fixture.BusinessId);
            await using var row = await rollback.ExecuteReaderAsync();
            Assert.True(await row.ReadAsync()); Assert.True(row.IsDBNull(0));
            Assert.Equal(before.AccountId,row.GetGuid(1)); Assert.True(row.IsDBNull(2));
            Assert.False(await row.ReadAsync());
        }
        form = await ChangeFormAsync(form,current.AccountId,"Caja utilizada");
        using var resolutions = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter: command => command.CommandText.Contains("DECLARE @Resolved TABLE(SourceLineId",StringComparison.Ordinal));
        using (var retry = await client.PostAsync($"/api/commerce/v1/accounting/postings/{capture.DocumentId}/retry",null))
            Assert.True(retry.IsSuccessStatusCode,await retry.Content.ReadAsStringAsync());
        Assert.Equal(1,resolutions.Count);
        var posted = (await client.GetFromJsonAsync<VoucherDraftView>(path))!;
        Assert.Equal("Posted",posted.Status);
        var applied = Assert.Single(posted.AppliedPaymentMethods!);
        Assert.Equal(current.AccountId,applied.AccountId); Assert.Equal(3,applied.Version); Assert.Equal("Caja utilizada",applied.Name);
        Assert.Equal(before.AccountId,posted.Lines[0].Value.AccountId); // captured intent stays immutable
        Assert.Equal(current.AccountId,(await client.GetFromJsonAsync<AccountingEntryView>(
            $"/api/commerce/v1/accounting/entries/by-document/{capture.DocumentId}"))!.Lines.Single(line => line.SourceLineId==moneyLine).AccountId);
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id",capture.DocumentId));
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.CustomerCredits WHERE SourceDocumentId=@Id AND AvailableAmount=500",capture.DocumentId));
        form = await ChangeFormAsync(form,before.AccountId,"Configuración posterior");
        using (var replay = await client.PostAsJsonAsync(path+"/send",new SendVoucherDraftRequest(posted.RowVersion)))
        {
            Assert.True(replay.IsSuccessStatusCode,await replay.Content.ReadAsStringAsync());
            Assert.Equal(current.AccountId,Assert.Single((await replay.Content.ReadFromJsonAsync<VoucherDraftView>())!.AppliedPaymentMethods!).AccountId);
        }
        Assert.Equal(1,resolutions.Count);
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.Customers WHERE PartyId=@Id",party));

        var cloneId = Guid.NewGuid();
        using var cloneResponse = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/documents/{capture.DocumentId}/clone",
            new CloneVoucherDraftRequest(cloneId,capture.OccurredAt.AddDays(1)));
        Assert.True(cloneResponse.IsSuccessStatusCode,await cloneResponse.Content.ReadAsStringAsync());
        var clone = (await cloneResponse.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        var clonedMoney = clone.Lines.Single(line => line.Value.PaymentEvidence?.PaymentMethodId==form.PaymentMethodId).Value;
        Assert.Equal(current.AccountId,clonedMoney.AccountId);
        Assert.Equal(3,clonedMoney.PaymentEvidence!.PaymentMethodVersion);
        Assert.Null(clonedMoney.PaymentEvidence.TenderedAmount); Assert.Null(clonedMoney.PaymentEvidence.Notes);
        Assert.Null(clone.SentAt); Assert.Null(clone.AppliedPaymentMethods);
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id",cloneId));

        SavePaymentMethodRequest Configuration(PaymentMethodView value) => new(value.PaymentMethodId,value.Code,value.Name,
            value.PaymentTypeId,value.AccountId,false,true,true,false,value.IsActive,value.SortOrder,value.RowVersion,UseForVouchers:false);
        async Task<PaymentMethodView> ChangeFormAsync(PaymentMethodView value,Guid accountId,string name)
        {
            using var response = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{value.PaymentMethodId}",Configuration(value) with { AccountId=accountId,Name=name });
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        }
        async Task FailAfterFinancialWritesAsync(Func<Task<HttpResponseMessage>> submit)
        {
            using var fault = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
                commandFilter: command => command.CommandText.Contains("DECLARE @OpeningLines TABLE",StringComparison.Ordinal),
                beforeExecute: command => {
                    var journal = JsonNode.Parse((string)command.Parameters["@Lines"].Value)!.AsArray();
                    journal.Single(line => line!["SourceLineId"]!.GetValue<Guid>()==advanceLine)!["SourceLineId"]=Guid.NewGuid();
                    command.Parameters["@Lines"].Value=journal.ToJsonString();
                });
            using var response = await submit();
            Assert.Equal(HttpStatusCode.InternalServerError,response.StatusCode); Assert.Equal(1,fault.Count);
        }
    }

    private static VoucherPaymentEvidence VoucherFormEvidence(PaymentMethodView form) => new(PaymentMethodId:form.PaymentMethodId,
        PaymentMethodTypeCode:form.MethodCode,PaymentMethodVersion:form.Version,PaymentMethodCode:form.Code,PaymentMethodName:form.Name);

    private static async Task<PaymentMethodView> CreateVoucherCashFormAsync(HttpClient client,AccountingAccountView account,string name)
    {
        var types = (await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!;
        var request = new SavePaymentMethodRequest(Guid.NewGuid(),$"VOUCHER-{Guid.NewGuid():N}",name,
            types.Single(type => type.Code=="Cash").Id,account.AccountId,false,true,true,false,true,10,UseForVouchers:false);
        using var response = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{request.PaymentMethodId}",request);
        Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
    }
}
