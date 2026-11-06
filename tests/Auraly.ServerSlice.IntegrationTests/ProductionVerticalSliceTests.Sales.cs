using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Application.Authorization;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Inventory;
using Auraly.Contracts.Organization;
using Auraly.Contracts.Sales;
using Auraly.Domain.Authorization;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class ProductionVerticalSliceTests
{
    [Fact]
    public async Task Accepted_composition_keeps_its_version_and_protects_inventory_mode_until_processing()
    {
        var (first, second, parent) = await SeedSaleCompositionAsync(false, false);
        using var client = fixture.CreateAdminClient(CommercePermissionCodes.SalesCreate,
            CommercePermissionCodes.SalesRestartDraft, CatalogPermissionCodes.Read, CatalogPermissionCodes.Update);
        var draft = await OpenCompositionDraftAsync(client);
        using var added = await SendRawAsync(client, $"/api/commerce/v1/pos/drafts/{draft.DraftId}/items",
            new AddOnlineSalesDraftItemRequest(parent.ToString(), 2, draft.Version), Guid.NewGuid().ToString());
        added.EnsureSuccessStatusCode();
        draft = (await added.Content.ReadFromJsonAsync<OnlineSalesDraft>())!;
        fixture.PauseDocumentProcessing();
        try
        {
            using var completed = await SendRawAsync(client, $"/api/commerce/v1/pos/drafts/{draft.DraftId}/complete",
                new CompleteOnlineSalesDraftRequest(draft.Version, [new("Cash", draft.PayableAmount, null)],
                    DocumentType: PosSaleDocumentTypes.Receipt), Guid.NewGuid().ToString());
            Assert.True(completed.IsSuccessStatusCode, await completed.Content.ReadAsStringAsync());
            var receipt = (await completed.Content.ReadFromJsonAsync<CompleteOnlineSalesDraftResponse>())!.Receipt;
            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
                await using var mutation = new SqlCommand("""
                    UPDATE dbo.Products SET ManageStock=1 WHERE ProductId=@Parent;
                    EXEC catalog.ProductCompositionValidate @TenantId=@Tenant,@ProductIds=@Products;
                    """, connection, transaction);
                mutation.Parameters.AddWithValue("@Parent", parent);
                mutation.Parameters.AddWithValue("@Tenant", fixture.TenantId);
                mutation.Parameters.AddWithValue("@Products", JsonSerializer.Serialize(new[] { parent }));
                var refused = await Assert.ThrowsAsync<SqlException>(() => mutation.ExecuteNonQueryAsync());
                Assert.Equal(51024, refused.Number);
                await transaction.RollbackAsync();
            }
            using var edited = await client.PutAsJsonAsync($"/api/commerce/v1/products/{parent}/recipe",
                new PublishProductRecipeRequest(1, [new(second, 9)]));
            Assert.True(edited.IsSuccessStatusCode, await edited.Content.ReadAsStringAsync());
            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
                await using var mutation = new SqlCommand("""
                    UPDATE dbo.Products SET ManageStock=0 WHERE ProductId=@First;
                    EXEC catalog.ProductCompositionValidate @TenantId=@Tenant,@ProductIds=@Products;
                    """, connection, transaction);
                mutation.Parameters.AddWithValue("@First", first);
                mutation.Parameters.AddWithValue("@Tenant", fixture.TenantId);
                mutation.Parameters.AddWithValue("@Products", JsonSerializer.Serialize(new[] { first }));
                var refused = await Assert.ThrowsAsync<SqlException>(() => mutation.ExecuteNonQueryAsync());
                Assert.Equal(51024, refused.Number);
                await transaction.RollbackAsync();
            }
            fixture.ResumeDocumentProcessing();
            var signal = Assert.Single(fixture.DrainDocumentSignals(), item => item.DocumentId == receipt.DocumentId);
            await fixture.DocumentSignals.PublishAsync(signal);
            await AssertCompositionSaleAsync(receipt.DocumentId, first, second, parent, false);
        }
        finally { fixture.ResumeDocumentProcessing(); }
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    public async Task Recipe_capture_and_device_availability_validate_the_component_stock(bool combo, int quantity)
    {
        var (first, second, parent) = await SeedSaleCompositionAsync(combo, false);
        var originalPolicy = await ScalarAsync<bool>(
            "SELECT AllowNegativeStockSales FROM dbo.Warehouses WHERE WarehouseId=@WarehouseId AND @Id=@Id", parent);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var policy = new SqlCommand("UPDATE dbo.Warehouses SET AllowNegativeStockSales=@Allow WHERE WarehouseId=@Warehouse", connection);
        policy.Parameters.AddWithValue("@Allow", false);
        policy.Parameters.AddWithValue("@Warehouse", fixture.WarehouseId);
        await policy.ExecuteNonQueryAsync();
        try
        {
            using var admin = fixture.CreateAdminClient(InventoryPermissionCodes.Adjust,
                CommercePermissionCodes.SalesCreate, CommercePermissionCodes.SalesRestartDraft);
            await SendAsync(admin, "/api/commerce/v1/inventory-adjustments/confirm",
                new ConfirmInventoryAdjustmentRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
                    DateTimeOffset.UtcNow, "MANUAL_ADJUSTMENT", null, null, [new(1, second, quantity-21, null)]), Guid.NewGuid().ToString());
            using var device = fixture.CreateClient();
            device.DefaultRequestHeaders.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString());
            device.DefaultRequestHeaders.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
            using var availability = await device.PostAsJsonAsync(
                $"/api/pos/v1/inventory/availability?businessId={fixture.BusinessId}&warehouseId={fixture.WarehouseId}",
                new InventoryAvailabilityRequest(parent, fixture.WarehouseId, quantity, Guid.NewGuid()));
            Assert.True(availability.IsSuccessStatusCode, await availability.Content.ReadAsStringAsync());
            var stock = (await availability.Content.ReadFromJsonAsync<InventoryAvailabilityResponse>())!;
            Assert.False(stock.IsAvailable); Assert.True(stock.ValidationRequired); Assert.Equal(quantity-1m, stock.AvailableQuantity);
            var draft = await OpenCompositionDraftAsync(admin);
            using var insufficient = await SendRawAsync(admin, $"/api/commerce/v1/pos/drafts/{draft.DraftId}/items",
                new AddOnlineSalesDraftItemRequest(parent.ToString(), quantity, draft.Version), Guid.NewGuid().ToString());
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, insufficient.StatusCode);
            Assert.Equal(20m, await BalanceAsync(first)); Assert.Equal(quantity-1m, await BalanceAsync(second));
        }
        finally
        {
            policy.Parameters["@Allow"].Value = originalPolicy;
            await policy.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Multiple_composed_lines_share_stock_and_checkout_revalidates_it(bool combo)
    {
        var (first, second, parent) = await SeedSaleCompositionAsync(combo, false);
        var other = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var clone = new SqlCommand("""
            INSERT dbo.Products(ProductId,TenantId,ProductCode,Name,BaseUnitCode,TaxProfileId,ManageStock,IsIngredient)
            SELECT @Other,TenantId,@Code,N'Otra composición',BaseUnitCode,TaxProfileId,0,0
            FROM dbo.Products WHERE ProductId=@Parent;
            INSERT dbo.ProductPrices(ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,ValidFrom,IsActive,CreatedAt,
                CostBasisAmount,TargetMarginPercent)
            VALUES(NEWID(),@Business,@Other,10000,N'COP',SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET(),1000,25);
            """, connection))
        {
            clone.Parameters.AddWithValue("@Other", other); clone.Parameters.AddWithValue("@Parent", parent);
            clone.Parameters.AddWithValue("@Business", fixture.BusinessId); clone.Parameters.AddWithValue("@Code", $"OTHER-{other:N}"[..32]);
            await clone.ExecuteNonQueryAsync();
        }
        using var admin = fixture.CreateAdminClient(CatalogPermissionCodes.Read, CatalogPermissionCodes.Update,
            InventoryPermissionCodes.Adjust, CommercePermissionCodes.SalesCreate, CommercePermissionCodes.SalesRestartDraft);
        using var composition = await admin.PutAsJsonAsync($"/api/commerce/v1/products/{other}/recipe",
            new PublishProductRecipeRequest(0, [new(first, 2), new(second, 1)], combo ? "Combo" : "Ingredients"));
        composition.EnsureSuccessStatusCode();
        var originalPolicy = await ScalarAsync<bool>("SELECT AllowNegativeStockSales FROM dbo.Warehouses WHERE WarehouseId=@WarehouseId AND @Id=@Id", parent);
        await using var policy = new SqlCommand("UPDATE dbo.Warehouses SET AllowNegativeStockSales=@Allow WHERE WarehouseId=@Warehouse", connection);
        policy.Parameters.AddWithValue("@Allow", false); policy.Parameters.AddWithValue("@Warehouse", fixture.WarehouseId);
        await policy.ExecuteNonQueryAsync();
        try
        {
            await SendAsync(admin, "/api/commerce/v1/inventory-adjustments/confirm",
                new ConfirmInventoryAdjustmentRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
                    DateTimeOffset.UtcNow, "MANUAL_ADJUSTMENT", null, null, [new(1, first, -7, null)]), Guid.NewGuid().ToString());
            using var device = fixture.CreateClient();
            device.DefaultRequestHeaders.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString());
            device.DefaultRequestHeaders.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
            using var batch = await device.PostAsJsonAsync(
                $"/api/pos/v1/inventory/availability-batch?businessId={fixture.BusinessId}&warehouseId={fixture.WarehouseId}",
                new InventoryAvailabilityBatchRequest(fixture.WarehouseId, Guid.NewGuid(), [new(parent, 3), new(other, 4)]));
            Assert.True(batch.IsSuccessStatusCode, await batch.Content.ReadAsStringAsync());
            var availability = (await batch.Content.ReadFromJsonAsync<InventoryAvailabilityResponse[]>())!;
            Assert.True(availability[0].IsAvailable); Assert.False(availability[1].IsAvailable);
            Assert.Equal(3.5m, availability[1].AvailableQuantity);
            var draft = await OpenCompositionDraftAsync(admin);
            using var captured = await SendRawAsync(admin, $"/api/commerce/v1/pos/drafts/{draft.DraftId}/items",
                new AddOnlineSalesDraftItemRequest(parent.ToString(), 3, draft.Version), Guid.NewGuid().ToString());
            captured.EnsureSuccessStatusCode(); draft = (await captured.Content.ReadFromJsonAsync<OnlineSalesDraft>())!;
            using var tooMany = await SendRawAsync(admin, $"/api/commerce/v1/pos/drafts/{draft.DraftId}/items",
                new AddOnlineSalesDraftItemRequest(other.ToString(), 4, draft.Version), Guid.NewGuid().ToString());
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, tooMany.StatusCode);
            await SendAsync(admin, "/api/commerce/v1/inventory-adjustments/confirm",
                new ConfirmInventoryAdjustmentRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
                    DateTimeOffset.UtcNow, "MANUAL_ADJUSTMENT", null, null, [new(1, second, -18, null)]), Guid.NewGuid().ToString());
            var validation = (await admin.GetFromJsonAsync<OnlineSalesInventoryValidation>(
                $"/api/commerce/v1/pos/drafts/{draft.DraftId}/inventory-validation"))!;
            Assert.False(validation.IsValid); Assert.Single(validation.Issues);
            using var checkout = await SendRawAsync(admin, $"/api/commerce/v1/pos/drafts/{draft.DraftId}/complete",
                new CompleteOnlineSalesDraftRequest(draft.Version, [new("Cash", draft.PayableAmount, null)],
                    DocumentType: PosSaleDocumentTypes.Receipt), Guid.NewGuid().ToString());
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, checkout.StatusCode);
            Assert.Equal(13m, await BalanceAsync(first)); Assert.Equal(2m, await BalanceAsync(second));
        }
        finally
        {
            policy.Parameters["@Allow"].Value = originalPolicy;
            await policy.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData("DirectSale", false, false)]
    [InlineData("DirectSale", false, true)]
    [InlineData("DirectSale", true, false)]
    [InlineData("DirectSale", true, true)]
    [InlineData("Store", false, false)]
    [InlineData("Store", false, true)]
    [InlineData("Store", true, false)]
    [InlineData("Store", true, true)]
    public async Task Web_sale_uses_the_shared_composition_inventory_engine(string mode, bool combo, bool ownStock)
    {
        var (first, second, parent) = await SeedSaleCompositionAsync(combo, ownStock);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var modeCommand = new SqlCommand("UPDATE dbo.Businesses SET PointOfSaleMode=@Mode WHERE BusinessId=@Business", connection);
        modeCommand.Parameters.AddWithValue("@Mode", mode);
        modeCommand.Parameters.AddWithValue("@Business", fixture.BusinessId);
        await modeCommand.ExecuteNonQueryAsync();
        try
        {
            using var client = fixture.CreateAdminClient(CommercePermissionCodes.SalesCreate, CommercePermissionCodes.SalesRestartDraft);
            var draft = await OpenCompositionDraftAsync(client);
            using var added = await SendRawAsync(client, $"/api/commerce/v1/pos/drafts/{draft.DraftId}/items",
                new AddOnlineSalesDraftItemRequest(parent.ToString(), 2, draft.Version), Guid.NewGuid().ToString());
            Assert.True(added.IsSuccessStatusCode, await added.Content.ReadAsStringAsync());
            draft = (await added.Content.ReadFromJsonAsync<OnlineSalesDraft>())!;
            var checkout = new CompleteOnlineSalesDraftRequest(draft.Version,
                [new("Cash", draft.PayableAmount, null)], DocumentType: PosSaleDocumentTypes.Receipt);
            var key = Guid.NewGuid().ToString();
            using var writes = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                commandFilter: command => command.CommandText.Contains("INSERT INTO dbo.SalesDocumentLines", StringComparison.Ordinal)
                    || command.CommandText.Contains("SET RecognizedCostAmount=cost.Cost", StringComparison.Ordinal));
            using var completed = await SendRawAsync(client, $"/api/commerce/v1/pos/drafts/{draft.DraftId}/complete", checkout, key);
            Assert.True(completed.IsSuccessStatusCode, await completed.Content.ReadAsStringAsync());
            var receipt = (await completed.Content.ReadFromJsonAsync<CompleteOnlineSalesDraftResponse>())!.Receipt;
            using var replay = await SendRawAsync(client, $"/api/commerce/v1/pos/drafts/{draft.DraftId}/complete", checkout, key);
            Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
            Assert.Equal(receipt.DocumentId, (await replay.Content.ReadFromJsonAsync<CompleteOnlineSalesDraftResponse>())!.Receipt.DocumentId);
            Assert.Equal(1, writes.Count);
            await AssertCompositionSaleAsync(receipt.DocumentId, first, second, parent, ownStock);
        }
        finally
        {
            modeCommand.Parameters["@Mode"].Value = "DirectSale";
            await modeCommand.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Disconnected_sale_survives_restart_and_consumes_the_shared_composition_once(bool combo, bool ownStock)
    {
        var (first, second, parent) = await SeedSaleCompositionAsync(combo, ownStock);
        var seriesId = Guid.NewGuid();
        var seriesCode = seriesId.ToString("N")[..8];
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var series = new SqlCommand("""
                INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
                    Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                VALUES(@Series,@Business,@Device,N'SalesReceipt',N'CVI',@Code,8,1,100,1,1,SYSDATETIMEOFFSET());
                """, connection);
            series.Parameters.AddWithValue("@Series", seriesId);
            series.Parameters.AddWithValue("@Business", fixture.BusinessId);
            series.Parameters.AddWithValue("@Device", fixture.DeviceId);
            series.Parameters.AddWithValue("@Code", seriesCode);
            await series.ExecuteNonQueryAsync();
        }
        var path = Path.Combine(Path.GetTempPath(), $"auraly-composition-sale-{Guid.NewGuid():N}.db");
        var user = new UserId(fixture.UserId);
        var context = new SalesExecutionContext(new TenantId(fixture.TenantId), new BusinessId(fixture.BusinessId),
            new WarehouseId(fixture.WarehouseId), user, new DeviceId(fixture.DeviceId), new WorkSessionId(fixture.WorkSessionId), false);
        var confirm = new ConfirmOfflineSaleService(new PermissionAuthorizer(new CompositionSalePermissions(
            new UserPermissionSet(context.TenantId, user, [CommercePermissionCodes.SalesCreate]))));
        try
        {
            var local = new PosEdgeSaleStore($"Data Source={path}", confirm);
            await local.InitializeAsync();
            await local.ProvisionDocumentSeriesAsync(new(seriesId, context.DeviceId!.Value,
                PosSaleDocumentTypes.Receipt, "CVI", seriesCode, 8, 1, 100));
            var product = new PosCatalogProduct(new ProductId(parent), "COMPOSITION", "Venta de composición", [], true, false, "01", 0);
            var command = new PosEdgeIssueCommand(user, new DocumentId(Guid.NewGuid()), context, DateTimeOffset.UtcNow,
                null, "222222222", null, null, null,
                [new OfflineSaleLine(product, 2, 10000, 0, 0, 3000, 20000, 20000)],
                [new OfflineSalePayment("Cash", 20000)], DocumentType: PosSaleDocumentTypes.Receipt);
            var issued = await local.IssueAsync(command);
            Assert.True((await local.IssueAsync(command)).WasAlreadyIssued);
            local = new PosEdgeSaleStore($"Data Source={path}", confirm);
            await local.InitializeAsync();
            var pending = Assert.Single(await local.GetPendingOutboxAsync());
            Assert.Equal(20m, await BalanceAsync(first));
            using var server = fixture.CreateClient();
            var upload = PosSaleContractSerializer.Deserialize(pending.Payload);
            using var message = fixture.CreateUploadMessage(upload);
            using var uploaded = await server.SendAsync(message);
            Assert.True(uploaded.IsSuccessStatusCode, await uploaded.Content.ReadAsStringAsync());
            using var retryMessage = fixture.CreateUploadMessage(upload);
            using var repeated = await server.SendAsync(retryMessage);
            Assert.True(repeated.IsSuccessStatusCode, await repeated.Content.ReadAsStringAsync());
            Assert.True((await repeated.Content.ReadFromJsonAsync<PosSaleUploadResponse>())!.IsDuplicate);
            await AssertCompositionSaleAsync(issued.DocumentId.Value, first, second, parent, ownStock);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private async Task<(Guid First, Guid Second, Guid Parent)> SeedSaleCompositionAsync(bool combo, bool ownStock)
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var parent = Guid.NewGuid();
        await SeedAsync(first, second, parent);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var setup = new SqlCommand("""
                UPDATE dbo.TaxProfiles SET Rate=0 WHERE TaxProfileId=(SELECT TaxProfileId FROM dbo.Products WHERE ProductId=@Parent);
                UPDATE dbo.Products SET IsIngredient=@Ingredient WHERE ProductId IN(@First,@Second);
                UPDATE dbo.Products SET ManageStock=@Own WHERE ProductId=@Parent;
                """, connection);
            setup.Parameters.AddWithValue("@Parent", parent);
            setup.Parameters.AddWithValue("@First", first);
            setup.Parameters.AddWithValue("@Second", second);
            setup.Parameters.AddWithValue("@Ingredient", !combo);
            setup.Parameters.AddWithValue("@Own", ownStock);
            await setup.ExecuteNonQueryAsync();
        }
        using var admin = fixture.CreateAdminClient(CatalogPermissionCodes.Read, CatalogPermissionCodes.Update,
            InventoryPermissionCodes.Adjust);
        using var composition = await admin.PutAsJsonAsync($"/api/commerce/v1/products/{parent}/recipe",
            new PublishProductRecipeRequest(0, [new(first, 2), new(second, 1)], combo ? "Combo" : "Ingredients"));
        Assert.True(composition.IsSuccessStatusCode, await composition.Content.ReadAsStringAsync());
        var lines = new List<InventoryAdjustmentLineRequest> { new(1, first, 20, 5), new(2, second, 20, 7) };
        if (ownStock) lines.Add(new(3, parent, 3, 17));
        await SendAsync(admin, "/api/commerce/v1/inventory-adjustments/confirm",
            new ConfirmInventoryAdjustmentRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
                DateTimeOffset.UtcNow, "INITIAL_BALANCE", null, null, lines), Guid.NewGuid().ToString());
        return (first, second, parent);
    }

    private async Task<OnlineSalesDraft> OpenCompositionDraftAsync(HttpClient client)
    {
        using var opened = await client.PostAsJsonAsync("/api/commerce/v1/pos/drafts/active",
            new OpenOnlineSalesDraftRequest(new(fixture.BusinessId, fixture.WarehouseId, await fixture.OpenWebWorkSessionAsync())));
        opened.EnsureSuccessStatusCode();
        var draft = (await opened.Content.ReadFromJsonAsync<OnlineSalesDraft>())!;
        if (draft.Lines.Count == 0) return draft;
        using var reset = await SendRawAsync(client, $"/api/commerce/v1/pos/drafts/{draft.DraftId}/reset",
            new ResetOnlineSalesDraftRequest(draft.Version), Guid.NewGuid().ToString());
        reset.EnsureSuccessStatusCode();
        return (await reset.Content.ReadFromJsonAsync<OnlineSalesDraft>())!;
    }

    private async Task AssertCompositionSaleAsync(Guid document, Guid first, Guid second, Guid parent, bool ownStock)
    {
        Assert.Equal(ownStock ? 20m : 16m, await BalanceAsync(first));
        Assert.Equal(ownStock ? 20m : 18m, await BalanceAsync(second));
        Assert.Equal(ownStock ? 1m : 0m, await BalanceAsync(parent));
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT ProductId,QuantityChange FROM dbo.InventoryMovements WHERE DocumentId=@Document ORDER BY ProductId;
            SELECT line.RecognizedCostAmount,line.ProductRecipeVersionId,payload.InventoryCompositionJson,
                payload.PayloadJson,job.Status,DATEDIFF_BIG(microsecond,job.StartedAt,job.CompletedAt)
            FROM dbo.SalesDocumentLines line
            JOIN dbo.DocumentProcessingPayloads payload ON payload.DocumentId=line.DocumentId
            JOIN dbo.DocumentProcessingJobs job ON job.DocumentId=payload.DocumentId AND job.DocumentType=payload.DocumentType
            WHERE line.DocumentId=@Document;
            """, connection);
        command.Parameters.AddWithValue("@Document", document);
        await using var reader = await command.ExecuteReaderAsync();
        var movements = new Dictionary<Guid, decimal>();
        while (await reader.ReadAsync()) movements.Add(reader.GetGuid(0), reader.GetDecimal(1));
        Assert.Equal(ownStock ? 1 : 2, movements.Count);
        if (ownStock) Assert.Equal(-2m, movements[parent]);
        else { Assert.Equal(-4m, movements[first]); Assert.Equal(-2m, movements[second]); }
        Assert.True(await reader.NextResultAsync()); Assert.True(await reader.ReadAsync());
        Assert.Equal(34m, reader.GetDecimal(0)); Assert.False(reader.IsDBNull(1));
        var frozen = JsonDocument.Parse(reader.GetString(2));
        Assert.Single(frozen.RootElement.EnumerateArray());
        Assert.False(frozen.RootElement[0].TryGetProperty("Ingredients", out _));
        var payload = JsonDocument.Parse(reader.GetString(3));
        Assert.False(payload.RootElement.GetProperty("lines")[0].TryGetProperty("productRecipeVersionId", out _));
        Assert.Equal("Completed", reader.GetString(4));
        var engineMilliseconds = reader.GetInt64(5) / 1000m;
        Assert.True(engineMilliseconds < 2000m, $"Composition sale engine took {engineMilliseconds:F1} ms.");
        output.WriteLine($"Sale {document}: {(ownStock ? "own stock" : "components")}, recognized cost 34, replay unchanged, engine {engineMilliseconds:F1} ms.");
    }

    private sealed class CompositionSalePermissions(UserPermissionSet permissions) : IUserPermissionSetProvider
    {
        public UserPermissionSet Get(TenantId tenantId, UserId userId) => permissions;
    }
}
