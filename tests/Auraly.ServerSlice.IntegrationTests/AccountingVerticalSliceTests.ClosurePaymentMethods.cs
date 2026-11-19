using System.Net;
using System.Net.Http.Json;
using Auraly.Application.WorkSessions;
using Auraly.Application.Returns;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Sales;
using Auraly.Contracts.Returns;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Commercial_closure_correction_and_card_refund_do_not_require_a_historical_journal(bool configuredForm)
    {
        using var admin = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure, CatalogPermissionCodes.Update);
        using (var defaults = await admin.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        await DisableAccountingAsync();
        using var scope = fixture.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<WorkSessionService>();
        var userId = await fixture.CreateWorkSessionUserAsync("commercial-closure-refund");
        var identity = new WorkSessionIdentity(userId, fixture.TenantId,
            new HashSet<string> { WorkSessionPermissionCodes.Read, WorkSessionPermissionCodes.Close,
                WorkSessionPermissionCodes.ReadCashDifferences, WorkSessionPermissionCodes.ReconcileClosures }, fixture.BusinessId);
        var sessionId = Guid.NewGuid();
        await sessions.RegisterDeviceOpenAsync(identity,
            new(userId, sessionId, fixture.BusinessId, new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero)), fixture.DeviceId);
        var sale = WithUblSnapshot(fixture.CreateValidRequest(configuredForm ? 9985 : 9984)) with
        {
            SoldByUserId = userId, WorkSessionId = sessionId,
            Payments = [new PosSalePaymentContract(1, "Cash", 11900, null)]
        };
        using (var upload = fixture.CreateUploadMessage(sale))
        using (var accepted = await fixture.CreateClient().SendAsync(upload))
            Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());
        Assert.Equal("CommercialEffectsApplied", await ScalarAsync<string>("SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", sale.DocumentId));
        var closure = await sessions.CloseFromDeviceAsync(identity, sessionId, Guid.NewGuid().ToString("N"),
            new CloseWorkSessionRequest(11900, null, fixture.UserId, [new("Cash", 11900), new("Card", 0), new("Transfer", 0)]));
        using var reviewer = fixture.CreateUserClient(userId, WorkSessionPermissionCodes.ReadCashDifferences,
            WorkSessionPermissionCodes.ReconcileClosures);
        var page = (await reviewer.GetFromJsonAsync<WorkSessionPaymentVerificationPage>(
            $"/api/commerce/v1/work-sessions/closures/{closure.WorkSessionClosureId}/payment-verifications/page?page=1&pageSize=100"))!;
        var item = Assert.Single(page.Items, value => value.SourceId == sale.DocumentId);
        var franchise = (await admin.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/card-franchise"))!.First().Code;
        var target = configuredForm ? await CreateClosureFormAsync(admin, "DebitCard") : null;
        // Both contracts preserve commercial effects. An original commercial
        // sale has no posted auxiliary, even when today's destination is native.
        var correction = new WorkSessionPaymentCorrection(item.VerificationKey, "Card", 11900,
            "Soporte del recaudo comercial", "DebitCard", franchise, "COMMERCIAL-CARD",
            PaymentMethodId: target?.PaymentMethodId, PaymentMethodVersion: target?.Version);
        var reconciliation = await scope.ServiceProvider.GetRequiredService<IWorkSessionStore>().ReconcileClosureAsync(
            identity, closure.WorkSessionClosureId, Guid.NewGuid().ToString("N"),
            new(closure.PaymentTotals.Where(value => value.RequiresCount)
                .Select(value => new ReconcileWorkSessionClosureLine(value.PaymentMethodCode,
                    value.PaymentMethodCode == "Card" ? 11900 : 0, true, null)).ToArray(), [], null, [], [correction]), default);
        await scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>().ProcessAsync(
            reconciliation.ReconciliationId, WorkSessionAccountingDocumentTypes.ClosureReconciliation, fixture.BusinessId, default);
        Assert.Equal("CommercialEffectsApplied", await ScalarAsync<string>("SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", reconciliation.ReconciliationId));
        var returnIdentity = new SalesReturnUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { SalesReturnPermissionCodes.Create });
        var query = scope.ServiceProvider.GetRequiredService<SalesReturnQueryService>();
        var payment = Assert.Single((await query.GetReturnableSaleAsync(returnIdentity, sale.DocumentId))!.Payments);
        Assert.Equal("DebitCard", payment.MethodCode);
        Assert.Equal("COMMERCIAL-CARD", payment.ApprovalNumber);
        var reason = await ScalarAsync<string>("SELECT TOP(1) Code FROM dbo.BusinessReasons WHERE BusinessId=@Id AND ReasonType=N'SalesReturn' AND IsActive=1 ORDER BY DisplayOrder,Code", fixture.BusinessId);
        var refund = new ConfirmSalesReturnRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            sale.DocumentId, new DateTimeOffset(2026, 11, 18, 18, 0, 0, TimeSpan.Zero), ReturnEconomicResolutions.Refund,
            "DebitCard", "Reembolso comercial sin contabilidad", [new(1, .5m, ReturnInventoryDispositions.Sellable)],
            WorkSessionId: await fixture.OpenWebWorkSessionAsync(), ReasonCode: reason, OriginalPaymentNumber: 1);
        var key = Guid.NewGuid().ToString("N");
        var returns = scope.ServiceProvider.GetRequiredService<SalesReturnService>();
        await returns.ConfirmAsync(returnIdentity, key, refund);
        Assert.True((await returns.ConfirmAsync(returnIdentity, key, refund)).IdempotentReplay);
        Assert.Equal("CommercialEffectsApplied", await ScalarAsync<string>("SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", refund.ReturnId));
        foreach (var source in new[] { sale.DocumentId, reconciliation.ReconciliationId, refund.ReturnId })
            Assert.Equal(0, await CountAsync("AccountingEntries", "SourceDocumentId", source));
        Assert.Equal(5950m, Assert.Single((await query.GetReturnableSaleAsync(returnIdentity, sale.DocumentId))!.Payments).AvailableAmount);
        Assert.Equal("Cash", await ScalarAsync<string>("SELECT MethodCode FROM dbo.SalesPayments WHERE DocumentId=@Id", sale.DocumentId));
    }

    [Theory]
    [InlineData("Cash", "Transfer", false, false, false, false)]
    [InlineData("Transfer", "Transfer", false, false, false, false)]
    [InlineData("Transfer", "Transfer", false, true, false, false)]
    [InlineData("DebitCard", "CreditCard", false, false, false, false)]
    [InlineData("DebitCard", "DebitCard", false, true, false, false)]
    [InlineData("Cash", "DebitCard", false, false, false, false)]
    [InlineData("Cash", "Cash", false, false, false, false)]
    [InlineData("Cash", "Cash", true, false, false, false)]
    [InlineData("Cash", "Cash", true, false, true, false)]
    [InlineData("Cash", "Transfer", false, false, false, true)]
    [InlineData("Cash", "Transfer", false, false, false, false, true)]
    public async Task Closure_native_form_correction_preserves_invoice_and_uses_actual_accounts_once(
        string originalType, string targetType, bool sameAccount, bool remapTarget, bool sameForm, bool actualBlindCount,
        bool refundBeforeCorrection = false)
    {
        using var admin = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, CatalogPermissionCodes.Update);
        using (var defaults = await admin.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activate = await admin.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) activate.EnsureSuccessStatusCode();
        var original = await CreateClosureFormAsync(admin, originalType);
        var target = sameForm ? original : await CreateClosureFormAsync(admin, targetType, sameAccount ? original.AccountId : null);
        var userId = await fixture.CreateWorkSessionUserAsync("native-closure");
        var sessionId = Guid.NewGuid();
        var identity = new WorkSessionIdentity(userId, fixture.TenantId,
            new HashSet<string> { WorkSessionPermissionCodes.Read, WorkSessionPermissionCodes.Close,
                WorkSessionPermissionCodes.ReadCashDifferences, WorkSessionPermissionCodes.ReconcileClosures }, fixture.BusinessId);
        using var scope = fixture.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<WorkSessionService>();
        await sessions.RegisterDeviceOpenAsync(identity,
            new(userId, sessionId, fixture.BusinessId, new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero)), fixture.DeviceId);
        var franchise = (await admin.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/card-franchise"))!.First().Code;
        var card = originalType is "DebitCard" or "CreditCard";
        var sales = new List<PosSaleUploadRequest>();
        // Each upload is an independent canonical sale. The larger closure
        // exercises set-based validation; no artificial Posted rows are seeded.
        for (var index = 0; index < (actualBlindCount ? 25 : 1); index++)
        {
            var createdSale = WithUblSnapshot(fixture.CreateValidRequest((actualBlindCount ? 9950 : 9910) + index + (refundBeforeCorrection ? 10 : 0) + (targetType == "DebitCard" ? 30 : 0) + (remapTarget ? 5 : 0) + (sameForm ? 6 : 0) + (originalType == "Transfer" ? 1 : card ? 2 : sameAccount ? 3 : targetType == "Cash" ? 4 : 0)) with
            {
                SoldByUserId = userId, WorkSessionId = sessionId,
                Payments = [new PosSalePaymentContract(1, originalType, 11900,
                    Reference: originalType == "Transfer" ? "ORIGINAL" : null,
                    CardFranchiseCode: card ? franchise : null, ApprovalNumber: card ? "ORIGINAL" : null,
                    PaymentMethodId: original.PaymentMethodId, PaymentMethodVersion: original.Version, PaymentMethodCode: original.Code, PaymentMethodName: original.Name, AccountId: original.AccountId)]
            });
            using (var upload = fixture.CreateUploadMessage(createdSale))
            using (var accepted = await fixture.CreateClient().SendAsync(upload))
                Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());
            await AssertBalancedAsync(createdSale.DocumentId);
            sales.Add(createdSale);
        }
        var sale = sales[0];
        var collectionTotal = 11900m * sales.Count;
        var sourceSignature = await ScalarAsync<string>("SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',PayloadJson),2) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id AND SourceDocumentType=N'SalesInvoice'", sale.DocumentId);
        var cash = originalType == "Cash" && !actualBlindCount ? collectionTotal : 0m;
        var closure = await sessions.CloseFromDeviceAsync(identity, sessionId, Guid.NewGuid().ToString("N"),
            new CloseWorkSessionRequest(cash, null, fixture.UserId,
                [new("Cash", cash), new("Transfer", originalType == "Transfer" || actualBlindCount ? collectionTotal : 0), new("Card", card ? collectionTotal : 0)]));
        using var reviewer = fixture.CreateUserClient(userId, WorkSessionPermissionCodes.ReadCashDifferences,
            WorkSessionPermissionCodes.ReconcileClosures);
        var path = $"/api/commerce/v1/work-sessions/closures/{closure.WorkSessionClosureId}";
        var page = (await reviewer.GetFromJsonAsync<WorkSessionPaymentVerificationPage>(path + "/payment-verifications/page?page=1&pageSize=100"))!;
        var item = Assert.Single(page.Items, item => item.SourceId == sale.DocumentId);
        var targetCard = targetType is "DebitCard" or "CreditCard";
        var group = targetCard ? "Card" : targetType;
        var correction = new WorkSessionPaymentCorrection(item.VerificationKey, group, 11900, "Soporte del recaudo real",
            targetType, targetCard ? franchise : null, targetCard ? "APROBACION" : null,
            targetType == "Transfer" ? "TRANSFERENCIA" : null, target.PaymentMethodId, target.Version);
        using (var catalog = await reviewer.GetAsync($"/api/commerce/v1/payment-methods/?page=1&pageSize=10&usage=Sales&methodCode={targetType}"))
            catalog.EnsureSuccessStatusCode();
        using (var forbiddenCatalog = await reviewer.GetAsync("/api/commerce/v1/payment-methods/?page=1&pageSize=10"))
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenCatalog.StatusCode);
        foreach (var invalid in new[] { correction with { PaymentMethodVersion = target.Version + 1 },
            correction with { PaymentMethodId = Guid.NewGuid() }, correction with { PaymentMethodId = Guid.Empty },
            correction with { Amount = correction.Amount - 1 }, correction with { Amount = correction.Amount + 1 },
            correction with { Amount = correction.Amount - 1, PaymentMethodId = null, PaymentMethodVersion = null } })
        {
            using var rejected = await reviewer.PostAsJsonAsync(path + "/payment-corrections/validate", new[] { invalid });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        if (targetType == "Transfer" || targetCard)
        {
            using var incomplete = await reviewer.PostAsJsonAsync(path + "/payment-corrections/validate",
                new[] { correction with { Reference = null, ApprovalNumber = null } });
            Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        }
        if (refundBeforeCorrection)
        {
            var returnIdentity = new SalesReturnUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
                new HashSet<string> { SalesReturnPermissionCodes.Create });
            var reason = await ScalarAsync<string>("SELECT TOP(1) Code FROM dbo.BusinessReasons WHERE BusinessId=@Id AND ReasonType=N'SalesReturn' AND IsActive=1 ORDER BY DisplayOrder,Code", fixture.BusinessId);
            var pendingRefund = new ConfirmSalesReturnRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
                sale.DocumentId, new DateTimeOffset(2026, 11, 18, 18, 0, 0, TimeSpan.Zero),
                ReturnEconomicResolutions.Refund, "Cash", "Reembolso aceptado antes de corregir el recaudo",
                [new(1, .5m, ReturnInventoryDispositions.Sellable)],
                WorkSessionId: await fixture.OpenWebWorkSessionAsync(), ReasonCode: reason);
            var pendingKey = Guid.NewGuid().ToString("N");
            await scope.ServiceProvider.GetRequiredService<ISalesReturnStore>().AcceptAsync(returnIdentity,
                pendingKey, pendingRefund, default);
            try
            {
                Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SalesReturnSettlements WHERE ReturnId=@Id", pendingRefund.ReturnId));
                var blocked = await Assert.ThrowsAsync<WorkSessionValidationException>(() => sessions.ValidatePaymentCorrectionsAsync(
                    identity, closure.WorkSessionClosureId, [correction]));
                Assert.Contains("reembolsos aceptados", blocked.Message);
                Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.WorkSessionClosureReconciliations WHERE WorkSessionClosureId=@Id", closure.WorkSessionClosureId));
            }
            finally
            {
                await scope.ServiceProvider.GetRequiredService<SalesReturnService>().ConfirmAsync(returnIdentity,
                    pendingKey, pendingRefund);
            }
            return;
        }
        using (var preview = await reviewer.PostAsJsonAsync(path + "/payment-corrections/validate", new[] { correction }))
            Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        var corrections = page.Items.Select(value => correction with { VerificationKey = value.VerificationKey }).ToArray();
        await sessions.ValidatePaymentCorrectionsAsync(identity, closure.WorkSessionClosureId, corrections);
        using (var commands = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnection(fixture.ConnectionString).Database))
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await sessions.ValidatePaymentCorrectionsAsync(identity, closure.WorkSessionClosureId, corrections);
            watch.Stop(); output.WriteLine("Native correction preview owner ({2} invoices): {0} SQL commands, {1:F1} ms", commands.Count, watch.Elapsed.TotalMilliseconds, sales.Count);
            Assert.Equal(3, commands.Count);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), watch.Elapsed.ToString());
        }
        var decisions = page.Items.Where(item => item.PaymentMethodCode != "Cash")
            .Select(item => new WorkSessionPaymentVerificationDecision(item.VerificationKey, "Verified")).ToArray();
        var request = new ReconcileWorkSessionClosureRequest(closure.PaymentTotals.Where(item => item.RequiresCount)
            .Select(item => new ReconcileWorkSessionClosureLine(item.PaymentMethodCode,
                item.PaymentMethodCode == group ? collectionTotal : 0, true, null)).ToArray(), [], "Conciliación nativa", decisions, corrections);
        var key = Guid.NewGuid().ToString("N");
        var store = scope.ServiceProvider.GetRequiredService<IWorkSessionStore>();
        var reconciliation = await store.ReconcileClosureAsync(identity, closure.WorkSessionClosureId, key, request, default);
        var returnUser = new SalesReturnUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { SalesReturnPermissionCodes.Create });
        var returns = scope.ServiceProvider.GetRequiredService<SalesReturnService>();
        var returnsQuery = scope.ServiceProvider.GetRequiredService<SalesReturnQueryService>();
        ConfirmSalesReturnRequest? refund = null;
        if (targetCard)
        {
            var reason = await ScalarAsync<string>("SELECT TOP(1) Code FROM dbo.BusinessReasons WHERE BusinessId=@Id AND ReasonType=N'SalesReturn' AND IsActive=1 ORDER BY DisplayOrder,Code", fixture.BusinessId);
            refund = new(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, sale.DocumentId,
                new DateTimeOffset(2026, 11, 18, 18, 0, 0, TimeSpan.Zero), ReturnEconomicResolutions.Refund, targetType,
                "Reversión del recaudo corregido", [new(1, .5m, ReturnInventoryDispositions.Sellable)],
                WorkSessionId: await fixture.OpenWebWorkSessionAsync(), ReasonCode: reason, OriginalPaymentNumber: 1);
            Assert.Empty((await returnsQuery.GetReturnableSaleAsync(returnUser, sale.DocumentId))!.Payments);
            var pending = await Assert.ThrowsAsync<SalesReturnConflictException>(() => returns.ConfirmAsync(returnUser,
                Guid.NewGuid().ToString("N"), refund));
            Assert.Contains("corrección del recaudo", pending.Message);
        }
        // Change the original form after acceptance. Its old posted auxiliary
        // must remain the source, even though today's catalog now says otherwise.
        var other = await CreateClosureFormAsync(admin, originalType);
        original = (await admin.GetFromJsonAsync<PaymentMethodPage>($"/api/commerce/v1/payment-methods/?paymentMethodId={original.PaymentMethodId}"))!.Items.Single();
        target = (await admin.GetFromJsonAsync<PaymentMethodPage>($"/api/commerce/v1/payment-methods/?paymentMethodId={target.PaymentMethodId}"))!.Items.Single();
        if (!sameForm)
        using (var changed = await admin.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{original.PaymentMethodId}",
            new SavePaymentMethodRequest(original.PaymentMethodId, original.Code, original.Name, original.PaymentTypeId, other.AccountId,
                true, true, false, false, true, 10, original.RowVersion)))
            Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());
        if (remapTarget)
        {
            var replacement = await CreateClosureFormAsync(admin, targetType);
            using var changed = await admin.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{target.PaymentMethodId}",
                new SavePaymentMethodRequest(target.PaymentMethodId, target.Code, target.Name, target.PaymentTypeId, replacement.AccountId,
                    true, true, false, false, true, 10, target.RowVersion));
            Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());
            target = (await changed.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        }
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        if (remapTarget)
        {
            using var deactivate = await admin.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{target.PaymentMethodId}",
                new SavePaymentMethodRequest(target.PaymentMethodId, target.Code, target.Name, target.PaymentTypeId, target.AccountId,
                    true, true, false, false, false, 10, target.RowVersion));
            Assert.True(deactivate.IsSuccessStatusCode, await deactivate.Content.ReadAsStringAsync());
            target = (await deactivate.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
            await processor.ProcessAsync(reconciliation.ReconciliationId, WorkSessionAccountingDocumentTypes.ClosureReconciliation, fixture.BusinessId, default);
            Assert.Equal("AccountingPendingConfiguration", await ScalarAsync<string>("SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", reconciliation.ReconciliationId));
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", reconciliation.ReconciliationId));
            using var activate = await admin.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{target.PaymentMethodId}",
                new SavePaymentMethodRequest(target.PaymentMethodId, target.Code, target.Name, target.PaymentTypeId, target.AccountId,
                    true, true, false, false, true, 10, target.RowVersion));
            Assert.True(activate.IsSuccessStatusCode, await activate.Content.ReadAsStringAsync());
            target = (await activate.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        }
        await processor.ProcessAsync(reconciliation.ReconciliationId, WorkSessionAccountingDocumentTypes.ClosureReconciliation, fixture.BusinessId, default);
        await processor.ProcessAsync(reconciliation.ReconciliationId, WorkSessionAccountingDocumentTypes.ClosureReconciliation, fixture.BusinessId, default);
        using (var replay = new HttpRequestMessage(HttpMethod.Post, path + "/reconcile") { Content = JsonContent.Create(request) })
        {
            replay.Headers.Add("Idempotency-Key", key);
            using var result = await reviewer.SendAsync(replay);
            Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync());
            Assert.True((await result.Content.ReadFromJsonAsync<WorkSessionClosureReconciliationView>())!.IdempotentReplay);
        }
        Assert.Equal(sourceSignature, await ScalarAsync<string>("SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',PayloadJson),2) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id AND SourceDocumentType=N'SalesInvoice'", sale.DocumentId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id", sale.DocumentId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id AND SourceDocumentType=N'SalesInvoice'", sale.DocumentId));
        var reviewed = (await reviewer.GetFromJsonAsync<WorkSessionPaymentVerificationPage>(path + "/payment-verifications/page?page=1&pageSize=100"))!;
        Assert.Equal(sales.Count, reviewed.Items.Count);
        var amended = Assert.Single(reviewed.Items, value => value.SourceId == sale.DocumentId);
        Assert.Equal(originalType, amended.TenderMethodCode);
        Assert.Equal(target.PaymentMethodId, amended.CorrectedPaymentMethodId);
        Assert.Equal(target.Name, amended.CorrectedPaymentMethodName);
        await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using var verify = new SqlCommand("""
            SELECT payment.MethodCode,payment.AccountId,payment.Amount,
              (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Reconciliation),
              (SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Reconciliation),
              (SELECT COUNT(*) FROM dbo.WorkSessionClosureReconciliations WHERE WorkSessionClosureId=@Closure),
              (SELECT COUNT(*) FROM dbo.Receivables WHERE SourceDocumentId=@Sale)
            FROM dbo.SalesPayments payment WHERE payment.DocumentId=@Sale;
            SELECT AccountId,SUM(Debit),SUM(Credit) FROM dbo.AccountingEntryLines line
            JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
            WHERE entry.SourceDocumentId=@Reconciliation GROUP BY AccountId;
            """, connection);
        verify.Parameters.AddWithValue("@Sale", sale.DocumentId); verify.Parameters.AddWithValue("@Reconciliation", reconciliation.ReconciliationId);
        verify.Parameters.AddWithValue("@Closure", closure.WorkSessionClosureId);
        await using var reader = await verify.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        Assert.Equal(originalType, reader.GetString(0)); Assert.Equal(original.AccountId, reader.GetGuid(1)); Assert.Equal(11900, reader.GetDecimal(2));
        Assert.Equal(sameAccount ? 0 : 1, reader.GetInt32(3));
        Assert.Equal(sameAccount ? "CommercialEffectsApplied" : "Posted", reader.GetString(4));
        Assert.Equal(1, reader.GetInt32(5)); Assert.Equal(0, reader.GetInt32(6));
        await reader.NextResultAsync();
        var amounts = new Dictionary<Guid, (decimal Debit, decimal Credit)>();
        while (await reader.ReadAsync()) amounts.Add(reader.GetGuid(0), (reader.GetDecimal(1), reader.GetDecimal(2)));
        if (!sameAccount)
        {
            Assert.Equal((0m, collectionTotal), amounts[original.AccountId]); Assert.Equal((collectionTotal, 0m), amounts[target.AccountId]);
            Assert.Equal(amounts.Values.Sum(item => item.Debit), amounts.Values.Sum(item => item.Credit));
        }
        await reader.CloseAsync();
        if (refund is not null)
        {
            // Changing the form again must not redirect a refund away from the
            // auxiliary actually used by the completed correction.
            var nextAccount = await CreateClosureFormAsync(admin, targetType);
            using var remapped = await admin.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{target.PaymentMethodId}",
                new SavePaymentMethodRequest(target.PaymentMethodId, target.Code, target.Name, target.PaymentTypeId,
                    nextAccount.AccountId, true, true, false, false, true, 10, target.RowVersion));
            Assert.True(remapped.IsSuccessStatusCode, await remapped.Content.ReadAsStringAsync());
            ReturnableSalePayment effective;
            using (var commands = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnection(fixture.ConnectionString).Database))
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                effective = Assert.Single((await returnsQuery.GetReturnableSaleAsync(returnUser, sale.DocumentId))!.Payments);
                watch.Stop();
                output.WriteLine("Returnable corrected tender: {0} SQL commands, {1:F1} ms", commands.Count, watch.Elapsed.TotalMilliseconds);
                Assert.Equal(4, commands.Count);
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), watch.Elapsed.ToString());
            }
            Assert.Equal(targetType, effective.MethodCode);
            Assert.Equal("APROBACION", effective.ApprovalNumber);
            var refundKey = Guid.NewGuid().ToString("N");
            var accepted = await returns.ConfirmAsync(returnUser, refundKey, refund);
            await AssertBalancedAsync(accepted.ReturnId);
            Assert.True((await returns.ConfirmAsync(returnUser, refundKey, refund)).IdempotentReplay);
            Assert.Equal(5950m, await AccountAmountAsync(accepted.ReturnId, target.AccountCode, debit: false));
            Assert.Equal("APROBACION", await ScalarAsync<string>("SELECT ApprovalNumber FROM dbo.SalesReturns WHERE ReturnId=@Id", accepted.ReturnId));
            Assert.Equal(5950m, Assert.Single((await returnsQuery.GetReturnableSaleAsync(returnUser, sale.DocumentId))!.Payments).AvailableAmount);
            var held = refund with { ReturnId = Guid.NewGuid() };
            // Acceptance alone reserves the tender; no worker/settlement is
            // required to prevent a second refund from spending it again.
            var heldKey = Guid.NewGuid().ToString("N");
            await scope.ServiceProvider.GetRequiredService<ISalesReturnStore>().AcceptAsync(returnUser, heldKey, held, default);
            try
            {
                Assert.Equal(0m, Assert.Single((await returnsQuery.GetReturnableSaleAsync(returnUser, sale.DocumentId))!.Payments).AvailableAmount);
                await Assert.ThrowsAsync<SalesReturnConflictException>(() => returns.ConfirmAsync(returnUser,
                    Guid.NewGuid().ToString("N"), refund with { ReturnId = Guid.NewGuid() }));
            }
            finally { await returns.ConfirmAsync(returnUser, heldKey, held); }
            await AssertBalancedAsync(held.ReturnId);
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", accepted.ReturnId));
        }
    }

    private async Task<PaymentMethodView> CreateClosureFormAsync(HttpClient admin, string method, Guid? accountId = null)
    {
        if (accountId is null)
        {
            Guid? bank = null;
            if (method == "Transfer")
            {
                bank = Guid.NewGuid();
                var types = (await admin.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/bank-account-type"))!;
                using var saved = await admin.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bank}",
                    new SaveBankAccountRequest(bank.Value, null, types.Single(item => item.Code == "Checking").Id,
                        "Banco de conciliación", bank.Value.ToString("N"), "Banco de conciliación", false, true, null));
                Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            }
            var account = method == "Cash" ? await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, admin)
                : await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, admin, method == "Transfer" ? "1118" : "1398",
                    "Asset", method == "Transfer" ? "Bank" : method=="PendingCollection"?"General":"CardClearing", "Debit", bank);
            accountId = account.AccountId;
        }
        var options = (await admin.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!;
        var request = new SavePaymentMethodRequest(Guid.NewGuid(), $"CLS-{Guid.NewGuid():N}", $"Forma de conciliación {method}",
            options.Single(item => item.Code == method).Id, accountId.Value, true, method!="PendingCollection", false, false, true, 10);
        using var response = await admin.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{request.PaymentMethodId}", request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
    }
}
