using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Purchasing;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class GoodsReceiptWorkspaceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Draft_is_durable_concurrent_and_visible_in_the_workspace()
    {
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var request = CreateDraft();

        using var createdResponse = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}", request);
        Assert.Equal(HttpStatusCode.OK, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<GoodsReceiptDraft>();
        Assert.NotNull(created);
        Assert.Equal(10m, created.NetAmount);
        Assert.Equal(1.9m, created.TaxAmount);
        Assert.Equal(11.9m, created.GrandTotal);
        Assert.NotEmpty(created.ConcurrencyToken);

        var recovered = await client.GetFromJsonAsync<GoodsReceiptDraft>(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}");
        Assert.NotNull(recovered);
        Assert.Single(recovered.Lines);

        var page = await client.GetFromJsonAsync<GoodsReceiptPage>(
            "/api/commerce/v1/goods-receipts?status=Draft&page=1&pageSize=25");
        Assert.NotNull(page);
        Assert.Contains(page.Items, item => item.DocumentId == request.DraftId && item.Status == "Draft");

        var changedRequest = request with
        {
            ConcurrencyToken = created.ConcurrencyToken,
            Lines = [request.Lines.Single() with { Quantity = 2m }]
        };
        using var changedResponse = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}", changedRequest);
        Assert.Equal(HttpStatusCode.OK, changedResponse.StatusCode);
        var changed = await changedResponse.Content.ReadFromJsonAsync<GoodsReceiptDraft>();
        Assert.NotNull(changed);
        Assert.Equal(23.8m, changed.GrandTotal);
        Assert.NotEqual(created.ConcurrencyToken, changed.ConcurrencyToken);

        using var staleResponse = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}", changedRequest);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        var staleConfirmation = new ConfirmGoodsReceiptRequest(
            request.DraftId, fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            request.SupplierInvoiceNumber, request.SupplierInvoiceDate, request.ReceivedAt,
            request.CreatesPayable, request.DueDate, request.CurrencyCode, request.Notes,
            changedRequest.Lines, created.ConcurrencyToken);
        using (var staleMessage = new HttpRequestMessage(
                   HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
               { Content = JsonContent.Create(staleConfirmation) })
        {
            staleMessage.Headers.Add("Idempotency-Key", $"stale-confirm-{request.DraftId:N}");
            using var staleConfirmationResponse = await client.SendAsync(staleMessage);
            Assert.Equal(HttpStatusCode.Conflict, staleConfirmationResponse.StatusCode);
        }

        using var deleted = await client.DeleteAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}" +
            $"?concurrencyToken={Uri.EscapeDataString(changed.ConcurrencyToken)}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        using var missing = await client.GetAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Workspace_is_scoped_and_confirmation_atomically_removes_the_draft()
    {
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var options = await client.GetFromJsonAsync<GoodsReceiptWorkspaceOptions>(
            "/api/commerce/v1/goods-receipts/options");
        Assert.NotNull(options);
        Assert.Contains(options.Warehouses, item => item.WarehouseId == fixture.WarehouseId);
        Assert.Contains(options.Suppliers, item => item.SupplierId == fixture.SupplierId);

        var products = await client.GetFromJsonAsync<GoodsReceiptProductPage>(
            $"/api/commerce/v1/goods-receipts/products?supplierId={fixture.SupplierId:D}&page=1&pageSize=50");
        Assert.NotNull(products);
        Assert.Contains(products.Items, item => item.ProductId == fixture.ProductId);

        var draftRequest = CreateDraft();
        using var savedResponse = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{draftRequest.DraftId:D}", draftRequest);
        Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode);
        var savedDraft = await savedResponse.Content.ReadFromJsonAsync<GoodsReceiptDraft>();
        Assert.NotNull(savedDraft);
        var confirm = new ConfirmGoodsReceiptRequest(
            draftRequest.DraftId, fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            draftRequest.SupplierInvoiceNumber, draftRequest.SupplierInvoiceDate,
            draftRequest.ReceivedAt, draftRequest.CreatesPayable, draftRequest.DueDate,
            draftRequest.CurrencyCode, draftRequest.Notes, draftRequest.Lines,
            savedDraft.ConcurrencyToken);
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
        {
            Content = JsonContent.Create(confirm)
        };
        message.Headers.Add("Idempotency-Key", $"workspace-confirm-{draftRequest.DraftId:N}");
        using var confirmedResponse = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, confirmedResponse.StatusCode);
        var accepted = await confirmedResponse.Content.ReadFromJsonAsync<GoodsReceiptAcceptance>();
        Assert.NotNull(accepted);
        Assert.StartsWith("EMC00-", accepted.DocumentNumber);

        using var draftGone = await client.GetAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{draftRequest.DraftId:D}");
        Assert.Equal(HttpStatusCode.NotFound, draftGone.StatusCode);

        using var denied = fixture.CreateAdminClient(PurchasingPermissionCodes.ReadGoodsReceipts);
        var deniedDraft = CreateDraft();
        using var deniedResponse = await denied.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{deniedDraft.DraftId:D}", deniedDraft);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var wrongSupplier = await client.GetAsync(
            $"/api/commerce/v1/goods-receipts/products?supplierId={Guid.NewGuid():D}&page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.BadRequest, wrongSupplier.StatusCode);
    }

    private SaveGoodsReceiptDraftRequest CreateDraft()
    {
        var receivedAt = new DateTimeOffset(2026, 8, 2, 10, 30, 0, TimeSpan.FromHours(-5));
        return new(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            $"PRV-{Guid.NewGuid():N}", receivedAt.AddDays(-1), receivedAt, true,
            receivedAt.AddDays(30), "COP", "Borrador de integraci?n",
            [new GoodsReceiptLineRequest(
                1, fixture.ProductId, "Producto de recepci?n", 1m, 10m, 0m,
                "01", 19m, PurchasingTaxTreatments.DeductibleInputVat)],
            null);
    }
}
