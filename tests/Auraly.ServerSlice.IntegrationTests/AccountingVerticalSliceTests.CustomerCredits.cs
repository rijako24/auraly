using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
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
    public async Task Return_settlement_preserves_receivables_and_binds_customer_credit_to_its_actual_source(bool correctedRefund)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read,
            AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate,
            SalesReturnPermissionCodes.Create, SalesReturnPermissionCodes.Read);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null))
            defaults.EnsureSuccessStatusCode();
        using (var activate = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared")))
            activate.EnsureSuccessStatusCode();
        var (customerId, siteId) = await CreateCustomerAsync();
        var sale = WithUblSnapshot(fixture.CreateValidRequest(correctedRefund ? 9_941 : 9_942)) with
        {
            CustomerId = customerId, CustomerPartySiteId = siteId
        };
        if (!correctedRefund)
            sale = sale with { Payments = [], Credit = new(customerId, 11_900m, sale.FiscalSnapshot!.IssuedAt.AddDays(30), PartySiteId: siteId),
                UblSnapshot = sale.UblSnapshot! with { PaymentFormCode = "2", DueDate = DateOnly.FromDateTime(sale.FiscalSnapshot.IssuedAt.AddDays(30).Date) } };
        await SetWarehouseNegativeSalesPolicyAsync(true);
        using (var upload = fixture.CreateUploadMessage(sale))
        using (var response = await fixture.CreateClient().SendAsync(upload))
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var returned = new ConfirmSalesReturnRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            sale.DocumentId, DateTimeOffset.UtcNow,
            correctedRefund ? ReturnEconomicResolutions.Refund : ReturnEconomicResolutions.CustomerCredit,
            correctedRefund ? SalesReturnRefundMethods.Cash : null, "Saldo a favor con origen de cuenta",
            [new(1, 1m, ReturnInventoryDispositions.Sellable)],
            correctedRefund ? await fixture.OpenWebWorkSessionAsync() : null, null, "Other");
        var alternate = !correctedRefund ? await PortfolioOriginalAccountScenario.RemapAsync(fixture, client, true) : ((Guid AccountId, string Previous)?)null;
        try
        {
            using (var message = CreateSalesReturnMessage(returned, returned.ReturnId.ToString("N")))
            using (var response = await client.SendAsync(message))
                Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
            if (!correctedRefund)
                await PortfolioOriginalAccountScenario.AssertReturnOriginalLineAsync(fixture, true, sale.DocumentId, returned.ReturnId, 11_900m);
        }
        finally
        {
            if (alternate is { } mapping) await PortfolioOriginalAccountScenario.RestoreAsync(fixture, mapping);
        }

        using var scope = fixture.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        var sourceId = returned.ReturnId;
        var sourceType = "SalesReturn";
        if (!correctedRefund)
        {
            Assert.Equal(0, await CountAsync("CustomerCredits", "SourceReturnId", returned.ReturnId));
            Assert.Equal(0m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE SourceDocumentId=@Id", sale.DocumentId));
            await processor.ProcessAsync(sourceId, sourceType, fixture.BusinessId, default);
            Assert.Equal(1, await CountAsync("SalesReturnReceivableApplications", "ReturnId", returned.ReturnId));
            return;
        }
        if (correctedRefund)
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            var partyId = await ScalarAsync<Guid>("SELECT PartyId FROM dbo.Customers WHERE CustomerId=@Id", customerId);
            var correction = new WorkSessionClosureReconciliationPayload(Guid.NewGuid(), Guid.NewGuid(),
                fixture.TenantId, fixture.BusinessId, fixture.UserId, returned.ReturnedAt,
                [new("Cash", "Cash", 10_000m, 12_000m, 12_000m, 2_000m, "CORRECTION")], [],
                [new("refund", "Refund", returned.ReturnId, 1, "Cash", -11_900m, "Cash", -9_900m,
                    "CORRECTION", partyId, customerId, null, "CustomerCreditsPayable")]);
            sourceId = correction.ReconciliationId;
            sourceType = WorkSessionAccountingDocumentTypes.ClosureReconciliation;
            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction,
                    [new(fixture.TenantId, fixture.BusinessId, sourceId, sourceType,
                        JsonSerializer.Serialize(correction, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                        correction.ReconciledAt, Guid.NewGuid())], correction.ReconciledAt,
                    AccountingJobRequirement.PreserveCommercialEffects, default);
                await transaction.CommitAsync();
            }
            // Force the late journal-link guard: no credit/history may survive a failed posting.
            using (var fault = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                commandFilter: command => command.CommandText.Contains("DECLARE @OpeningLines TABLE", StringComparison.Ordinal),
                beforeExecute: command =>
                {
                    var lines = JsonNode.Parse((string)command.Parameters["@Lines"].Value)!.AsArray();
                    lines.First(line => line!["OpeningDocumentId"] is not null)!["OpeningDocumentId"] = Guid.NewGuid();
                    command.Parameters["@Lines"].Value = lines.ToJsonString();
                }))
            {
                var error = await Assert.ThrowsAsync<SqlException>(() => processor.ProcessAsync(sourceId,
                    sourceType, fixture.BusinessId, default));
                Assert.Contains(error.Errors.Cast<SqlError>(), item => item.Number == 51844);
                Assert.Equal(1, fault.Count);
            }
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CustomerCredits WHERE SourceReturnId=@Id", returned.ReturnId));
            Assert.Equal(0, await CountAsync("CustomerCreditTransactions", "SourceDocumentId", sourceId));
            await processor.ProcessAsync(sourceId, sourceType, fixture.BusinessId, default);
        }
        await PortfolioOriginalAccountScenario.AssertCustomerCreditOpeningAsync(fixture, sourceId, sourceType);
        Assert.Equal(correctedRefund ? 2_000m : 11_900m, await ScalarAsync<decimal>(
            "SELECT AvailableAmount FROM dbo.CustomerCredits WHERE SourceReturnId=@Id", returned.ReturnId));
        await processor.ProcessAsync(sourceId, sourceType, fixture.BusinessId, default);
        await PortfolioOriginalAccountScenario.AssertCustomerCreditOpeningAsync(fixture, sourceId, sourceType);
        Assert.Equal(1, await CountAsync("CustomerCredits", "SourceReturnId", returned.ReturnId));
        Assert.Equal(1, await CountAsync("CustomerCreditTransactions", "SourceDocumentId", sourceId));
    }
}
