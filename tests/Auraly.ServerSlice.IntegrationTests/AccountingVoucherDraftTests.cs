using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Payables;
using Auraly.Commerce.Taxation.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed partial class AccountingVoucherDraftTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Header_reference_reaches_the_source_and_auxiliary_without_changing_legacy_replays(bool legacyAcceptedSource)
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var store = scope.ServiceProvider.GetRequiredService<IAccountingStore>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
                AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var accounts = await service.ListAccountsAsync(user);
        var expenseCode = "519595" + BitConverter.ToUInt64(Guid.NewGuid().ToByteArray()).ToString("D20");
        var expense = await service.CreateAccountAsync(user,
            new(Guid.NewGuid(), fixture.TenantId, expenseCode, "Referencia del comprobante", "Expense", true, true));
        var bank = accounts.Single(account => account.Code == "111005");
        var reference = "REF-" + Guid.NewGuid().ToString("N");
        var at = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        var capture = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
            at, "MANUAL_VOUCHER", "Referencia del encabezado", reference, [
                new(expense.AccountId, fixture.SupplierPartyId, null, "Débito", 100, 0, LineId: Guid.NewGuid()),
                new(bank.AccountId, null, null, "Crédito", 0, 100, LineId: Guid.NewGuid())],
            null, null, MainPartyId: fixture.SupplierPartyId, PayloadVersion: 2);
        var saved = await service.SaveVoucherDraftAsync(user, capture);
        var command = new ConfirmManualAccountingVoucherRequest(saved.DocumentId, fixture.BusinessId, at,
            saved.ConceptCode, saved.Description, saved.Lines.Select(line => new ManualVoucherLineRequest(
                line.Value.AccountId!.Value, line.Value.PartyId, line.Value.CostCenterId, line.Value.Description,
                line.Value.Debit, line.Value.Credit, line.Value.LineId, line.Value.BaseAmount)).ToArray(),
            saved.MainPartyId, saved.PayloadVersion);
        if (legacyAcceptedSource)
        {
            // Reproduce the original immutable payload: the draft had a header
            // reference, but the old sender omitted it from the accepted contract.
            Assert.DoesNotContain("Reference", JsonSerializer.Serialize(command));
            await store.ConfirmManualVoucherAsync(user, command, default, saved.RowVersion);
            await scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>().ProcessAsync(
                saved.DocumentId, saved.DocumentType, fixture.BusinessId, default);
        }
        var legacySnapshot = legacyAcceptedSource ? await SnapshotAsync() : default;
        var sent = await service.SendVoucherDraftAsync(user, saved.DocumentId, new(saved.RowVersion));
        Assert.Equal("Posted", sent.Status);
        Assert.Equal(reference, sent.Reference);
        var snapshot = await SnapshotAsync();
        using (var source = JsonDocument.Parse(snapshot.Payload))
        {
            if (legacyAcceptedSource) Assert.False(source.RootElement.TryGetProperty("Reference", out _));
            else
            {
                Assert.True(source.RootElement.TryGetProperty("Reference", out var recordedReference),
                    "The accepted manual voucher must retain its captured header reference.");
                Assert.Equal(reference, recordedReference.GetString());
            }
        }
        if (legacyAcceptedSource) Assert.Equal(legacySnapshot, snapshot);
        var report = await service.GetAccountMovementsPageAsync(user, new(new(2026, 10, 22), new(2026, 10, 22),
            AccountFrom: expense.Code, AccountTo: expense.Code));
        var movement = Assert.Single(report.Items.Where(line => line.SourceDocumentId == saved.DocumentId));
        Assert.Equal(legacyAcceptedSource ? null : reference, movement.Reference);
        var json = JsonSerializer.SerializeToElement(sent, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(json.TryGetProperty("acceptedReference", out _));
        using (var counter = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog))
        {
            var replay = await service.SendVoucherDraftAsync(user, saved.DocumentId, new(saved.RowVersion));
            Assert.Equal(3, counter.Count);
            Assert.Equal(sent.RowVersion, replay.RowVersion);
            Assert.Equal(sent.Row.EntryId, replay.Row.EntryId);
        }
        Assert.Equal(snapshot, await SnapshotAsync());
        Assert.Equal(1, snapshot.Jobs);
        Assert.Equal(1, snapshot.Entries);
        await Assert.ThrowsAsync<AccountingConflictException>(() => store.ConfirmManualVoucherAsync(user,
            command with { Reference = "CAMBIADA" }, default, saved.RowVersion));
        await Assert.ThrowsAsync<AccountingValidationException>(() => service.ConfirmManualVoucherAsync(user,
            command with { VoucherId = Guid.NewGuid(), Reference = new string('R', 101) }));

        async Task<(string Payload, string Hash, int Jobs, int Entries)> SnapshotAsync()
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var query = new SqlCommand("""
                SELECT source.PayloadJson,CONVERT(varchar(64),source.PayloadHash,2),
                  (SELECT COUNT(*) FROM dbo.AccountingPostingJobs job WHERE job.TenantId=@TenantId
                    AND job.BusinessId=@BusinessId AND job.SourceDocumentId=@Id AND job.SourceDocumentType=@Type),
                  (SELECT COUNT(*) FROM dbo.AccountingEntries entry WHERE entry.TenantId=@TenantId
                    AND entry.BusinessId=@BusinessId AND entry.SourceDocumentId=@Id AND entry.SourceDocumentType=@Type)
                FROM dbo.AccountingSourceDocuments source WHERE source.TenantId=@TenantId
                  AND source.BusinessId=@BusinessId AND source.SourceDocumentId=@Id AND source.SourceDocumentType=@Type;
                """, connection);
            query.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            query.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            query.Parameters.AddWithValue("@Id", saved.DocumentId);
            query.Parameters.AddWithValue("@Type", saved.DocumentType);
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3));
        }
    }

    [Fact]
    public async Task Recorded_line_bases_survive_save_post_clone_and_auxiliary_pages_without_inventing_legacy_values()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
                AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var code = "519595" + BitConverter.ToUInt64(Guid.NewGuid().ToByteArray()).ToString("D20");
        var expense = await service.CreateAccountAsync(user,
            new(Guid.NewGuid(), fixture.TenantId, code, "Base registrada por partida", "Expense", true, true));
        var bank = (await service.ListAccountsAsync(user)).Single(account => account.Code == "111005");
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        var path = $"/api/commerce/v1/accounting/manual/drafts/{id}";
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read,
            AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        using var save = await client.PutAsJsonAsync(path, new {
            DocumentId = id, DocumentType = AccountingManualDocumentTypes.ManualVoucher,
            OccurredAt = at, ConceptCode = "MANUAL_VOUCHER", Description = "Bases explícitas", PayloadVersion = 2,
            Lines = new[] {
                new { AccountId = expense.AccountId, PartyId = (Guid?)fixture.SupplierPartyId, Description = "Base conocida",
                    Debit = 100m, Credit = 0m, LineId = Guid.NewGuid(), BaseAmount = (decimal?)123.4567m },
                new { AccountId = expense.AccountId, PartyId = (Guid?)fixture.SupplierPartyId, Description = "Base cero explícita",
                    Debit = 100m, Credit = 0m, LineId = Guid.NewGuid(), BaseAmount = (decimal?)0m },
                new { AccountId = expense.AccountId, PartyId = (Guid?)fixture.SupplierPartyId, Description = "Base con signo registrado",
                    Debit = 100m, Credit = 0m, LineId = Guid.NewGuid(), BaseAmount = (decimal?)-23.4567m },
                new { AccountId = bank.AccountId, PartyId = (Guid?)null, Description = "Contrapartida sin base",
                    Debit = 0m, Credit = 300m, LineId = Guid.NewGuid(), BaseAmount = (decimal?)null } }
        });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        using var savedJson = JsonDocument.Parse(await save.Content.ReadAsStringAsync());
        Assert.True(savedJson.RootElement.GetProperty("lines")[0].GetProperty("value")
            .TryGetProperty("baseAmount", out var storedBase), "La captura debe conservar la base explícita de la partida.");
        Assert.Equal(123.4567m, storedBase.GetDecimal());
        var saved = savedJson.RootElement.Deserialize<VoucherDraftView>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        using var send = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion));
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        using var entry = await client.GetAsync($"/api/commerce/v1/accounting/entries/by-document/{id}");
        entry.EnsureSuccessStatusCode();
        using var entryJson = JsonDocument.Parse(await entry.Content.ReadAsStringAsync());
        Assert.Equal(123.4567m, entryJson.RootElement.GetProperty("lines")[0].GetProperty("baseAmount").GetDecimal());
        Assert.Equal(0m, entryJson.RootElement.GetProperty("lines")[1].GetProperty("baseAmount").GetDecimal());
        Assert.Equal(-23.4567m, entryJson.RootElement.GetProperty("lines")[2].GetProperty("baseAmount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, entryJson.RootElement.GetProperty("lines")[3].GetProperty("baseAmount").ValueKind);
        var copy = await service.CloneVoucherDraftAsync(user, id, new(Guid.NewGuid(), at));
        var copyJson = JsonSerializer.SerializeToElement(copy, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(123.4567m, copyJson.GetProperty("lines")[0].GetProperty("value").GetProperty("baseAmount").GetDecimal());
        Assert.Equal(0m, copyJson.GetProperty("lines")[1].GetProperty("value").GetProperty("baseAmount").GetDecimal());
        Assert.Equal(-23.4567m, copyJson.GetProperty("lines")[2].GetProperty("value").GetProperty("baseAmount").GetDecimal());
        var expectedBases = new[] { 123.4567m, 0m, -23.4567m };
        for (var page = 1; page <= 3; page++)
        {
            using var counter = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog);
            var report = await service.GetAccountMovementsPageAsync(user, new(new(2026, 10, 22), new(2026, 10, 22),
                page, 1, code, code));
            Assert.Equal(1, counter.Count);
            Assert.Equal(3, report.TotalCount);
            Assert.Equal(100m, Assert.Single(report.Accounts).BaseAmount);
            Assert.Equal(expectedBases[page - 1], Assert.Single(report.Items).BaseAmount);
        }
        var legacy = new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), fixture.BusinessId, at,
            "MANUAL_VOUCHER", "Sin evidencia de base", [
                new(expense.AccountId, fixture.SupplierPartyId, null, "Histórico sin base", 10m, 0m),
                new(bank.AccountId, null, null, "Contrapartida", 0m, 10m)]);
        Assert.DoesNotContain("BaseAmount", JsonSerializer.Serialize(legacy));
        await service.ConfirmManualVoucherAsync(user, legacy);
        var mixed = await service.GetAccountMovementsPageAsync(user, new(new(2026, 10, 22), new(2026, 10, 22),
            AccountFrom: code, AccountTo: code));
        Assert.Equal(4, mixed.TotalCount);
        Assert.Null(Assert.Single(mixed.Accounts).BaseAmount);
        Assert.Null(Assert.Single(mixed.Items.Where(line => line.SourceDocumentId == legacy.VoucherId)).BaseAmount);
        Assert.Equal(310m, mixed.Accounts[0].Debit);
    }

    [Fact]
    public async Task Direct_recorded_bases_are_immutable_on_replay_and_survive_cloning_a_posted_entry_without_a_draft()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
                AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var accounts = await service.ListAccountsAsync(user);
        var request = new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), fixture.BusinessId,
            new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Base explícita por API", [
                new(accounts.Single(account => account.Code == "519595").AccountId, fixture.SupplierPartyId, null,
                    "Base firmada", 100m, 0m, BaseAmount: -123.4567m),
                new(accounts.Single(account => account.Code == "111005").AccountId, null, null,
                    "Contrapartida", 0m, 100m)]);
        await service.ConfirmManualVoucherAsync(user, request);
        Assert.Null(await service.GetVoucherDraftAsync(user, request.VoucherId));
        var entry = await service.GetEntryAsync(user, request.VoucherId);
        Assert.Equal(-123.4567m, entry!.Lines[0].BaseAmount);
        Assert.Null(entry.Lines[1].BaseAmount);
        var replay = await service.ConfirmManualVoucherAsync(user, request);
        Assert.True(replay.IsDuplicate);
        Assert.Equal(entry.EntryId, (await service.GetEntryAsync(user, request.VoucherId))!.EntryId);
        await Assert.ThrowsAsync<AccountingConflictException>(() => service.ConfirmManualVoucherAsync(user,
            request with { Lines = request.Lines.Select((line, index) => index == 0 ? line with { BaseAmount = 0m } : line).ToArray() }));
        var clone = await service.CloneVoucherDraftAsync(user, request.VoucherId, new(Guid.NewGuid(), request.OccurredAt));
        Assert.Equal(-123.4567m, clone.Lines[0].Value.BaseAmount);
        Assert.Null(clone.Lines[1].Value.BaseAmount);
        Assert.Null(clone.SentAt);
    }

    [Theory]
    [InlineData("0.00005", "0.00005", "0.0001", HttpStatusCode.BadRequest, null)]
    [InlineData("0.00004", null, "0.00004", HttpStatusCode.BadRequest, null)]
    [InlineData("1.00004", "1.00004", "2.0000", HttpStatusCode.Accepted, "2.0000")]
    [InlineData("0.00005", null, "0.00005", HttpStatusCode.Accepted, "0.0001")]
    [InlineData("1000000000000000", null, "1000000000000000", HttpStatusCode.BadRequest, null)]
    public async Task Direct_manual_acceptance_uses_the_same_line_precision_and_balance_rule_as_posting(
        string firstDebit, string? secondDebit, string credit, HttpStatusCode expectedStatus, string? postedTotal)
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var accounts = await service.ListAccountsAsync(user);
        var debitAccount = accounts.Single(account => account.Code == "519595").AccountId;
        var creditAccount = accounts.Single(account => account.Code == "111005").AccountId;
        var lines = new List<ManualVoucherLineRequest>
        {
            new(debitAccount, fixture.SupplierPartyId, null, "Primera partida", Parse(firstDebit), 0)
        };
        if (secondDebit is not null)
            lines.Add(new(debitAccount, fixture.SupplierPartyId, null, "Segunda partida", Parse(secondDebit), 0));
        lines.Add(new(creditAccount, null, null, "Contrapartida", 0, Parse(credit)));
        var id = Guid.NewGuid();
        var command = new ConfirmManualAccountingVoucherRequest(id, fixture.BusinessId,
            new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Precisión por partida", lines);
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read,
            AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        using var response = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers", command);
        Assert.Equal(expectedStatus, response.StatusCode);
        if (postedTotal is not null)
        {
            var entry = await client.GetFromJsonAsync<AccountingEntryView>($"/api/commerce/v1/accounting/entries/by-document/{id}");
            Assert.NotNull(entry);
            Assert.Equal(Parse(postedTotal), entry.DebitTotal);
            Assert.Equal(entry.DebitTotal, entry.CreditTotal);
        }
        else
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var absent = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id),
                  (SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id),
                  (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id);
                """, connection);
            absent.Parameters.AddWithValue("@Id", id);
            await using var reader = await absent.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            for (var column = 0; column < 3; column++) Assert.Equal(0, reader.GetInt32(column));
        }

        static decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task A_directly_accepted_source_cannot_be_reopened_as_a_new_editable_draft()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate,
                AccountingPermissionCodes.ManualSend, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var accounts = await service.ListAccountsAsync(user);
        var command = new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), fixture.BusinessId,
            new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Comprobante directo", [
                new(accounts.First(account => account.Code == "519595").AccountId, fixture.SupplierPartyId, null, "Servicio", 100, 0),
                new(accounts.First(account => account.Code == "111005").AccountId, null, null, "Banco", 0, 100)]);
        await service.ConfirmManualVoucherAsync(user, command);
        var entry = await service.GetEntryAsync(user, command.VoucherId);
        Assert.NotNull(entry);
        await Assert.ThrowsAsync<AccountingConflictException>(() => service.SaveVoucherDraftAsync(user, new(command.VoucherId,
            AccountingManualDocumentTypes.ManualVoucher, command.OccurredAt, command.ConceptCode, "Intento de reabrir fuente aceptada", null,
            command.Lines.Select(line => new VoucherDraftLine(line.AccountId, line.PartyId, line.CostCenterId,
                line.Description, line.Debit, line.Credit)).ToArray(), null, null)));
        Assert.Null(await service.GetVoucherDraftAsync(user, command.VoucherId));
        Assert.Equal(entry.EntryId, (await service.GetEntryAsync(user, command.VoucherId))!.EntryId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Accepted_five_hundred_character_descriptions_survive_the_canonical_posting(
        bool fromDraft, bool longLine)
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var accounts = await service.ListAccountsAsync(user);
        var description = longLine ? "Encabezado" : new string('ñ', 500);
        var lineDescription = longLine ? new string('ñ', 500) : "Partida";
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var lines = new ManualVoucherLineRequest[]
        {
            new(accounts.Single(account => account.Code == "519595").AccountId,
                fixture.SupplierPartyId, null, lineDescription, 100m, 0m),
            new(accounts.Single(account => account.Code == "111005").AccountId,
                null, null, "Contrapartida", 0m, 100m)
        };
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read,
            AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        if (fromDraft)
        {
            var path = $"/api/commerce/v1/accounting/manual/drafts/{id}";
            using var save = await client.PutAsJsonAsync(path, new SaveVoucherDraftRequest(id,
                AccountingManualDocumentTypes.ManualVoucher, at, "MANUAL_VOUCHER", description, null,
                lines.Select(line => new VoucherDraftLine(line.AccountId, line.PartyId, line.CostCenterId,
                    line.Description, line.Debit, line.Credit, LineId: Guid.NewGuid())).ToArray(), null, null, PayloadVersion: 2));
            Assert.Equal(HttpStatusCode.OK, save.StatusCode);
            var saved = await save.Content.ReadFromJsonAsync<VoucherDraftView>();
            Assert.Equal(description, saved!.Description);
            Assert.Equal(lineDescription, saved.Lines[0].Value.Description);
            using var send = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion));
            Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        }
        else
        {
            using var accepted = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers",
                new ConfirmManualAccountingVoucherRequest(id, fixture.BusinessId, at,
                    "MANUAL_VOUCHER", description, lines));
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }
        var entry = await client.GetFromJsonAsync<AccountingEntryView>($"/api/commerce/v1/accounting/entries/by-document/{id}");
        Assert.NotNull(entry);
        Assert.Equal(description, entry.Description);
        Assert.Equal(lineDescription, entry.Lines[0].Description);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sent_draft_replay_detects_lost_acceptance_without_repairing_it(bool missingSource)
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var store = scope.ServiceProvider.GetRequiredService<IAccountingStore>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate,
                AccountingPermissionCodes.ManualSend, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var accounts = await service.ListAccountsAsync(user);
        var saved = await service.SaveVoucherDraftAsync(user, new(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
            new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Aceptación para probar integridad", null, [
                new(accounts.First(account => account.Code == "519595").AccountId, fixture.SupplierPartyId, null, "Servicio", 100, 0, LineId: Guid.NewGuid()),
                new(accounts.First(account => account.Code == "111005").AccountId, null, null, "Banco", 0, 100, LineId: Guid.NewGuid())],
            null, null, PayloadVersion: 2));
        var command = new ConfirmManualAccountingVoucherRequest(saved.DocumentId, fixture.BusinessId, saved.OccurredAt,
            saved.ConceptCode, saved.Description, saved.Lines.Select(line => new ManualVoucherLineRequest(line.Value.AccountId!.Value,
                line.Value.PartyId, line.Value.CostCenterId, line.Value.Description, line.Value.Debit, line.Value.Credit, line.Value.LineId)).ToArray(),
            saved.MainPartyId, saved.PayloadVersion);
        await store.ConfirmManualVoucherAsync(user, command, default, saved.RowVersion);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        try
        {
            await using (var corrupt = new SqlCommand("""
                UPDATE dbo.AccountingSourceDocuments SET AccountingPostingJobId=NULL WHERE SourceDocumentId=@Id;
                DELETE dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id;
                IF @MissingSource=1
                BEGIN
                  DELETE accounting.SourcePartyIndex WHERE SourceDocumentId=@Id;
                  DELETE dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id;
                END;
                """, connection))
            {
                corrupt.Parameters.AddWithValue("@Id", saved.DocumentId);
                corrupt.Parameters.AddWithValue("@MissingSource", missingSource);
                await corrupt.ExecuteNonQueryAsync();
            }
            await Assert.ThrowsAsync<AccountingConflictException>(() => service.SendVoucherDraftAsync(user, saved.DocumentId, new(saved.RowVersion)));
            await using var verify = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id),
                  (SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id),
                  (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id);
                """, connection);
            verify.Parameters.AddWithValue("@Id", saved.DocumentId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(missingSource ? 0 : 1, reader.GetInt32(0));
            Assert.Equal(0, reader.GetInt32(1));
            Assert.Equal(0, reader.GetInt32(2));
        }
        finally
        {
            await using var cleanup = new SqlCommand("""
                DELETE accounting.VoucherDraftLines WHERE DocumentId=@Id;
                DELETE accounting.VoucherDrafts WHERE DocumentId=@Id;
                DELETE accounting.SourcePartyIndex WHERE SourceDocumentId=@Id;
                DELETE dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id;
                DELETE dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id;
                """, connection);
            cleanup.Parameters.AddWithValue("@Id", saved.DocumentId);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Identified_lines_survive_reordering_and_posting_and_clones_get_new_identities()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate,
                AccountingPermissionCodes.ManualSend, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var accounts = await service.ListAccountsAsync(user);
        var request = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
            new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Identidades de partidas", null, [
                new(accounts.First(account => account.Code == "519595").AccountId, fixture.SupplierPartyId, null, "Servicio", 100, 0, LineId: Guid.NewGuid()),
                new(accounts.First(account => account.Code == "111005").AccountId, null, null, "Banco", 0, 100, LineId: Guid.NewGuid())],
            null, null, PayloadVersion: 2);
        var saved = await service.SaveVoucherDraftAsync(user, request);
        var reversed = request with { RowVersion = saved.RowVersion, Lines = request.Lines.Reverse().ToArray() };
        saved = await service.SaveVoucherDraftAsync(user, reversed);
        Assert.Equal(reversed.Lines.Select(line => line.LineId), saved.Lines.Select(line => line.Value.LineId));
        await Assert.ThrowsAsync<AccountingConflictException>(() => service.SaveVoucherDraftAsync(user, reversed with {
            RowVersion = saved.RowVersion, PayloadVersion = null, Lines = reversed.Lines.Select(line => line with { LineId = null }).ToArray() }));
        foreach (var invalid in new[] {
            reversed with { PayloadVersion = 3 },
            reversed with { Lines = reversed.Lines.Select(line => line with { LineId = reversed.Lines[0].LineId }).ToArray() },
            reversed with { Lines = [reversed.Lines[0] with { LineId = null }, reversed.Lines[1]] },
            reversed with { Lines = [reversed.Lines[0] with { LineId = Guid.Empty }, reversed.Lines[1]] } })
            await Assert.ThrowsAsync<AccountingValidationException>(() => service.SaveVoucherDraftAsync(user, invalid with { RowVersion = saved.RowVersion }));
        var copy = await service.CloneVoucherDraftAsync(user, saved.DocumentId, new(Guid.NewGuid(), saved.OccurredAt));
        Assert.Empty(copy.Lines.Select(line => line.Value.LineId).Intersect(saved.Lines.Select(line => line.Value.LineId)));
        var sent = await service.SendVoucherDraftAsync(user, saved.DocumentId, new(saved.RowVersion));
        var entry = await service.GetEntryAsync(user, saved.DocumentId);
        Assert.NotNull(entry);
        Assert.Equal(reversed.Lines.Select(line => line.LineId), entry.Lines.Select(line => line.SourceLineId));
        var replay = await service.SendVoucherDraftAsync(user, saved.DocumentId, new(saved.RowVersion));
        Assert.Equal(sent.RowVersion, replay.RowVersion);
        Assert.Equal(entry.EntryId, replay.Row.EntryId);
    }

    [Fact]
    public async Task Clone_reuses_accounts_without_reusing_execution_references_and_replays_the_authoritative_draft()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate,
                AccountingPermissionCodes.ManualSend, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var accounts = await service.ListAccountsAsync(user);
        var expense = accounts.First(account => account.Code == "519595");
        var bank = accounts.First(account => account.Code == "111005");
        var original = await service.SaveVoucherDraftAsync(user, new(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
            new(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(-5)), "MANUAL_VOUCHER", "Servicios del mes", "FACTURA-ANTERIOR",
            [new(expense.AccountId, fixture.SupplierPartyId, null, "Servicio", 900, 0, "FACTURA-ANTERIOR"),
             new(bank.AccountId, fixture.SupplierPartyId, null, "Contrapartida", 0, 900, "REFERENCIA-BANCARIA-ANTERIOR")], null, null, fixture.SupplierPartyId));
        var command = new CloneVoucherDraftRequest(Guid.NewGuid(), new(2026, 10, 20, 12, 0, 0, TimeSpan.FromHours(-5)));
        var copies = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => service.CloneVoucherDraftAsync(user, original.DocumentId, command)));
        var copy = copies[0];
        Assert.Equal(copy.DocumentId, copies[1].DocumentId);
        Assert.Equal(copy.RowVersion, copies[1].RowVersion);
        Assert.Equal(original.DocumentId, copy.ClonedFromDocumentId);
        Assert.Equal(command.OccurredAt, copy.OccurredAt);
        Assert.Equal(original.MainPartyId, copy.MainPartyId);
        Assert.Null(copy.Reference);
        Assert.Null(copy.SentAt);
        Assert.Null(copy.Adjustment);
        Assert.All(copy.Lines, line => Assert.Null(line.Value.Reference));
        Assert.Equal(original.Lines.Select(line => line.Value with { Reference = null, LineId = null }),
            copy.Lines.Select(line => line.Value with { LineId = null }));
        Assert.Equal(2, copy.PayloadVersion);
        Assert.All(copy.Lines, line => Assert.NotNull(line.Value.LineId));
        Assert.Equal(copy.Lines.Count, copy.Lines.Select(line => line.Value.LineId).Distinct().Count());
        copy = await service.SaveVoucherDraftAsync(user, new(copy.DocumentId, copy.DocumentType, copy.OccurredAt, copy.ConceptCode,
            "Servicios del mes siguiente", "FACTURA-NUEVA", copy.Lines.Select(line => line.Value).ToArray(), null,
            copy.RowVersion, copy.MainPartyId, copy.ClonedFromDocumentId, copy.PayloadVersion));
        using (var counter = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog))
        {
            var replay = await service.CloneVoucherDraftAsync(user, original.DocumentId, command);
            Assert.Equal(copy.RowVersion, replay.RowVersion);
            Assert.Equal("FACTURA-NUEVA", replay.Reference);
            Assert.Equal(1, counter.Count);
        }
        await Assert.ThrowsAsync<AccountingConflictException>(() => service.CloneVoucherDraftAsync(user, Guid.NewGuid(), command));
        await Assert.ThrowsAsync<AccountingConflictException>(() => service.CloneVoucherDraftAsync(user with { TenantId = Guid.NewGuid() }, original.DocumentId,
            command with { DocumentId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<AccountingForbiddenException>(() => service.CloneVoucherDraftAsync(user with {
            Permissions = new HashSet<string> { AccountingPermissionCodes.ManualCreate } }, original.DocumentId, command));
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var inactiveCopyId = Guid.NewGuid();
        try
        {
            await using (var deactivate = new SqlCommand("UPDATE dbo.AccountingAccounts SET IsActive=0 WHERE AccountId=@Id", connection))
            {
                deactivate.Parameters.AddWithValue("@Id", expense.AccountId);
                await deactivate.ExecuteNonQueryAsync();
            }
            var inactive = await service.CloneVoucherDraftAsync(user, original.DocumentId, command with { DocumentId = inactiveCopyId });
            Assert.Equal(expense.AccountId, inactive.Lines[0].Value.AccountId);
            Assert.NotNull(inactive.Lines[0].ValidationError);
            await Assert.ThrowsAsync<AccountingValidationException>(() => service.SendVoucherDraftAsync(user, inactive.DocumentId, new(inactive.RowVersion)));
        }
        finally
        {
            await using var restore = new SqlCommand("UPDATE dbo.AccountingAccounts SET IsActive=1 WHERE AccountId=@Id", connection);
            restore.Parameters.AddWithValue("@Id", expense.AccountId);
            await restore.ExecuteNonQueryAsync();
        }
        await using (var effects = new SqlCommand("""
            SELECT (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId IN(@Copy,@Inactive)),
              (SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId IN(@Copy,@Inactive)),
              (SELECT COUNT(*) FROM dbo.DocumentProcessingJobs WHERE DocumentId IN(@Copy,@Inactive)),
              (SELECT COUNT(*) FROM dbo.FiscalDocuments WHERE DocumentId IN(@Copy,@Inactive));
            """, connection))
        {
            effects.Parameters.AddWithValue("@Copy", copy.DocumentId);
            effects.Parameters.AddWithValue("@Inactive", inactiveCopyId);
            await using var reader = await effects.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            for (var column = 0; column < 4; column++) Assert.Equal(0, reader.GetInt32(column));
        }
        var postedSourceId = Guid.NewGuid();
        var mentionedPartyId = Guid.NewGuid();
        var postedRequest = new ConfirmManualAccountingVoucherRequest(postedSourceId, fixture.BusinessId, command.OccurredAt,
            "MANUAL_VOUCHER", $"Origen sin borrador: {mentionedPartyId:D}", original.Lines.Select(line => new ManualVoucherLineRequest(
                line.Value.AccountId!.Value, null, line.Value.CostCenterId, line.Value.Description,
                line.Value.Debit, line.Value.Credit)).ToArray(), fixture.SupplierPartyId);
        await service.ConfirmManualVoucherAsync(user, postedRequest);
        var headerOnly = await service.ListDocumentsAsync(user, new(2026, 1, 1), new(2026, 12, 31),
            null, null, postedSourceId.ToString(), 1, 25, partyId: fixture.SupplierPartyId);
        Assert.Equal(fixture.SupplierPartyId, Assert.Single(headerOnly.Items).MainPartyId);
        var headerLines = await service.ListFinancialTraceabilityLinesAsync(user, new(2026, 1, 1), new(2026, 12, 31),
            null, null, postedSourceId.ToString(), 1, 25, partyId: fixture.SupplierPartyId);
        Assert.Equal(2, headerLines.TotalCount);
        var mentionedOnly = await service.ListDocumentsAsync(user, new(2026, 1, 1), new(2026, 12, 31),
            null, null, postedSourceId.ToString(), 1, 25, partyId: mentionedPartyId);
        Assert.Empty(mentionedOnly.Items);
        await Assert.ThrowsAsync<AccountingConflictException>(() => service.ConfirmManualVoucherAsync(
            user with { TenantId = Guid.NewGuid() }, postedRequest));
        AccountingEntryView? entry = null;
        for (var attempt = 0; attempt < 50 && entry is null; attempt++)
        {
            entry = await service.GetEntryAsync(user, postedSourceId);
            if (entry is null) await Task.Delay(100);
        }
        Assert.NotNull(entry);
        var postedCopy = await service.CloneVoucherDraftAsync(user, postedSourceId, command with { DocumentId = Guid.NewGuid() });
        Assert.Equal(fixture.SupplierPartyId, postedCopy.MainPartyId);
        Assert.Equal(entry.Lines.Select(line => line.AccountId), postedCopy.Lines.Select(line => line.Value.AccountId!.Value));
        Assert.Equal(postedSourceId, postedCopy.ClonedFromDocumentId);
        Assert.Null(postedCopy.SentAt);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Pending_adjustments_reserve_the_obligation_without_applying_it_twice(bool savedDraft, bool legacyReference)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate,
            AccountingPermissionCodes.ManualSend, AccountingPermissionCodes.Retry, ExpensePermissionCodes.Create,
            TaxationPermissionCodes.ManageWithholdingRules, TaxationPermissionCodes.ViewWithholdingRules,
            PayablesPermissionCodes.RegisterPayment, PayablesPermissionCodes.Read);
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
                new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
                    AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate });
            await service.EnsureDefaultsAsync(user);
            await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
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
            await using (var provision = new SqlCommand("dbo.BusinessDocumentSeriesDefaultsProvision", connection)
                { CommandType = System.Data.CommandType.StoredProcedure })
            {
                provision.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                await provision.ExecuteNonQueryAsync();
            }
            Guid? obligationId = null;
            for (var attempt = 0; attempt < 50 && obligationId is null; attempt++)
            {
                await using var find = new SqlCommand("SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id", connection);
                find.Parameters.AddWithValue("@Id", expense.ExpenseId);
                obligationId = await find.ExecuteScalarAsync() as Guid?;
                if (obligationId is null) await Task.Delay(100);
            }
            Assert.NotNull(obligationId);
            var request = new SaveVoucherDraftRequest(Guid.NewGuid(), "AccountAdjustment", date, "ACCOUNT_ADJUSTMENT", "Ajuste de prueba", "AJ-REF-" + Guid.NewGuid().ToString("N"),
                [], new("Payable", obligationId.Value, "Decrease", 700, account.AccountId, null), null);
            var first = await service.SaveVoucherDraftAsync(user, request);
            var second = await service.SaveVoucherDraftAsync(user, request with { DocumentId = Guid.NewGuid() });
            // Accept directly at the canonical store so the native worker has not yet applied the reservation.
            var firstCommand = Command(first) with { AdjustmentId = savedDraft ? first.DocumentId : Guid.NewGuid() };
            var secondCommand = Command(second) with { AdjustmentId = savedDraft ? second.DocumentId : Guid.NewGuid() };
            var firstVersion = savedDraft ? first.RowVersion : null;
            var secondVersion = savedDraft ? second.RowVersion : null;
            var candidates = await Task.WhenAll(AcceptAsync(firstCommand, firstVersion), AcceptAsync(secondCommand, secondVersion));
            Assert.Equal(1, candidates.Count(accepted => accepted));
            if (!candidates[0])
            {
                (firstCommand, secondCommand) = (secondCommand, firstCommand);
                (firstVersion, secondVersion) = (secondVersion, firstVersion);
            }
            var replay = await store.ConfirmAccountAdjustmentAsync(user, firstCommand, CancellationToken.None,
                firstVersion);
            Assert.True(replay.IsDuplicate);
            await Assert.ThrowsAsync<AccountingConflictException>(() => store.ConfirmAccountAdjustmentAsync(user,
                firstCommand with { Reference = "CAMBIADA" }, CancellationToken.None, firstVersion));
            await Assert.ThrowsAsync<AccountingConflictException>(() => store.ConfirmAccountAdjustmentAsync(user,
                secondCommand, CancellationToken.None, secondVersion));
            await using (var reserved = new SqlCommand("""
                IF @Failed=1
                    UPDATE dbo.AccountingPostingJobs
                    SET Status=N'AccountingPendingConfiguration',LastErrorCode=N'FIXTURE_CONFIGURATION',
                        LastErrorMessage=N'Fallo de configuración simulado para verificar la reserva'
                    WHERE SourceDocumentId=@DocumentId AND BusinessId=@BusinessId;
                SELECT PendingAdjustmentAmount,PendingPaymentAmount
                FROM accounting.ObligationAvailability(@BusinessId,N'Payable',@Id,1000,NULL);
                """, connection))
            {
                reserved.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                reserved.Parameters.AddWithValue("@Id", obligationId.Value);
                reserved.Parameters.AddWithValue("@DocumentId", firstCommand.AdjustmentId);
                reserved.Parameters.AddWithValue("@Failed", !savedDraft);
                await using var reservation = await reserved.ExecuteReaderAsync();
                Assert.True(await reservation.ReadAsync());
                Assert.Equal(700m, reservation.GetDecimal(0));
                Assert.Equal(0m, reservation.GetDecimal(1));
            }
            var payment = new ConfirmSupplierPaymentRequest(Guid.NewGuid(), fixture.BusinessId, fixture.SupplierId,
                date.AddDays(1), "COP", "No consumir reserva aceptada", [new(obligationId.Value, 700m)],
                [new(SupplierPaymentMethods.Cash, 700m, 700m)]);
            var positionPath = $"/api/commerce/v1/accounting/open-positions?subledgerKind=Payable&subledgerId={obligationId.Value:D}&pageSize=1";
            var positions = (await client.GetFromJsonAsync<AccountingOpenPositionPage>(positionPath))!;
            var position = Assert.Single(positions.Items);
            Assert.Equal(1, positions.TotalCount);
            Assert.Equal(1000m, positions.TotalOutstandingAmount);
            Assert.Equal(fixture.SupplierPartyId, position.PartyId);
            Assert.Equal(fixture.SupplierId, position.CounterpartyRoleId);
            Assert.Equal(expense.ExpenseId, position.SourceDocumentId);
            Assert.Equal(1000m, position.OutstandingAmount);
            Assert.Equal(700m, position.PendingAdjustmentAmount);
            Assert.Equal(300m, position.AvailableAmount);
            Assert.NotNull(position.AccountId);
            Assert.Null(position.OpeningLinkIssue);
            Assert.Equal(1000m, position.OpeningCredit);
            var beyond = (await client.GetFromJsonAsync<AccountingOpenPositionPage>(positionPath + "&page=2"))!;
            Assert.Empty(beyond.Items);
            Assert.Equal(positions.TotalCount, beyond.TotalCount);
            Assert.Equal(positions.TotalOutstandingAmount, beyond.TotalOutstandingAmount);
            var accountFiltered = (await client.GetFromJsonAsync<AccountingOpenPositionPage>(positionPath + $"&accountId={position.AccountId:D}"))!;
            Assert.Equal(position.SubledgerId, Assert.Single(accountFiltered.Items).SubledgerId);
            var wrongParty = (await client.GetFromJsonAsync<AccountingOpenPositionPage>(positionPath + $"&partyId={Guid.NewGuid():D}"))!;
            Assert.Empty(wrongParty.Items);
            using (var nativeReader = fixture.CreateAdminClient(PayablesPermissionCodes.Read))
            {
                using var allowed = await nativeReader.GetAsync(positionPath);
                allowed.EnsureSuccessStatusCode();
                using var forbidden = await nativeReader.GetAsync(positionPath.Replace("subledgerKind=Payable", "subledgerKind=Receivable", StringComparison.Ordinal));
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            }
            using (var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/payable-payments/confirm")
                { Content = JsonContent.Create(payment) })
            {
                message.Headers.Add("Idempotency-Key", payment.PaymentId.ToString("N"));
                using var rejected = await client.SendAsync(message);
                Assert.True(rejected.StatusCode == HttpStatusCode.Conflict,
                    await rejected.Content.ReadAsStringAsync());
            }
            using var posting = await client.PostAsync($"/api/commerce/v1/accounting/postings/{firstCommand.AdjustmentId}/retry", null);
            Assert.True(posting.IsSuccessStatusCode, await posting.Content.ReadAsStringAsync());
            using var repeat = await client.PostAsync($"/api/commerce/v1/accounting/postings/{firstCommand.AdjustmentId}/retry", null);
            repeat.EnsureSuccessStatusCode();
            await using var balance = new SqlCommand("""
                SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id;
                SELECT COUNT(*) FROM dbo.PayableTransactions WHERE SourceDocumentId=@DocumentId;
                SELECT PendingAdjustmentAmount FROM accounting.ObligationAvailability(@BusinessId,N'Payable',@Id,300,NULL);
                SELECT COUNT(*) FROM dbo.SupplierPayments WHERE PaymentId=@PaymentId;
                """, connection);
            balance.Parameters.AddWithValue("@Id", obligationId.Value);
            balance.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            balance.Parameters.AddWithValue("@PaymentId", payment.PaymentId);
            balance.Parameters.AddWithValue("@DocumentId", firstCommand.AdjustmentId);
            await using var reader = await balance.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync()); Assert.Equal(300m, reader.GetDecimal(0));
            await reader.NextResultAsync(); Assert.True(await reader.ReadAsync()); Assert.Equal(1, reader.GetInt32(0));
            await reader.NextResultAsync(); Assert.True(await reader.ReadAsync()); Assert.Equal(0m, reader.GetDecimal(0));
            await reader.NextResultAsync(); Assert.True(await reader.ReadAsync()); Assert.Equal(0, reader.GetInt32(0));

            if (savedDraft)
            {
                var entry = (await service.GetEntryAsync(user, firstCommand.AdjustmentId))!;
                VoucherDraftView resent;
                using (var counter = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog))
                {
                    resent = await service.SendVoucherDraftAsync(user, firstCommand.AdjustmentId, new(firstVersion!));
                    Assert.Equal(3, counter.Count);
                }
                Assert.Equal("Posted", resent.Status);
                Assert.Equal(request.Reference, resent.Reference);
                Assert.Equal(firstCommand.Reference, resent.AcceptedReference);
                Assert.Equal(entry.EntryId, resent.Row.EntryId);
            }

            ConfirmAccountAdjustmentRequest Command(VoucherDraftView value) => new(value.DocumentId, fixture.BusinessId, "Payable",
                obligationId.Value, "Decrease", value.Adjustment!.Amount, account.AccountId, null, value.OccurredAt, value.ConceptCode, value.Description,
                legacyReference ? null : value.Reference);

            async Task<bool> AcceptAsync(ConfirmAccountAdjustmentRequest command, string? version)
            {
                try
                {
                    await store.ConfirmAccountAdjustmentAsync(user, command, CancellationToken.None, version);
                    return true;
                }
                catch (AccountingConflictException) { return false; }
            }
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
        await Assert.ThrowsAsync<AccountingValidationException>(() => service.SaveVoucherDraftAsync(user,
            request with { MainPartyId = Guid.NewGuid() }));
        request = request with { MainPartyId = fixture.SupplierPartyId };
        var saved = await Save(request);
        Assert.Equal(fixture.SupplierPartyId, saved.MainPartyId);
        Assert.Equal("Proveedor E2E", saved.MainPartyName);
        // This fixture's canonical Party is incomplete; do not invent its identification
        // from the legacy supplier row when displaying the principal.
        Assert.Null(saved.MainPartyIdentification);
        Assert.Equal("Created", saved.Status);
        Assert.True(saved.CanEdit);
        Assert.Null(saved.SentAt);
        var headerOnly = (await client.GetFromJsonAsync<AccountingDocumentPage>($"/api/commerce/v1/accounting/documents?from=2026-01-01&to=2026-12-31&status=Created&partyId={fixture.SupplierPartyId}&search={request.Reference}"))!;
        var headerRow = Assert.Single(headerOnly.Items);
        Assert.Equal(fixture.SupplierPartyId, headerRow.MainPartyId);
        Assert.Equal(saved.MainPartyName, headerRow.MainPartyName);
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
        var reportSecondPage = (await client.GetFromJsonAsync<FinancialTraceabilityLinePage>($"/api/commerce/v1/accounting/reports/financial-traceability-lines?from=2026-01-01&to=2026-12-31&status=Created&partyId={fixture.SupplierPartyId}&search={request.Reference}&page=2&pageSize=1"))!;
        Assert.Equal(2, reportSecondPage.TotalCount);
        Assert.Equal(2, Assert.Single(reportSecondPage.Items).LineNumber);
        using (var bypass = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers",
            new ConfirmManualAccountingVoucherRequest(request.DocumentId, fixture.BusinessId, request.OccurredAt,
                request.ConceptCode, request.Description, request.Lines.Select(line => new ManualVoucherLineRequest(
                    line.AccountId!.Value, line.PartyId, line.CostCenterId, line.Description, line.Debit, line.Credit)).ToArray())))
            Assert.False(bypass.IsSuccessStatusCode);
        var sends = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion))));
        foreach (var response in sends) { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); response.Dispose(); }
        var sent = (await client.GetFromJsonAsync<VoucherDraftView>(path))!;
        Assert.NotNull(sent.SentAt);
        Assert.Equal(fixture.SupplierPartyId, sent.MainPartyId);
        Assert.Equal(saved.MainPartyName, sent.MainPartyName);
        Assert.Null(sent.Lines[1].Value.PartyId);
        Assert.False(sent.CanEdit);
        using (var frozen = await client.PutAsJsonAsync(path, request with { RowVersion = sent.RowVersion }))
            Assert.Equal(HttpStatusCode.Conflict, frozen.StatusCode);
        await AssertEffectCounts(1);
        // Read the draft, lock its acceptance scope and invoke the canonical
        // source/job integrity owner. No dimension validation or final refetch
        // is needed when the authoritative posting status is unchanged.
        using (var counter = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog))
        {
            await service.SendVoucherDraftAsync(user, request.DocumentId, new(saved.RowVersion));
            Assert.Equal(3, counter.Count);
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
    public async Task Posting_two_fifty_and_five_hundred_lines_keeps_one_batched_journal_write()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var store = scope.ServiceProvider.GetRequiredService<IAccountingStore>();
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate,
                AccountingPermissionCodes.ManualSend, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var account = (await service.ListAccountsAsync(user)).First(item => item.AccountType == "Expense" && item.AllowsPosting);
        var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        int? commandCount = null;
        foreach (var size in new[] { 2, 50, 500 })
        {
            var lines = Enumerable.Range(0, size).Select(index => new ManualVoucherLineRequest(
                account.AccountId, fixture.SupplierPartyId, null, "Partida", index % 2 == 0 ? 100 : 0,
                index % 2 == 1 ? 100 : 0, Guid.NewGuid())).ToArray();
            var request = new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), fixture.BusinessId,
                new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Asiento por lotes", lines,
                PayloadVersion: 2);
            using (var acceptanceActivity = new Activity("AccountingAcceptanceVolume").SetIdFormat(ActivityIdFormat.W3C).Start())
            using (var acceptanceCounter = new InventoryBalanceProcessingTests.CommandCounter(database, acceptanceActivity.TraceId))
            {
                var acceptanceTimer = Stopwatch.StartNew();
                await store.ConfirmManualVoucherAsync(user, request, default);
                acceptanceTimer.Stop();
                Assert.Equal(4, acceptanceCounter.Count);
                Assert.True(acceptanceTimer.Elapsed < TimeSpan.FromSeconds(2),
                    $"Accepting {size} lines took {acceptanceTimer.Elapsed.TotalMilliseconds:F1}ms.");
                output.WriteLine($"Aceptar {size} partidas: {acceptanceCounter.Count} SQL, {acceptanceTimer.Elapsed.TotalMilliseconds:F1} ms.");
            }
            using (var activity = new Activity("AccountingPostingVolume").SetIdFormat(ActivityIdFormat.W3C).Start())
            using (var counter = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId))
            {
                var timer = Stopwatch.StartNew();
                await processor.ProcessAsync(request.VoucherId, AccountingManualDocumentTypes.ManualVoucher,
                    fixture.BusinessId, default);
                timer.Stop();
                var sqlCount = counter.Count;
                Assert.InRange(sqlCount, 1, 12);
                commandCount ??= sqlCount;
                Assert.Equal(commandCount.Value, sqlCount);
                Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), $"Posting {size} lines took {timer.Elapsed.TotalMilliseconds:F1}ms.");
                output.WriteLine($"Contabilizar {size} partidas: {sqlCount} SQL, {timer.Elapsed.TotalMilliseconds:F1} ms.");
            }
            var entry = await service.GetEntryAsync(user, request.VoucherId);
            Assert.NotNull(entry);
            Assert.Equal(size, entry.Lines.Count);
            Assert.Equal(lines.Select(line => line.LineId).Order(), entry.Lines.Select(line => line.SourceLineId).Order());
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
                Enumerable.Range(0, size).Select(index => new VoucherDraftLine(account.AccountId, null, null, "Partida",
                    index % 2 == 0 ? 100 : 0, index % 2 == 1 ? 100 : 0, LineId: Guid.NewGuid())).ToArray(), null, null, PayloadVersion: 2);
            using var counter = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog);
            var watch = Stopwatch.StartNew();
            var result = await service.SaveVoucherDraftAsync(user, request);
            watch.Stop();
            Assert.Equal(size, result.Lines.Count);
            Assert.Equal(4, counter.Count);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
            output.WriteLine($"Guardar {size} partidas: {counter.Count} SQL, {watch.ElapsedMilliseconds} ms.");
            if (size == 500)
            {
                using (var cloneCounter = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog))
                {
                    watch.Restart();
                    var cloned = await service.CloneVoucherDraftAsync(user, request.DocumentId, new(Guid.NewGuid(), request.OccurredAt));
                    watch.Stop();
                    Assert.Equal(500, cloned.Lines.Count);
                    Assert.Equal(6, cloneCounter.Count);
                    Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
                    output.WriteLine($"Clonar 500 partidas: {cloneCounter.Count} SQL, {watch.ElapsedMilliseconds} ms.");
                }
                foreach (var pageNumber in new[] { 1, 2 })
                {
                    using var pageCounter = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog);
                    watch.Restart();
                    var pageResult = await service.ListFinancialTraceabilityLinesAsync(user, new(2026, 1, 1), new(2026, 12, 31),
                        null, "Created", request.DocumentId.ToString(), pageNumber, 25);
                    watch.Stop();
                    Assert.Equal(500, pageResult.TotalCount);
                    Assert.Equal(25, pageResult.Items.Count);
                    Assert.Equal((pageNumber - 1) * 25 + 1, pageResult.Items[0].LineNumber);
                    Assert.Equal(1, pageCounter.Count);
                    Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
                    output.WriteLine($"Trazabilidad página {pageNumber}, 500 partidas: {pageCounter.Count} SQL, {watch.ElapsedMilliseconds} ms.");
                }
            }
        }
    }
}
