using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Pricing;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Contracts.Purchasing;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PricingVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Goods_receipt_proposal_is_reviewed_published_pushed_and_applied_once()
    {
        fixture.DrainSynchronizationMessages();
        var productId = Guid.NewGuid();
        var barcode = $"79{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";
        await SeedProductAsync(productId, barcode, 10_000m, 20m);

        var documentId = Guid.NewGuid();
        var receivedAt = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.FromHours(-5));
        var receipt = new ConfirmGoodsReceiptRequest(
            documentId, fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            $"PRICE-{documentId:N}", receivedAt, receivedAt, false, null, "COP",
            "Costo para pricing",
            [new GoodsReceiptLineRequest(
                1, productId, "Producto pricing", 2m, 8_500m, 0m,
                "00", 0m, PurchasingTaxTreatments.NotApplicable)]);
        using (var purchasing = fixture.CreateAdminClient(
                   PurchasingPermissionCodes.CreateGoodsReceipts,
                   PurchasingPermissionCodes.ConfirmGoodsReceipts))
        using (var message = new HttpRequestMessage(
                   HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
               { Content = JsonContent.Create(receipt) })
        {
            message.Headers.Add("Idempotency-Key", $"pricing-receipt-{documentId:N}");
            using var response = await purchasing.SendAsync(message);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        Assert.Equal(10_000m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1",
            productId));

        var allPermissions = new[]
        {
            PricingPermissionCodes.Read,
            PricingPermissionCodes.ReadCostBasis,
            PricingPermissionCodes.ReviewProposals,
            PricingPermissionCodes.PublishPrices,
            PricingPermissionCodes.BulkPublish,
            PricingPermissionCodes.ReadHistory
        };
        using var pricing = fixture.CreateAdminClient(allPermissions);
        var pending = await pricing.GetFromJsonAsync<PriceRevisionPage>(
            "/api/commerce/v1/pricing/proposals?page=1&pageSize=20&status=PendingReview");
        var proposal = Assert.Single(pending!.Items.Where(x => x.ProductId == productId));
        Assert.Equal(8_500m, proposal.ObservedUnitCost);
        Assert.Equal(10_000m, proposal.CurrentSalePrice);

        using var calculatedResponse = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/calculate",
            new PriceCalculationRequest(8_500m, PriceInputModes.Margin, 20m, null, 50m, PricingRoundingModes.Up));
        calculatedResponse.EnsureSuccessStatusCode();
        var calculated = await calculatedResponse.Content.ReadFromJsonAsync<PriceCalculationResult>();
        Assert.NotNull(calculated);
        Assert.Equal(10_650m, calculated.RoundedSalePrice);
        Assert.Equal(
            decimal.Round((10_650m - 8_500m) / 10_650m * 100m, 6, MidpointRounding.AwayFromZero),
            calculated.EffectiveMarginPercent);

        using (var review = await pricing.PutAsJsonAsync(
                   $"/api/commerce/v1/pricing/proposals/{proposal.ProposalId:D}",
                   new ReviewPriceProposalRequest(
                       PriceInputModes.Margin, 20m, null, 50m,
                       PricingRoundingModes.Up, proposal.ConcurrencyToken)))
            Assert.Equal(HttpStatusCode.NoContent, review.StatusCode);

        var approved = await pricing.GetFromJsonAsync<PriceRevisionPage>(
            "/api/commerce/v1/pricing/proposals?page=1&pageSize=20&status=Approved");
        var reviewed = Assert.Single(approved!.Items.Where(x => x.ProductId == productId));
        Assert.Equal(10_650m, reviewed.SuggestedSalePrice);

        var publishRequest = new PublishPricesRequest([
            new PublishPriceItem(
                reviewed.ProposalId, PriceInputModes.Margin, 20m, null,
                50m, PricingRoundingModes.Up, reviewed.ConcurrencyToken)
        ]);
        using var publishedResponse = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/publish", publishRequest);
        publishedResponse.EnsureSuccessStatusCode();
        var published = await publishedResponse.Content.ReadFromJsonAsync<PublishPricesResult>();
        var publication = Assert.Single(published!.Items);
        Assert.Equal(10_650m, publication.Amount);
        Assert.True(publication.CatalogCursor > 0);

        PosSynchronizationInvalidation signal;
        do
        {
            signal = await fixture.ReadSynchronizationMessageAsync();
        }
        while (signal.Stream != "Catalog");
        Assert.Equal(fixture.BusinessId, signal.BusinessId);
        Assert.True(signal.AvailableThroughCursor >= publication.CatalogCursor);

        Assert.Equal(10_650m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1",
            productId));
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPrices WHERE ProductId=@Product", productId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PricePublicationAudits WHERE ProductId=@Product", productId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages WHERE BusinessId=@Business AND Stream=N'Catalog' AND AvailableThroughCursor=@Cursor",
            productId, new SqlParameter("@Cursor", publication.CatalogCursor)));

        using var replay = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/publish", publishRequest);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPrices WHERE ProductId=@Product", productId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PricePublicationAudits WHERE ProductId=@Product", productId));

        var sqlitePath = Path.Combine(Path.GetTempPath(), $"auraly-pricing-{Guid.NewGuid():N}.db");
        try
        {
            var local = new PosCatalogStore($"Data Source={sqlitePath}");
            var sync = new PosCatalogSynchronizer(
                fixture.CreateClient(), local,
                new PosDeviceCredentials(fixture.DeviceId, ServerSliceFixture.DeviceSecret),
                new PosOperationalScope(fixture.BusinessId, fixture.WarehouseId));
            await sync.SynchronizeAsync();
            var captured = await local.CaptureAsync(barcode);
            Assert.NotNull(captured);
            Assert.Equal(10_650m, captured.Product.UnitPrice);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(sqlitePath)) File.Delete(sqlitePath);
        }
    }


    [Fact]
    public async Task Product_without_supplier_cost_is_prepared_then_published_from_the_single_pricing_view()
    {
        fixture.DrainSynchronizationMessages();
        var productId = Guid.NewGuid();
        var barcode = $"79{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";
        await SeedProductAsync(productId, barcode, 4_000m);

        using var pricing = fixture.CreateAdminClient(
            PricingPermissionCodes.Read,
            PricingPermissionCodes.ReadCostBasis,
            PricingPermissionCodes.PreparePrices,
            PricingPermissionCodes.PublishPrices);
        var context = await pricing.GetFromJsonAsync<ProductPricingContext>(
            $"/api/commerce/v1/pricing/products/{productId:D}/context");
        Assert.NotNull(context);
        Assert.Null(context!.CostBasisAmount);

        using var response = await pricing.PutAsJsonAsync(
            $"/api/commerce/v1/pricing/products/{productId:D}/prepared-price",
            new PublishProductPriceRequest(
                PriceInputModes.SalePrice, null, 5_500m, 1m, PricingRoundingModes.Nearest));
        response.EnsureSuccessStatusCode();
        var published = await response.Content.ReadFromJsonAsync<PreparedProductPrice>();
        Assert.NotNull(published);
        Assert.Equal(5_500m, published!.PreparedAmount);
        Assert.Null(published.CostBasisAmount);
        Assert.Null(published.EffectiveMarginPercent);
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PricePublicationAudits WHERE ProductId=@Product", productId));

        var candidates = await pricing.GetFromJsonAsync<PriceRevisionPage>(
            "/api/commerce/v1/pricing/proposals?page=1&pageSize=20&status=Pending");
        var candidate = Assert.Single(candidates!.Items.Where(x => x.ProductId == productId));
        Assert.Equal("Product", candidate.Origin);
        Assert.Equal(4_000m, candidate.CurrentSalePrice);
        Assert.Equal(5_500m, candidate.SuggestedSalePrice);

        using var publicationResponse = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/publish",
            new PublishPricesRequest([new PublishPriceItem(
                candidate.ProposalId, PriceInputModes.SalePrice, null, 5_500m,
                1m, PricingRoundingModes.Nearest, candidate.ConcurrencyToken)]));
        publicationResponse.EnsureSuccessStatusCode();
        var remaining = await pricing.GetFromJsonAsync<PriceRevisionPage>(
            "/api/commerce/v1/pricing/proposals?page=1&pageSize=20&status=Pending");
        Assert.DoesNotContain(remaining!.Items, item => item.ProductId == productId);
        Assert.Equal(5_500m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", productId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PricePublicationAudits WHERE ProductId=@Product AND PublicationOrigin=N'Manual' AND ProposalId IS NULL", productId));
    }
    [Fact]
    public async Task Product_manual_cost_calculates_margin_and_persists_the_published_price_version()
    {
        fixture.DrainSynchronizationMessages();
        var productId = Guid.NewGuid();
        var barcode = $"79{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";
        await SeedProductAsync(productId, barcode, 4_000m);

        using var pricing = fixture.CreateAdminClient(
            PricingPermissionCodes.Read,
            PricingPermissionCodes.ReadCostBasis,
            PricingPermissionCodes.PreparePrices);
        using var response = await pricing.PutAsJsonAsync(
            $"/api/commerce/v1/pricing/products/{productId:D}/prepared-price",
            new PublishProductPriceRequest(
                PriceInputModes.Margin, 25m, null, 1m, PricingRoundingModes.Nearest, 4_000m));
        response.EnsureSuccessStatusCode();
        var published = await response.Content.ReadFromJsonAsync<PreparedProductPrice>();

        Assert.NotNull(published);
        // The established gross-margin rule uses cost / (100 - margin) * 100.
        // It is intentionally not a 25% markup over cost.
        Assert.Equal(5_333m, published!.PreparedAmount);
        Assert.Equal(4_000m, published.CostBasisAmount);
        Assert.Equal(24.995312m, published.EffectiveMarginPercent);
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPrices WHERE ProductId=@Product AND CostBasisType=N'Manual' AND CostBasisAmount=4000 AND TargetMarginPercent=25 AND Amount=4000 AND PreparedAmount=5333",
            productId));
    }
    [Fact]
    public async Task Pricing_permissions_and_business_scope_are_enforced()
    {
        using var readOnly = fixture.CreateAdminClient(
            PricingPermissionCodes.Read, PricingPermissionCodes.ReadCostBasis);
        using var denied = await readOnly.PostAsJsonAsync(
            "/api/commerce/v1/pricing/publish", new PublishPricesRequest([]));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var withoutCosts = fixture.CreateAdminClient(PricingPermissionCodes.Read);
        using var hiddenCosts = await withoutCosts.GetAsync(
            "/api/commerce/v1/pricing/proposals?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.Forbidden, hiddenCosts.StatusCode);
    }

    private async Task SeedProductAsync(
        Guid productId,
        string barcode,
        decimal price,
        decimal? targetMarginPercent = null)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @TaxProfileId UNIQUEIDENTIFIER=NEWID();
            INSERT dbo.TaxProfiles
              (TaxProfileId,BusinessId,Code,Name,Rate,IsActive,CreatedAt)
            VALUES
              (@TaxProfileId,@BusinessId,@TaxCode,N'Sin impuesto',0,1,SYSDATETIMEOFFSET());
            INSERT dbo.Products
              (ProductId,BusinessId,ProductCode,Reference,Sku,Name,Description,
               BaseUnitCode,TaxProfileId,ManageStock,IsWeighable,IsActive,Source,
               Currency,CreatedAt)
            VALUES
              (@ProductId,@BusinessId,@ProductCode,@ProductCode,@ProductCode,
               N'Producto pricing',N'Producto de prueba de publicacion',N'EA',
               @TaxProfileId,1,0,1,0,N'COP',SYSDATETIMEOFFSET());
            INSERT dbo.ProductBarcodes
              (ProductBarcodeId,BusinessId,ProductId,Barcode,IsPrimary,IsActive,CreatedAt)
            VALUES
              (NEWID(),@BusinessId,@ProductId,@Barcode,1,1,SYSDATETIMEOFFSET());
            INSERT dbo.ProductPrices
              (ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,ValidFrom,
               TargetMarginPercent,RoundingIncrement,RoundingMode,IsActive,CreatedAt)
            VALUES
              (NEWID(),@BusinessId,@ProductId,@Price,N'COP',SYSDATETIMEOFFSET(),
               @TargetMarginPercent,1,N'Nearest',1,SYSDATETIMEOFFSET());
            INSERT dbo.SupplierProducts
              (SupplierProductId,BusinessId,ProductId,SupplierId,SupplierProductCode,IsPrimary,IsActive,CreatedAt)
            VALUES
              (NEWID(),@BusinessId,@ProductId,@SupplierId,@ProductCode,1,1,SYSDATETIMEOFFSET());
            IF NOT EXISTS (SELECT 1 FROM dbo.PosDevicePermissions
                           WHERE DeviceId=@DeviceId AND PermissionCode=@CatalogSyncPermission)
              INSERT dbo.PosDevicePermissions(DeviceId,PermissionCode,IsGranted,GrantedAt)
              VALUES(@DeviceId,@CatalogSyncPermission,1,SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@SupplierId", fixture.SupplierId);
        command.Parameters.AddWithValue("@DeviceId", fixture.DeviceId);
        command.Parameters.AddWithValue("@CatalogSyncPermission", Auraly.Contracts.Catalog.CatalogPermissionCodes.Sync);
        command.Parameters.AddWithValue("@ProductCode", $"PR-{productId:N}");
        command.Parameters.AddWithValue("@TaxCode", $"T-{productId:N}"[..32]);
        command.Parameters.AddWithValue("@Barcode", barcode);
        command.Parameters.AddWithValue("@Price", price);
        command.Parameters.AddWithValue(
            "@TargetMarginPercent",
            targetMarginPercent is { } margin ? margin : DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(
        string sql, Guid productId, params SqlParameter[] extra)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Product", productId);
        command.Parameters.AddWithValue("@Business", fixture.BusinessId);
        command.Parameters.AddRange(extra);
        var value = await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Expected SQL scalar was not returned.");
        return (T)Convert.ChangeType(value, typeof(T));
    }
}
