using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Purchasing;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Inventory;
using Auraly.Application.Purchasing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PayablesVerticalSliceTests
{
    [Fact]
    public async Task Confirmed_receipt_rejects_new_cost_voucher_revisions()
    {
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        using var response = await client.PostAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/{Guid.NewGuid():D}/cost-revisions",
            new { revisionId = Guid.NewGuid(), businessId = fixture.BusinessId });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Explicit_unallocated_asset_reclassifies_once_and_never_reoffers_native_capitalized_inventory()
    {
        var productId = Guid.NewGuid();
        await ConfigureSeriesAndAccountingAsync(productId);
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
            PurchasingPermissionCodes.ReadGoodsReceipts, PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var pending = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "1398", "Asset", "UnallocatedPurchaseCost", "Debit");
        var general = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "1397", "Asset", "General", "Debit");
        var control = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "2298", "Liability", "Payable", "Credit");
        var at = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var capture = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher, at,
            "MANUAL_VOUCHER", "Flete por distribuir y otro activo", "FLETE-PENDIENTE",
            [new(pending.AccountId, null, null, "Costo por distribuir", 5_000m, 0, LineId: Guid.NewGuid()),
             new(general.AccountId, fixture.SupplierPartyId, null, "Otro activo que no es costo disponible", 700m, 0, LineId: Guid.NewGuid()),
             new(control.AccountId, fixture.SupplierPartyId, null, "Proveedor", 0, 5_700m, "FLETE-PENDIENTE",
                Guid.NewGuid(), DueDate: at.AddDays(30))], null, null,
            MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "Accounting");
        var path = $"/api/commerce/v1/accounting/manual/drafts/{capture.DocumentId}";
        using var savedResponse = await client.PutAsJsonAsync(path, capture);
        Assert.True(savedResponse.IsSuccessStatusCode, await savedResponse.Content.ReadAsStringAsync());
        var saved = (await savedResponse.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        using var sentResponse = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion));
        Assert.True(sentResponse.IsSuccessStatusCode, await sentResponse.Content.ReadAsStringAsync());
        var sent = (await sentResponse.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        Assert.Equal("Posted", sent.Status);
        var used = Assert.Single((await client.GetFromJsonAsync<AccountingAccountOptionPage>(
            $"/api/commerce/v1/accounting/account-options?accountId={pending.AccountId}&page=1&pageSize=1"))!.Items);
        Assert.True(used.ConfigurationLocked);
        using (var reclassified = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/accounts/{used.AccountId}",
            new UpdateAccountingAccountRequest(used.Name, used.RequiresParty, used.RowVersion!,
                used.Semantics! with { FinancialFunction = "General" })))
            Assert.Equal(HttpStatusCode.Conflict, reclassified.StatusCode);
        var entryId = sent.Row.EntryId!.Value;
        var costPath = $"/api/commerce/v1/goods-receipts/cost-vouchers/{entryId}";
        ConfirmGoodsReceiptRequest Receipt(Guid sourceEntry, int sourceLine, decimal amount) =>
            new(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId, Guid.NewGuid().ToString("N"),
                at, at, true, at.AddDays(30), "COP", "Recepción con costo existente",
                [new(1, productId, "Mercancía", 1m, 10_000m, 0, "IVA-0", 0, PurchasingTaxTreatments.NotApplicable)],
                ReferencedCostDocuments: [new(Guid.NewGuid(), sourceEntry, GoodsReceiptCostAssociationModes.Distribute,
                    [new(sourceLine, amount, PurchaseCostAllocationMethods.Value)])]);
        // RequiresParty is prospective configuration. An old line without that
        // dimension must be rejected before accepting physical stock/cost work.
        using var requiredResponse = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/accounts/{used.AccountId}",
            new UpdateAccountingAccountRequest(used.Name, true, used.RowVersion!, used.Semantics));
        Assert.True(requiredResponse.IsSuccessStatusCode, await requiredResponse.Content.ReadAsStringAsync());
        var required = (await requiredResponse.Content.ReadFromJsonAsync<AccountingAccountView>())!;
        try
        {
            var missingParty = (await client.GetFromJsonAsync<GoodsReceiptCostVoucher>(costPath))!;
            var original = missingParty.Lines.Single(line => line.AccountId == pending.AccountId);
            Assert.Equal(0m, original.EligibleFunctionalAmount);
            var invalidDimension = Receipt(entryId, original.LineNumber, 1m);
            using var denied = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", invalidDimension,
                invalidDimension.DocumentId.ToString("N"));
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            Assert.Equal(0, await ScalarAsync<int>("""
                SELECT (SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id)
                  +(SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id)
                  +(SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id)
                  +(SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id);
                """, invalidDimension.DocumentId));
        }
        finally
        {
            using var restored = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/accounts/{used.AccountId}",
                new UpdateAccountingAccountRequest(required.Name, false, required.RowVersion!, required.Semantics));
            Assert.True(restored.IsSuccessStatusCode, await restored.Content.ReadAsStringAsync());
        }
        var voucher = (await client.GetFromJsonAsync<GoodsReceiptCostVoucher>(costPath))!;
        var eligible = Assert.Single(voucher.Lines.Where(line => line.EligibleFunctionalAmount > 0));
        Assert.Equal((pending.AccountId, 5_000m, 0m), (eligible.AccountId, eligible.EligibleFunctionalAmount, eligible.AssignedFunctionalAmount));
        var other = Assert.Single(voucher.Lines.Where(line => line.AccountId == general.AccountId));
        Assert.Equal(0m, other.EligibleFunctionalAmount);
        var payableId = Assert.Single(sent.FinancialEffects!.Positions).PositionId;
        var invalid = Receipt(entryId, other.LineNumber, 700m);
        using (var denied = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", invalid, invalid.DocumentId.ToString("N")))
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", invalid.DocumentId));
        foreach (var amount in new[] { 3_000m, 2_000m })
        {
            var receipt = Receipt(entryId, eligible.LineNumber, amount);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var accepted = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", receipt, receipt.DocumentId.ToString("N"));
                Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());
            }
            Assert.Equal(10_000m + amount, await ScalarAsync<decimal>(
                "SELECT SUM(ValueChange) FROM dbo.InventoryMovements WHERE DocumentId=@Id", receipt.DocumentId));
            Assert.Equal(amount, await ScalarAsync<decimal>("""
                SELECT SUM(line.Credit) FROM dbo.AccountingEntries entry
                JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
                JOIN purchasing.GoodsReceiptCostDocuments association ON association.GoodsReceiptId=entry.SourceDocumentId
                JOIN dbo.AccountingEntryLines original ON original.EntryId=association.SourceEntryId AND original.LineNumber=1
                WHERE entry.SourceDocumentId=@Id AND line.AccountId=original.AccountId;
                """, receipt.DocumentId));
        }
        Assert.Equal(5_700m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        voucher = (await client.GetFromJsonAsync<GoodsReceiptCostVoucher>(costPath))!;
        Assert.Equal(5_000m, voucher.Lines.Single(line => line.AccountId == pending.AccountId).AssignedFunctionalAmount);
        var exhausted = Receipt(entryId, eligible.LineNumber, 1m);
        using (var denied = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", exhausted, exhausted.DocumentId.ToString("N")))
            Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);

        // Even if Inventory is configured to this same asset, an additional
        // document already capitalized by the receipt cannot be capitalized again.
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var invariant = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            try
            {
                await using var invalidNature = new SqlCommand(
                    "UPDATE dbo.AccountingAccounts SET BalanceSide=N'Credit' WHERE AccountId=@Id;", connection, invariant);
                invalidNature.Parameters.AddWithValue("@Id", pending.AccountId);
                var error = await Assert.ThrowsAsync<SqlException>(() => invalidNature.ExecuteNonQueryAsync());
                Assert.Equal(547, error.Number);
                Assert.Contains("CK_AccountingAccounts_UnallocatedPurchaseCost", error.Message);
            }
            finally { await invariant.RollbackAsync(); }
        }
        await using var snapshot = new SqlCommand("""
            SELECT COALESCE((SELECT * FROM dbo.AccountingAccountMappings WHERE TenantId=@Tenant AND BusinessId=@Business
              AND Category=N'Inventory' AND EffectiveFrom='2026-01-01' FOR JSON PATH),N'[]');
            """, connection);
        snapshot.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        snapshot.Parameters.AddWithValue("@Business", fixture.BusinessId);
        var previous = (string)(await snapshot.ExecuteScalarAsync())!;
        try
        {
            using (var mapped = await client.PutAsJsonAsync("/api/commerce/v1/accounting/account-mappings",
                new SetAccountMappingRequest(fixture.TenantId, fixture.BusinessId, AccountingCategories.Inventory,
                    pending.AccountId, new(2026, 1, 1), null)))
                Assert.True(mapped.IsSuccessStatusCode, await mapped.Content.ReadAsStringAsync());
            var costId = Guid.NewGuid();
            var native = Receipt(entryId, eligible.LineNumber, 1m) with { ReferencedCostDocuments = null,
                AdditionalCostDocuments = [new(costId, fixture.SupplierId, PurchaseEvidenceTypes.InternalReceiptVoucher,
                    Guid.NewGuid().ToString("N"), at, true, at.AddDays(30), "COP", 1m, DateOnly.FromDateTime(at.Date),
                    "FunctionalCurrency", [new(1, PurchaseCostKinds.Freight, "Flete ya capitalizado", 5_000m, 0,
                        "IVA-0", 0, 0, PurchasingTaxTreatments.NotApplicable, PurchaseCostTreatments.Capitalize,
                        PurchaseCostAllocationMethods.Value)])] };
            using (var accepted = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", native, native.DocumentId.ToString("N")))
                Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());
            var nativeEntry = await ScalarAsync<Guid>("SELECT EntryId FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostDocument'", costId);
            var nativeVoucher = (await client.GetFromJsonAsync<GoodsReceiptCostVoucher>($"/api/commerce/v1/goods-receipts/cost-vouchers/{nativeEntry}"))!;
            Assert.Contains(nativeVoucher.Lines, line => line.AccountId == pending.AccountId && line.Debit == 5_000m);
            Assert.All(nativeVoucher.Lines, line => Assert.Equal(0m, line.EligibleFunctionalAmount));
            var doubled = Receipt(nativeEntry, nativeVoucher.Lines.Single(line => line.Debit > 0).LineNumber, 5_000m);
            using var rejected = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", doubled, doubled.DocumentId.ToString("N"));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", doubled.DocumentId));
        }
        finally { await PortfolioOriginalAccountScenario.RestoreAsync(fixture, (pending.AccountId, previous)); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Existing_expense_can_be_associated_or_partially_distributed_without_another_debt(bool distribute)
    {
        var productId = Guid.NewGuid();
        await ConfigureSeriesAndAccountingAsync(productId);
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            ExpensePermissionCodes.Read, ExpensePermissionCodes.Create, ExpensePermissionCodes.Configure, ExpensePermissionCodes.Cancel,
            PurchasingPermissionCodes.ReadGoodsReceipts, PurchasingPermissionCodes.CreateGoodsReceipts, PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var account = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "5196", "Expense", "General", "Debit");
        var concept = new SaveExpenseConceptRequest(Guid.NewGuid(), fixture.BusinessId, "Flete para repartir",
            account.AccountId, null, null, true);
        using (var configured = await client.PutAsJsonAsync($"/api/commerce/v1/expenses/concepts/{concept.ConceptId}", concept))
            Assert.True(configured.IsSuccessStatusCode, await configured.Content.ReadAsStringAsync());
        var at = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var expense = new ConfirmExpenseRequest(Guid.NewGuid(), fixture.BusinessId, fixture.SupplierId,
            concept.ConceptId, null, $"FLETE-{Guid.NewGuid():N}", at, at.AddDays(30), "COP", "Flete compartido",
            5_000m, 0, null, null);
        using (var confirmed = await SendAsync(client, "/api/commerce/v1/expenses/confirm", expense, expense.ExpenseId.ToString("N")))
            Assert.True(confirmed.StatusCode == HttpStatusCode.Accepted, await confirmed.Content.ReadAsStringAsync());
        var entryId = await ScalarAsync<Guid>("SELECT EntryId FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id AND SourceDocumentType=N'Expense'", expense.ExpenseId);
        using (var purchasingOnly = fixture.CreateAdminClient(PurchasingPermissionCodes.ReadGoodsReceipts))
        using (var forbidden = await purchasingOnly.GetAsync($"/api/commerce/v1/goods-receipts/cost-vouchers/{entryId}"))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var voucher = (await client.GetFromJsonAsync<GoodsReceiptCostVoucher>($"/api/commerce/v1/goods-receipts/cost-vouchers/{entryId}"))!;
        var cost = Assert.Single(voucher.Lines.Where(line => line.EligibleFunctionalAmount > 0));
        using (var scope = fixture.CreateScope())
        {
            var workspace = scope.ServiceProvider.GetRequiredService<GoodsReceiptWorkspaceService>();
            var foreign = new PurchasingUserIdentity(fixture.UserId, fixture.TenantId, Guid.NewGuid(),
                new HashSet<string> { PurchasingPermissionCodes.ReadGoodsReceipts, AccountingPermissionCodes.Read });
            Assert.Null(await workspace.GetCostVoucherAsync(foreign, entryId));
            Assert.Null(await workspace.GetCostVoucherAsync(foreign with { TenantId = Guid.NewGuid(), BusinessId = fixture.BusinessId }, entryId));
        }
        Assert.Equal(5_000m, cost.EligibleFunctionalAmount);
        Assert.Equal(0m, cost.AssignedFunctionalAmount);
        Assert.All(voucher.Lines.Where(line => line.Credit > 0), line => Assert.Equal(0m, line.EligibleFunctionalAmount));
        var requests = new List<ConfirmGoodsReceiptRequest>();
        foreach (var amount in new[] { 3_000m, 2_000m })
        {
            // An association is not a financial source, even if its independent
            // identity coincides with an existing source in another namespace.
            var association = new GoodsReceiptVoucherCostRequest(amount == 3_000m ? expense.ExpenseId : Guid.NewGuid(), entryId,
                distribute ? GoodsReceiptCostAssociationModes.Distribute : GoodsReceiptCostAssociationModes.Associate,
                distribute ? [new(cost.LineNumber, amount, PurchaseCostAllocationMethods.Value)] : [], voucher.EntryNumber);
            var receipt = new ConfirmGoodsReceiptRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
                fixture.SupplierId, $"COMPRA-{Guid.NewGuid():N}", at, at, true, at.AddDays(30), "COP", "Cargo existente",
                [new(1, productId, "Producto con costo aplicado", 1, 10_000m, 0, "00", 0, PurchasingTaxTreatments.NotApplicable)],
                ReferencedCostDocuments: [association]);
            if (amount == 3_000m && distribute)
            {
                var earlier = receipt with { DocumentId = Guid.NewGuid(), SupplierInvoiceNumber = $"ANTERIOR-{Guid.NewGuid():N}",
                    SupplierInvoiceDate = at.AddDays(-1) };
                using var rejected = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", earlier, earlier.DocumentId.ToString("N"));
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                Assert.Contains("posterior", await rejected.Content.ReadAsStringAsync());
                Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", earlier.DocumentId));
            }
            else if (!distribute)
                receipt = receipt with { SupplierInvoiceDate = at.AddDays(-1) };
            if (amount == 3_000m)
            {
                var draft = new SaveGoodsReceiptDraftRequest(receipt.DocumentId, receipt.BusinessId, receipt.WarehouseId,
                    receipt.SupplierId, receipt.SupplierInvoiceNumber, receipt.SupplierInvoiceDate, receipt.ReceivedAt,
                    true, receipt.DueDate, receipt.CurrencyCode, receipt.Notes, receipt.Lines, null,
                    PurchaseEvidenceType: receipt.PurchaseEvidenceType, ReferencedCostDocuments: receipt.ReferencedCostDocuments);
                using var saved = await client.PutAsJsonAsync($"/api/commerce/v1/goods-receipts/drafts/{receipt.DocumentId}", draft);
                Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
                var recovered = (await client.GetFromJsonAsync<GoodsReceiptDraft>($"/api/commerce/v1/goods-receipts/drafts/{receipt.DocumentId}"))!;
                Assert.Equal(association.EntryId, Assert.Single(recovered.ReferencedCostDocuments!).EntryId);
                Assert.Equal(association.Mode, recovered.ReferencedCostDocuments![0].Mode);
                Assert.Equal(0m, await ScalarAsync<decimal>("SELECT COALESCE(SUM(FunctionalAmount),0) FROM purchasing.GoodsReceiptCostLines WHERE SourceEntryId=@Id", entryId));
                receipt = receipt with { DraftConcurrencyToken = recovered.ConcurrencyToken };
            }
            if (distribute && amount == 2_000m)
            {
                var competitor = receipt with { DocumentId = Guid.NewGuid(), SupplierInvoiceNumber = $"CARRERA-{Guid.NewGuid():N}",
                    ReferencedCostDocuments = [association with { AssociationId = Guid.NewGuid() }] };
                var outcomes = await Task.WhenAll(
                    SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", receipt, receipt.DocumentId.ToString("N")),
                    SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", competitor, competitor.DocumentId.ToString("N")));
                using var first = outcomes[0];
                using var second = outcomes[1];
                Assert.Single(outcomes.Where(outcome => outcome.StatusCode == HttpStatusCode.Accepted));
                Assert.Single(outcomes.Where(outcome => outcome.StatusCode == HttpStatusCode.Conflict));
                var failedId = first.StatusCode == HttpStatusCode.Accepted ? competitor.DocumentId : receipt.DocumentId;
                if (second.StatusCode == HttpStatusCode.Accepted) receipt = competitor;
                Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", failedId));
            }
            else
            {
                using var response = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", receipt, receipt.DocumentId.ToString("N"));
                Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
            }
            requests.Add(receipt);
            Assert.Equal(10_000m + (distribute ? amount : 0m), await ScalarAsync<decimal>(
                "SELECT SUM(ValueChange) FROM dbo.InventoryMovements WHERE DocumentId=@Id AND DocumentType=N'GoodsReceipt'", receipt.DocumentId));
            Assert.Equal(distribute ? amount : 0m, await ScalarAsync<decimal>("""
                SELECT COALESCE(SUM(line.Credit),0) FROM dbo.AccountingEntries entry
                JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
                JOIN purchasing.GoodsReceiptCostDocuments association ON association.GoodsReceiptId=entry.SourceDocumentId
                JOIN dbo.AccountingEntryLines original ON original.EntryId=association.SourceEntryId AND original.LineNumber=1
                WHERE entry.SourceDocumentId=@Id AND line.AccountId=original.AccountId
                """, receipt.DocumentId));
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostDocument'", association.AssociationId));
            var detail = (await client.GetFromJsonAsync<GoodsReceiptDetail>($"/api/commerce/v1/goods-receipts/{receipt.DocumentId}"))!;
            Assert.Equal(voucher.EntryNumber, Assert.Single(detail.ReferencedCostDocuments!).EntryNumber);
            Assert.Empty(detail.AdditionalCostDocuments ?? []);
            var relabeled = receipt with { ReferencedCostDocuments = receipt.ReferencedCostDocuments!
                .Select(item => item with { EntryNumber = "Etiqueta de presentación" }).ToArray() };
            using var replay = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", relabeled, receipt.DocumentId.ToString("N"));
            Assert.True((await replay.Content.ReadFromJsonAsync<GoodsReceiptAcceptance>())!.IdempotentReplay);
        }
        Assert.Equal(5_000m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'Expense'", expense.ExpenseId));
        voucher = (await client.GetFromJsonAsync<GoodsReceiptCostVoucher>($"/api/commerce/v1/goods-receipts/cost-vouchers/{entryId}"))!;
        Assert.Equal(distribute ? 5_000m : 0m, voucher.Lines.Single(line => line.LineNumber == cost.LineNumber).AssignedFunctionalAmount);
        if (distribute)
        {
            var excessive = requests[0] with { DocumentId = Guid.NewGuid(), SupplierInvoiceNumber = $"EXCESO-{Guid.NewGuid():N}",
                ReferencedCostDocuments = [new(Guid.NewGuid(), entryId, GoodsReceiptCostAssociationModes.Distribute,
                    [new(cost.LineNumber, 1m, PurchaseCostAllocationMethods.Value)])] };
            using var denied = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", excessive, excessive.DocumentId.ToString("N"));
            Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", excessive.DocumentId));
            using var cancelled = await client.PostAsJsonAsync($"/api/commerce/v1/expenses/{expense.ExpenseId}/cancel", new CancelExpenseRequest(Guid.NewGuid(), "Repartido"));
            Assert.Equal(HttpStatusCode.Conflict, cancelled.StatusCode);
            Assert.Contains("distribuidos", await cancelled.Content.ReadAsStringAsync());
            using var paymentClient = fixture.CreateAdminClient(PayablesPermissionCodes.RegisterPayment);
            var payableId = await ScalarAsync<Guid>("SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'Expense'", expense.ExpenseId);
            var payment = new ConfirmSupplierPaymentRequest(Guid.NewGuid(), fixture.BusinessId, fixture.SupplierId,
                at.AddDays(1), "COP", "El pago no libera el presupuesto de costo",
                [new(payableId, 5_000m)], [new(SupplierPaymentMethods.Cash, 5_000m, 5_000m)]);
            using var paid = await SendAsync(paymentClient, "/api/commerce/v1/payable-payments/confirm", payment, payment.PaymentId.ToString("N"));
            Assert.True(paid.StatusCode == HttpStatusCode.Accepted, await paid.Content.ReadAsStringAsync());
            Assert.Equal(0m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'Expense'", expense.ExpenseId));
            var afterPayment = (await client.GetFromJsonAsync<GoodsReceiptCostVoucher>($"/api/commerce/v1/goods-receipts/cost-vouchers/{entryId}"))!;
            Assert.Equal(5_000m, afterPayment.Lines.Single(line => line.LineNumber == cost.LineNumber).AssignedFunctionalAmount);
            using var stillDenied = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", excessive, excessive.DocumentId.ToString("N"));
            Assert.Equal(HttpStatusCode.Conflict, stillDenied.StatusCode);
        }
    }
}
