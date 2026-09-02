using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PurchaseOrderReceiptTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Purchase_order_suggestion_is_scoped_and_returns_the_requested_horizon()
    {
        using var client = CreateClient();
        var request = new PurchaseOrderSuggestionRequest(
            fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            [fixture.ProductId], 14);
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/purchase-orders/suggestions", request);
        response.EnsureSuccessStatusCode();
        var suggestion = Assert.Single(await response.Content
            .ReadFromJsonAsync<PurchaseOrderSuggestion[]>() ?? []);
        Assert.Equal(fixture.ProductId, suggestion.ProductId);
        Assert.Equal(14, suggestion.TargetCoverageDays);
        Assert.True(suggestion.UnitsPerPresentation > 0);

        using var denied = await client.PostAsJsonAsync(
            "/api/commerce/v1/purchase-orders/suggestions",
            request with { BusinessId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Local_capture_is_saved_only_on_request_and_the_saved_draft_can_be_confirmed()
    {
        using var client = CreateClient();
        var orderedAt = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.FromHours(-5));
        var purchaseOrderId = Guid.NewGuid();
        var line = new PurchaseOrderLineRequest(
            Guid.NewGuid(), 1, fixture.ProductId, "Producto desde captura",
            2m, 5_000m, 0m, "01", 19m,
            PurchasingTaxTreatments.DeductibleInputVat, "Unidad", 2m, 1m);
        var draftRequest = new SavePurchaseOrderDraftRequest(
            purchaseOrderId, fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            orderedAt, orderedAt.AddDays(7), "COP", "Captura local",
            [line], null);

        using var save = await client.PutAsJsonAsync(
            $"/api/commerce/v1/purchase-orders/{purchaseOrderId:D}/draft", draftRequest);
        var saveBody = await save.Content.ReadAsStringAsync();
        Assert.True(save.IsSuccessStatusCode,
            $"Expected draft save to succeed, got {save.StatusCode}: {saveBody}");
        var saved = await save.Content.ReadFromJsonAsync<PurchaseOrderDetail>();
        Assert.NotNull(saved);
        Assert.Equal(PurchaseOrderStatuses.Draft, saved.Status);
        Assert.False(string.IsNullOrWhiteSpace(saved.ConcurrencyToken));
        Assert.Equal(2m, Assert.Single(saved.Lines).OrderedQuantity);

        var confirmRequest = new ConfirmPurchaseOrderRequest(
            purchaseOrderId, fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            orderedAt, orderedAt.AddDays(7), "COP", "Captura local",
            [line], saved.ConcurrencyToken);
        using var confirmMessage = new HttpRequestMessage(
            HttpMethod.Post, "/api/commerce/v1/purchase-orders/confirm")
        {
            Content = JsonContent.Create(confirmRequest)
        };
        confirmMessage.Headers.Add("Idempotency-Key", $"purchase-order-{purchaseOrderId:N}");
        using var confirm = await client.SendAsync(confirmMessage);
        var confirmBody = await confirm.Content.ReadAsStringAsync();
        Assert.True(confirm.StatusCode == HttpStatusCode.Created,
            $"Expected confirmation to succeed, got {confirm.StatusCode}: {confirmBody}");
    }

    [Fact]
    public async Task Saved_purchase_order_draft_can_be_discarded_with_its_version()
    {
        using var client = CreateClient();
        var orderedAt = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.FromHours(-5));
        var purchaseOrderId = Guid.NewGuid();
        var request = new SavePurchaseOrderDraftRequest(
            purchaseOrderId, fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            orderedAt, orderedAt.AddDays(12), "COP", "Borrador descartable", [], null);
        using var save = await client.PutAsJsonAsync(
            $"/api/commerce/v1/purchase-orders/{purchaseOrderId:D}/draft", request);
        save.EnsureSuccessStatusCode();
        var draft = await save.Content.ReadFromJsonAsync<PurchaseOrderDetail>();
        Assert.NotNull(draft);

        using var delete = await client.DeleteAsync(
            $"/api/commerce/v1/purchase-orders/{purchaseOrderId:D}/draft?concurrencyToken={Uri.EscapeDataString(draft.ConcurrencyToken!)}");
        delete.EnsureSuccessStatusCode();
        using var get = await client.GetAsync(
            $"/api/commerce/v1/purchase-orders/{purchaseOrderId:D}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Order_can_be_recovered_and_received_in_changed_partial_quantities()
    {
        using var client = CreateClient();
        var order = await ConfirmOrderAsync(client, 10m);
        Assert.Matches(@"^OCP[A-Z0-9]{1,16}-\d{8}$", order.DocumentNumber);

        var source = await client.GetFromJsonAsync<PurchaseOrderReceiptSource>(
            $"/api/commerce/v1/purchase-orders/{order.PurchaseOrderId:D}/receipt-source");
        Assert.NotNull(source);
        Assert.Equal(10m, source.Lines.Single().RemainingQuantity);

        await ConfirmReceiptAsync(client, source, 8m);
        var partial = await WaitForOrderAsync(order.PurchaseOrderId, PurchaseOrderStatuses.PartiallyReceived);
        Assert.Equal(8m, partial.Lines.Single().ReceivedQuantity);
        Assert.Equal(2m, partial.Lines.Single().RemainingQuantity);

        var recoveredAgain = await client.GetFromJsonAsync<PurchaseOrderReceiptSource>(
            $"/api/commerce/v1/purchase-orders/{order.PurchaseOrderId:D}/receipt-source");
        Assert.NotNull(recoveredAgain);
        Assert.Equal(2m, recoveredAgain.Lines.Single().RemainingQuantity);

        await ConfirmReceiptAsync(client, recoveredAgain, 2m);
        var completed = await WaitForOrderAsync(order.PurchaseOrderId, PurchaseOrderStatuses.Received);
        Assert.Equal(10m, completed.Lines.Single().ReceivedQuantity);
        Assert.Equal(0m, completed.Lines.Single().RemainingQuantity);
    }

    [Fact]
    public async Task Over_receipt_requires_reason_and_explicit_permission_but_preserves_actual_quantity()
    {
        using var authorized = CreateClient(PurchasingPermissionCodes.AuthorizeOverReceipt);
        var order = await ConfirmOrderAsync(authorized, 10m);
        var source = await authorized.GetFromJsonAsync<PurchaseOrderReceiptSource>(
            $"/api/commerce/v1/purchase-orders/{order.PurchaseOrderId:D}/receipt-source");
        Assert.NotNull(source);

        using (var missingReason = CreateReceiptMessage(source, 12m, null))
        using (var response = await authorized.SendAsync(missingReason))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var unauthorized = CreateClient();
        using (var denied = CreateReceiptMessage(source, 12m, "El proveedor despachó dos unidades adicionales"))
        using (var response = await unauthorized.SendAsync(denied))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await ConfirmReceiptAsync(authorized, source, 12m,
            "El proveedor despachó dos unidades adicionales");
        var completed = await WaitForOrderAsync(order.PurchaseOrderId, PurchaseOrderStatuses.Received);
        Assert.Equal(12m, completed.Lines.Single().ReceivedQuantity);
        Assert.Equal(0m, completed.Lines.Single().RemainingQuantity);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT OverReceiptAuthorized,OverReceiptReason
            FROM dbo.GoodsReceiptLines
            WHERE PurchaseOrderLineId=@LineId AND Quantity=12;
            """, connection);
        command.Parameters.AddWithValue("@LineId", source.Lines.Single().LineId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.Equal("El proveedor despachó dos unidades adicionales", reader.GetString(1));
    }

    [Fact]
    public async Task Multiple_orders_are_listed_in_the_standard_paged_grid_and_scoped_by_status()
    {
        using var client = CreateClient();
        var first = await ConfirmOrderAsync(client, 4m);
        var second = await ConfirmOrderAsync(client, 7m);

        var page = await client.GetFromJsonAsync<PurchaseOrderPage>(
            "/api/commerce/v1/purchase-orders?status=Open&page=1&pageSize=100");
        Assert.NotNull(page);
        Assert.Contains(page.Items, item => item.PurchaseOrderId == first.PurchaseOrderId);
        Assert.Contains(page.Items, item => item.PurchaseOrderId == second.PurchaseOrderId);
        Assert.All(page.Items, item => Assert.Equal(PurchaseOrderStatuses.Open, item.Status));
    }

    [Fact]
    public async Task Accepted_receipt_is_counted_as_pending_until_processing_finishes()
    {
        using var client = CreateClient();
        var order = await ConfirmOrderAsync(client, 10m);
        var source = await client.GetFromJsonAsync<PurchaseOrderReceiptSource>(
            $"/api/commerce/v1/purchase-orders/{order.PurchaseOrderId:D}/receipt-source");
        Assert.NotNull(source);

        fixture.PauseDocumentProcessing();
        try
        {
            await ConfirmReceiptAsync(client, source, 8m);
            var recovered = await client.GetFromJsonAsync<PurchaseOrderReceiptSource>(
                $"/api/commerce/v1/purchase-orders/{order.PurchaseOrderId:D}/receipt-source");
            Assert.NotNull(recovered);
            Assert.Equal(2m, recovered.Lines.Single().RemainingQuantity);

            using var duplicateBalance = CreateReceiptMessage(recovered, 3m, null);
            using var response = await client.SendAsync(duplicateBalance);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var signal = Assert.Single(fixture.DrainDocumentSignals());
            fixture.ResumeDocumentProcessing();
            await fixture.DocumentSignals.PublishAsync(signal);
        }
        finally
        {
            fixture.ResumeDocumentProcessing();
            fixture.DrainDocumentSignals();
        }

        var partial = await WaitForOrderAsync(order.PurchaseOrderId,
            PurchaseOrderStatuses.PartiallyReceived);
        Assert.Equal(8m, partial.Lines.Single().ReceivedQuantity);
    }

    private HttpClient CreateClient(params string[] extraPermissions) => fixture.CreateAdminClient(
        [
            PurchasingPermissionCodes.ReadPurchaseOrders,
            PurchasingPermissionCodes.CreatePurchaseOrders,
            PurchasingPermissionCodes.ConfirmPurchaseOrders,
            PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts,
            .. extraPermissions
        ]);

    private async Task<PurchaseOrderConfirmation> ConfirmOrderAsync(HttpClient client, decimal quantity)
    {
        var orderedAt = new DateTimeOffset(2026, 8, 30, 9, 0, 0, TimeSpan.FromHours(-5));
        var request = new ConfirmPurchaseOrderRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            orderedAt, orderedAt.AddDays(5), "COP", "Orden de prueba",
            [new PurchaseOrderLineRequest(Guid.NewGuid(), 1, fixture.ProductId,
                "Producto E2E", quantity, 5_000m, 0m, "01", 19m,
                PurchasingTaxTreatments.DeductibleInputVat, "Unidad", quantity, 1m)], null);
        using var message = new HttpRequestMessage(HttpMethod.Post,
            "/api/commerce/v1/purchase-orders/confirm") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", $"purchase-order-{request.PurchaseOrderId:N}");
        using var response = await client.SendAsync(message);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"Expected Created, got {response.StatusCode}: {responseBody}");
        return await response.Content.ReadFromJsonAsync<PurchaseOrderConfirmation>()
            ?? throw new InvalidOperationException("The purchase order response was empty.");
    }

    private async Task ConfirmReceiptAsync(HttpClient client, PurchaseOrderReceiptSource source,
        decimal quantity, string? overReceiptReason = null)
    {
        using var message = CreateReceiptMessage(source, quantity, overReceiptReason);
        using var response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private HttpRequestMessage CreateReceiptMessage(PurchaseOrderReceiptSource source,
        decimal quantity, string? overReceiptReason)
    {
        var receivedAt = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.FromHours(-5));
        var orderLine = source.Lines.Single();
        var request = new ConfirmGoodsReceiptRequest(
            Guid.NewGuid(), fixture.BusinessId, source.WarehouseId, source.SupplierId,
            $"FC-{Guid.NewGuid():N}", receivedAt, receivedAt, false, null, "COP",
            "Recepción vinculada", [new GoodsReceiptLineRequest(1, orderLine.ProductId,
                orderLine.Description, quantity, orderLine.UnitCost, 0m, orderLine.TaxCode,
                orderLine.TaxRate, orderLine.TaxTreatment, orderLine.PresentationName,
                quantity / orderLine.UnitsPerPresentation, orderLine.UnitsPerPresentation,
                orderLine.LineId, overReceiptReason)], PurchaseEvidenceType:
                PurchaseEvidenceTypes.SupplierElectronicInvoice, PurchaseOrderId: source.PurchaseOrderId);
        var message = new HttpRequestMessage(HttpMethod.Post,
            "/api/commerce/v1/goods-receipts/confirm") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", $"purchase-receipt-{request.DocumentId:N}");
        return message;
    }

    private async Task<PurchaseOrderDetail> WaitForOrderAsync(Guid orderId, string expectedStatus)
    {
        using var client = CreateClient(PurchasingPermissionCodes.AuthorizeOverReceipt);
        PurchaseOrderDetail? order = null;
        for (var attempt = 0; attempt < 80; attempt++)
        {
            order = await client.GetFromJsonAsync<PurchaseOrderDetail>(
                $"/api/commerce/v1/purchase-orders/{orderId:D}");
            if (order?.Status == expectedStatus) return order;
            await Task.Delay(25);
        }
        return Assert.IsType<PurchaseOrderDetail>(order);
    }
}
