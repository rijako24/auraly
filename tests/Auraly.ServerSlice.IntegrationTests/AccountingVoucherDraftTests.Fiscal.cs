using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Taxation.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVoucherDraftTests
{
    [Fact]
    public async Task Fiscal_capture_stays_in_the_same_draft_and_clone_clears_consumable_fiscal_evidence()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate,
                AccountingPermissionCodes.ManualSend, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var account = (await service.ListAccountsAsync(user)).First(item => item.AccountType == "Expense" && item.AllowsPosting);
        var lineId = Guid.NewGuid();
        var capture = new VoucherFiscalCapture([new(lineId, 100m, null, "CapitalizedCost")]);
        var request = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
            new(2026, 9, 30, 10, 0, 0, TimeSpan.FromHours(-5)), "MANUAL_VOUCHER", "Soporte en preparación", null,
            [new(account.AccountId, fixture.SupplierPartyId, null, "Base de adquisición", 100m, 0, LineId: lineId),
             new(account.AccountId, fixture.SupplierPartyId, null, "Contrapartida en preparación", 0, 100m, LineId: Guid.NewGuid())],
            null, null, MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "SupportDocument",
            FiscalCapture: capture);
        using var client = fixture.CreateAdminClient(user.Permissions.ToArray());
        var path = $"/api/commerce/v1/accounting/manual/drafts/{request.DocumentId}";
        using var savedResponse = await client.PutAsJsonAsync(path, request);
        Assert.True(savedResponse.IsSuccessStatusCode, await savedResponse.Content.ReadAsStringAsync());
        var saved = (await savedResponse.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        Assert.Equal(JsonSerializer.Serialize(capture), JsonSerializer.Serialize(saved.FiscalCapture));
        var read = (await client.GetFromJsonAsync<VoucherDraftView>(path))!;
        Assert.Equal(JsonSerializer.Serialize(capture), JsonSerializer.Serialize(read.FiscalCapture));
        var replay = await service.SaveVoucherDraftAsync(user, request with { RowVersion = saved.RowVersion });
        Assert.Equal(saved.RowVersion, replay.RowVersion);
        using (var incomplete = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
            Assert.Contains("revisa", await incomplete.Content.ReadAsStringAsync());
        }

        var cloned = await service.CloneVoucherDraftAsync(user, request.DocumentId,
            new(Guid.NewGuid(), request.OccurredAt.AddDays(1)));
        Assert.Equal("Accounting", cloned.VoucherTypeCode);
        Assert.Null(cloned.FiscalCapture);
        Assert.DoesNotContain(lineId, cloned.Lines.Select(line => line.Value.LineId));

        foreach (var invalid in new[] {
            request with { VoucherTypeCode = "Accounting" },
            request with { FiscalCapture = capture with { Acquisitions = [new(Guid.NewGuid(), 100m, null, "CapitalizedCost")] } },
            request with { FiscalCapture = capture with { Acquisitions = [capture.Acquisitions[0], capture.Acquisitions[0]] } },
            request with { FiscalCapture = capture with { Acquisitions = [capture.Acquisitions[0] with { TaxExclusiveAmount = 0 }] } },
            request with { FiscalCapture = capture with { WithholdingBindings = [new(lineId, Guid.NewGuid(), Guid.NewGuid())] } }
        })
            await Assert.ThrowsAsync<AccountingValidationException>(() => service.SaveVoucherDraftAsync(user,
                invalid with { DocumentId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<AccountingForbiddenException>(() => service.SaveVoucherDraftAsync(user,
            request with { DocumentId = Guid.NewGuid(), FiscalCapture = capture with {
                WithholdingAdjustments = [new(Guid.Empty, "Manual", 100m, 1m, "Revisión", Guid.NewGuid(),
                    "IncomeTax", "Retención", 1m, AccountId: account.AccountId)] } }));
        Assert.DoesNotContain("FiscalCapture", JsonSerializer.Serialize(request with {
            PayloadVersion = 2, VoucherTypeCode = null, FiscalCapture = null }));

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            try
            {
                await using var invalidShape = new SqlCommand(
                    "UPDATE accounting.VoucherDrafts SET VoucherTypeCode=N'Accounting' WHERE DocumentId=@Id;", connection, transaction);
                invalidShape.Parameters.AddWithValue("@Id", request.DocumentId);
                var error = await Assert.ThrowsAsync<SqlException>(() => invalidShape.ExecuteNonQueryAsync());
                Assert.Equal(547, error.Number);
                Assert.Contains("CK_VoucherDrafts_FiscalCapture", error.Message);
            }
            finally { await transaction.RollbackAsync(); }
        }
        await using var effects = new SqlCommand("""
            SELECT (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id)
              +(SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id)
              +(SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id)
              +(SELECT COUNT(*) FROM dbo.FiscalDocuments WHERE DocumentId=@Id)
              +(SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id);
            """, connection);
        effects.Parameters.AddWithValue("@Id", request.DocumentId);
        Assert.Equal(0, (int)(await effects.ExecuteScalarAsync())!);
    }
}
