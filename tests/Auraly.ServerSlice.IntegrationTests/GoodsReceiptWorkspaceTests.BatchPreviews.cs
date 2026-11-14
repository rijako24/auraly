using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Auraly.Application.Parties;
using Auraly.Application.Purchasing;
using Auraly.Contracts.Parties;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class GoodsReceiptWorkspaceTests
{
    [Fact]
    public async Task Cost_previews_share_one_tax_plan_for_one_or_one_hundred_documents()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();
        var actor = new PurchasingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { PurchasingPermissionCodes.CreateGoodsReceipts });
        var request = CreateDraft();
        var documents = Enumerable.Range(1, 100).Select(number => new GoodsReceiptCostDocumentRequest(
            Guid.NewGuid(), fixture.SupplierId, PurchaseEvidenceTypes.SupplierElectronicInvoice,
            $"FLETE-PREVIEW-{number}", request.ReceivedAt, true, request.DueDate,
            "COP", 1, DateOnly.FromDateTime(request.ReceivedAt.Date), "FunctionalCurrency",
            [new(1, PurchaseCostKinds.Freight, "Flete", number * 100m, number * 100m,
                "00", 0, 0, PurchasingTaxTreatments.NotApplicable,
                PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Value)])).ToArray();
        foreach (var count in new[] { 1, 100 })
        {
            var samples = new List<double>();
            for (var sample = 0; sample < 20; sample++)
            {
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(
                    new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog);
                var timer = Stopwatch.StartNew();
                var results = await service.PreviewCostWithholdingsAsync(actor,
                    new(fixture.BusinessId, documents.Take(count).ToArray()));
                timer.Stop();
                Assert.Equal(2, counter.Count);
                Assert.Equal(count, results.Count);
                Assert.Equal(documents.Take(count).Select(item => item.CostDocumentId), results.Select(item => item.CostDocumentId));
                samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            var p95 = samples.Order().ElementAt(18);
            output.WriteLine($"Cost preview {count} documents: p95={p95:F1} ms, SQL=2.");
            Assert.True(p95 < 500, $"Cost preview p95={p95:F1} ms exceeded 500 ms.");
        }
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts);
        foreach (var invalid in new[] { Array.Empty<GoodsReceiptCostDocumentRequest>(), new[] { documents[0], documents[0] } })
        {
            using var response = await client.PostAsJsonAsync("/api/commerce/v1/goods-receipts/cost-withholdings-preview",
                new PreviewGoodsReceiptCostWithholdingsRequest(fixture.BusinessId, invalid));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var foreign = await client.PostAsJsonAsync("/api/commerce/v1/goods-receipts/cost-withholdings-preview",
            new PreviewGoodsReceiptCostWithholdingsRequest(Guid.NewGuid(), documents));
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    }

    [Fact]
    public async Task Selected_supplier_metadata_is_filtered_and_projected_in_one_command()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        var ids = new List<Guid>();
        await using (var seed = new SqlCommand("""
            DECLARE @Rows TABLE(PartyId uniqueidentifier,RoleId uniqueidentifier,Number int);
            INSERT @Rows SELECT TOP(100) NEWID(),NEWID(),ROW_NUMBER() OVER(ORDER BY object_id) FROM sys.all_objects;
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
              SELECT PartyId,@TenantId,N'Organization',CONCAT(N'Metadata por lote ',Number),N'Incomplete',1,@UserId,SYSDATETIMEOFFSET() FROM @Rows;
            INSERT dbo.Suppliers(SupplierId,PartyId,TenantId,Identification,Name,IsActive,CreatedAt,PurchaseEvidencePolicy,DefaultPaymentDueDays)
              SELECT RoleId,PartyId,@TenantId,CONVERT(nvarchar(36),RoleId),CONCAT(N'Metadata por lote ',Number),
                1,SYSDATETIMEOFFSET(),N'InternalReceiptVoucher',Number FROM @Rows;
            SELECT RoleId FROM @Rows;
            """, connection))
        {
            seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            seed.Parameters.AddWithValue("@UserId", fixture.UserId);
            await using var reader = await seed.ExecuteReaderAsync();
            while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        }
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PartyWorkspaceService>();
        var actor = new PartyActorIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { PartyWorkspacePermissionCodes.SupplierRead });
        foreach (var count in new[] { 1, 100 })
        {
            using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
            var result = await service.RoleOptionsAsync(actor, 1,
                new("Supplier", count, RoleIds: ids.Take(count).ToArray()), CancellationToken.None);
            Assert.Equal(1, counter.Count);
            Assert.Equal(count, result.Items.Count);
            Assert.Equal(count, result.TotalCount);
            Assert.Equal(ids.Take(count).Order(), result.Items.Select(item => item.RoleId).Order());
            Assert.All(result.Items, item => Assert.Equal("InternalReceiptVoucher", item.SupplierPurchaseEvidencePolicy));
        }
        using var client = fixture.CreateAdminClient(PartyWorkspacePermissionCodes.SupplierRead);
        using var response = await client.PostAsJsonAsync("/api/commerce/v1/parties/role-options",
            new PartyRoleOptionQuery("Supplier", 100, RoleIds: [fixture.SupplierId, Guid.NewGuid()]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PartyRoleOptionPage>();
        Assert.Equal(fixture.SupplierId, Assert.Single(page!.Items).RoleId);
        using var expenseOnly = fixture.CreateAdminClient(Auraly.Contracts.Expenses.ExpensePermissionCodes.Create);
        using var forbidden = await expenseOnly.PostAsJsonAsync("/api/commerce/v1/parties/role-options",
            new PartyRoleOptionQuery("User", 100, RoleIds: ids));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
