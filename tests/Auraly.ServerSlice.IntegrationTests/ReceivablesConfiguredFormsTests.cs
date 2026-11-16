using System.Net.Http.Json;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Payments;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Domain.Receivables;
using Auraly.Infrastructure.Persistence;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class ReceivablesVerticalSliceTests
{
    [Fact]
    public async Task Pending_configured_collection_uses_the_account_current_at_posting_and_completed_replay_keeps_the_result()
    {
        var (customerId,userId,_)=await ConfigureAsync();
        using var client=fixture.CreateUserClient(userId,ReceivablesPermissionCodes.Read,
            ReceivablesPermissionCodes.ManageCredit,ReceivablesPermissionCodes.RegisterPayment,
            CatalogPermissionCodes.Read,CatalogPermissionCodes.Update,AccountingPermissionCodes.Read,
            AccountingPermissionCodes.Configure,AccountingPermissionCodes.Retry);
        var before=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"1108","Asset","Cash","Debit");
        var current=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"1109","Asset","Cash","Debit");
        var type=(await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!
            .Single(value=>value.Code=="Cash");
        var configuration=new SavePaymentMethodRequest(Guid.NewGuid(),$"CURRENT-{Guid.NewGuid():N}","Caja anterior",
            type.Id,before.AccountId,false,true,false,false,true,10);
        using var saved=await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{configuration.PaymentMethodId}",configuration);
        Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
        var form=(await saved.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        var invoice=Guid.NewGuid();
        var at=new DateTimeOffset(2026,8,1,12,0,0,TimeSpan.FromHours(-5));
        using(var imported=await client.PostAsJsonAsync("/api/commerce/v1/receivables/preexisting/import",
            new ImportPreexistingReceivablesRequest(fixture.BusinessId,[new(invoice,customerId,null,null,
                $"CURRENT-{invoice:N}",at,at.AddDays(30),1000,await AccountIdByCodeAsync("413595"),null)])))
            Assert.True(imported.IsSuccessStatusCode,await imported.Content.ReadAsStringAsync());
        var opening=await ScalarAsync<Guid>("SELECT OpeningEntryId FROM dbo.Receivables WHERE ReceivableId=@Id",invoice);
        var line=await ScalarAsync<int>("SELECT OpeningEntryLineNumber FROM dbo.Receivables WHERE ReceivableId=@Id",invoice);
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var binding=new SqlCommand("UPDATE dbo.Receivables SET OpeningEntryId=NULL,OpeningEntryLineNumber=NULL WHERE ReceivableId=@Id",connection);
        binding.Parameters.AddWithValue("@Id",invoice);
        Assert.Equal(1,await binding.ExecuteNonQueryAsync());
        var payment=new ConfirmCustomerPaymentRequest(Guid.NewGuid(),fixture.BusinessId,customerId,null,at.AddDays(1),"COP",null,
            [new(invoice,100)],[new("Cash",100,100,PaymentMethodId:form.PaymentMethodId,PaymentMethodVersion:form.Version)]);
        await PortfolioOriginalAccountScenario.AssertInactiveFormAccountRejectedAsync(fixture,client,before.AccountId,
            form.PaymentMethodId,payment.PaymentId,true,
            () => SendAsync(client,"/api/commerce/v1/receivable-payments/confirm",payment,payment.PaymentId.ToString("N")));
        try
        {
            using var accepted=await SendAsync(client,"/api/commerce/v1/receivable-payments/confirm",payment,payment.PaymentId.ToString("N"));
            Assert.True(accepted.IsSuccessStatusCode,await accepted.Content.ReadAsStringAsync());
            Assert.Equal("AccountingPendingConfiguration",await ScalarAsync<string>(
                "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",payment.PaymentId));
            var receipts = (await client.GetFromJsonAsync<AccountingDocumentPage>(
                $"/api/commerce/v1/accounting/documents?from=2026-08-01&to=2026-08-31&voucherTypeCode=CashReceipt&search={payment.PaymentId}"))!;
            var receipt = Assert.Single(receipts.Items);
            Assert.Equal(payment.PaymentId,receipt.SourceDocumentId);
            Assert.Equal("ReceivablePayment",receipt.SourceDocumentType);
            Assert.Equal("CashReceipt",receipt.VoucherTypeCode);
            Assert.False(receipt.HasManualDraft);
            Assert.Equal("AccountingPendingConfiguration",receipt.Status);
            await using var pending=new SqlCommand("SELECT AccountId FROM dbo.CustomerPaymentTenders WHERE PaymentId=@Id",connection);
            pending.Parameters.AddWithValue("@Id",payment.PaymentId);
            Assert.Equal(DBNull.Value,await pending.ExecuteScalarAsync());
            var transferType = (await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!
                .Single(value => value.Code == "Transfer");
            var lockedForm = Assert.Single((await client.GetFromJsonAsync<PaymentMethodPage>(
                $"/api/commerce/v1/payment-methods/?paymentMethodId={form.PaymentMethodId}"))!.Items);
            using var semanticEdit = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{form.PaymentMethodId}",
                configuration with { PaymentTypeId = transferType.Id, IsActive = false, RowVersion = lockedForm.RowVersion });
            Assert.Equal(System.Net.HttpStatusCode.Conflict, semanticEdit.StatusCode);
            using var changed=await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{form.PaymentMethodId}",
                configuration with {AccountId=current.AccountId,Name="Caja vigente",RowVersion=lockedForm.RowVersion});
            Assert.True(changed.IsSuccessStatusCode,await changed.Content.ReadAsStringAsync());
        }
        finally
        {
            binding.CommandText="UPDATE dbo.Receivables SET OpeningEntryId=@Entry,OpeningEntryLineNumber=@Line WHERE ReceivableId=@Id";
            binding.Parameters.AddWithValue("@Entry",opening);binding.Parameters.AddWithValue("@Line",line);
            Assert.Equal(1,await binding.ExecuteNonQueryAsync());
        }
        using var resolutionReads=new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter:command=>command.CommandText.Contains("DECLARE @Resolved TABLE(Line int PRIMARY KEY",StringComparison.Ordinal));
        using(var retry=await client.PostAsync($"/api/commerce/v1/accounting/postings/{payment.PaymentId}/retry",null))
            Assert.True(retry.IsSuccessStatusCode,await retry.Content.ReadAsStringAsync());
        Assert.Equal(1,resolutionReads.Count);
        await using var result=new SqlCommand("""
            SELECT tender.AccountId,tender.PaymentMethodName,tender.PaymentMethodVersion,
              (SELECT SUM(line.Debit) FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
                WHERE entry.SourceDocumentId=@Id AND line.AccountId=tender.AccountId)
            FROM dbo.CustomerPaymentTenders tender WHERE tender.PaymentId=@Id;
            """,connection);
        result.Parameters.AddWithValue("@Id",payment.PaymentId);
        await using(var row=await result.ExecuteReaderAsync())
        {
            Assert.True(await row.ReadAsync());Assert.Equal(current.AccountId,row.GetGuid(0));
            Assert.Equal("Caja vigente",row.GetString(1));Assert.Equal(2,row.GetInt32(2));Assert.Equal(100m,row.GetDecimal(3));
        }
        var latest=Assert.Single((await client.GetFromJsonAsync<PaymentMethodPage>(
            $"/api/commerce/v1/payment-methods/?paymentMethodId={form.PaymentMethodId}"))!.Items);
        using(var changedAgain=await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{form.PaymentMethodId}",
            configuration with {Name="Otra cuenta después de aplicar",RowVersion=latest.RowVersion}))
            Assert.True(changedAgain.IsSuccessStatusCode,await changedAgain.Content.ReadAsStringAsync());
        using(var replay=await SendAsync(client,"/api/commerce/v1/receivable-payments/confirm",payment,payment.PaymentId.ToString("N")))
            Assert.True(replay.IsSuccessStatusCode,await replay.Content.ReadAsStringAsync());
        Assert.Equal(1,await CountAsync("AccountingEntries","SourceDocumentId",payment.PaymentId));
        Assert.Equal(1,resolutionReads.Count);
        Assert.Equal(current.AccountId,await ScalarAsync<Guid>("SELECT AccountId FROM dbo.CustomerPaymentTenders WHERE PaymentId=@Id",payment.PaymentId));
        Assert.Equal(900m,await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id",invoice));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Configured_receipts_restrict_new_multi_invoice_tenders_but_preserve_accepted_replay(bool historical)
    {
        var (customerId,userId,_)=await ConfigureAsync();
        using var client=fixture.CreateUserClient(userId,ReceivablesPermissionCodes.Read,
            ReceivablesPermissionCodes.ManageCredit,ReceivablesPermissionCodes.RegisterPayment,
            CatalogPermissionCodes.Read,CatalogPermissionCodes.Update,AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure);
        var first=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"1108","Asset","Cash","Debit");
        var second=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"1109","Asset","Cash","Debit");
        var types=(await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!;
        var type=types.Single(item=>item.Code=="Cash");
        async Task<PaymentMethodView> SaveForm(Guid account,string name)
        {
            var request=new SavePaymentMethodRequest(Guid.NewGuid(),$"C-{Guid.NewGuid():N}",name,type.Id,account,
                false,true,false,false,true,10);
            using var result=await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{request.PaymentMethodId}?page=1&pageSize=50",request);
            Assert.True(result.IsSuccessStatusCode,await result.Content.ReadAsStringAsync());
            return (await result.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        }
        var form1=await SaveForm(first.AccountId,"Caja uno");var form2=await SaveForm(second.AccountId,"Caja dos");
        var invoice1=Guid.NewGuid();var invoice2=Guid.NewGuid();
        var issued=new DateTimeOffset(2026,8,1,12,0,0,TimeSpan.FromHours(-5));
        var counterpart=await AccountIdByCodeAsync("413595");
        using(var imported=await client.PostAsJsonAsync("/api/commerce/v1/receivables/preexisting/import",
            new ImportPreexistingReceivablesRequest(fixture.BusinessId,[
                new(invoice1,customerId,null,null,$"FORMS-1-{invoice1:N}",issued,issued.AddDays(30),1000,counterpart,null),
                new(invoice2,customerId,null,null,$"FORMS-2-{invoice2:N}",issued,issued.AddDays(30),1000,counterpart,null)])))
            Assert.True(imported.IsSuccessStatusCode,await imported.Content.ReadAsStringAsync());
        var payment=new ConfirmCustomerPaymentRequest(Guid.NewGuid(),fixture.BusinessId,customerId,null,issued.AddDays(1),"COP",null,
            [new(invoice1,500),new(invoice2,500)],[
                new("Cash",600,600,PaymentMethodId:form1.PaymentMethodId,PaymentMethodVersion:form1.Version),
                new("Cash",400,400,PaymentMethodId:form2.PaymentMethodId,PaymentMethodVersion:form2.Version)],PaidOn:new(2026,8,2));
        if (historical)
        {
            // Reconstruct a receipt accepted before the new capture policy using
            // the existing SQL participant and source writer, then replay via API.
            var settlement = ReceivableSettlement.Create(payment.Allocations.Select(item => new ReceivableAllocation(item.ReceivableId, item.Amount)));
            var tenders = PaymentTenderBreakdown.Create(payment.Payments.Select(item => new PaymentTender(item.MethodCode, item.Amount,
                item.TenderedAmount, PaymentMethodId: item.PaymentMethodId, PaymentMethodVersion: item.PaymentMethodVersion)), 1000, 2, PortfolioPaymentMethods.Supported);
            var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { payment.PaymentId, payment.BusinessId,
                payment.CustomerId, payment.WorkSessionId, payment.PaidAt, payment.CurrencyCode, payment.Notes, settlement.Allocations, tenders.Tenders }));
            hash = SHA256.HashData(hash.Concat(Encoding.UTF8.GetBytes(payment.PaidOn!.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture))).ToArray());
            await using var seedConnection = new SqlConnection(fixture.ConnectionString); await seedConnection.OpenAsync();
            await using var transaction = (SqlTransaction)await seedConnection.BeginTransactionAsync(IsolationLevel.Serializable);
            var number = await SqlOperationalDocumentAllocator.AllocateExistingNumberAsync(seedConnection, transaction,
                fixture.TenantId, fixture.BusinessId, ReceivablesDocumentTypes.Payment, payment.PaidAt, default);
            var resolved = await SqlPaymentMethodResolver.ResolveAsync(seedConnection, transaction, fixture.TenantId,
                fixture.BusinessId, "Receipts", tenders.Tenders, default);
            var payload = CustomerPaymentContractSerializer.Serialize(new(fixture.TenantId, fixture.BusinessId, payment.PaymentId,
                customerId, userId, null, number.FullNumber, number.SeriesId, number.Prefix, number.SeriesCode, number.Consecutive,
                payment.PaidAt, "COP", null, 1000, settlement.Allocations.Select((item,index) => new CustomerPaymentAllocationSnapshot(index+1,item.ReceivableId,item.Amount)).ToArray(),
                resolved.Select((item,index) => new CustomerPaymentTenderSnapshot(index+1,item.MethodCode,item.Amount,item.TenderedAmount,
                    item.BankAccountId,item.Reference,item.Notes,item.CardFranchiseCode,item.ApprovalNumber,item.PaymentMethodId,
                    item.PaymentMethodVersion,item.PaymentMethodCode,item.PaymentMethodName,item.AccountId)).ToArray()));
            await using var seed = new SqlCommand("accounting.PortfolioPaymentAccept", seedConnection, transaction) { CommandType = CommandType.StoredProcedure };
            seed.Parameters.AddWithValue("@TenantId", fixture.TenantId); seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@UserId", userId); seed.Parameters.AddWithValue("@SubledgerKind", "Receivable");
            seed.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = payload;
            seed.Parameters.AddWithValue("@IdempotencyKey", payment.PaymentId.ToString("N")); seed.Parameters.Add("@PayloadHash", SqlDbType.Binary,32).Value = hash;
            seed.Parameters.AddWithValue("@AcceptedAt", payment.PaidAt); await seed.ExecuteNonQueryAsync();
            await SqlAccountingPostingJobWriter.InsertSourceAsync(seedConnection, transaction, fixture.TenantId, fixture.BusinessId,
                payment.PaymentId, ReceivablesDocumentTypes.Payment, payload, payment.PaidAt, new Uuid7AuralyIdGenerator(TimeProvider.System),
                TimeProvider.System, default, AccountingJobRequirement.PreserveCommercialEffects);
            await transaction.CommitAsync();
            await ReprocessAsync(payment.PaymentId, ReceivablesDocumentTypes.Payment);
        }
        else
        {
            using var rejected = await SendAsync(client,"/api/commerce/v1/receivable-payments/confirm",payment,payment.PaymentId.ToString("N"));
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Contains("una sola forma de pago", await rejected.Content.ReadAsStringAsync());
            Assert.Equal(0, await CountAsync("AccountingEntries","SourceDocumentId",payment.PaymentId));
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CustomerPayments WHERE PaymentId=@Id", payment.PaymentId));
            Assert.Equal(1000m,await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id",invoice1));
            payment = payment with { Payments = [payment.Payments.First() with { Amount = 1000, TenderedAmount = 1000 }] };
        }
        using(var accepted=await SendAsync(client,"/api/commerce/v1/receivable-payments/confirm",payment,payment.PaymentId.ToString("N")))
        {
            Assert.True(accepted.IsSuccessStatusCode,await accepted.Content.ReadAsStringAsync());
            Assert.Equal(new DateTimeOffset(2026,8,2,17,0,0,TimeSpan.Zero),
                (await accepted.Content.ReadFromJsonAsync<CustomerPaymentAcceptance>())!.PaidAt);
        }
        Assert.Equal(500m,await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id",invoice1));
        Assert.Equal(500m,await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id",invoice2));
        Assert.Equal(1,await CountAsync("AccountingEntries","SourceDocumentId",payment.PaymentId));
        Assert.Equal(historical ? 2 : 1,await CountAsync("CustomerPaymentTenders","PaymentId",payment.PaymentId));
        var lockedFirst = Assert.Single((await client.GetFromJsonAsync<PaymentMethodPage>(
            $"/api/commerce/v1/payment-methods/?paymentMethodId={form1.PaymentMethodId}"))!.Items);
        var edit=new SavePaymentMethodRequest(form1.PaymentMethodId,form1.Code,"Caja renombrada",form1.PaymentTypeId,form1.AccountId,
            form1.UseForSales,form1.UseForReceipts,form1.UseForSupplierPayments,form1.UseForPayroll,true,10,lockedFirst.RowVersion);
        using(var changed=await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{form1.PaymentMethodId}?page=1&pageSize=50",edit))
            Assert.True(changed.IsSuccessStatusCode,await changed.Content.ReadAsStringAsync());
        using(var replay=await SendAsync(client,"/api/commerce/v1/receivable-payments/confirm",payment,payment.PaymentId.ToString("N")))
        {
            Assert.True(replay.IsSuccessStatusCode,await replay.Content.ReadAsStringAsync());
            Assert.True((await replay.Content.ReadFromJsonAsync<CustomerPaymentAcceptance>())!.IdempotentReplay);
        }
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var verify=new SqlCommand("""
            SELECT tender.PaymentMethodName,tender.PaymentMethodVersion,
              (SELECT SUM(line.Debit) FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
               WHERE entry.SourceDocumentId=@Payment AND line.AccountId=tender.AccountId)
            FROM dbo.CustomerPaymentTenders tender WHERE tender.PaymentId=@Payment ORDER BY tender.LineNumber;
            """,connection);
        verify.Parameters.AddWithValue("@Payment",payment.PaymentId);
        await using var reader=await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());Assert.Equal("Caja uno",reader.GetString(0));Assert.Equal(1,reader.GetInt32(1));Assert.Equal(historical ? 600m : 1000m,reader.GetDecimal(2));
        Assert.Equal(historical, await reader.ReadAsync());
        if (historical) { Assert.Equal("Caja dos",reader.GetString(0));Assert.Equal(1,reader.GetInt32(1));Assert.Equal(400m,reader.GetDecimal(2)); }
    }
}
