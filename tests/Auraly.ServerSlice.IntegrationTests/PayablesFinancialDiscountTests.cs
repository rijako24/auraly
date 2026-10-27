using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PayablesVerticalSliceTests
{
    [Theory]
    [InlineData(false, true, 100_000, 90_000, 10_000, "421040")]
    [InlineData(true, false, 90_000, 100_000, -10_000, "53959504")]
    public async Task Manual_egress_discount_uses_the_same_payment_and_preserves_inventory(
        bool anticipated, bool earned, decimal payableReduction, decimal cash, decimal adjustment, string discountCode)
    {
        var productId = Guid.NewGuid();
        await ConfigureSeriesAndAccountingAsync(productId);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await EnsureAccountAsync(connection, transaction, "421040", "Descuentos financieros obtenidos", "Revenue", true);
            await EnsureAccountAsync(connection, transaction, "53959504", "Descuentos anticipados no obtenidos", "Expense", true);
            await EnsureMappingAsync(connection, transaction, AccountingCategories.SupplierFinancialDiscountEarned, "421040");
            await EnsureMappingAsync(connection, transaction, AccountingCategories.SupplierFinancialDiscountLost, "53959504");
            await using var classify = new SqlCommand("UPDATE dbo.AccountingAccounts SET FinancialFunction=N'General',BalanceSide=CASE WHEN Code=N'421040' THEN N'Credit' ELSE N'Debit' END WHERE TenantId=@TenantId AND Code IN(N'421040',N'53959504');", connection, transaction);
            classify.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            await classify.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        var at = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var receipt = new ConfirmGoodsReceiptRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            fixture.SupplierId, $"MAN-DISC-{Guid.NewGuid():N}", at, at, true,
            at.AddDays(30), "COP", "Compra con descuento financiero",
            [new GoodsReceiptLineRequest(1, productId, "Producto", 10m, 10_000m,
                0m, "00", 0m, PurchasingTaxTreatments.NotApplicable)])
        { FinancialDiscount = new GoodsReceiptFinancialDiscountRequest(10m, null, anticipated) };
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts, PayablesPermissionCodes.Read,
            PayablesPermissionCodes.RegisterPayment, AccountingPermissionCodes.Read,
            AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        using (var accepted = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm",
                   receipt, $"manual-discount-receipt-{receipt.DocumentId:N}"))
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var payableId = await ScalarAsync<Guid>(
            "SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id", receipt.DocumentId);
        var balanceBefore = await ScalarAsync<decimal>(
            "SELECT InventoryValue FROM dbo.InventoryBalances WHERE BusinessId=(SELECT BusinessId FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id) AND ProductId=(SELECT ProductId FROM dbo.GoodsReceiptLines WHERE GoodsReceiptId=@Id AND LineNumber=1) AND WarehouseId=(SELECT WarehouseId FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id)", receipt.DocumentId);
        var open = await client.GetFromJsonAsync<AccountingOpenPositionPage>(
            $"/api/commerce/v1/accounting/open-positions?subledgerKind=Payable&subledgerId={payableId:D}&page=1&pageSize=1&effectiveOn=2026-08-03");
        var position = Assert.Single(open!.Items);
        Assert.Equal(payableReduction, position.AvailableAmount);
        Assert.Equal(10_000m, position.FinancialDiscountEligibleAmount);
        var accounts = (await client.GetFromJsonAsync<AccountingAccountView[]>("/api/commerce/v1/accounting/accounts"))!;
        var bank = accounts.Single(item => item.Code == "111020");
        var discount = accounts.Single(item => item.Code == discountCode);
        var id = Guid.NewGuid(); var group = Guid.NewGuid();
        var lines = new List<VoucherDraftLine> {
            new(position.AccountId!.Value, fixture.SupplierPartyId, null, "Pago proveedor", payableReduction, 0m,
                LineId: Guid.NewGuid(), ExistingPosition: new("Payable", payableId), FinancialGroupId: group,
                FinancialDiscountEarned: earned),
            new(bank.AccountId, fixture.SupplierPartyId, null, "Salida banco", 0m, cash,
                Reference: "TRANSFERENCIA-1", LineId: Guid.NewGuid(), FinancialGroupId: group)
        };
        lines.Add(new(discount.AccountId, fixture.SupplierPartyId, null, "Descuento financiero",
            adjustment < 0m ? -adjustment : 0m, adjustment > 0m ? adjustment : 0m,
            LineId: Guid.NewGuid(), FinancialGroupId: group));
        var draft = new SaveVoucherDraftRequest(id, AccountingManualDocumentTypes.ManualVoucher, at.AddDays(1),
            "MANUAL_VOUCHER", "Egreso con descuento", "TRANSFERENCIA-1", lines, null, null,
            MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "Disbursement");
        var path = $"/api/commerce/v1/accounting/manual/drafts/{id:D}";
        using var savedResponse = await client.PutAsJsonAsync(path, draft);
        Assert.True(savedResponse.IsSuccessStatusCode, await savedResponse.Content.ReadAsStringAsync());
        var saved = (await savedResponse.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        using var sent = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion));
        Assert.True(sent.IsSuccessStatusCode, await sent.Content.ReadAsStringAsync());
        using var replay = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion));
        Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        var copyId = Guid.NewGuid();
        using var cloned = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/documents/{id:D}/clone",
            new CloneVoucherDraftRequest(copyId, at.AddDays(2)));
        Assert.True(cloned.IsSuccessStatusCode, await cloned.Content.ReadAsStringAsync());
        var copy = (await cloned.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        Assert.All(copy.Lines, line => { Assert.Null(line.Value.FinancialDiscountEarned);
            Assert.Null(line.Value.ExistingPosition); Assert.Null(line.Value.FinancialGroupId); });
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        Assert.Equal(adjustment, await ScalarAsync<decimal>(
            "SELECT FinancialDiscountAdjustment FROM dbo.SupplierPaymentApplications application JOIN dbo.SupplierPayments payment ON payment.PaymentId=application.PaymentId WHERE payment.ParentSourceDocumentId=@Id", id));
        Assert.Equal(cash, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.PayableTransactions WHERE SourceDocumentId=(SELECT PaymentId FROM dbo.SupplierPayments WHERE ParentSourceDocumentId=@Id) AND TransactionType=N'Payment'", id));
        Assert.Equal(payableReduction, await AccountAmountAsync(id, "220505", true));
        Assert.Equal(cash, await AccountAmountAsync(id, "111020", false));
        Assert.Equal(10_000m, await AccountAmountAsync(id, discountCode, !earned));
        Assert.Equal(balanceBefore, await ScalarAsync<decimal>(
            "SELECT InventoryValue FROM dbo.InventoryBalances WHERE BusinessId=(SELECT BusinessId FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id) AND ProductId=(SELECT ProductId FROM dbo.GoodsReceiptLines WHERE GoodsReceiptId=@Id AND LineNumber=1) AND WarehouseId=(SELECT WarehouseId FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id)", receipt.DocumentId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id", id));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SupplierPayments WHERE ParentSourceDocumentId=@Id", id));
    }

    [Fact]
    public async Task Unavailable_financial_discount_rejects_the_payment_without_any_payment_effect()
    {
        var productId = Guid.NewGuid();
        await ConfigureSeriesAndAccountingAsync(productId);
        var occurredAt = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.FromHours(-5));
        var receipt = new ConfirmGoodsReceiptRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            fixture.SupplierId, $"NO-DISC-{Guid.NewGuid():N}", occurredAt, occurredAt, true,
            occurredAt.AddDays(30), "COP", "Compra sin descuento financiero",
            [new GoodsReceiptLineRequest(1, productId, "Producto", 10m, 10_000m,
                0m, "00", 0m, PurchasingTaxTreatments.NotApplicable)]);
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts, PayablesPermissionCodes.Read,
            PayablesPermissionCodes.RegisterPayment);
        using (var accepted = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm",
                   receipt, $"no-discount-receipt-{receipt.DocumentId:N}"))
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var payableId = await ScalarAsync<Guid>(
            "SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id", receipt.DocumentId);
        var payment = new ConfirmSupplierPaymentRequest(Guid.NewGuid(), fixture.BusinessId,
            fixture.SupplierId, occurredAt.AddDays(1), "COP", "Descuento no pactado",
            [new SupplierPaymentAllocationRequest(payableId, 100_000m, true)],
            [new SupplierPaymentTenderRequest(SupplierPaymentMethods.Cash, 100_000m, 100_000m)]);
        using var rejected = await SendAsync(client, "/api/commerce/v1/payable-payments/confirm",
            payment, $"no-discount-payment-{payment.PaymentId:N}");
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("El pago no se realizó", await rejected.Content.ReadAsStringAsync());
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.SupplierPayments WHERE PaymentId=@Id", payment.PaymentId));
        Assert.Equal(100_000m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
    }

    [Theory]
    [InlineData(false, true, 90_000, 10_000, "421040")]
    [InlineData(true, false, 100_000, -10_000, "53959504")]
    public async Task Financial_discount_decision_uses_the_existing_payment_and_does_not_revalue_inventory(
        bool anticipated, bool earned, decimal cash, decimal adjustment, string discountAccount)
    {
        var productId = Guid.NewGuid();
        await ConfigureSeriesAndAccountingAsync(productId);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await EnsureAccountAsync(connection, transaction, "421040", "Descuentos financieros obtenidos", "Revenue", true);
            await EnsureAccountAsync(connection, transaction, "53959504", "Descuentos anticipados no obtenidos", "Expense", true);
            await EnsureMappingAsync(connection, transaction, AccountingCategories.SupplierFinancialDiscountEarned, "421040");
            await EnsureMappingAsync(connection, transaction, AccountingCategories.SupplierFinancialDiscountLost, "53959504");
            await transaction.CommitAsync();
        }
        var occurredAt = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var receipt = new ConfirmGoodsReceiptRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            fixture.SupplierId, $"DISC-{Guid.NewGuid():N}", occurredAt, occurredAt, true,
            occurredAt.AddDays(30), "COP", "Compra con descuento financiero",
            [new GoodsReceiptLineRequest(1, productId, "Producto", 10m, 10_000m,
                0m, "00", 0m, PurchasingTaxTreatments.NotApplicable)])
        { FinancialDiscount = new GoodsReceiptFinancialDiscountRequest(10m, new DateOnly(2020, 1, 1), anticipated) };
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts, PayablesPermissionCodes.Read,
            PayablesPermissionCodes.RegisterPayment,
            AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate,
            PurchasingPermissionCodes.CreatePurchaseReturns,
            PurchasingPermissionCodes.ConfirmPurchaseReturns);
        using (var accepted = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm",
                   receipt, $"discount-receipt-{receipt.DocumentId:N}"))
            Assert.True(accepted.StatusCode == HttpStatusCode.Accepted,
                $"Receipt: {(int)accepted.StatusCode} {await accepted.Content.ReadAsStringAsync()}");
        var payableId = await ScalarAsync<Guid>(
            "SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id", receipt.DocumentId);
        var originalBalance = await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId);
        Assert.Equal(anticipated ? 90_000m : 100_000m, originalBalance);
        var inventoryBefore = await ScalarAsync<decimal>(
            "SELECT InventoryValue FROM dbo.InventoryBalances WHERE BusinessId=(SELECT BusinessId FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id) AND ProductId=(SELECT ProductId FROM dbo.GoodsReceiptLines WHERE GoodsReceiptId=@Id AND LineNumber=1) AND WarehouseId=(SELECT WarehouseId FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id)", receipt.DocumentId);
        var payment = new ConfirmSupplierPaymentRequest(Guid.NewGuid(), fixture.BusinessId,
            fixture.SupplierId, occurredAt.AddDays(40), "COP", "Decisión expresa del descuento",
            [new SupplierPaymentAllocationRequest(payableId, cash, earned)],
            [new SupplierPaymentTenderRequest(SupplierPaymentMethods.Cash, cash, cash)]);
        var key = $"discount-payment-{payment.PaymentId:N}";
        using (var accepted = await SendAsync(client, "/api/commerce/v1/payable-payments/confirm", payment, key))
            Assert.True(accepted.StatusCode == HttpStatusCode.Accepted,
                $"Payment: {(int)accepted.StatusCode} {await accepted.Content.ReadAsStringAsync()}");
        Assert.Equal("Processed", await ScalarAsync<string>(
            "SELECT Status FROM dbo.SupplierPayments WHERE PaymentId=@Id", payment.PaymentId));
        Assert.Equal(0m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        Assert.Equal(adjustment, await ScalarAsync<decimal>(
            "SELECT FinancialDiscountAdjustment FROM dbo.SupplierPaymentApplications WHERE PaymentId=@Id", payment.PaymentId));
        Assert.Equal(cash, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.PayableTransactions WHERE SourceDocumentId=@Id AND TransactionType=N'Payment'", payment.PaymentId));
        Assert.Equal(-adjustment, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.PayableTransactions WHERE SourceDocumentId=@Id AND TransactionType=N'Adjustment'", payment.PaymentId));
        Assert.Equal(originalBalance, await AccountAmountAsync(payment.PaymentId, "220505", true));
        Assert.Equal(cash, await AccountAmountAsync(payment.PaymentId, "110505", false));
        Assert.Equal(10_000m, await AccountAmountAsync(payment.PaymentId, discountAccount, !earned));
        Assert.Equal(anticipated ? 10_000m : 9_000m, await ScalarAsync<decimal>(
            "SELECT CostBasisAmount FROM dbo.ProductPricePreparations WHERE SourceDocumentId=@Id AND PreparationOrigin=N'SupplierDiscount' AND Status=N'Pending'", receipt.DocumentId));
        Assert.Equal(10_000m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=(SELECT ProductId FROM dbo.GoodsReceiptLines WHERE GoodsReceiptId=@Id AND LineNumber=1) AND IsActive=1", receipt.DocumentId));
        Assert.Equal(inventoryBefore, await ScalarAsync<decimal>(
            "SELECT InventoryValue FROM dbo.InventoryBalances WHERE BusinessId=(SELECT BusinessId FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id) AND ProductId=(SELECT ProductId FROM dbo.GoodsReceiptLines WHERE GoodsReceiptId=@Id AND LineNumber=1) AND WarehouseId=(SELECT WarehouseId FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id)", receipt.DocumentId));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id", payment.PaymentId));
        var paymentTransactionId = await ScalarAsync<Guid>(
            "SELECT PayableTransactionId FROM dbo.PayableTransactions WHERE SourceDocumentId=@Id AND TransactionType=N'Payment'", payment.PaymentId);
        using (var rejectedUnapplication = await client.GetAsync(
                   $"/api/commerce/v1/accounting/unapplication-capture?subledgerKind=Payable&subledgerId={payableId:D}&transactionId={paymentTransactionId:D}"))
        {
            Assert.Equal(HttpStatusCode.Conflict, rejectedUnapplication.StatusCode);
            Assert.Contains("No se desaplicó el pago", await rejectedUnapplication.Content.ReadAsStringAsync());
        }
        using var replay = await SendAsync(client, "/api/commerce/v1/payable-payments/confirm", payment, key);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<SupplierPaymentAcceptance>())!.IdempotentReplay);

        var returned = new ConfirmPurchaseReturnRequest(Guid.NewGuid(), fixture.BusinessId,
            receipt.DocumentId, occurredAt.AddDays(41), "QualityIssue", "Devolución posterior al pago",
            [new PurchaseReturnLineRequest(1, 10m)]);
        using (var accepted = await SendAsync(client, "/api/commerce/v1/purchase-returns/confirm",
                   returned, $"discount-return-{returned.ReturnId:N}"))
            Assert.True(accepted.StatusCode == HttpStatusCode.Accepted,
                $"Return: {(int)accepted.StatusCode} {await accepted.Content.ReadAsStringAsync()}");
        Assert.Equal("Processed", await ScalarAsync<string>(
            "SELECT Status FROM dbo.PurchaseReturns WHERE PurchaseReturnId=@Id", returned.ReturnId));
        Assert.Equal(cash, await ScalarAsync<decimal>(
            "SELECT AvailableAmount FROM dbo.SupplierCredits WHERE SourcePurchaseReturnId=@Id", returned.ReturnId));
        Assert.Equal(10_000m, await AccountAmountAsync(returned.ReturnId, discountAccount, earned));
    }
}
