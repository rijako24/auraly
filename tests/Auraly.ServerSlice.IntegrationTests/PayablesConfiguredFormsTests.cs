using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PayablesVerticalSliceTests
{
    [Fact]
    public async Task Pending_configured_supplier_payment_resolves_the_current_account_once_and_preserves_its_type()
    {
        var product = Guid.NewGuid();
        await ConfigureSeriesAndAccountingAsync(product);
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts,PayablesPermissionCodes.Read,
            PayablesPermissionCodes.RegisterPayment,CatalogPermissionCodes.Read,CatalogPermissionCodes.Update,
            AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure,AccountingPermissionCodes.Retry);
        var at = new DateTimeOffset(2026,8,2,9,0,0,TimeSpan.FromHours(-5));
        var receipt = new ConfirmGoodsReceiptRequest(Guid.NewGuid(),fixture.BusinessId,fixture.WarehouseId,
            fixture.SupplierId,$"CURRENT-{Guid.NewGuid():N}",at,at,true,at.AddDays(30),"COP",null,
            [new GoodsReceiptLineRequest(1,product,"Compra a crédito",1,1_000,0,"00",0,PurchasingTaxTreatments.NotApplicable)]);
        using (var response = await SendAsync(client,"/api/commerce/v1/goods-receipts/confirm",receipt,receipt.DocumentId.ToString("N")))
            Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);
        var payable = await ScalarAsync<Guid>("SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id",receipt.DocumentId);
        var opening = await ScalarAsync<Guid>("SELECT OpeningEntryId FROM dbo.Payables WHERE PayableId=@Id",payable);
        var line = await ScalarAsync<int>("SELECT OpeningEntryLineNumber FROM dbo.Payables WHERE PayableId=@Id",payable);
        var before = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture,client);
        var current = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture,client);
        var types = (await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!;
        var configuration = new SavePaymentMethodRequest(Guid.NewGuid(),$"SUP-{Guid.NewGuid():N}","Caja anterior",
            types.Single(type => type.Code == "Cash").Id,before.AccountId,false,false,true,false,true,10);
        using var saved = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{configuration.PaymentMethodId}",configuration);
        Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
        var form = (await saved.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        var payment = new ConfirmSupplierPaymentRequest(Guid.NewGuid(),fixture.BusinessId,fixture.SupplierId,at.AddDays(1),"COP",null,
            [new(payable,100)],[new("Cash",100,100,PaymentMethodId:form.PaymentMethodId,PaymentMethodVersion:form.Version)]);
        await PortfolioOriginalAccountScenario.AssertInactiveFormAccountRejectedAsync(fixture,client,before.AccountId,
            form.PaymentMethodId,payment.PaymentId,false,
            () => SendAsync(client,"/api/commerce/v1/payable-payments/confirm",payment,payment.PaymentId.ToString("N")));

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        // Exercise the real pending-configuration gate in the isolated fixture.
        await using var binding = new SqlCommand("UPDATE dbo.Payables SET OpeningEntryId=NULL,OpeningEntryLineNumber=NULL WHERE PayableId=@Id",connection);
        binding.Parameters.AddWithValue("@Id",payable);
        Assert.Equal(1,await binding.ExecuteNonQueryAsync());
        try
        {
            using var accepted = await SendAsync(client,"/api/commerce/v1/payable-payments/confirm",payment,payment.PaymentId.ToString("N"));
            Assert.True(accepted.IsSuccessStatusCode,await accepted.Content.ReadAsStringAsync());
            Assert.Equal("AccountingPendingConfiguration",await ScalarAsync<string>(
                "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",payment.PaymentId));
            var egresses = (await client.GetFromJsonAsync<AccountingDocumentPage>(
                $"/api/commerce/v1/accounting/documents?from=2026-08-01&to=2026-08-31&voucherTypeCode=Disbursement&search={payment.PaymentId}"))!;
            var egress = Assert.Single(egresses.Items);
            Assert.Equal(payment.PaymentId,egress.SourceDocumentId);
            Assert.Equal("PayablePayment",egress.SourceDocumentType);
            Assert.Equal("Disbursement",egress.VoucherTypeCode);
            Assert.False(egress.HasManualDraft);
            Assert.Equal("AccountingPendingConfiguration",egress.Status);
            await using var unresolved = new SqlCommand("SELECT AccountId FROM dbo.SupplierPaymentTenders WHERE PaymentId=@Id",connection);
            unresolved.Parameters.AddWithValue("@Id",payment.PaymentId);
            Assert.Equal(DBNull.Value,await unresolved.ExecuteScalarAsync());
            form = Assert.Single((await client.GetFromJsonAsync<PaymentMethodPage>(
                $"/api/commerce/v1/payment-methods?paymentMethodId={form.PaymentMethodId}"))!.Items);
            using var semanticEdit = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{form.PaymentMethodId}",
                configuration with { PaymentTypeId = types.Single(type => type.Code == "Transfer").Id,
                    IsActive = false,RowVersion = form.RowVersion });
            Assert.Equal(HttpStatusCode.Conflict,semanticEdit.StatusCode);
            using var changed = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{form.PaymentMethodId}",
                configuration with { AccountId = current.AccountId,Name = "Caja vigente",RowVersion = form.RowVersion });
            Assert.True(changed.IsSuccessStatusCode,await changed.Content.ReadAsStringAsync());
        }
        finally
        {
            binding.CommandText = "UPDATE dbo.Payables SET OpeningEntryId=@Entry,OpeningEntryLineNumber=@Line WHERE PayableId=@Id";
            binding.Parameters.AddWithValue("@Entry",opening);
            binding.Parameters.AddWithValue("@Line",line);
            Assert.Equal(1,await binding.ExecuteNonQueryAsync());
        }

        using var resolutionReads = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter: command => command.CommandText.Contains("DECLARE @Resolved TABLE(Line int PRIMARY KEY",StringComparison.Ordinal));
        using (var retry = await client.PostAsync($"/api/commerce/v1/accounting/postings/{payment.PaymentId}/retry",null))
            Assert.True(retry.IsSuccessStatusCode,await retry.Content.ReadAsStringAsync());
        Assert.Equal(1,resolutionReads.Count);
        await using var verify = new SqlCommand("""
            SELECT tender.AccountId,tender.PaymentMethodVersion,tender.PaymentMethodName,
              (SELECT SUM(line.Credit) FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
                WHERE entry.SourceDocumentId=@Id AND entry.BusinessId=@Business AND line.AccountId=tender.AccountId)
            FROM dbo.SupplierPaymentTenders tender WHERE tender.PaymentId=@Id;
            """,connection);
        verify.Parameters.AddWithValue("@Id",payment.PaymentId);
        verify.Parameters.AddWithValue("@Business",fixture.BusinessId);
        await using (var row = await verify.ExecuteReaderAsync())
        {
            Assert.True(await row.ReadAsync());
            Assert.Equal(current.AccountId,row.GetGuid(0));
            Assert.Equal(2,row.GetInt32(1));
            Assert.Equal("Caja vigente",row.GetString(2));
            Assert.Equal(100m,row.GetDecimal(3));
            Assert.False(await row.ReadAsync());
        }
        var latest = Assert.Single((await client.GetFromJsonAsync<PaymentMethodPage>(
            $"/api/commerce/v1/payment-methods/?paymentMethodId={form.PaymentMethodId}"))!.Items);
        using (var changedAgain = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{form.PaymentMethodId}",
            configuration with { RowVersion = latest.RowVersion }))
            Assert.True(changedAgain.IsSuccessStatusCode,await changedAgain.Content.ReadAsStringAsync());
        using (var replay = await SendAsync(client,"/api/commerce/v1/payable-payments/confirm",payment,payment.PaymentId.ToString("N")))
        {
            Assert.True(replay.IsSuccessStatusCode,await replay.Content.ReadAsStringAsync());
            Assert.True((await replay.Content.ReadFromJsonAsync<SupplierPaymentAcceptance>())!.IdempotentReplay);
        }
        Assert.Equal(1,resolutionReads.Count);
        Assert.Equal(1,await CountAsync("AccountingEntries","SourceDocumentId",payment.PaymentId));
        Assert.Equal(900m,await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id",payable));
        Assert.Equal(current.AccountId,await ScalarAsync<Guid>("SELECT AccountId FROM dbo.SupplierPaymentTenders WHERE PaymentId=@Id",payment.PaymentId));
    }
}
