using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Application.Expenses;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Contracts.Expenses;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class ExpenseCaptureTests
{
    [Fact]
    public async Task Missing_supplier_profile_can_be_reviewed_explicitly_without_creating_a_profile_or_duplicate_payable()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            TaxationPermissionCodes.ManageWithholdingRules, TaxationPermissionCodes.ViewWithholdingRules);
        var account = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "5198", "Expense", "General", "Debit");
        using var priorResponse = await client.GetAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}");
        var prior = priorResponse.StatusCode == HttpStatusCode.NotFound ? null :
            await priorResponse.Content.ReadFromJsonAsync<CounterpartyTaxProfileView>();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using var query = connection.CreateCommand();
        query.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        query.Parameters.AddWithValue("@Supplier", fixture.SupplierId);
        query.CommandText = "DELETE dbo.CounterpartyTaxProfiles WHERE TenantId=@Tenant AND CounterpartyId=@Supplier;";
        await query.ExecuteNonQueryAsync();
        try
        {
            using var scope = fixture.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ExpenseService>();
            var actor = new ExpenseUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
                new HashSet<string> { ExpensePermissionCodes.Read, ExpensePermissionCodes.Create, TaxationPermissionCodes.ManageWithholdingRules });
            var date = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(-5));
            var request = new ConfirmExpenseRequest(Guid.NewGuid(), fixture.BusinessId, fixture.SupplierId, null, null,
                Guid.NewGuid().ToString("N"), date, date.AddDays(30), "COP", "Revisión del soporte", 0, 0, null, null,
                Lines: [new(account.AccountId, null, null, "Servicio revisado", 100m, null, "CapitalizedCost", null)]);
            var incomplete = await service.PreviewAsync(actor, request);
            Assert.False(incomplete.CanConfirm);
            Assert.NotNull(incomplete.Withholding.ReviewBasisHash);
            await Assert.ThrowsAsync<ExpenseValidationException>(() => service.ConfirmAsync(actor,
                request.ExpenseId.ToString("N"), request with { CalculationHash = incomplete.CalculationHash }));
            request = request with { WithholdingAdjustments = [new(Guid.Empty, "Review", null, null,
                "Soporte y retenciones revisados para este documento", ReviewBasisHash: incomplete.Withholding.ReviewBasisHash)] };
            var unauthorized = actor with { Permissions = new HashSet<string> { ExpensePermissionCodes.Create } };
            await Assert.ThrowsAsync<ExpenseForbiddenException>(() => service.PreviewAsync(unauthorized, request));
            await Assert.ThrowsAsync<ExpenseValidationException>(() => service.PreviewAsync(actor,
                request with { Lines = [request.Lines![0] with { TaxExclusiveAmount = 200m }] }));
            var reviewed = await service.PreviewAsync(actor, request);
            Assert.True(reviewed.CanConfirm);
            Assert.Equal(0m, reviewed.Withholding.WithholdingTotal);
            request = request with { CalculationHash = reviewed.CalculationHash };
            var accepted = await service.ConfirmAsync(actor, request.ExpenseId.ToString("N"), request);
            Assert.False(accepted.IdempotentReplay);
            Assert.True((await service.ConfirmAsync(actor, request.ExpenseId.ToString("N"), request)).IdempotentReplay);
            query.Parameters.AddWithValue("@Id", request.ExpenseId);
            query.CommandText = """
                SELECT (SELECT COUNT(*) FROM dbo.CounterpartyTaxProfiles WHERE TenantId=@Tenant AND CounterpartyId=@Supplier),
                  (SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id AND OriginalAmount=100 AND OutstandingAmount=100),
                  (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id AND DebitTotal=100 AND CreditTotal=100),
                  (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id),
                  (SELECT AdjustmentsJson FROM dbo.DocumentWithholdingSnapshots WHERE DocumentId=@Id AND DocumentType=N'Expense');
                """;
            await using var data = await query.ExecuteReaderAsync();
            Assert.True(await data.ReadAsync());
            Assert.Equal(0, data.GetInt32(0)); Assert.Equal(1, data.GetInt32(1));
            Assert.Equal(1, data.GetInt32(2)); Assert.Equal(0, data.GetInt32(3));
            using var audit = JsonDocument.Parse(data.GetString(4));
            var item = Assert.Single(audit.RootElement.EnumerateArray());
            Assert.Equal("Review", item.GetProperty("Action").GetString());
            Assert.Equal(fixture.UserId, item.GetProperty("AdjustedByUserId").GetGuid());
        }
        finally
        {
            if (prior is not null)
            {
                using var restored = await client.PutAsJsonAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}",
                    new SaveCounterpartyTaxProfileRequest(fixture.BusinessId, fixture.SupplierId,
                        prior.AppliesWithholding, prior.Responsibilities, prior.JurisdictionCode));
                restored.EnsureSuccessStatusCode();
            }
        }
    }
}
