using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class GoodsReceiptProcessingTests
{
    [Fact]
    public async Task Cost_edit_adds_an_independent_invoice_and_adjusts_the_same_receipt()
    {
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var original = await CreateIsolatedCostEditRequestAsync();
        var line = original.Lines.Single();
        original = original with
        {
            PurchaseEvidenceType = PurchaseEvidenceTypes.InternalReceiptVoucher,
            SupplierInvoiceNumber = null,
            Lines = [line with { DiscountAmount = 0m, TaxCode = "IVA-0",
                TaxRate = 0m, TaxTreatment = PurchasingTaxTreatments.NotApplicable }]
        };
        using (var message = CreateMessage(original, $"cost-add-original-{original.DocumentId:N}"))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Completed", (await ReadJobAsync(original.DocumentId)).Status);
        var before = (await client.GetFromJsonAsync<GoodsReceiptDetail>(
            $"/api/commerce/v1/goods-receipts/{original.DocumentId:D}"))!;
        var invoiceId = Guid.NewGuid();
        var invoice = new GoodsReceiptCostDocumentRequest(invoiceId, fixture.SupplierId,
            PurchaseEvidenceTypes.SupplierElectronicInvoice, $"FLETE-{invoiceId:N}",
            original.ReceivedAt.AddDays(2), true, original.ReceivedAt.AddDays(30),
            "COP", 1m, DateOnly.FromDateTime(original.ReceivedAt.AddDays(2).Date),
            "FunctionalCurrency", [new GoodsReceiptCostLineRequest(
                1, PurchaseCostKinds.Freight, "Flete adicional", 2_000m, 0m,
                "IVA-0", 0m, 0m, PurchasingTaxTreatments.NotApplicable,
                PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Value)]);
        var draftId = Guid.NewGuid();
        var draftRequest = new SaveGoodsReceiptDraftRequest(draftId, fixture.BusinessId,
            original.WarehouseId, original.SupplierId, null, original.SupplierInvoiceDate,
            original.ReceivedAt, true, original.DueDate, "COP", original.Notes,
            original.Lines, null, original.PurchaseEvidenceType, original.PurchaseOrderId,
            original.ExchangeRate, before.ExchangeRateDate, original.ExchangeRateSource,
            PartySiteId: original.PartySiteId, CostEditOfReceiptId: original.DocumentId,
            BaseCostRevisionNumber: 0, AdditionalCostDocuments: [invoice]);
        using var saved = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{draftId:D}", draftRequest);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var draft = (await saved.Content.ReadFromJsonAsync<GoodsReceiptDraft>())!;
        var revisionId = Guid.NewGuid();
        using (var message = CreateMessage(original with
        {
            DocumentId = revisionId, CurrencyCode = "COP",
            AdditionalCostDocuments = [invoice],
            ExchangeRateDate = before.ExchangeRateDate,
            DraftConcurrencyToken = draft.ConcurrencyToken,
            CostEditOfReceiptId = original.DocumentId, CostEditDraftId = draftId,
            BaseCostRevisionNumber = 0
        }, $"cost-add-invoice-{revisionId:N}"))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Completed", await ScalarAsync<string>(
            "SELECT CONCAT(Status,CASE WHEN Status=N'Completed' THEN N'' ELSE N': '+COALESCE(LastError,N'') END) FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id AND DocumentType=N'GoodsReceiptCostRevision'",
            revisionId));
        Assert.Equal("Posted", await ScalarAsync<string>(
            "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostRevision'",
            revisionId));
        string invoicePosting = "Pending::";
        for (var attempt = 0; attempt < 100; attempt++)
        {
            invoicePosting = await ScalarAsync<string>(
                "SELECT CONCAT(Status,N':',COALESCE(LastErrorCode,N''),N':',COALESCE(LastErrorMessage,N'')) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostDocument'",
                invoiceId);
            if (invoicePosting != "Pending::") break;
            await Task.Delay(200);
        }
        Assert.Equal("Posted::", invoicePosting);
        var after = (await client.GetFromJsonAsync<GoodsReceiptDetail>(
            $"/api/commerce/v1/goods-receipts/{original.DocumentId:D}"))!;
        Assert.Equal(invoiceId, Assert.Single(after.AdditionalCostDocuments ?? []).CostDocumentId);
        Assert.Equal(before.Lines.Single().RecognizedInventoryCostAmount + 2_000m,
            after.Lines.Single().RecognizedInventoryCostAmount);
        Assert.Equal(2_000m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostDocument'",
            invoiceId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", original.DocumentId));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", revisionId));
    }

    [Fact]
    public async Task Cost_edit_removes_invoice_from_cost_without_cancelling_its_payable()
    {
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var original = await CreateIsolatedCostEditRequestAsync();
        var line = original.Lines.Single();
        var invoiceId = Guid.NewGuid();
        original = original with
        {
            PurchaseEvidenceType = PurchaseEvidenceTypes.InternalReceiptVoucher,
            SupplierInvoiceNumber = null,
            Lines = [line with { DiscountAmount = 0m, TaxCode = "IVA-0",
                TaxRate = 0m, TaxTreatment = PurchasingTaxTreatments.NotApplicable }],
            AdditionalCostDocuments = [new GoodsReceiptCostDocumentRequest(
                invoiceId, fixture.SupplierId, PurchaseEvidenceTypes.SupplierElectronicInvoice,
                $"FLETE-{invoiceId:N}", original.ReceivedAt, true,
                original.ReceivedAt.AddDays(30), "COP", 1m,
                DateOnly.FromDateTime(original.ReceivedAt.Date), "FunctionalCurrency",
                [new GoodsReceiptCostLineRequest(1, PurchaseCostKinds.Freight,
                    "Flete asociado a mercancía", 2_000m, 0m, "IVA-0", 0m, 0m,
                    PurchasingTaxTreatments.NotApplicable,
                    PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Value)])]
        };
        using (var message = CreateMessage(original, $"cost-invoice-original-{original.DocumentId:N}"))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Completed", (await ReadJobAsync(original.DocumentId)).Status);
        var before = (await client.GetFromJsonAsync<GoodsReceiptDetail>(
            $"/api/commerce/v1/goods-receipts/{original.DocumentId:D}"))!;
        Assert.Single(before.AdditionalCostDocuments ?? []);
        var invoicePayable = await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostDocument'",
            invoiceId);
        var draftId = Guid.NewGuid();
        var draftRequest = new SaveGoodsReceiptDraftRequest(draftId, fixture.BusinessId,
            original.WarehouseId, original.SupplierId, null, original.SupplierInvoiceDate,
            original.ReceivedAt, true, original.DueDate, "COP", original.Notes,
            original.Lines, null, original.PurchaseEvidenceType, original.PurchaseOrderId,
            original.ExchangeRate, before.ExchangeRateDate, original.ExchangeRateSource,
            PartySiteId: original.PartySiteId, CostEditOfReceiptId: original.DocumentId,
            BaseCostRevisionNumber: 0);
        using var saved = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{draftId:D}", draftRequest);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var draft = (await saved.Content.ReadFromJsonAsync<GoodsReceiptDraft>())!;
        var revisionId = Guid.NewGuid();
        using (var message = CreateMessage(original with
        {
            DocumentId = revisionId, CurrencyCode = "COP", AdditionalCostDocuments = null,
            ExchangeRateDate = before.ExchangeRateDate,
            DraftConcurrencyToken = draft.ConcurrencyToken,
            CostEditOfReceiptId = original.DocumentId, CostEditDraftId = draftId,
            BaseCostRevisionNumber = 0
        }, $"cost-invoice-remove-{revisionId:N}"))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Completed", await ScalarAsync<string>(
            "SELECT CONCAT(Status,CASE WHEN Status=N'Completed' THEN N'' ELSE N': '+COALESCE(LastError,N'') END) FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id AND DocumentType=N'GoodsReceiptCostRevision'",
            revisionId));
        Assert.Equal("Posted", await ScalarAsync<string>(
            "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostRevision'",
            revisionId));
        var after = (await client.GetFromJsonAsync<GoodsReceiptDetail>(
            $"/api/commerce/v1/goods-receipts/{original.DocumentId:D}"))!;
        Assert.Empty(after.AdditionalCostDocuments ?? []);
        Assert.Equal(before.Lines.Single().RecognizedInventoryCostAmount - 2_000m,
            after.Lines.Single().RecognizedInventoryCostAmount);
        Assert.Equal(invoicePayable, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostDocument'",
            invoiceId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id",
            original.DocumentId));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id",
            revisionId));
    }

    [Fact]
    public async Task Cost_edit_distributes_an_existing_expense_without_creating_another_receipt_or_payable()
    {
        using var client = fixture.CreateAdminClient(
            AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            ExpensePermissionCodes.Read, ExpensePermissionCodes.Create, ExpensePermissionCodes.Configure,
            PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var account = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(
            fixture, client, "5196", "Expense", "General", "Debit");
        var concept = new SaveExpenseConceptRequest(Guid.NewGuid(), fixture.BusinessId,
            "Flete para edición de costo", account.AccountId, null, null, true);
        using (var response = await client.PutAsJsonAsync(
            $"/api/commerce/v1/expenses/concepts/{concept.ConceptId}", concept))
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var occurredAt = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var expense = new ConfirmExpenseRequest(Guid.NewGuid(), fixture.BusinessId,
            fixture.SupplierId, concept.ConceptId, null, $"FLETE-{Guid.NewGuid():N}",
            occurredAt, occurredAt.AddDays(30), "COP", "Flete compartido",
            5_000m, 0, null, null);
        using (var message = new HttpRequestMessage(HttpMethod.Post,
            "/api/commerce/v1/expenses/confirm") { Content = JsonContent.Create(expense) })
        {
            message.Headers.Add("Idempotency-Key", $"expense-cost-edit-{expense.ExpenseId:N}");
            using var response = await client.SendAsync(message);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        var entryId = await ScalarAsync<Guid>(
            "SELECT EntryId FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id AND SourceDocumentType=N'Expense'",
            expense.ExpenseId);
        var voucher = (await client.GetFromJsonAsync<GoodsReceiptCostVoucher>(
            $"/api/commerce/v1/goods-receipts/cost-vouchers/{entryId:D}"))!;
        var sourceLine = Assert.Single(voucher.Lines.Where(line => line.EligibleFunctionalAmount > 0));

        var original = await CreateIsolatedCostEditRequestAsync();
        var originalLine = original.Lines.Single();
        original = original with
        {
            PurchaseEvidenceType = PurchaseEvidenceTypes.InternalReceiptVoucher,
            SupplierInvoiceNumber = null,
            Lines = [originalLine with { DiscountAmount = 0m, TaxCode = "IVA-0",
                TaxRate = 0m, TaxTreatment = PurchasingTaxTreatments.NotApplicable }]
        };
        using (var message = CreateMessage(original, $"cost-voucher-original-{original.DocumentId:N}"))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Completed", (await ReadJobAsync(original.DocumentId)).Status);
        var before = (await client.GetFromJsonAsync<GoodsReceiptDetail>(
            $"/api/commerce/v1/goods-receipts/{original.DocumentId:D}"))!;
        var originalPayable = await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
            original.DocumentId);
        var reference = new GoodsReceiptVoucherCostRequest(Guid.NewGuid(), entryId,
            GoodsReceiptCostAssociationModes.Distribute,
            [new(sourceLine.LineNumber, 2_000m, PurchaseCostAllocationMethods.Value)],
            voucher.EntryNumber);
        var draftId = Guid.NewGuid();
        var draftRequest = new SaveGoodsReceiptDraftRequest(draftId, fixture.BusinessId,
            original.WarehouseId, original.SupplierId, null, original.SupplierInvoiceDate,
            original.ReceivedAt, true, original.DueDate, "COP", original.Notes,
            original.Lines, null, original.PurchaseEvidenceType, original.PurchaseOrderId,
            original.ExchangeRate, before.ExchangeRateDate, original.ExchangeRateSource,
            PartySiteId: original.PartySiteId, CostEditOfReceiptId: original.DocumentId,
            BaseCostRevisionNumber: 0, ReferencedCostDocuments: [reference]);
        using var saved = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{draftId:D}", draftRequest);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var draft = (await saved.Content.ReadFromJsonAsync<GoodsReceiptDraft>())!;
        var revisionId = Guid.NewGuid();
        var revision = original with
        {
            DocumentId = revisionId, CurrencyCode = "COP",
            ExchangeRateDate = before.ExchangeRateDate,
            DraftConcurrencyToken = draft.ConcurrencyToken,
            CostEditOfReceiptId = original.DocumentId, CostEditDraftId = draftId,
            BaseCostRevisionNumber = 0, ReferencedCostDocuments = [reference]
        };
        using (var message = CreateMessage(revision, $"cost-voucher-edit-{revisionId:N}"))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Completed", await ScalarAsync<string>(
            "SELECT CONCAT(Status,CASE WHEN Status=N'Completed' THEN N'' ELSE N': '+COALESCE(LastError,N'') END) FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id AND DocumentType=N'GoodsReceiptCostRevision'",
            revisionId));
        Assert.Equal("Posted", await ScalarAsync<string>(
            "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostRevision'",
            revisionId));
        Assert.Equal(originalPayable, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
            original.DocumentId));

        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", original.DocumentId));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", revisionId));
        var after = (await client.GetFromJsonAsync<GoodsReceiptDetail>(
            $"/api/commerce/v1/goods-receipts/{original.DocumentId:D}"))!;
        Assert.Equal(before.Lines.Single().RecognizedInventoryCostAmount + 2_000m,
            after.Lines.Single().RecognizedInventoryCostAmount);
        var remaining = (await client.GetFromJsonAsync<GoodsReceiptCostVoucher>(
            $"/api/commerce/v1/goods-receipts/cost-vouchers/{entryId:D}"))!;
        Assert.Equal(2_000m, Assert.Single(remaining.Lines.Where(line =>
            line.LineNumber == sourceLine.LineNumber)).AssignedFunctionalAmount);

        // Replacing an assignment in one revision may reuse only the amount released
        // by this same receipt; the voucher remains reserved for other receipts.
        var replacement = reference with { AssociationId = Guid.NewGuid() };
        var replacementDraftId = Guid.NewGuid();
        using var replacementSaved = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{replacementDraftId:D}",
            draftRequest with { DraftId = replacementDraftId,
                BaseCostRevisionNumber = 1, ReferencedCostDocuments = [replacement] });
        Assert.True(replacementSaved.IsSuccessStatusCode,
            await replacementSaved.Content.ReadAsStringAsync());
        var replacementDraft = (await replacementSaved.Content.ReadFromJsonAsync<GoodsReceiptDraft>())!;
        var replacementId = Guid.NewGuid();
        using (var message = CreateMessage(revision with
        {
            DocumentId = replacementId, CostEditDraftId = replacementDraftId,
            BaseCostRevisionNumber = 1,
            DraftConcurrencyToken = replacementDraft.ConcurrencyToken,
            ReferencedCostDocuments = [replacement]
        }, $"cost-voucher-replace-{replacementId:N}"))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Completed", await ScalarAsync<string>(
            "SELECT CONCAT(Status,CASE WHEN Status=N'Completed' THEN N'' ELSE N': '+COALESCE(LastError,N'') END) FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id AND DocumentType=N'GoodsReceiptCostRevision'",
            replacementId));
        Assert.Equal("Posted", await ScalarAsync<string>(
            "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostRevision'",
            replacementId));
        var afterReplacement = (await client.GetFromJsonAsync<GoodsReceiptCostVoucher>(
            $"/api/commerce/v1/goods-receipts/cost-vouchers/{entryId:D}"))!;
        Assert.Equal(2_000m, Assert.Single(afterReplacement.Lines.Where(line =>
            line.LineNumber == sourceLine.LineNumber)).AssignedFunctionalAmount);

        var removalDraftId = Guid.NewGuid();
        using var removalSaved = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{removalDraftId:D}",
            draftRequest with { DraftId = removalDraftId,
                BaseCostRevisionNumber = 2, ReferencedCostDocuments = null });
        Assert.True(removalSaved.IsSuccessStatusCode,
            await removalSaved.Content.ReadAsStringAsync());
        var removalDraft = (await removalSaved.Content.ReadFromJsonAsync<GoodsReceiptDraft>())!;
        var removalId = Guid.NewGuid();
        using (var message = CreateMessage(revision with
        {
            DocumentId = removalId, CostEditDraftId = removalDraftId,
            BaseCostRevisionNumber = 2,
            DraftConcurrencyToken = removalDraft.ConcurrencyToken,
            ReferencedCostDocuments = null
        }, $"cost-voucher-remove-{removalId:N}"))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Completed", await ScalarAsync<string>(
            "SELECT CONCAT(Status,CASE WHEN Status=N'Completed' THEN N'' ELSE N': '+COALESCE(LastError,N'') END) FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id AND DocumentType=N'GoodsReceiptCostRevision'",
            removalId));
        Assert.Equal("Posted", await ScalarAsync<string>(
            "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostRevision'",
            removalId));
        var restored = (await client.GetFromJsonAsync<GoodsReceiptCostVoucher>(
            $"/api/commerce/v1/goods-receipts/cost-vouchers/{entryId:D}"))!;
        Assert.Equal(0m, Assert.Single(restored.Lines.Where(line =>
            line.LineNumber == sourceLine.LineNumber)).AssignedFunctionalAmount);
        var final = (await client.GetFromJsonAsync<GoodsReceiptDetail>(
            $"/api/commerce/v1/goods-receipts/{original.DocumentId:D}"))!;
        Assert.Empty(final.ReferencedCostDocuments ?? []);
        Assert.Equal(before.Lines.Single().RecognizedInventoryCostAmount,
            final.Lines.Single().RecognizedInventoryCostAmount);
        Assert.Equal(originalPayable, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
            original.DocumentId));
        var association = new GoodsReceiptVoucherCostRequest(Guid.NewGuid(), entryId,
            GoodsReceiptCostAssociationModes.Associate, [], voucher.EntryNumber);
        var associationDraftId = Guid.NewGuid();
        using var associationSaved = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{associationDraftId:D}",
            draftRequest with { DraftId = associationDraftId,
                BaseCostRevisionNumber = 3, ReferencedCostDocuments = [association] });
        Assert.True(associationSaved.IsSuccessStatusCode,
            await associationSaved.Content.ReadAsStringAsync());
        var associationDraft = (await associationSaved.Content.ReadFromJsonAsync<GoodsReceiptDraft>())!;
        var associationRevisionId = Guid.NewGuid();
        using (var message = CreateMessage(revision with
        {
            DocumentId = associationRevisionId, CostEditDraftId = associationDraftId,
            BaseCostRevisionNumber = 3,
            DraftConcurrencyToken = associationDraft.ConcurrencyToken,
            ReferencedCostDocuments = [association]
        }, $"cost-voucher-associate-{associationRevisionId:N}"))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Completed", await ScalarAsync<string>(
            "SELECT CONCAT(Status,CASE WHEN Status=N'Completed' THEN N'' ELSE N': '+COALESCE(LastError,N'') END) FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id AND DocumentType=N'GoodsReceiptCostRevision'",
            associationRevisionId));
        Assert.Equal("CommercialEffectsApplied", await ScalarAsync<string>(
            "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostRevision'",
            associationRevisionId));
        var associated = (await client.GetFromJsonAsync<GoodsReceiptDetail>(
            $"/api/commerce/v1/goods-receipts/{original.DocumentId:D}"))!;
        Assert.Equal(GoodsReceiptCostAssociationModes.Associate,
            Assert.Single(associated.ReferencedCostDocuments ?? []).Mode);
        Assert.Equal(before.Lines.Single().RecognizedInventoryCostAmount,
            associated.Lines.Single().RecognizedInventoryCostAmount);
    }

    [Fact]
    public async Task Cost_edit_keeps_the_same_receipt_and_quantity_and_adjusts_its_payable_once()
    {
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var original = await CreateIsolatedCostEditRequestAsync();
        var line = original.Lines.Single();
        original = original with
        {
            PurchaseEvidenceType = PurchaseEvidenceTypes.InternalReceiptVoucher,
            SupplierInvoiceNumber = null,
            Lines = [line with { DiscountAmount = 0m, TaxCode = "IVA-0",
                TaxRate = 0m, TaxTreatment = PurchasingTaxTreatments.NotApplicable }]
        };
        using (var accepted = CreateMessage(original, $"cost-edit-original-{original.DocumentId:N}"))
        using (var response = await client.SendAsync(accepted))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Completed", (await ReadJobAsync(original.DocumentId)).Status);
        var before = (await client.GetFromJsonAsync<GoodsReceiptDetail>(
            $"/api/commerce/v1/goods-receipts/{original.DocumentId:D}"))!;
        var originalPayable = await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
            original.DocumentId);
        var state = (await client.GetFromJsonAsync<GoodsReceiptCostEditState>(
            $"/api/commerce/v1/goods-receipts/{original.DocumentId:D}/cost-edit"))!;
        Assert.Equal(0, state.BaseCostRevisionNumber);
        var draftId = Guid.NewGuid();
        var changedLine = original.Lines.Single() with { UnitCost = original.Lines.Single().UnitCost + 100m };
        var draftRequest = new SaveGoodsReceiptDraftRequest(draftId, fixture.BusinessId,
            original.WarehouseId, original.SupplierId, null, original.SupplierInvoiceDate,
            original.ReceivedAt, true, original.DueDate, "COP", original.Notes, [changedLine],
            null, original.PurchaseEvidenceType, original.PurchaseOrderId,
            original.ExchangeRate, before.ExchangeRateDate, original.ExchangeRateSource,
            PartySiteId: original.PartySiteId,
            CostEditOfReceiptId: original.DocumentId, BaseCostRevisionNumber: 0);
        using (var invalid = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{draftId:D}",
            draftRequest with { Lines = [changedLine with { Quantity = changedLine.Quantity + 1m }] }))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var save = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{draftId:D}", draftRequest);
        Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
        var draft = (await save.Content.ReadFromJsonAsync<GoodsReceiptDraft>())!;
        var revisionId = Guid.NewGuid();
        var change = original with
        {
            DocumentId = revisionId, CurrencyCode = "COP", Lines = [changedLine],
            ExchangeRateDate = before.ExchangeRateDate,
            DraftConcurrencyToken = draft.ConcurrencyToken,
            CostEditOfReceiptId = original.DocumentId, CostEditDraftId = draftId,
            BaseCostRevisionNumber = 0
        };
        var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        using (var activity = new System.Diagnostics.Activity("receipt-cost-edit-measurement")
            .SetIdFormat(System.Diagnostics.ActivityIdFormat.W3C).Start())
        using (var counter = new InventoryBalanceProcessingTests.CommandCounter(
            database, activity.TraceId))
        using (var message = CreateMessage(change, $"cost-edit-{revisionId:N}"))
        {
            message.Headers.Add("traceparent", activity.Id);
            var started = System.Diagnostics.Stopwatch.StartNew();
            using var response = await client.SendAsync(message);
            started.Stop();
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.InRange(counter.Count, 1, 55);
            output.WriteLine($"Edición de costos, una partida: {counter.Count} viajes SQL, {started.Elapsed.TotalMilliseconds:F1} ms.");
        }
        Assert.Equal("Completed", await ScalarAsync<string>(
            "SELECT CONCAT(Status,CASE WHEN Status=N'Completed' THEN N'' ELSE N': '+COALESCE(LastError,N'') END) FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id AND DocumentType=N'GoodsReceiptCostRevision'",
            revisionId));
        using (var replay = CreateMessage(change, $"cost-edit-{revisionId:N}"))
        using (var response = await client.SendAsync(replay))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", original.DocumentId));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", revisionId));
        Assert.Equal(before.Lines.Single().Quantity, await ScalarAsync<decimal>(
            "SELECT Quantity FROM dbo.GoodsReceiptLines WHERE GoodsReceiptId=@Id", original.DocumentId));
        Assert.Equal(changedLine.UnitCost, await ScalarAsync<decimal>(
            "SELECT UnitCost FROM dbo.GoodsReceiptLines WHERE GoodsReceiptId=@Id", original.DocumentId));
        Assert.Equal(originalPayable + changedLine.Quantity * 100m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
            original.DocumentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.GoodsReceiptCostRevisions WHERE GoodsReceiptId=@Id", original.DocumentId));
        Assert.Equal("Posted", await ScalarAsync<string>(
            "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostRevision'",
            revisionId));

        var nextState = (await client.GetFromJsonAsync<GoodsReceiptCostEditState>(
            $"/api/commerce/v1/goods-receipts/{original.DocumentId:D}/cost-edit"))!;
        Assert.Equal(1, nextState.BaseCostRevisionNumber);
        var discountDraftId = Guid.NewGuid();
        var discount = new GoodsReceiptFinancialDiscountRequest(10m, null, true);
        using var discountSave = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{discountDraftId:D}",
            draftRequest with { DraftId = discountDraftId, BaseCostRevisionNumber = 1,
                FinancialDiscount = discount });
        Assert.True(discountSave.IsSuccessStatusCode,
            await discountSave.Content.ReadAsStringAsync());
        var discountDraft = (await discountSave.Content.ReadFromJsonAsync<GoodsReceiptDraft>())!;
        var discountRevisionId = Guid.NewGuid();
        using (var discountMessage = CreateMessage(change with {
            DocumentId = discountRevisionId, CostEditDraftId = discountDraftId,
            BaseCostRevisionNumber = 1, DraftConcurrencyToken = discountDraft.ConcurrencyToken,
            FinancialDiscount = discount
        }, $"cost-edit-discount-{discountRevisionId:N}"))
        using (var discountResponse = await client.SendAsync(discountMessage))
            Assert.Equal(HttpStatusCode.Accepted, discountResponse.StatusCode);
        Assert.Equal("Completed", await ScalarAsync<string>(
            "SELECT CONCAT(Status,CASE WHEN Status=N'Completed' THEN N'' ELSE N': '+COALESCE(LastError,N'') END) FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id AND DocumentType=N'GoodsReceiptCostRevision'",
            discountRevisionId));
        var afterDiscount = (await client.GetFromJsonAsync<GoodsReceiptDetail>(
            $"/api/commerce/v1/goods-receipts/{original.DocumentId:D}"))!;
        Assert.NotNull(afterDiscount.FinancialDiscount);
        Assert.Equal(originalPayable + changedLine.Quantity * 100m -
            afterDiscount.FinancialDiscount.FunctionalAmount,
            await ScalarAsync<decimal>(
                "SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
                original.DocumentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", original.DocumentId));
    }

    private async Task<ConfirmGoodsReceiptRequest> CreateIsolatedCostEditRequestAsync()
    {
        var request = CreateRequest();
        var productId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            DECLARE @TaxId uniqueidentifier=NEWID();
            INSERT dbo.TaxProfiles(TaxProfileId,TenantId,Code,DianTaxCode,Name,Rate,IsActive,CreatedAt)
            VALUES(@TaxId,@TenantId,LEFT(CONVERT(nvarchar(36),@TaxId),32),N'01',N'IVA prueba costo',19,1,SYSDATETIMEOFFSET());
            INSERT dbo.Products(ProductId,TenantId,ProductCode,Reference,Sku,Name,Description,BaseUnitCode,
              TaxProfileId,PurchaseTaxProfileId,ManageStock,IsWeighable,IsActive,Source,Currency,CreatedAt)
            SELECT @ProductId,source.TenantId,CONVERT(nvarchar(36),@ProductId),
              CONVERT(nvarchar(36),@ProductId),CONVERT(nvarchar(36),@ProductId),
              N'Producto edición de costo',source.Description,COALESCE(source.BaseUnitCode,N'EA'),
              COALESCE(source.TaxProfileId,@TaxId),source.PurchaseTaxProfileId,
              source.ManageStock,source.IsWeighable,1,source.Source,source.Currency,SYSDATETIMEOFFSET()
            FROM dbo.Products source WHERE source.ProductId=@Parent;
            INSERT dbo.ProductPrices(ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,
              CostBasisType,CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,InputMode,
              RoundingIncrement,RoundingMode,ValidFrom,IsActive,CreatedAt)
            SELECT NEWID(),source.BusinessId,@ProductId,source.Amount,source.PreparedAmount,source.CurrencyCode,
              source.CostBasisType,source.CostBasisAmount,source.TargetMarginPercent,source.EffectiveMarginPercent,
              source.InputMode,source.RoundingIncrement,source.RoundingMode,SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET()
            FROM dbo.ProductPrices source
            WHERE source.ProductId=@Parent AND source.BusinessId=@BusinessId AND source.IsActive=1;
            INSERT dbo.SupplierProducts(SupplierProductId,ProductId,SupplierId,TenantId,IsPrimary,IsActive,CreatedAt)
            VALUES(NEWID(),@ProductId,@SupplierId,@TenantId,1,1,SYSDATETIMEOFFSET());
            """, connection);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@Parent", fixture.ProductId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@SupplierId", fixture.SupplierId);
        await command.ExecuteNonQueryAsync();
        return request with { Lines = request.Lines.Select(line => line with
            { ProductId = productId }).ToArray() };
    }
}
