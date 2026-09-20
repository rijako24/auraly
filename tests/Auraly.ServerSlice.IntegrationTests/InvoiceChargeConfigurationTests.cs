using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Expenses;
using Auraly.Application.Sales;
using Auraly.Fiscal.Core;
using Auraly.Contracts.Sales;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class InvoiceChargeConfigurationTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Configuration_is_versioned_scoped_and_notifies_the_existing_configuration_stream()
    {
        using var client = fixture.CreateAdminClient(InvoiceChargePermissions.Read, InvoiceChargePermissions.Configure,
            ExpensePermissionCodes.Read, ExpensePermissionCodes.Configure, CatalogPermissionCodes.Update);
        var expenseOptions = await client.GetFromJsonAsync<ExpenseWorkspaceOptions>("/api/commerce/v1/expenses/options");
        Assert.NotNull(expenseOptions);
        var conceptId = Guid.NewGuid();
        using var conceptResponse = await client.PutAsJsonAsync($"/api/commerce/v1/expenses/concepts/{conceptId}",
            new SaveExpenseConceptRequest(conceptId, fixture.BusinessId, "Costo de domicilio de prueba",
                expenseOptions.ExpenseAccounts.First().AccountId, expenseOptions.CostCenters.First().CostCenterId, null, true));
        conceptResponse.EnsureSuccessStatusCode();
        var id = Guid.NewGuid();
        var code = $"TEST-{id:N}"[..32];
        using var taxResponse = await client.PostAsJsonAsync("/api/commerce/v1/tax-profiles",
            new SaveTaxProfileRequest(fixture.BusinessId, code, "Impuesto de prueba", 0));
        taxResponse.EnsureSuccessStatusCode();
        var tax = await taxResponse.Content.ReadFromJsonAsync<TaxProfileSummary>();
        var request = new SaveInvoiceChargeRequest(id, 0, code, "Domicilio de prueba", true, 0,
            "Fixed", 5000, "UpToInvoiceAmount", 80000, conceptId, tax!.TaxProfileId, [], [fixture.SupplierId], tax.TaxProfileId);
        fixture.DrainSynchronizationMessages();
        using var createdResponse = await client.PutAsJsonAsync($"/api/commerce/v1/invoice-charges/{id}", request);
        Assert.True(createdResponse.IsSuccessStatusCode, await createdResponse.Content.ReadAsStringAsync());
        var created = await createdResponse.Content.ReadFromJsonAsync<InvoiceChargeDefinition>();
        Assert.NotNull(created);
        Assert.Equal(1, created.Version);
        Assert.Equal(fixture.BusinessId, created.BusinessId);
        Assert.Equal(fixture.SupplierId, Assert.Single(created.Suppliers).SupplierId);
        Assert.Equal(80000m, created.InvoiceAmountLimit);
        var notification = await fixture.ReadSynchronizationMessageAsync();
        Assert.Equal(fixture.BusinessId, notification.BusinessId);
        Assert.Equal("Configuration", notification.Stream);

        using (var parties = fixture.CreateAdminClient(PartyWorkspacePermissionCodes.Read, PartyWorkspacePermissionCodes.Deactivate))
        {
            var detail = (await parties.GetFromJsonAsync<PartyWorkspaceDetail>(
                $"/api/commerce/v1/parties/{fixture.SupplierPartyId}"))!;
            fixture.DrainSynchronizationMessages();
            using var deactivate = await parties.PostAsJsonAsync($"/api/commerce/v1/parties/{fixture.SupplierPartyId}/status",
                new SetPartyBusinessStatusRequest(false, detail.RowVersion));
            deactivate.EnsureSuccessStatusCode();
            var inactiveSupplier = (await deactivate.Content.ReadFromJsonAsync<PartyWorkspaceItem>())!;
            Assert.Equal("Configuration", (await fixture.ReadSynchronizationMessageAsync()).Stream);
            var unavailable = (await client.GetFromJsonAsync<InvoiceChargePage>($"/api/commerce/v1/invoice-charges?search={code}"))!;
            Assert.False(Assert.Single(Assert.Single(unavailable.Items).Suppliers).IsActive);
            using var reactivate = await parties.PostAsJsonAsync($"/api/commerce/v1/parties/{fixture.SupplierPartyId}/status",
                new SetPartyBusinessStatusRequest(true, inactiveSupplier.RowVersion));
            reactivate.EnsureSuccessStatusCode();
        }

        await using (var masters = new SqlConnection(fixture.ConnectionString))
        {
            await masters.OpenAsync();
            await using var edit = new SqlCommand("""
                UPDATE dbo.TaxProfiles SET Rate=19 WHERE TaxProfileId=@TaxId;
                UPDATE dbo.ExpenseConcepts SET DefaultCostCenterId=NULL WHERE ExpenseConceptId=@ConceptId;
                """, masters);
            edit.Parameters.AddWithValue("@TaxId", tax.TaxProfileId);
            edit.Parameters.AddWithValue("@ConceptId", conceptId);
            await edit.ExecuteNonQueryAsync();
        }
        var frozenPage = await client.GetFromJsonAsync<InvoiceChargePage>(
            $"/api/commerce/v1/invoice-charges?search={code}");
        var frozen = Assert.Single(frozenPage!.Items);
        Assert.Equal(0m, frozen.TaxRate);
        Assert.Equal(0m, frozen.PurchaseTaxRate);
        Assert.Equal(created.CostCenterId, frozen.CostCenterId);

        using var stale = await client.PutAsJsonAsync($"/api/commerce/v1/invoice-charges/{id}", request with { Name = "Stale" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var updatedResponse = await client.PutAsJsonAsync($"/api/commerce/v1/invoice-charges/{id}",
            request with { ExpectedVersion = 1, Value = 6000, IsActive = false });
        updatedResponse.EnsureSuccessStatusCode();
        var updated = await updatedResponse.Content.ReadFromJsonAsync<InvoiceChargeDefinition>();
        Assert.Equal(2, updated!.Version);
        Assert.False(updated.IsActive);
        Assert.Equal(19m, updated.TaxRate);
        Assert.Equal(19m, updated.PurchaseTaxRate);
        Assert.Null(updated.CostCenterId);
        // A delayed Edge document keeps the original tariff, accounts and supplier snapshot.
        var sale = fixture.CreateValidRequest(9876);
        var applied = InvoiceChargeApplication.Calculate(sale.Lines.Sum(line => line.LineTotal),
            new InvoiceChargeSelection(Guid.NewGuid(), created, fixture.SupplierId, null));
        var commercial = sale.CommercialSnapshot with {
            UntaxedAmount = sale.CommercialSnapshot.UntaxedAmount + applied.InvoicedUntaxedAmount,
            TaxAmount = sale.CommercialSnapshot.TaxAmount + applied.InvoicedTaxAmount,
            PayableAmount = sale.CommercialSnapshot.PayableAmount + applied.InvoicedAmount,
            Taxes = PosSaleTaxSummary.Calculate(sale.CommercialSnapshot.Taxes, [applied])
        };
        var fiscal = sale.FiscalSnapshot!;
        var cufe = CufeCalculator.Calculate(new CufeInput(fiscal.FiscalNumber, fiscal.IssuedAt,
            commercial.UntaxedAmount, commercial.PayableAmount, fiscal.SupplierTaxId, fiscal.CustomerIdentification,
            new FiscalTechnicalKey(ServerSliceFixture.TechnicalKeyValue, ServerSliceFixture.TechnicalKeyVersion),
            FiscalEnvironment.Test, commercial.Taxes.Select(tax => new FiscalTaxAmount(tax.Code, tax.Amount))),
            ServerSliceFixture.QrValidationUrl);
        sale = sale with { CommercialSnapshot = commercial, Charges = [applied],
            Payments = [new(1, "Cash", commercial.PayableAmount, null)],
            FiscalSnapshot = fiscal with { UntaxedAmount = commercial.UntaxedAmount, TaxAmount = commercial.TaxAmount,
                PayableAmount = commercial.PayableAmount, Taxes = commercial.Taxes, Cufe = cufe.Cufe, QrPayload = cufe.QrPayload } };
        using var device = fixture.CreateClient();
        using (var alteredMessage = fixture.CreateUploadMessage(sale with {
            Charges = [applied with { ExpenseAccountId = Guid.NewGuid() }] }))
        using (var alteredResponse = await device.SendAsync(alteredMessage))
            Assert.Equal(HttpStatusCode.BadRequest, alteredResponse.StatusCode);
        using (var originalMessage = fixture.CreateUploadMessage(sale))
        using (var originalResponse = await device.SendAsync(originalMessage))
            Assert.True(originalResponse.IsSuccessStatusCode, await originalResponse.Content.ReadAsStringAsync());
        using (var replayMessage = fixture.CreateUploadMessage(sale))
        using (var replayResponse = await device.SendAsync(replayMessage))
            Assert.True(replayResponse.IsSuccessStatusCode, await replayResponse.Content.ReadAsStringAsync());

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var effects = new SqlCommand("""
            SELECT e.SourceInvoiceId,e.GrossAmount,e.SupplierDocumentNumber,e.Status,p.OriginalAmount,
              (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=e.ExpenseId),
              (SELECT COUNT(*) FROM dbo.DocumentProcessingJobs WHERE DocumentId=e.ExpenseId)
            FROM dbo.Expenses e JOIN dbo.Payables p ON p.SourceDocumentId=e.ExpenseId
            WHERE e.ExpenseId=@Id AND e.BusinessId=@BusinessId;
            """, connection))
        {
            effects.Parameters.AddWithValue("@Id", applied.AppliedChargeId);
            effects.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            await using var effectReader = await effects.ExecuteReaderAsync();
            Assert.True(await effectReader.ReadAsync());
            Assert.Equal(sale.DocumentId, effectReader.GetGuid(0));
            Assert.Equal(5000m, effectReader.GetDecimal(1));
            Assert.True(effectReader.IsDBNull(2));
            Assert.Equal("Processed", effectReader.GetString(3));
            Assert.Equal(5000m, effectReader.GetDecimal(4));
            Assert.Equal(1, effectReader.GetInt32(5));
            Assert.Equal(0, effectReader.GetInt32(6));
            Assert.False(await effectReader.ReadAsync());
        }
        await using var versions = new SqlCommand("SELECT Value FROM sales.InvoiceChargeVersions WHERE ChargeId=@Id ORDER BY Version;", connection);
        versions.Parameters.AddWithValue("@Id", id);
        await using var reader = await versions.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync()); Assert.Equal(5000, reader.GetDecimal(0));
        Assert.True(await reader.ReadAsync()); Assert.Equal(6000, reader.GetDecimal(0));
        Assert.False(await reader.ReadAsync());

        var active = await client.GetFromJsonAsync<InvoiceChargePage>($"/api/commerce/v1/invoice-charges?search={code}");
        Assert.Empty(active!.Items);
        var all = await client.GetFromJsonAsync<InvoiceChargePage>($"/api/commerce/v1/invoice-charges?search={code}&includeInactive=true&pageSize=1");
        Assert.Equal(id, Assert.Single(all!.Items).ChargeId);
        Assert.Equal(1, all.TotalCount);
    }

    [Fact]
    public async Task Configuration_permissions_do_not_come_from_sale_permission()
    {
        using var client = fixture.CreateAdminClient("sales.create");
        using var response = await client.GetAsync("/api/commerce/v1/invoice-charges");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var options = await client.GetAsync("/api/commerce/v1/invoice-charges/options");
        Assert.Equal(HttpStatusCode.Forbidden, options.StatusCode);
    }
}
