using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class GoodsReceiptProcessingTests
{
    [Fact]
    public async Task Cost_corrections_and_returns_keep_the_original_payable_account()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var actor = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Configure, AccountingPermissionCodes.Read });
        var payable = Assert.Single(await service.ListMappingsAsync(actor),
            item => item.Category == AccountingCategories.AccountsPayable && item.BusinessId is null);
        var alternatePayable = await service.CreateAccountAsync(actor, new(Guid.NewGuid(), fixture.TenantId,
            "220591", "Obligación alterna de prueba de correcciones", "Liability", true, true,
            new(null, "Payable", "Credit", null, null, null)));
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts, PurchasingPermissionCodes.ConfirmGoodsReceipts,
            PurchasingPermissionCodes.CreatePurchaseReturns, PurchasingPermissionCodes.ConfirmPurchaseReturns);
        var original = await CreateIsolatedCostEditRequestAsync();
        var initialLine = original.Lines.Single() with { Quantity = 2m, UnitCost = 10_000m, DiscountAmount = 0m,
            TaxCode = "IVA-0", TaxRate = 0m, TaxTreatment = PurchasingTaxTreatments.NotApplicable };
        original = original with { Lines = [initialLine], PurchaseEvidenceType = PurchaseEvidenceTypes.InternalReceiptVoucher,
            SupplierInvoiceNumber = null };
        using (var message = CreateMessage(original, $"original-accounts-{original.DocumentId:N}"))
        using (var accepted = await client.SendAsync(message))
            Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
        Assert.Equal("Posted", await ScalarAsync<string>(
            "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'", original.DocumentId));
        try
        {
            await service.SetMappingAsync(actor, new(fixture.TenantId, null, payable.Category, alternatePayable.AccountId,
                payable.EffectiveFrom, payable.EffectiveTo));
            for (var revisionNumber = 0; revisionNumber < 2; revisionNumber++)
            {
                var detail = (await client.GetFromJsonAsync<GoodsReceiptDetail>(
                    $"/api/commerce/v1/goods-receipts/{original.DocumentId:D}"))!;
                var line = initialLine with { UnitCost = 20_000m + revisionNumber * 10_000m };
                var draftId = Guid.NewGuid();
                var draftRequest = new SaveGoodsReceiptDraftRequest(draftId, fixture.BusinessId,
                    original.WarehouseId, original.SupplierId, null, original.SupplierInvoiceDate,
                    original.ReceivedAt, true, original.DueDate, "COP", original.Notes, [line], null,
                    original.PurchaseEvidenceType, original.PurchaseOrderId, original.ExchangeRate,
                    detail.ExchangeRateDate, original.ExchangeRateSource, PartySiteId: original.PartySiteId,
                    CostEditOfReceiptId: original.DocumentId, BaseCostRevisionNumber: revisionNumber);
                using var saved = await client.PutAsJsonAsync($"/api/commerce/v1/goods-receipts/drafts/{draftId:D}", draftRequest);
                Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
                var draft = (await saved.Content.ReadFromJsonAsync<GoodsReceiptDraft>())!;
                var revision = original with { DocumentId = Guid.NewGuid(), Lines = [line],
                    ExchangeRateDate = detail.ExchangeRateDate, CostEditOfReceiptId = original.DocumentId,
                    CostEditDraftId = draftId, BaseCostRevisionNumber = revisionNumber, DraftConcurrencyToken = draft.ConcurrencyToken };
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    using var message = CreateMessage(revision, $"original-accounts-{revision.DocumentId:N}");
                    using var accepted = await client.SendAsync(message);
                    Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
                }
                await AssertPostingAsync(revision.DocumentId, -20_000m);
            }
            var returned = new ConfirmPurchaseReturnRequest(Guid.NewGuid(), fixture.BusinessId, original.DocumentId,
                original.ReceivedAt.AddDays(1), "QualityIssue", "Devolución parcial después de corregir", [new(1, 1m)]);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/purchase-returns/confirm")
                    { Content = JsonContent.Create(returned) };
                message.Headers.Add("Idempotency-Key", $"return-accounts-{returned.ReturnId:N}");
                using var accepted = await client.SendAsync(message);
                Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
            }
            await AssertPostingAsync(returned.ReturnId, 30_000m);
            Assert.Equal(30_000m, await ScalarAsync<decimal>(
                "SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'", original.DocumentId));
        }
        finally
        {
            await service.SetMappingAsync(actor, new(fixture.TenantId, null, payable.Category, payable.AccountId,
                payable.EffectiveFrom, payable.EffectiveTo));
        }

        async Task AssertPostingAsync(Guid documentId, decimal expectedPayableChange)
        {
            Assert.Equal("Posted:", await ScalarAsync<string>(
                "SELECT CONCAT(Status,N':',COALESCE(LastErrorMessage,N'')) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", documentId));
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                SELECT entry.DebitTotal,entry.CreditTotal,
                  SUM(CASE WHEN line.AccountId=@Payable THEN line.Debit-line.Credit ELSE 0 END),
                  COUNT(CASE WHEN line.AccountId=@Alternate THEN 1 END)
                FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
                WHERE entry.SourceDocumentId=@Document AND entry.BusinessId=@Business
                GROUP BY entry.EntryId,entry.DebitTotal,entry.CreditTotal;
                """, connection);
            command.Parameters.AddWithValue("@Document", documentId); command.Parameters.AddWithValue("@Business", fixture.BusinessId);
            command.Parameters.AddWithValue("@Payable", payable.AccountId); command.Parameters.AddWithValue("@Alternate", alternatePayable.AccountId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync()); Assert.Equal(reader.GetDecimal(0), reader.GetDecimal(1));
            Assert.Equal(expectedPayableChange, reader.GetDecimal(2)); Assert.Equal(0, reader.GetInt32(3));
            Assert.False(await reader.ReadAsync());
        }
    }
}
