using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class GoodsReceiptWorkspaceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accepted_receipt_replay_does_not_reload_weight_or_publish_another_signal(bool changeWeight)
    {
        var draft = CreateDraft();
        var request = new ConfirmGoodsReceiptRequest(draft.DraftId, fixture.BusinessId, fixture.WarehouseId,
            fixture.SupplierId, draft.SupplierInvoiceNumber, draft.SupplierInvoiceDate, draft.ReceivedAt,
            true, draft.DueDate, "COP", draft.Notes, draft.Lines);
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts, PurchasingPermissionCodes.ConfirmGoodsReceipts);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        object? originalWeight;
        await using (var read = new SqlCommand("SELECT UnitGrossWeightKg FROM dbo.Products WHERE ProductId=@Id", connection))
        {
            read.Parameters.AddWithValue("@Id", fixture.ProductId);
            originalWeight = await read.ExecuteScalarAsync();
        }
        try
        {
            await SetWeightAsync(2m);
            using (var accepted = await ConfirmAsync())
                Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
            Assert.Single(fixture.DrainDocumentSignals().Where(signal => signal.DocumentId == request.DocumentId));
            if (changeWeight) await SetWeightAsync(9m);
            using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                commandFilter: command => command.CommandText.Contains("product.UnitGrossWeightKg", StringComparison.Ordinal));
            using var replay = await ConfirmAsync();
            Assert.True(replay.StatusCode == HttpStatusCode.Accepted, await replay.Content.ReadAsStringAsync());
            Assert.True((await replay.Content.ReadFromJsonAsync<GoodsReceiptAcceptance>())!.IdempotentReplay);
            Assert.Equal(0, counter.Count);
            Assert.Empty(fixture.DrainDocumentSignals());
        }
        finally { await SetWeightAsync(originalWeight); }

        async Task SetWeightAsync(object? value)
        {
            await using var update = new SqlCommand("UPDATE dbo.Products SET UnitGrossWeightKg=@Weight WHERE ProductId=@Id", connection);
            update.Parameters.AddWithValue("@Id", fixture.ProductId);
            update.Parameters.AddWithValue("@Weight", value ?? DBNull.Value);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        async Task<HttpResponseMessage> ConfirmAsync()
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm") { Content = JsonContent.Create(request) };
            message.Headers.Add("Idempotency-Key", request.DocumentId.ToString("N"));
            return await client.SendAsync(message);
        }
    }
}
