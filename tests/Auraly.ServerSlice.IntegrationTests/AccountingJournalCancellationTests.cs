using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVoucherDraftTests
{
    [Fact]
    public async Task Plain_journal_cancellation_posts_one_exact_inverse_and_cannot_be_repeated_with_another_identity()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read,
            AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate,
            AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        using (var defaults = await client.PutAsJsonAsync("/api/commerce/v1/accounting/defaults", new { }))
            Assert.True(defaults.IsSuccessStatusCode, await defaults.Content.ReadAsStringAsync());
        using (var activation = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared")))
            Assert.True(activation.IsSuccessStatusCode, await activation.Content.ReadAsStringAsync());
        var debit = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(
            fixture, client, "5198", "Expense", "General", "Debit");
        var credit = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(
            fixture, client, "2398", "Liability", "General", "Credit");
        var occurredAt = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-5));
        var documentId = Guid.NewGuid();
        var draft = new SaveVoucherDraftRequest(documentId, AccountingManualDocumentTypes.ManualVoucher,
            occurredAt, "MANUAL_VOUCHER", "Asiento de prueba", null,
            [new(debit.AccountId, fixture.SupplierPartyId, null, "Gasto", 100m, 0m, LineId: Guid.NewGuid()),
             new(credit.AccountId, fixture.SupplierPartyId, null, "Contrapartida", 0m, 100m, LineId: Guid.NewGuid())],
            null, null, MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4,
            VoucherTypeCode: "Accounting");
        var path = $"/api/commerce/v1/accounting/manual/drafts/{documentId:D}";
        using var savedResponse = await client.PutAsJsonAsync(path, draft);
        Assert.True(savedResponse.IsSuccessStatusCode, await savedResponse.Content.ReadAsStringAsync());
        var saved = (await savedResponse.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        using var sentResponse = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion));
        Assert.True(sentResponse.IsSuccessStatusCode, await sentResponse.Content.ReadAsStringAsync());
        var sent = (await sentResponse.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        Assert.Equal("Posted", sent.Status);

        var forged = new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), fixture.BusinessId,
            occurredAt.AddDays(1), "MANUAL_VOUCHER", "Anulación adulterada",
            [new(debit.AccountId, fixture.SupplierPartyId, null, "Gasto", 0m, 101m),
             new(credit.AccountId, fixture.SupplierPartyId, null, "Contrapartida", 101m, 0m)],
            fixture.SupplierPartyId, ReversesDocumentId: documentId);
        using (var denied = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers", forged))
            Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);

        var cancellation = new CancelManualAccountingVoucherRequest(Guid.NewGuid(), occurredAt.AddDays(1));
        var cancellationPath = $"/api/commerce/v1/accounting/manual/vouchers/{documentId:D}/cancel";
        using var first = await client.PostAsJsonAsync(cancellationPath, cancellation);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        using var replay = await client.PostAsJsonAsync(cancellationPath, cancellation);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        using var duplicate = await client.PostAsJsonAsync(cancellationPath,
            cancellation with { CancellationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var reopened = (await client.GetFromJsonAsync<VoucherDraftView>(path))!;
        Assert.Equal(cancellation.CancellationId, reopened.ReversalDocumentId);
        Assert.Equal("Posted", reopened.ReversalStatus);
        var listed = (await client.GetFromJsonAsync<AccountingDocumentPage>(
            "/api/commerce/v1/accounting/documents?from=2026-10-26&to=2026-10-28&page=1&pageSize=25"))!;
        var originalRow = Assert.Single(listed.Items.Where(item => item.SourceDocumentId == documentId));
        Assert.Equal(cancellation.CancellationId, originalRow.ReversalDocumentId);
        Assert.Equal("Posted", originalRow.ReversalStatus);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.AccountingSourceDocuments
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND ReversesDocumentId=@OriginalId;
            SELECT original.Debit,original.Credit,reversed.Debit,reversed.Credit
            FROM dbo.AccountingEntries source
            JOIN dbo.AccountingEntries cancellation ON cancellation.SourceDocumentId=@CancellationId
              AND cancellation.TenantId=source.TenantId AND cancellation.BusinessId=source.BusinessId
            JOIN dbo.AccountingEntryLines original ON original.EntryId=source.EntryId
            JOIN dbo.AccountingEntryLines reversed ON reversed.EntryId=cancellation.EntryId
              AND reversed.LineNumber=original.LineNumber
            WHERE source.SourceDocumentId=@OriginalId AND source.TenantId=@TenantId
              AND source.BusinessId=@BusinessId ORDER BY original.LineNumber;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@OriginalId", documentId);
        command.Parameters.AddWithValue("@CancellationId", cancellation.CancellationId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.True(await reader.NextResultAsync());
        var lines = 0;
        while (await reader.ReadAsync())
        {
            lines++;
            Assert.Equal(reader.GetDecimal(0), reader.GetDecimal(3));
            Assert.Equal(reader.GetDecimal(1), reader.GetDecimal(2));
        }
        Assert.Equal(2, lines);
    }
}
