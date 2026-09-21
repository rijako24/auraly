using System.Net;
using System.Net.Http.Json;
using Auraly.Application.Sales;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Auraly.Application.WorkSessions;
using Auraly.Fiscal.Core;
using Auraly.Pos.Printing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Fact]
    public async Task Thirty_invoice_charge_scenarios_reconcile_accounting_payables_payments_and_closure()
    {
        var cashierId = Guid.NewGuid();
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = new SqlCommand("""
                INSERT dbo.AppUsers(UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
                  FirstName,LastName,IsActive,CreatedAt)
                VALUES(@Id,@TenantId,@Name,UPPER(@Name),@Email,UPPER(@Email),N'Cajera',N'Matriz',1,SYSUTCDATETIME());
                INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
                VALUES(NEWID(),@Id,@RoleId,@BusinessId,SYSUTCDATETIME());
                IF NOT EXISTS(SELECT 1 FROM dbo.DocumentSeries WHERE BusinessId=@BusinessId AND DocumentType=N'PayablePayment' AND IsActive=1)
                  INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
                    Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                  VALUES(NEWID(),@BusinessId,NULL,N'PayablePayment',N'PGP',N'00',8,1,99999999,0,1,SYSDATETIMEOFFSET());
                IF NOT EXISTS(SELECT 1 FROM dbo.DocumentSeries WHERE BusinessId=@BusinessId AND DocumentType=N'ReceivablePayment' AND IsActive=1)
                  INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
                    Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                  VALUES(NEWID(),@BusinessId,NULL,N'ReceivablePayment',N'RCC',N'00',8,1,99999999,0,1,SYSDATETIMEOFFSET());
                """, connection);
            seed.Parameters.AddWithValue("@Id", cashierId);
            seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@RoleId", fixture.RoleId);
            seed.Parameters.AddWithValue("@Name", $"matrix-{cashierId:N}");
            seed.Parameters.AddWithValue("@Email", $"matrix-{cashierId:N}@test.local");
            await seed.ExecuteNonQueryAsync();
        }
        using var admin = fixture.CreateUserClient(cashierId, AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, InvoiceChargePermissions.Read, InvoiceChargePermissions.Configure,
            ExpensePermissionCodes.Read, ExpensePermissionCodes.Configure, ExpensePermissionCodes.Create,
            CatalogPermissionCodes.Update, PayablesPermissionCodes.Read, PayablesPermissionCodes.RegisterPayment,
            ReceivablesPermissionCodes.Read, ReceivablesPermissionCodes.RegisterPayment,
            SalesReturnPermissionCodes.Read, SalesReturnPermissionCodes.Create, SalesReturnPermissionCodes.Confirm,
            WorkSessionPermissionCodes.Read, WorkSessionPermissionCodes.Close, WorkSessionPermissionCodes.ReadCashDifferences);
        using (var defaults = await admin.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activate = await admin.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
                   new ActivateAccountingRequest(new DateOnly(2026, 1, 1), "COP", "ZeroDeclared"))) activate.EnsureSuccessStatusCode();
        var bankAccountId = await CreatePrimaryBankAccountAsync(admin);
        using var openResponse = await admin.PostAsJsonAsync("/api/commerce/v1/work-sessions/current",
            new OpenWorkSessionRequest(fixture.BusinessId, fixture.WarehouseId, null));
        Assert.True(openResponse.IsSuccessStatusCode, await openResponse.Content.ReadAsStringAsync());
        var sessionId = (await openResponse.Content.ReadFromJsonAsync<WorkSessionView>())!.WorkSessionId;
        await SetWarehouseNegativeSalesPolicyAsync(true);
        var options = (await admin.GetFromJsonAsync<ExpenseWorkspaceOptions>("/api/commerce/v1/expenses/options"))!;
        var expenseAccount = options.ExpenseAccounts.Single(account => account.Code == "519595");
        var conceptId = Guid.NewGuid();
        using (var concept = await admin.PutAsJsonAsync($"/api/commerce/v1/expenses/concepts/{conceptId}",
                   new SaveExpenseConceptRequest(conceptId, fixture.BusinessId, "Domicilio matriz",
                       expenseAccount.AccountId, options.CostCenters.First(center => center.IsDefault).CostCenterId, null, true)))
            concept.EnsureSuccessStatusCode();
        var code = $"MATRIX-{Guid.NewGuid():N}"[..26];
        using var taxResponse = await admin.PostAsJsonAsync("/api/commerce/v1/tax-profiles",
            new SaveTaxProfileRequest(fixture.BusinessId, code, "Sin IVA matriz", 0));
        taxResponse.EnsureSuccessStatusCode();
        var tax = (await taxResponse.Content.ReadFromJsonAsync<TaxProfileSummary>())!;
        async Task<InvoiceChargeDefinition> ConfigureCharge(string suffix, bool manual)
        {
            var chargeId = Guid.NewGuid();
            var request = new SaveInvoiceChargeRequest(chargeId, 0, code + suffix, manual ? "Agotados matriz" : "Domicilio matriz",
                true, manual ? 1 : 0, manual ? "Manual" : "Ranges", manual ? 5000m : null,
                manual ? "Never" : "UpToInvoiceAmount", manual ? null : 80000m, conceptId, tax.TaxProfileId,
                manual ? [] : [new(0,100000,"Fixed",5000),new(100000,200000,"Fixed",6000),
                    new(200000,300000,"Fixed",7000),new(300000,400000,"Fixed",8000),new(400000,null,"Percentage",2)],
                [fixture.SupplierId], tax.TaxProfileId);
            using var response = await admin.PutAsJsonAsync($"/api/commerce/v1/invoice-charges/{chargeId}", request);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<InvoiceChargeDefinition>())!;
        }
        var delivery = await ConfigureCharge("-D", false);
        var exhausted = await ConfigureCharge("-A", true);
        var (customerId, siteId) = await CreateCustomerAsync();
        using var pos = fixture.CreateClient();
        var baseline = (await admin.GetFromJsonAsync<WorkSessionClosurePreviewView>(
            $"/api/commerce/v1/work-sessions/{sessionId}/closure-preview"))!;
        var issued = new List<PosSaleUploadRequest>();
        var cases = new[] { 79999m, 80000m, 80000.01m, 100000m, 400000m };
        var sequence = 9700L;
        foreach (var productTotal in cases)
        foreach (var chargeType in new[] { "None", "Delivery", "Manual" })
        foreach (var mixed in new[] { false, true })
        {
            var charge = chargeType == "None" ? null : InvoiceChargeApplication.Calculate(productTotal,
                new InvoiceChargeSelection(Guid.NewGuid(), chargeType == "Delivery" ? delivery : exhausted,
                    fixture.SupplierId, chargeType == "Manual" ? 6500m : null));
            var sale = CreateChargeMatrixSale(sequence++, productTotal, charge, mixed, customerId, siteId, bankAccountId) with { SoldByUserId = cashierId, WorkSessionId = sessionId };
            using (var message = fixture.CreateUploadMessage(sale))
            using (var response = await pos.SendAsync(message))
                Assert.True(response.IsSuccessStatusCode, $"{productTotal}/{chargeType}/{mixed}: {await response.Content.ReadAsStringAsync()}");
            using (var message = fixture.CreateUploadMessage(sale))
            using (var replay = await pos.SendAsync(message))
            {
                replay.EnsureSuccessStatusCode();
                Assert.True((await replay.Content.ReadFromJsonAsync<PosSaleUploadResponse>())!.IsDuplicate);
            }
            await AssertBalancedAsync(sale.DocumentId);
            Assert.Equal(1, await CountAsync("AccountingEntries", "SourceDocumentId", sale.DocumentId));
            Assert.Equal(sale.CommercialSnapshot.PayableAmount -
                sale.CommercialSnapshot.PayableRoundingAmount, await ScalarAsync<decimal>(
                "SELECT SUM(TotalAmount) FROM reporting.SalesReportTaxFacts WHERE SourceDocumentId=@Id", sale.DocumentId));
            Assert.Equal(sale.CommercialSnapshot.PayableAmount, await ScalarAsync<decimal>(
                "SELECT TotalAmount FROM reporting.SalesReportDocuments WHERE DocumentId=@Id", sale.DocumentId));
            Assert.Equal(sale.Credit?.Amount ?? 0, await ScalarAsync<decimal>(
                "SELECT COALESCE(SUM(OriginalAmount),0) FROM dbo.Receivables WHERE SourceDocumentId=@Id", sale.DocumentId));
            if (charge is not null)
            {
                Assert.Equal(chargeType == "Delivery" && productTotal <= 80000 ? charge.Amount : 0, charge.InvoicedAmount);
                Assert.Equal(chargeType == "Manual" ? 6500m : productTotal < 100000 ? 5000m : productTotal < 400000 ? 6000m : 8000m, charge.Amount);
                await AssertBalancedAsync(charge.AppliedChargeId);
                Assert.Equal(charge.Amount, await AccountAmountAsync(charge.AppliedChargeId, "519595", debit: true));
                Assert.Equal(charge.Amount, await ScalarAsync<decimal>(
                    "SELECT OriginalAmount FROM dbo.Payables WHERE SourceDocumentId=@Id", charge.AppliedChargeId));
                Assert.Equal(0, await CountAsync("DocumentProcessingJobs", "DocumentId", charge.AppliedChargeId));
                Assert.Equal(1, await CountAsync("AccountingEntries", "SourceDocumentId", charge.AppliedChargeId));
            }
            issued.Add(sale);
        }
        Assert.Equal(30, issued.Count);
        Assert.Equal(issued.Sum(sale => sale.CommercialSnapshot.PayableAmount), await ScalarAsync<decimal>("""
            SELECT SUM(NetTotalSales) FROM reporting.SalesReportDailyDimensionTotals
            WHERE DimensionType=N'Customer' AND DimensionKey=CONVERT(nvarchar(80),@Id);
            """, customerId));
        var longestProcessingMilliseconds = await ScalarAsync<long>("""
            SELECT MAX(DATEDIFF_BIG(millisecond,job.StartedAt,job.CompletedAt))
            FROM dbo.DocumentProcessingJobs job JOIN dbo.SalesDocuments sale ON sale.DocumentId=job.DocumentId
            WHERE sale.WorkSessionId=@Id;
            """, sessionId);
        Assert.True(longestProcessingMilliseconds < 2000,
            $"El procesamiento documental de la matriz tardó hasta {longestProcessingMilliseconds} ms.");
        var preview = (await admin.GetFromJsonAsync<WorkSessionClosurePreviewView>(
            $"/api/commerce/v1/work-sessions/{sessionId}/closure-preview"))!;
        var expectedCharges = issued.SelectMany(sale => sale.Charges ?? []).ToArray();
        Assert.Equal(20, expectedCharges.Length);
        var matrixCharges = (preview.InvoiceCharges ?? []).Where(charge => expectedCharges.Any(expected => expected.AppliedChargeId == charge.AppliedChargeId)).ToArray();
        Assert.Equal(20, matrixCharges.Length);
        Assert.Equal(baseline.ExpectedCash + issued.SelectMany(sale => sale.Payments)
            .Where(payment => payment.MethodCode == "Cash").Sum(payment => payment.CollectedAmount), preview.ExpectedCash);
        Assert.Equal(expectedCharges.Sum(charge => charge.InvoicedAmount), matrixCharges.Sum(charge => charge.InvoicedAmount));
        Assert.Equal(expectedCharges.Sum(charge => charge.ExpenseAmount), matrixCharges.Sum(charge => charge.ExpenseAmount));
        Assert.Equal(matrixCharges.Sum(charge => charge.InvoicedAmount), matrixCharges.SelectMany(charge => charge.Payments).Sum(payment => payment.Amount));

        // Paying the carrier settles the existing obligation; it does not expense the charge again.
        var firstCharge = expectedCharges.First(charge => issued.Any(sale =>
            sale.Credit is not null && sale.Charges?.Any(candidate =>
                candidate.AppliedChargeId == charge.AppliedChargeId) == true));
        var payableId = await ScalarAsync<Guid>("SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id", firstCharge.AppliedChargeId);
        var payment = new ConfirmSupplierPaymentRequest(Guid.NewGuid(), fixture.BusinessId, fixture.SupplierId,
            issued[0].CommercialSnapshot.IssuedAt.AddHours(1), "COP", SupplierPaymentMethods.Cash, null,
            "Pago domiciliario matriz", [new(payableId, firstCharge.Amount)], sessionId);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/payable-payments/confirm") { Content = JsonContent.Create(payment) };
            message.Headers.Add("Idempotency-Key", payment.PaymentId.ToString("N"));
            using var response = await admin.SendAsync(message);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        await AssertBalancedAsync(payment.PaymentId);
        Assert.Equal(0, await AccountAmountAsync(payment.PaymentId, "519595", debit: true));
        Assert.Equal(0, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        var afterPayment = (await admin.GetFromJsonAsync<WorkSessionClosurePreviewView>(
            $"/api/commerce/v1/work-sessions/{sessionId}/closure-preview"))!;
        Assert.Equal(preview.ExpectedCash - firstCharge.Amount, afterPayment.ExpectedCash);
        var supplierExit = Assert.Single(afterPayment.CashMovements!, item => item.DocumentId == payment.PaymentId);
        Assert.Equal("Pago a proveedor", supplierExit.ReasonName);
        Assert.Equal(payment.Notes, supplierExit.Notes);
        Assert.Equal(firstCharge.Amount, supplierExit.Amount);

        // Returning a paid charge reverses its expense and creates a supplier credit.
        var chargedSale = issued.Single(sale =>
            sale.Charges?.Any(charge => charge.AppliedChargeId == firstCharge.AppliedChargeId) == true);
        var returnId = Guid.NewGuid();
        var chargeReturn = new ConfirmSalesReturnRequest(
            returnId, fixture.BusinessId, fixture.WarehouseId, chargedSale.DocumentId,
            chargedSale.CommercialSnapshot.IssuedAt.AddHours(2),
            ReturnEconomicResolutions.CustomerCredit, null, "Devolución con cargo de facturación",
            [new ConfirmSalesReturnLineRequest(1, .5m, ReturnInventoryDispositions.Sellable)],
            ReasonCode: "Other", ReturnedChargeIds: [firstCharge.AppliedChargeId]);
        using (var message = new HttpRequestMessage(
                   HttpMethod.Post, "/api/commerce/v1/sales-returns/confirm")
               { Content = JsonContent.Create(chargeReturn) })
        {
            message.Headers.Add("Idempotency-Key", returnId.ToString("N"));
            using var response = await admin.SendAsync(message);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        await AssertBalancedAsync(returnId);
        Assert.Equal(firstCharge.Amount, await AccountAmountAsync(
            returnId, "519595", debit: false));
        Assert.Equal(firstCharge.Amount, await ScalarAsync<decimal>("""
            SELECT SupplierCreditAmount FROM dbo.SalesReturnChargeFinancialEffects
            WHERE ReturnId=@Id;
            """, returnId));
        Assert.Equal(firstCharge.Amount, await ScalarAsync<decimal>("""
            SELECT OriginalAmount FROM dbo.SupplierCredits
            WHERE SourceDocumentId=@Id AND SourceDocumentType=N'SalesReturnCharge';
            """, firstCharge.AppliedChargeId));
        Assert.Equal(0, await ScalarAsync<decimal>("""
            SELECT PayableCreditAmount FROM dbo.SalesReturnChargeFinancialEffects
            WHERE ReturnId=@Id;
            """, returnId));

        // A company-paid charge with an open payable is cancelled instead of creating credit.
        var companyCharge = expectedCharges.First(charge => charge.ExpenseAmount > 0 &&
            charge.AppliedChargeId != firstCharge.AppliedChargeId && issued.Any(sale =>
                sale.Credit is not null && sale.Charges?.Any(candidate =>
                    candidate.AppliedChargeId == charge.AppliedChargeId) == true));
        var companySale = issued.Single(sale =>
            sale.Charges?.Any(charge => charge.AppliedChargeId == companyCharge.AppliedChargeId) == true);
        var companyReturnId = Guid.NewGuid();
        var companyReturn = new ConfirmSalesReturnRequest(
            companyReturnId, fixture.BusinessId, fixture.WarehouseId, companySale.DocumentId,
            companySale.CommercialSnapshot.IssuedAt.AddHours(3),
            ReturnEconomicResolutions.CustomerCredit, null, "Devolución de domicilio asumido",
            [new ConfirmSalesReturnLineRequest(1, .5m, ReturnInventoryDispositions.Sellable)],
            ReasonCode: "Other", ReturnedChargeIds: [companyCharge.AppliedChargeId]);
        using (var message = new HttpRequestMessage(
                   HttpMethod.Post, "/api/commerce/v1/sales-returns/confirm")
               { Content = JsonContent.Create(companyReturn) })
        {
            message.Headers.Add("Idempotency-Key", companyReturnId.ToString("N"));
            using var response = await admin.SendAsync(message);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        await AssertBalancedAsync(companyReturnId);
        Assert.Equal(companyCharge.Amount, await AccountAmountAsync(
            companyReturnId, "519595", debit: false));
        Assert.Equal(companyCharge.Amount, await ScalarAsync<decimal>("""
            SELECT PayableCreditAmount FROM dbo.SalesReturnChargeFinancialEffects
            WHERE ReturnId=@Id;
            """, companyReturnId));
        Assert.Equal(0, await ScalarAsync<decimal>("""
            SELECT SupplierCreditAmount FROM dbo.SalesReturnChargeFinancialEffects
            WHERE ReturnId=@Id;
            """, companyReturnId));
        Assert.Equal("Cancelled", await ScalarAsync<string>("""
            SELECT Status FROM dbo.Payables WHERE SourceDocumentId=@Id;
            """, companyCharge.AppliedChargeId));

        // Collecting a credit invoice is a new cash entry, never a second supplier expense.
        var creditSale = issued.First(sale => sale.Credit is not null &&
            sale.DocumentId != chargedSale.DocumentId && sale.DocumentId != companySale.DocumentId &&
            sale.Charges?.Any(charge => charge.InvoicedAmount > 0) == true);
        var receivableId = await ScalarAsync<Guid>("SELECT ReceivableId FROM dbo.Receivables WHERE SourceDocumentId=@Id", creditSale.DocumentId);
        var collection = new ConfirmCustomerPaymentRequest(Guid.NewGuid(), fixture.BusinessId, creditSale.CustomerId!.Value,
            sessionId, DateTimeOffset.UtcNow, "COP", CustomerPaymentMethods.Cash, null, "Recaudo factura con domicilio",
            [new(receivableId, creditSale.Credit!.Amount)]);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/receivable-payments/confirm")
                { Content = JsonContent.Create(collection) };
            message.Headers.Add("Idempotency-Key", collection.PaymentId.ToString("N"));
            using var response = await admin.SendAsync(message);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        await AssertBalancedAsync(collection.PaymentId);
        Assert.Equal(0, await AccountAmountAsync(collection.PaymentId, "519595", debit: true));
        Assert.Equal(0, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id", receivableId));
        var afterCollection = (await admin.GetFromJsonAsync<WorkSessionClosurePreviewView>(
            $"/api/commerce/v1/work-sessions/{sessionId}/closure-preview"))!;
        Assert.Equal(afterPayment.ExpectedCash + creditSale.Credit.Amount, afterCollection.ExpectedCash);
        Assert.Equal(20, afterCollection.InvoiceCharges!.Count);
        Assert.Equal(collection.Notes, Assert.Single(afterCollection.CashMovements!, item => item.DocumentId == collection.PaymentId).Notes);
        afterPayment = afterCollection;
        using var closeMessage = new HttpRequestMessage(HttpMethod.Post,
            $"/api/commerce/v1/work-sessions/{sessionId}/close") { Content = JsonContent.Create(
                new CloseWorkSessionRequest(afterPayment.ExpectedCash, "Matriz de 30 escenarios", PaymentCounts:
                    afterPayment.PaymentTotals.Where(total => total.RequiresCount).Select(total => new WorkSessionPaymentCount(total.PaymentMethodCode, total.NetAmount)).ToArray())) };
        closeMessage.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var closeResponse = await admin.SendAsync(closeMessage);
        Assert.True(closeResponse.IsSuccessStatusCode, await closeResponse.Content.ReadAsStringAsync());
        var closure = (await closeResponse.Content.ReadFromJsonAsync<WorkSessionClosureView>())!;
        Assert.Equal(0, closure.CashDifference);
        Assert.Equal(matrixCharges.Length + (baseline.InvoiceCharges?.Count ?? 0), closure.InvoiceCharges!.Count);
        using (var cashierReader = fixture.CreateAdminClient(WorkSessionPermissionCodes.Read))
        using (var denied = await cashierReader.GetAsync($"/api/commerce/v1/work-sessions/{sessionId}/closure"))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using (var supervisor = fixture.CreateAdminClient(WorkSessionPermissionCodes.ReadCashDifferences))
        {
            var snapshot = (await supervisor.GetFromJsonAsync<WorkSessionClosureView>(
                $"/api/commerce/v1/work-sessions/{sessionId}/closure"))!;
            Assert.Equal(closure.WorkSessionClosureId, snapshot.WorkSessionClosureId);
            Assert.Equal(20, snapshot.InvoiceCharges!.Count);
        }
        using (var scope = fixture.CreateScope())
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IWorkSessionStore>().GetClosureAsync(
                new(fixture.UserId, Guid.NewGuid(), new HashSet<string> { WorkSessionPermissionCodes.ReadCashDifferences }),
                sessionId, CancellationToken.None));
        foreach (var width in new[] { 58, 80 })
        {
            var html = WorkSessionClosureReceiptRenderer.RenderHtml(closure, paperWidthMillimeters: width);
            Assert.Contains("Cargos de facturación", html);
            Assert.Contains("Domicilio matriz", html);
            Assert.Contains("Agotados matriz", html);
            Assert.Contains("Registrado como gasto", html);
        }
        var date = DateOnly.FromDateTime(DianFiscalDateTime.InColombia(issued[0].CommercialSnapshot.IssuedAt).Date);
        var historyTimer = System.Diagnostics.Stopwatch.StartNew();
        using var historyResponse = await admin.GetAsync(
            $"/api/commerce/v1/invoice-charges/history?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}&pageSize=100&search={code}");
        Assert.True(historyResponse.IsSuccessStatusCode, await historyResponse.Content.ReadAsStringAsync());
        var history = (await historyResponse.Content.ReadFromJsonAsync<InvoiceChargeHistoryPage>())!;
        Assert.True(historyTimer.Elapsed < TimeSpan.FromSeconds(2), $"El historial de 30 facturas tardó {historyTimer.Elapsed.TotalMilliseconds:F0} ms.");
        Assert.Equal(20, history.TotalCount);
        Assert.Equal(expectedCharges.Sum(charge => charge.InvoicedAmount), history.InvoicedTotal);
        Assert.Equal(expectedCharges.Sum(charge => charge.ExpenseAmount), history.ExpenseTotal);
        Assert.Equal(0, history.Items.Single(item => item.Charge.AppliedChargeId == firstCharge.AppliedChargeId).PayableBalance);
    }

    private PosSaleUploadRequest CreateChargeMatrixSale(long consecutive, decimal productTotal,
        AppliedInvoiceCharge? charge, bool mixed, Guid customerId, Guid siteId, Guid bankAccountId)
    {
        var source = fixture.CreateValidRequest(consecutive);
        var productNet = decimal.Round(productTotal / 1.19m, 2, MidpointRounding.AwayFromZero);
        var productTax = productTotal - productNet;
        var line = source.Lines[0] with { Quantity = 1, UnitPrice = productNet, UntaxedAmount = productNet,
            TaxAmount = productTax, LineTotal = productTotal, DiscountAmount = 0 };
        var gross = productTotal + (charge?.InvoicedAmount ?? 0);
        var roundingAdjustment = PosPaymentRoundingPolicy.Adjustment(gross);
        var roundedGross = gross + roundingAdjustment;
        var net = productNet + (charge?.InvoicedUntaxedAmount ?? 0);
        var taxes = PosSaleTaxSummary.Calculate([new("01", productTax)], charge is null ? [] : [charge]);
        var vat = taxes.Sum(tax => tax.Amount);
        var paid = mixed ? decimal.Round(gross / 4, 2, MidpointRounding.AwayFromZero) : gross;
        var fiscal = source.FiscalSnapshot!;
        var cufe = CufeCalculator.Calculate(new CufeInput(fiscal.FiscalNumber, fiscal.IssuedAt, net, roundedGross,
            fiscal.SupplierTaxId, fiscal.CustomerIdentification,
            new FiscalTechnicalKey(ServerSliceFixture.TechnicalKeyValue, ServerSliceFixture.TechnicalKeyVersion),
            FiscalEnvironment.Test, taxes.Select(tax => new FiscalTaxAmount(tax.Code, tax.Amount)).ToArray()), ServerSliceFixture.QrValidationUrl);
        var sale = WithUblSnapshot(source with { Lines = [line], Charges = charge is null ? null : [charge],
            CustomerId = customerId, CustomerPartySiteId = siteId,
            Credit = mixed ? new(customerId, gross - paid, fiscal.IssuedAt.AddDays(30), PartySiteId: siteId) : null,
            Payments = [new(1, mixed ? "Transfer" : "Cash", paid,
                mixed ? $"MATRIX-{consecutive}" : null,
                BankAccountId: mixed ? bankAccountId : null,
                RoundingAdjustment: roundingAdjustment)],
            CommercialSnapshot = source.CommercialSnapshot with {
                UntaxedAmount = net, TaxAmount = vat, PayableAmount = roundedGross,
                PayableRoundingAmount = roundingAdjustment, Taxes = taxes },
            FiscalSnapshot = fiscal with {
                UntaxedAmount = net, TaxAmount = vat, PayableAmount = roundedGross,
                PayableRoundingAmount = roundingAdjustment, Taxes = taxes,
                Cufe = cufe.Cufe, QrPayload = cufe.QrPayload }
        });
        return sale with { UblSnapshot = sale.UblSnapshot! with {
            PaymentFormCode = mixed ? "2" : "1",
            DueDate = DateOnly.FromDateTime((sale.Credit?.DueDate ?? fiscal.IssuedAt).Date)
        } };
    }
}
