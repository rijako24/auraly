using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Expenses;
using Auraly.Commerce.Taxation.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class AccountingVoucherDraftTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Pending_adjustments_reserve_the_obligation_without_applying_it_twice()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate,
            AccountingPermissionCodes.ManualSend, AccountingPermissionCodes.Retry, ExpensePermissionCodes.Create,
            TaxationPermissionCodes.ManageWithholdingRules, TaxationPermissionCodes.ViewWithholdingRules);
        var profilePath = $"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}";
        using var priorResponse = await client.GetAsync(profilePath);
        var prior = priorResponse.StatusCode == HttpStatusCode.NotFound ? null : await priorResponse.Content.ReadFromJsonAsync<CounterpartyTaxProfileView>();
        using (var profile = await client.PutAsJsonAsync(profilePath, new SaveCounterpartyTaxProfileRequest(fixture.BusinessId, fixture.SupplierId, false, [], null)))
            profile.EnsureSuccessStatusCode();
        try
        {
            using var scope = fixture.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
            var store = scope.ServiceProvider.GetRequiredService<IAccountingStore>();
            var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
                new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend });
            var account = (await service.ListAccountsAsync(user)).First(value => value.Code == "519595");
            var date = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(-5));
            var expense = new ConfirmExpenseRequest(Guid.NewGuid(), fixture.BusinessId, fixture.SupplierId, null, null,
                "AJ-" + Guid.NewGuid().ToString("N"), date, date.AddDays(30), "COP", "Obligación para ajuste", 0, 0, null, null,
                Lines: [new(account.AccountId, null, null, "Servicio", 1000, null, "CapitalizedCost", null)]);
            using var previewResponse = await client.PostAsJsonAsync("/api/commerce/v1/expenses/preview", expense);
            Assert.True(previewResponse.IsSuccessStatusCode, await previewResponse.Content.ReadAsStringAsync());
            var preview = (await previewResponse.Content.ReadFromJsonAsync<ExpensePreview>())!;
            using var confirm = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/expenses/confirm") { Content = JsonContent.Create(expense with { CalculationHash = preview.CalculationHash }) };
            confirm.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var expenseResponse = await client.SendAsync(confirm);
            Assert.True(expenseResponse.IsSuccessStatusCode, await expenseResponse.Content.ReadAsStringAsync());
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            Guid? obligationId = null;
            for (var attempt = 0; attempt < 50 && obligationId is null; attempt++)
            {
                await using var find = new SqlCommand("SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id", connection);
                find.Parameters.AddWithValue("@Id", expense.ExpenseId);
                obligationId = await find.ExecuteScalarAsync() as Guid?;
                if (obligationId is null) await Task.Delay(100);
            }
            Assert.NotNull(obligationId);
            var request = new SaveVoucherDraftRequest(Guid.NewGuid(), "AccountAdjustment", date, "ACCOUNT_ADJUSTMENT", "Ajuste de prueba", null,
                [], new("Payable", obligationId.Value, "Decrease", 700, account.AccountId, null), null);
            var first = await service.SaveVoucherDraftAsync(user, request);
            var second = await service.SaveVoucherDraftAsync(user, request with { DocumentId = Guid.NewGuid() });
            // Accept directly at the canonical store so the native worker has not yet applied the reservation.
            await store.ConfirmAccountAdjustmentAsync(user, Command(first), CancellationToken.None, first.RowVersion);
            await Assert.ThrowsAsync<AccountingConflictException>(() => store.ConfirmAccountAdjustmentAsync(user, Command(second), CancellationToken.None, second.RowVersion));
            using var posting = await client.PostAsync($"/api/commerce/v1/accounting/postings/{first.DocumentId}/retry", null);
            Assert.True(posting.IsSuccessStatusCode, await posting.Content.ReadAsStringAsync());
            using var repeat = await client.PostAsync($"/api/commerce/v1/accounting/postings/{first.DocumentId}/retry", null);
            repeat.EnsureSuccessStatusCode();
            await using var balance = new SqlCommand("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id; SELECT COUNT(*) FROM dbo.PayableTransactions WHERE SourceDocumentId=@DocumentId", connection);
            balance.Parameters.AddWithValue("@Id", obligationId.Value);
            balance.Parameters.AddWithValue("@DocumentId", first.DocumentId);
            await using var reader = await balance.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync()); Assert.Equal(300m, reader.GetDecimal(0));
            await reader.NextResultAsync(); Assert.True(await reader.ReadAsync()); Assert.Equal(1, reader.GetInt32(0));

            ConfirmAccountAdjustmentRequest Command(VoucherDraftView value) => new(value.DocumentId, fixture.BusinessId, "Payable",
                obligationId.Value, "Decrease", 700, account.AccountId, null, date, value.ConceptCode, value.Description);
        }
        finally
        {
            if (prior is not null)
            {
                using var restore = await client.PutAsJsonAsync(profilePath, new SaveCounterpartyTaxProfileRequest(fixture.BusinessId, fixture.SupplierId, prior.AppliesWithholding, prior.Responsibilities, prior.JurisdictionCode));
                restore.EnsureSuccessStatusCode();
            }
            else
            {
                await using var cleanup = new SqlConnection(fixture.ConnectionString);
                await cleanup.OpenAsync();
                await using var command = new SqlCommand("DELETE dbo.CounterpartyTaxProfiles WHERE TenantId=@TenantId AND CounterpartyId=@Id", cleanup);
                command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                command.Parameters.AddWithValue("@Id", fixture.SupplierId);
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    [Fact]
    public async Task Draft_is_editable_without_effects_and_explicit_send_is_versioned_and_idempotent()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate,
            AccountingPermissionCodes.ManualSend, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activate = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate", new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) activate.EnsureSuccessStatusCode();
        var accounts = (await client.GetFromJsonAsync<AccountingAccountView[]>("/api/commerce/v1/accounting/accounts"))!;
        var expense = accounts.First(account => account.AccountType == "Expense" && account.AllowsPosting && !account.RequiresParty);
        var bank = accounts.First(account => account.Code == "111005");
        var request = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
            new(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(-5)), "MANUAL_VOUCHER", "Prueba de comprobante abierto",
            "BOR-" + Guid.NewGuid().ToString("N"), [new(null, null, null, "Por completar", 0, 0)], null, null);
        var path = $"/api/commerce/v1/accounting/manual/drafts/{request.DocumentId}";
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend });
        await Assert.ThrowsAsync<AccountingValidationException>(() => service.SaveVoucherDraftAsync(user,
            request with { Lines = [new(null, null, null, " ", 0, 0)] }));
        var saved = await Save(request);
        Assert.Equal("Created", saved.Status);
        Assert.True(saved.CanEdit);
        Assert.Null(saved.SentAt);
        await AssertEffectCounts(0);
        var replay = await Save(request);
        Assert.Equal(saved.RowVersion, replay.RowVersion);
        using (var invalid = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await AssertEffectCounts(0);
        request = request with { RowVersion = saved.RowVersion, Lines = [
            new(expense.AccountId, fixture.SupplierPartyId, null, "Débito", 1200, 0),
            new(bank.AccountId, null, null, "Crédito", 0, 1200)] };
        saved = await Save(request);
        using (var stale = await client.PutAsJsonAsync(path, request with { Description = "No sobrescribir" }))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var staleSend = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(request.RowVersion!)))
            Assert.Equal(HttpStatusCode.Conflict, staleSend.StatusCode);
        using var creator = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate);
        using (var denied = await creator.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Null(await service.GetVoucherDraftAsync(user with { TenantId = Guid.NewGuid() }, request.DocumentId));
        var page = (await client.GetFromJsonAsync<AccountingDocumentPage>($"/api/commerce/v1/accounting/documents?from=2026-01-01&to=2026-12-31&status=Created&search={request.Reference}"))!;
        Assert.Equal(saved.DocumentId, Assert.Single(page.Items).SourceDocumentId);
        Assert.True(page.Items[0].HasManualDraft);
        var partyPage = (await client.GetFromJsonAsync<AccountingDocumentPage>($"/api/commerce/v1/accounting/documents?from=2026-01-01&to=2026-12-31&status=Created&partyId={fixture.SupplierPartyId}&search={request.Reference}&page=2&pageSize=1"))!;
        Assert.Empty(partyPage.Items);
        Assert.Equal(1, partyPage.TotalCount);
        var report = (await client.GetFromJsonAsync<FinancialTraceabilityLinePage>($"/api/commerce/v1/accounting/reports/financial-traceability-lines?from=2026-01-01&to=2026-12-31&status=Created&partyId={fixture.SupplierPartyId}&search={request.Reference}"))!;
        Assert.Equal(2, report.Items.Count);
        Assert.Equal(1200m, report.Items.Sum(line => line.Debit));
        using (var bypass = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers",
            new ConfirmManualAccountingVoucherRequest(request.DocumentId, fixture.BusinessId, request.OccurredAt,
                request.ConceptCode, request.Description, request.Lines.Select(line => new ManualVoucherLineRequest(
                    line.AccountId!.Value, line.PartyId, line.CostCenterId, line.Description, line.Debit, line.Credit)).ToArray())))
            Assert.False(bypass.IsSuccessStatusCode);
        var sends = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion))));
        foreach (var response in sends) { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); response.Dispose(); }
        var sent = (await client.GetFromJsonAsync<VoucherDraftView>(path))!;
        Assert.NotNull(sent.SentAt);
        Assert.False(sent.CanEdit);
        using (var frozen = await client.PutAsJsonAsync(path, request with { RowVersion = sent.RowVersion }))
            Assert.Equal(HttpStatusCode.Conflict, frozen.StatusCode);
        await AssertEffectCounts(1);
        // A replay reads only the authoritative draft; no publish, source or posting rewrite.
        using (var counter = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog))
        {
            await service.SendVoucherDraftAsync(user, request.DocumentId, new(saved.RowVersion));
            Assert.Equal(1, counter.Count);
        }
        for (var attempt = 0; attempt < 50 && sent.Status != "Posted"; attempt++)
        {
            await Task.Delay(100);
            sent = (await client.GetFromJsonAsync<VoucherDraftView>(path))!;
        }
        Assert.Equal("Posted", sent.Status);
        var entry = (await client.GetFromJsonAsync<AccountingEntryView>($"/api/commerce/v1/accounting/entries/by-document/{request.DocumentId}"))!;
        Assert.Equal(1200m, entry.DebitTotal);
        Assert.Equal(1200m, entry.CreditTotal);

        async Task<VoucherDraftView> Save(SaveVoucherDraftRequest value)
        {
            using var response = await client.PutAsJsonAsync(path, value);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
        async Task AssertEffectCounts(int expected)
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id),
                  (SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id),
                  (SELECT COUNT(*) FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id),
                  (SELECT COUNT(*) FROM dbo.FiscalDocuments WHERE DocumentId=@Id);
                """, connection);
            command.Parameters.AddWithValue("@Id", request.DocumentId);
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            Assert.Equal(expected, reader.GetInt32(0)); Assert.Equal(expected, reader.GetInt32(1));
            Assert.Equal(0, reader.GetInt32(2)); Assert.Equal(0, reader.GetInt32(3));
        }
    }

    [Fact]
    public async Task Saving_two_fifty_and_five_hundred_lines_has_constant_database_roundtrips()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate });
        var account = (await service.ListAccountsAsync(user)).First(item => item.AccountType == "Expense" && item.AllowsPosting);
        foreach (var size in new[] { 2, 50, 500 })
        {
            var request = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
                new(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(-5)), "MANUAL_VOUCHER", "Borrador por lotes", null,
                Enumerable.Range(0, size).Select(index => new VoucherDraftLine(account.AccountId, null, null, "Partida", index % 2 == 0 ? 100 : 0, index % 2 == 1 ? 100 : 0)).ToArray(), null, null);
            using var counter = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog);
            var watch = Stopwatch.StartNew();
            var result = await service.SaveVoucherDraftAsync(user, request);
            watch.Stop();
            Assert.Equal(size, result.Lines.Count);
            Assert.Equal(4, counter.Count);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
            output.WriteLine($"Guardar {size} partidas: {counter.Count} SQL, {watch.ElapsedMilliseconds} ms.");
        }
    }
}
