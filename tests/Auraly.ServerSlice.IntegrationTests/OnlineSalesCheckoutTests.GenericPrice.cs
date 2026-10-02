using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class OnlineSalesCheckoutTests
{
    [Fact]
    public async Task Lowering_a_generic_product_price_preserves_the_final_price_in_receipt_and_reporting()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var configure = new SqlCommand("""
            UPDATE dbo.Products SET IsGenericProduct=1,ManageStock=0
              OUTPUT deleted.IsGenericProduct,deleted.ManageStock
            WHERE ProductId=@ProductId AND TenantId=@TenantId;
            """, connection);
        configure.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        configure.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        bool wasGeneric, managedStock;
        await using (var reader = await configure.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            wasGeneric = reader.GetBoolean(0); managedStock = reader.GetBoolean(1);
        }
        try
        {
            var userId = await CreateUserAsync("generic-final-price");
            using var client = fixture.CreateUserClient(userId, CommercePermissionCodes.SalesCreate,
                CommercePermissionCodes.SalesChangePrice, CommercePermissionCodes.SalesReadCostAndMargin, CommercePermissionCodes.SalesReprint);
            var draft = await CaptureAsync(client, await OpenAsync(client));
            Assert.True(Assert.Single(draft.Lines).AllowsDocumentCostOverride);
            foreach (var price in new[] { 12_000m, 10_000m })
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/commerce/v1/pos/drafts/{draft.DraftId}/lines") {
                    Content = JsonContent.Create(new UpdateOnlineSalesDraftLinesRequest(
                        [new(draft.Lines[0].LineId, draft.Lines[0].Description, price, 0, 6_000m)], draft.Version)) };
                request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
                using var response = await client.SendAsync(request);
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                draft = (await response.Content.ReadFromJsonAsync<OnlineSalesDraft>())!;
                Assert.Equal(price, draft.Lines[0].PublicUnitPrice);
                Assert.Equal(price, draft.PayableAmount);
            }
            var completed = await CompleteAsync(client, draft.DraftId, new(draft.Version,
                [new OnlineSalesPayment("Cash", 10_000m, null)]), Guid.NewGuid().ToString("N"));
            Assert.Equal(10_000m, Assert.Single(completed.Receipt.Lines).Total);
            await using var report = new SqlCommand("""
                SELECT line.LineTotal,fact.TotalAmount,fact.RecognizedCostAmount,
                  (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=line.DocumentId)
                FROM dbo.SalesDocumentLines line
                JOIN reporting.SalesReportLineFacts fact ON fact.SourceDocumentId=line.DocumentId
                  AND fact.SourceLineNumber=line.LineNumber AND fact.MovementType=N'Sale'
                WHERE line.DocumentId=@DocumentId;
                """, connection);
            report.Parameters.AddWithValue("@DocumentId", completed.Receipt.DocumentId);
            await using var facts = await report.ExecuteReaderAsync();
            Assert.True(await facts.ReadAsync());
            Assert.Equal(10_000m, facts.GetDecimal(0));
            Assert.Equal(10_000m, facts.GetDecimal(1));
            Assert.Equal(6_000m, facts.GetDecimal(2));
            Assert.Equal(0, facts.GetInt32(3));
            Assert.False(await facts.ReadAsync());
        }
        finally
        {
            await using var restore = new SqlCommand("UPDATE dbo.Products SET IsGenericProduct=@Generic,ManageStock=@Stock WHERE ProductId=@ProductId", connection);
            restore.Parameters.AddWithValue("@Generic", wasGeneric);
            restore.Parameters.AddWithValue("@Stock", managedStock);
            restore.Parameters.AddWithValue("@ProductId", fixture.ProductId);
            await restore.ExecuteNonQueryAsync();
        }
    }
}
