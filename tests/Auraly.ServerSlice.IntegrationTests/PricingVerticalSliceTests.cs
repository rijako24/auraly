using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Pricing;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Contracts.Purchasing;
using Auraly.Infrastructure.Persistence;
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
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPrices WHERE ProductId=@Product", productId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND PreparationOrigin=N'GoodsReceipt' AND CostBasisAmount=8500 AND PreparedAmount=10625 AND Status=N'Pending'",
            productId));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PricePublicationAudits WHERE ProductId=@Product", productId));

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
        Assert.Equal(8_500m, proposal.LatestUnitCost);
        Assert.Equal(8_500m, proposal.LatestLandedUnitCost);
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
        {
            Assert.Equal(HttpStatusCode.OK, review.StatusCode);
            var saved = await review.Content.ReadFromJsonAsync<ReviewedPricePreparation>();
            Assert.NotNull(saved);
            Assert.Equal(productId, saved!.ProductId);
            Assert.Equal(10_650m, saved.PreparedAmount);
            Assert.False(string.IsNullOrWhiteSpace(saved.ConcurrencyToken));
        }

        var approved = await pricing.GetFromJsonAsync<PriceRevisionPage>(
            "/api/commerce/v1/pricing/proposals?page=1&pageSize=20&status=Approved");
        var reviewed = Assert.Single(approved!.Items.Where(x => x.ProductId == productId));
        Assert.Equal(10_650m, reviewed.SuggestedSalePrice);

        var publishRequest = new PublishPricesRequest([reviewed.ProductId]);
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
        while (signal.Stream != "Catalog" || signal.BusinessId != fixture.BusinessId);
        Assert.Equal(fixture.BusinessId, signal.BusinessId);
        Assert.True(signal.AvailableThroughCursor >= publication.CatalogCursor);

        Assert.Equal(10_650m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1",
            productId));
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPrices WHERE ProductId=@Product", productId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PricePublicationAudits WHERE ProductId=@Product", productId));
        var history = await pricing.GetFromJsonAsync<ProductPriceHistoryPage>(
            $"/api/commerce/v1/pricing/products/{productId:D}/history");
        Assert.NotNull(history);
        Assert.Equal(5, history!.PageSize);
        Assert.Contains(history.Items, item => item.ActivityType == "Preparation"
            && item.Origin == "GoodsReceipt" && item.Status == "Superseded");
        Assert.Contains(history.Items, item => item.ActivityType == "Preparation"
            && item.Origin == "ProposalReview" && item.Status == "Published");
        Assert.Contains(history.Items, item => item.ActivityType == "Publication"
            && item.PublicAmount == 10_000m && item.PreparedAmount == 10_650m);
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
            new PublishPricesRequest([candidate.ProductId]));
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
            "SELECT COUNT(*) FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND PreparationOrigin=N'Product' AND CostBasisAmount=4000 AND TargetMarginPercent=25 AND PublicAmountSnapshot=4000 AND PreparedAmount=5333 AND Status=N'Pending'",
            productId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPrices WHERE ProductId=@Product AND Amount=4000 AND PreparedAmount IN(0,4000) AND CostBasisAmount IS NULL",
            productId));
    }

    [Fact]
    public async Task Publishing_new_cost_refreshes_only_automatic_tier_price()
    {
        await using var tenantAccess = await GrantTenantBusinessAccessAsync();
        var productId = Guid.NewGuid();
        var barcode = $"79{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";
        await SeedProductAsync(productId, barcode, 4_000m);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var tax = new SqlCommand("""
                UPDATE profile SET Rate=19
                FROM dbo.TaxProfiles profile
                JOIN dbo.Products product ON product.TaxProfileId=profile.TaxProfileId
                WHERE product.ProductId=@ProductId;
                """, connection);
            tax.Parameters.AddWithValue("@ProductId", productId);
            Assert.Equal(1, await tax.ExecuteNonQueryAsync());
        }
        await fixture.EnsureAdminAccessToTenantBusinessesAsync();
        using var pricing = fixture.CreateAdminClient(
            PricingPermissionCodes.Read, PricingPermissionCodes.ReadCostBasis,
            PricingPermissionCodes.PreparePrices, PricingPermissionCodes.PublishPrices,
            PricingPermissionCodes.BulkPublish,
            "pricing.segments.read", "pricing.segments.manage");

        using (var prepared = await pricing.PutAsJsonAsync(
            $"/api/commerce/v1/pricing/products/{productId:D}/prepared-price",
            new PublishProductPriceRequest(PriceInputModes.Margin, 20m,
                null, 1m, PricingRoundingModes.Nearest, 4_000m)))
            prepared.EnsureSuccessStatusCode();
        using (var published = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/publish",
            new PublishPricesRequest([productId])))
            published.EnsureSuccessStatusCode();

        using var created = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/segments",
            new Auraly.Api.SavePriceSegmentRequest(
                $"Auto {Guid.NewGuid():N}", "TieredProductPrice", null,
                [new Auraly.Api.CreatePriceSegmentItemRequest(
                    productId, 0m, 1m, 50m, true, PriceInputModes.Margin),
                 new Auraly.Api.CreatePriceSegmentItemRequest(
                    productId, 10_000m, 3m, null, false,
                    PriceInputModes.SalePrice)]));
        created.EnsureSuccessStatusCode();
        var channel = (await created.Content.ReadFromJsonAsync<
            Auraly.Api.PriceSegmentSummary>())!;
        Assert.Equal(9_520m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.PriceChannelItems WHERE PriceChannelId=@ChannelId AND ProductId=@Product AND MinimumQuantity=1 AND IsActive=1",
            productId, new SqlParameter("@ChannelId", channel.Id)));

        using (var prepared = await pricing.PutAsJsonAsync(
            $"/api/commerce/v1/pricing/products/{productId:D}/prepared-price",
            new PublishProductPriceRequest(PriceInputModes.Margin, 20m,
                null, 1m, PricingRoundingModes.Nearest, 6_000m)))
            prepared.EnsureSuccessStatusCode();
        using (var published = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/publish",
            new PublishPricesRequest([productId])))
            published.EnsureSuccessStatusCode();
        Assert.Equal(14_280m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.PriceChannelItems WHERE PriceChannelId=@ChannelId AND ProductId=@Product AND MinimumQuantity=1 AND IsActive=1",
            productId, new SqlParameter("@ChannelId", channel.Id)));
        Assert.Equal(10_000m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.PriceChannelItems WHERE PriceChannelId=@ChannelId AND ProductId=@Product AND MinimumQuantity=3 AND IsActive=1",
            productId, new SqlParameter("@ChannelId", channel.Id)));
    }

    [Fact]
    public async Task Publishing_twenty_products_updates_all_automatic_channel_prices_in_one_batch()
    {
        await using var tenantAccess = await GrantTenantBusinessAccessAsync();
        var products = new List<Guid>();
        for (var index = 0; index < 20; index++)
        {
            var productId = Guid.NewGuid();
            products.Add(productId);
            await SeedProductAsync(productId,
                $"79{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}",
                4_000m);
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var update = new SqlCommand("""
                UPDATE dbo.ProductPrices SET CostBasisAmount=@Cost,
                  EffectiveMarginPercent=20
                WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND IsActive=1;
                """, connection);
            update.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            update.Parameters.AddWithValue("@ProductId", productId);
            update.Parameters.AddWithValue("@Cost", 1_000m * (index + 1));
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        await fixture.EnsureAdminAccessToTenantBusinessesAsync();
        using var pricing = fixture.CreateAdminClient(
            PricingPermissionCodes.Read, PricingPermissionCodes.ReadCostBasis,
            PricingPermissionCodes.PreparePrices, PricingPermissionCodes.PublishPrices,
            PricingPermissionCodes.BulkPublish,
            "pricing.segments.read", "pricing.segments.manage");
        using var created = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/segments",
            new Auraly.Api.SavePriceSegmentRequest(
                $"Bulk {Guid.NewGuid():N}", "TieredProductPrice", null,
                products.Select((productId, index) =>
                    new Auraly.Api.CreatePriceSegmentItemRequest(productId, 0m, 1m,
                        50m, true, PriceInputModes.Margin)).ToArray()));
        created.EnsureSuccessStatusCode();
        var channel = (await created.Content.ReadFromJsonAsync<
            Auraly.Api.PriceSegmentSummary>())!;

        foreach (var (productId, index) in products.Select((id, at) => (id, at)))
        {
            using var prepared = await pricing.PutAsJsonAsync(
                $"/api/commerce/v1/pricing/products/{productId:D}/prepared-price",
                new PublishProductPriceRequest(PriceInputModes.Margin, 20m,
                    null, 1m, PricingRoundingModes.Nearest, 2_000m * (index + 1)));
            prepared.EnsureSuccessStatusCode();
        }

        using var published = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/publish", new PublishPricesRequest(products));
        published.EnsureSuccessStatusCode();
        var result = (await published.Content.ReadFromJsonAsync<PublishPricesResult>())!;
        Assert.Equal(20, result.Items.Count);
        foreach (var (productId, index) in products.Select((id, at) => (id, at)))
        {
            Assert.Equal(2_500m * (index + 1), await ScalarAsync<decimal>(
                "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND BusinessId=@Business AND IsActive=1",
                productId));
            Assert.Equal(4_000m * (index + 1), await ScalarAsync<decimal>(
                "SELECT Amount FROM dbo.PriceChannelItems WHERE PriceChannelId=@ChannelId AND ProductId=@Product AND IsActive=1",
                productId, new SqlParameter("@ChannelId", channel.Id)));
        }
    }

    [Fact]
    public async Task A_channel_recalculation_failure_rolls_back_the_whole_price_batch()
    {
        await using var tenantAccess = await GrantTenantBusinessAccessAsync();
        var products = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var productId in products)
        {
            await SeedProductAsync(productId,
                $"79{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}",
                4_000m);
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var update = new SqlCommand("""
                UPDATE dbo.ProductPrices SET CostBasisAmount=1000,
                  EffectiveMarginPercent=20
                WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND IsActive=1;
                """, connection);
            update.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            update.Parameters.AddWithValue("@ProductId", productId);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }
        await fixture.EnsureAdminAccessToTenantBusinessesAsync();
        using var pricing = fixture.CreateAdminClient(
            PricingPermissionCodes.Read, PricingPermissionCodes.ReadCostBasis,
            PricingPermissionCodes.PreparePrices, PricingPermissionCodes.PublishPrices,
            PricingPermissionCodes.BulkPublish,
            "pricing.segments.read", "pricing.segments.manage");
        using var created = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/segments",
            new Auraly.Api.SavePriceSegmentRequest(
                $"Rollback {Guid.NewGuid():N}", "TieredProductPrice", null,
                products.Select(productId =>
                    new Auraly.Api.CreatePriceSegmentItemRequest(productId, 0m, 1m,
                        50m, true, PriceInputModes.Margin)).ToArray()));
        created.EnsureSuccessStatusCode();
        var channel = (await created.Content.ReadFromJsonAsync<
            Auraly.Api.PriceSegmentSummary>())!;

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var update = new SqlCommand("""
                UPDATE dbo.PriceChannelItems SET TargetMarginPercent=99.999999
                WHERE PriceChannelId=@ChannelId AND ProductId=@ProductId AND IsActive=1;
                """, connection);
            update.Parameters.AddWithValue("@ChannelId", channel.Id);
            update.Parameters.AddWithValue("@ProductId", products[1]);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        foreach (var (productId, cost) in new[]
                 { (products[0], 2_000m), (products[1], 1_000_000_000m) })
        {
            using var prepared = await pricing.PutAsJsonAsync(
                $"/api/commerce/v1/pricing/products/{productId:D}/prepared-price",
                new PublishProductPriceRequest(PriceInputModes.Margin, 20m,
                    null, 1m, PricingRoundingModes.Nearest, cost));
            prepared.EnsureSuccessStatusCode();
        }
        using var published = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/publish", new PublishPricesRequest(products));
        Assert.Equal(HttpStatusCode.Conflict, published.StatusCode);
        foreach (var productId in products)
        {
            Assert.Equal(4_000m, await ScalarAsync<decimal>(
                "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND BusinessId=@Business AND IsActive=1",
                productId));
            Assert.Equal(0, await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.PricePublicationAudits WHERE ProductId=@Product AND BusinessId=@Business",
                productId));
            Assert.Equal(2_000m, await ScalarAsync<decimal>(
                "SELECT Amount FROM dbo.PriceChannelItems WHERE PriceChannelId=@ChannelId AND ProductId=@Product AND IsActive=1",
                productId, new SqlParameter("@ChannelId", channel.Id)));
        }
    }

    [Fact]
    public async Task Goods_receipt_preparation_never_changes_the_price_downloaded_by_a_box_until_publication()
    {
        fixture.DrainSynchronizationMessages();
        var productId = Guid.NewGuid();
        var barcode = $"79{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";
        await SeedProductAsync(productId, barcode, 10_000m, 20m);
        var sqlitePath = Path.Combine(
            Path.GetTempPath(), $"auraly-receipt-price-{Guid.NewGuid():N}.db");

        try
        {
            var local = new PosCatalogStore($"Data Source={sqlitePath}");
            var sync = new PosCatalogSynchronizer(
                fixture.CreateClient(), local,
                new PosDeviceCredentials(fixture.DeviceId, ServerSliceFixture.DeviceSecret),
                new PosOperationalScope(fixture.BusinessId, fixture.WarehouseId));
            await sync.SynchronizeAsync();
            Assert.Equal(10_000m, (await local.CaptureAsync(barcode))!.Product.UnitPrice);

            await ConfirmReceiptAsync(productId, 8_500m);
            await sync.SynchronizeAsync();
            Assert.Equal(10_000m, (await local.CaptureAsync(barcode))!.Product.UnitPrice);
            Assert.Equal(1, await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.ProductPrices WHERE ProductId=@Product AND Amount=10000 AND IsActive=1",
                productId));
            Assert.Equal(1, await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND PreparedAmount=10625 AND Status=N'Pending'",
                productId));

            using var pricing = fixture.CreateAdminClient(
                PricingPermissionCodes.Read, PricingPermissionCodes.ReadCostBasis,
                PricingPermissionCodes.ReviewProposals, PricingPermissionCodes.PublishPrices);
            var pending = await pricing.GetFromJsonAsync<PriceRevisionPage>(
                "/api/commerce/v1/pricing/proposals?page=1&pageSize=100&status=PendingReview");
            var proposal = Assert.Single(pending!.Items.Where(item => item.ProductId == productId));
            using (var review = await pricing.PutAsJsonAsync(
                       $"/api/commerce/v1/pricing/proposals/{proposal.ProposalId:D}",
                       new ReviewPriceProposalRequest(
                           PriceInputModes.Margin, 20m, null, 1m,
                           PricingRoundingModes.Nearest, proposal.ConcurrencyToken)))
                review.EnsureSuccessStatusCode();
            var approved = await pricing.GetFromJsonAsync<PriceRevisionPage>(
                "/api/commerce/v1/pricing/proposals?page=1&pageSize=100&status=Approved");
            var reviewed = Assert.Single(approved!.Items.Where(item => item.ProductId == productId));
            using (var publication = await pricing.PostAsJsonAsync(
                       "/api/commerce/v1/pricing/publish",
                       new PublishPricesRequest([reviewed.ProductId])))
                publication.EnsureSuccessStatusCode();

            await sync.SynchronizeAsync();
            Assert.Equal(10_625m, (await local.CaptureAsync(barcode))!.Product.UnitPrice);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(sqlitePath)) File.Delete(sqlitePath);
        }
    }

    [Fact]
    public async Task Receipt_price_publication_updates_channel_tier_and_enrolled_box_together()
    {
        await using var tenantAccess = await GrantTenantBusinessAccessAsync();
        var productId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var barcode = $"79{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";
        await SeedProductAsync(productId, barcode, 10_000m, 20m);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = new SqlCommand("""
                UPDATE dbo.ProductPrices SET CostBasisAmount=8000,EffectiveMarginPercent=20
                WHERE ProductId=@ProductId AND BusinessId=@BusinessId AND IsActive=1;
                INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,
                    CompletionStatus,IsActive,CreatedBy,CreatedAt)
                VALUES(@PartyId,@TenantId,N'NaturalPerson',N'Cliente prueba canal',
                    N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());
                INSERT dbo.Customers(CustomerId,PartyId,TenantId,IsActive,CreatedBy,CreatedAt)
                VALUES(@CustomerId,@PartyId,@TenantId,1,@UserId,SYSDATETIMEOFFSET());
                """, connection);
            seed.Parameters.AddWithValue("@ProductId", productId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@PartyId", partyId);
            seed.Parameters.AddWithValue("@CustomerId", customerId);
            seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            seed.Parameters.AddWithValue("@UserId", fixture.UserId);
            await seed.ExecuteNonQueryAsync();
        }

        await fixture.EnsureAdminAccessToTenantBusinessesAsync();
        using var pricing = fixture.CreateAdminClient(
            PricingPermissionCodes.Read, PricingPermissionCodes.ReadCostBasis,
            PricingPermissionCodes.ReviewProposals, PricingPermissionCodes.PublishPrices,
            "pricing.segments.read", "pricing.segments.manage");
        using var created = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/segments",
            new Auraly.Api.SavePriceSegmentRequest(
                $"Receipt box {Guid.NewGuid():N}", "TieredProductPrice", null,
                [new Auraly.Api.CreatePriceSegmentItemRequest(
                    productId, 0m, 1m, 50m, true, PriceInputModes.Margin),
                 new Auraly.Api.CreatePriceSegmentItemRequest(
                    productId, 12_000m, 3m, null, false, PriceInputModes.SalePrice)]));
        created.EnsureSuccessStatusCode();
        var channel = (await created.Content.ReadFromJsonAsync<Auraly.Api.PriceSegmentSummary>())!;
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var assign = new SqlCommand("""
                INSERT dbo.CustomerPricingSettings(CustomerId,PriceChannelId,UpdatedBy,UpdatedAt)
                VALUES(@CustomerId,@ChannelId,@UserId,SYSDATETIMEOFFSET());
                """, connection);
            assign.Parameters.AddWithValue("@CustomerId", customerId);
            assign.Parameters.AddWithValue("@ChannelId", channel.Id);
            assign.Parameters.AddWithValue("@UserId", fixture.UserId);
            await assign.ExecuteNonQueryAsync();
        }

        var sqlitePath = Path.Combine(Path.GetTempPath(),
            $"auraly-receipt-channel-box-{Guid.NewGuid():N}.db");
        try
        {
            var local = new PosCatalogStore($"Data Source={sqlitePath}");
            using var server = fixture.CreateClient();
            var sync = new PosCatalogSynchronizer(server, local,
                new PosDeviceCredentials(fixture.DeviceId, ServerSliceFixture.DeviceSecret),
                new PosOperationalScope(fixture.BusinessId, fixture.WarehouseId));
            await sync.SynchronizeAsync();
            Assert.Equal(10_000m, (await local.CaptureAsync(barcode))!.Product.UnitPrice);
            Assert.Equal(16_000m, (await local.ResolvePriceAsync(productId, customerId, 1m)).Amount);
            Assert.Equal(12_000m, (await local.ResolvePriceAsync(productId, customerId, 3m)).Amount);

            await ConfirmReceiptAsync(productId, 8_500m);
            await sync.SynchronizeAsync();
            Assert.Equal(10_000m, (await local.CaptureAsync(barcode))!.Product.UnitPrice);
            Assert.Equal(16_000m, (await local.ResolvePriceAsync(productId, customerId, 1m)).Amount);
            Assert.Equal(16_000m, await ScalarAsync<decimal>(
                "SELECT Amount FROM dbo.PriceChannelItems WHERE PriceChannelId=@ChannelId AND ProductId=@Product AND MinimumQuantity=1 AND IsActive=1",
                productId, new SqlParameter("@ChannelId", channel.Id)));
            Assert.Equal(10_625m, await ScalarAsync<decimal>(
                "SELECT PreparedAmount FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND Status=N'Pending' AND PreparationOrigin=N'GoodsReceipt'",
                productId));

            var pending = await pricing.GetFromJsonAsync<PriceRevisionPage>(
                "/api/commerce/v1/pricing/proposals?page=1&pageSize=100&status=PendingReview");
            var proposal = Assert.Single(pending!.Items.Where(item => item.ProductId == productId));
            using (var review = await pricing.PutAsJsonAsync(
                       $"/api/commerce/v1/pricing/proposals/{proposal.ProposalId:D}",
                       new ReviewPriceProposalRequest(
                           PriceInputModes.Margin, 20m, null, 1m,
                           PricingRoundingModes.Nearest, proposal.ConcurrencyToken)))
                review.EnsureSuccessStatusCode();
            using (var publication = await pricing.PostAsJsonAsync(
                       "/api/commerce/v1/pricing/publish",
                       new PublishPricesRequest([productId])))
                publication.EnsureSuccessStatusCode();

            Assert.Equal(10_625m, await ScalarAsync<decimal>(
                "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND BusinessId=@Business AND IsActive=1",
                productId));
            Assert.Equal(17_000m, await ScalarAsync<decimal>(
                "SELECT Amount FROM dbo.PriceChannelItems WHERE PriceChannelId=@ChannelId AND ProductId=@Product AND MinimumQuantity=1 AND IsActive=1",
                productId, new SqlParameter("@ChannelId", channel.Id)));
            Assert.Equal(12_000m, await ScalarAsync<decimal>(
                "SELECT Amount FROM dbo.PriceChannelItems WHERE PriceChannelId=@ChannelId AND ProductId=@Product AND MinimumQuantity=3 AND IsActive=1",
                productId, new SqlParameter("@ChannelId", channel.Id)));

            // The enrolled box still has its previous snapshot until it synchronizes.
            Assert.Equal(10_000m, (await local.CaptureAsync(barcode))!.Product.UnitPrice);
            Assert.Equal(16_000m, (await local.ResolvePriceAsync(productId, customerId, 1m)).Amount);
            await sync.SynchronizeAsync();
            await sync.SynchronizeConfigurationAsync();
            Assert.Equal(10_625m, (await local.CaptureAsync(barcode))!.Product.UnitPrice);
            Assert.Equal(17_000m, (await local.ResolvePriceAsync(productId, customerId, 1m)).Amount);
            Assert.Equal(12_000m, (await local.ResolvePriceAsync(productId, customerId, 3m)).Amount);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var path in new[] { sqlitePath, sqlitePath + "-wal", sqlitePath + "-shm" })
                if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Last_preparation_wins_in_both_receipt_and_product_directions_and_kardex_preserves_the_replaced_values()
    {
        fixture.DrainSynchronizationMessages();
        var productId = Guid.NewGuid();
        await SeedProductAsync(productId,
            $"79{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}", 10_000m, 20m);

        await ConfirmReceiptAsync(productId, 8_000m);
        Assert.Equal(10_000m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", productId));
        Assert.Equal(10_000m, await ScalarAsync<decimal>(
            "SELECT PreparedAmount FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND Status=N'Pending'", productId));

        using var pricing = fixture.CreateAdminClient(
            PricingPermissionCodes.Read, PricingPermissionCodes.ReadCostBasis,
            PricingPermissionCodes.PreparePrices, PricingPermissionCodes.PublishPrices,
            PricingPermissionCodes.ReadHistory);
        using (var productPreparation = await pricing.PutAsJsonAsync(
                   $"/api/commerce/v1/pricing/products/{productId:D}/prepared-price",
                   new PublishProductPriceRequest(
                       PriceInputModes.Margin, 20m, null, 1m,
                       PricingRoundingModes.Nearest, 7_000m)))
            productPreparation.EnsureSuccessStatusCode();

        Assert.Equal(8_750m, await ScalarAsync<decimal>(
            "SELECT PreparedAmount FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND Status=N'Pending' AND PreparationOrigin=N'Product'", productId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND PreparationOrigin=N'GoodsReceipt' AND Status=N'Superseded'", productId));

        await ConfirmReceiptAsync(productId, 9_000m);
        Assert.Equal(11_250m, await ScalarAsync<decimal>(
            "SELECT PreparedAmount FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND Status=N'Pending' AND PreparationOrigin=N'GoodsReceipt'", productId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPricePreparations WHERE ProductId=@Product AND PreparationOrigin=N'Product' AND Status=N'Superseded'", productId));
        Assert.Equal(10_000m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", productId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductPrices WHERE ProductId=@Product", productId));

        var pending = await pricing.GetFromJsonAsync<PriceRevisionPage>(
            "/api/commerce/v1/pricing/proposals?page=1&pageSize=100&status=Pending");
        var latest = Assert.Single(pending!.Items.Where(item => item.ProductId == productId));
        using var publicationResponse = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/publish",
            new PublishPricesRequest([latest.ProductId]));
        publicationResponse.EnsureSuccessStatusCode();

        Assert.Equal(11_250m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Product AND IsActive=1", productId));
        var history = await pricing.GetFromJsonAsync<ProductPriceHistoryPage>(
            $"/api/commerce/v1/pricing/products/{productId:D}/history?page=1&pageSize=5");
        Assert.NotNull(history);
        Assert.True(history!.TotalCount >= history.Items.Count);
        Assert.Contains(history.Items, item => item.ActivityType == "Preparation"
            && item.Origin == "GoodsReceipt" && item.PreparedAmount == 10_000m
            && item.Status == "Superseded");
        Assert.Contains(history.Items, item => item.ActivityType == "Preparation"
            && item.Origin == "Product" && item.PreparedAmount == 8_750m
            && item.Status == "Superseded");
        Assert.Contains(history.Items, item => item.ActivityType == "Preparation"
            && item.Origin == "GoodsReceipt" && item.PreparedAmount == 11_250m
            && item.Status == "Published");
        Assert.Contains(history.Items, item => item.ActivityType == "Publication"
            && item.PreparedAmount == 11_250m && item.Status == "Published");
    }

    [Fact]
    public async Task Published_price_reaches_only_its_site_when_isolated_and_all_site_boxes_when_prices_are_shared()
    {
        fixture.DrainSynchronizationMessages();
        var productId = Guid.NewGuid();
        var barcode = $"79{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";
        await SeedProductAsync(productId, barcode, 10_000m, 20m);
        var otherBusinessId = Guid.NewGuid();
        var otherWarehouseId = Guid.NewGuid();
        var otherDeviceId = Guid.NewGuid();
        var otherDeviceSecret = $"Auraly-price-box-{Guid.NewGuid():N}";
        var otherCredential = PosDeviceCredentialHasher.Create(otherDeviceSecret);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE dbo.Businesses SET SharesProductPrices=0 WHERE BusinessId=@BusinessId;
                INSERT dbo.Businesses
                  (BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,
                   TimeZone,SharesProductPrices,IsActive,CreatedAt)
                VALUES(@OtherBusinessId,@TenantId,N'Sede de prueba de precios',N'',N'',N'',N'',N'',
                       N'America/Bogota',0,1,SYSDATETIMEOFFSET());
                INSERT dbo.Warehouses
                  (WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,IsSystem,
                   UseForSales,UseForGoodsReceipts,IsInventoryVisible,PriceFormationCostBasis,
                   IsActive,CreatedAt)
                VALUES(@OtherWarehouseId,@OtherBusinessId,N'VEN',N'Bodega de venta',0,1,1,1,1,
                       N'LatestReceiptCost',1,SYSDATETIMEOFFSET());
                INSERT dbo.ProductPrices
                  (ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,
                   CostBasisType,CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,
                   InputMode,RoundingIncrement,RoundingMode,ValidFrom,IsActive,CreatedAt)
                VALUES(NEWID(),@OtherBusinessId,@ProductId,10000,10000,N'COP',N'Manual',8000,
                       20,20,N'Margin',1,N'Nearest',SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET());
                INSERT dbo.EnrolledDevices
                  (DeviceId,TenantId,Name,CredentialSalt,CredentialHash,
                   CredentialIterations,IsActive,CreatedAt)
                VALUES(@OtherDeviceId,@TenantId,N'Caja sede B',@CredentialSalt,@CredentialHash,
                       @CredentialIterations,1,SYSDATETIMEOFFSET());
                """;
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@OtherBusinessId", otherBusinessId);
            command.Parameters.AddWithValue("@OtherWarehouseId", otherWarehouseId);
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            command.Parameters.AddWithValue("@ProductId", productId);
            command.Parameters.AddWithValue("@OtherDeviceId", otherDeviceId);
            command.Parameters.AddWithValue("@CredentialSalt", otherCredential.Salt);
            command.Parameters.AddWithValue("@CredentialHash", otherCredential.Hash);
            command.Parameters.AddWithValue("@CredentialIterations", otherCredential.Iterations);
            await command.ExecuteNonQueryAsync();
        }

        var firstPath = Path.Combine(Path.GetTempPath(), $"auraly-price-site-a-{Guid.NewGuid():N}.db");
        var secondPath = Path.Combine(Path.GetTempPath(), $"auraly-price-site-b-{Guid.NewGuid():N}.db");
        try
        {
            var firstBox = new PosCatalogStore($"Data Source={firstPath}");
            var secondBox = new PosCatalogStore($"Data Source={secondPath}");
            var firstSync = new PosCatalogSynchronizer(
                fixture.CreateClient(), firstBox,
                new PosDeviceCredentials(fixture.DeviceId, ServerSliceFixture.DeviceSecret),
                new PosOperationalScope(fixture.BusinessId, fixture.WarehouseId));
            var secondSync = new PosCatalogSynchronizer(
                fixture.CreateClient(), secondBox,
                new PosDeviceCredentials(otherDeviceId, otherDeviceSecret),
                new PosOperationalScope(otherBusinessId, otherWarehouseId));
            await firstSync.SynchronizeAsync();
            await secondSync.SynchronizeAsync();
            Assert.Equal(10_000m, (await firstBox.CaptureAsync(barcode))!.Product.UnitPrice);
            Assert.Equal(10_000m, (await secondBox.CaptureAsync(barcode))!.Product.UnitPrice);
            fixture.DrainSynchronizationMessages();

            using var pricing = fixture.CreateAdminClient(
                PricingPermissionCodes.Read, PricingPermissionCodes.ReadCostBasis,
                PricingPermissionCodes.PreparePrices, PricingPermissionCodes.PublishPrices);
            await PrepareAndPublishAsync(pricing, productId, 12_000m);
            var isolatedSignals = new List<PosSynchronizationInvalidation>
            {
                await fixture.ReadSynchronizationMessageAsync()
            };
            isolatedSignals.AddRange(fixture.DrainSynchronizationMessages());
            Assert.Contains(isolatedSignals, signal =>
                signal.Stream == "Catalog" && signal.BusinessId == fixture.BusinessId);
            Assert.DoesNotContain(isolatedSignals, signal =>
                signal.Stream == "Catalog" && signal.BusinessId == otherBusinessId);
            await firstSync.SynchronizeAsync();
            await secondSync.SynchronizeAsync();
            Assert.Equal(12_000m, (await firstBox.CaptureAsync(barcode))!.Product.UnitPrice);
            Assert.Equal(10_000m, (await secondBox.CaptureAsync(barcode))!.Product.UnitPrice);

            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var share = connection.CreateCommand();
                share.CommandText = "UPDATE dbo.Businesses SET SharesProductPrices=1 WHERE BusinessId IN(@BusinessId,@OtherBusinessId);";
                share.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                share.Parameters.AddWithValue("@OtherBusinessId", otherBusinessId);
                await share.ExecuteNonQueryAsync();
            }

            fixture.DrainSynchronizationMessages();
            await PrepareAndPublishAsync(pricing, productId, 14_000m);
            var sharedSignals = new List<PosSynchronizationInvalidation>();
            while (sharedSignals
                       .Where(signal => signal.Stream == "Catalog")
                       .Select(signal => signal.BusinessId)
                       .Distinct()
                       .Count() < 2)
                sharedSignals.Add(await fixture.ReadSynchronizationMessageAsync());
            Assert.Contains(sharedSignals, signal =>
                signal.Stream == "Catalog" && signal.BusinessId == fixture.BusinessId);
            Assert.Contains(sharedSignals, signal =>
                signal.Stream == "Catalog" && signal.BusinessId == otherBusinessId);
            await firstSync.SynchronizeAsync();
            await secondSync.SynchronizeAsync();
            Assert.Equal(14_000m, (await firstBox.CaptureAsync(barcode))!.Product.UnitPrice);
            Assert.Equal(14_000m, (await secondBox.CaptureAsync(barcode))!.Product.UnitPrice);
            Assert.Equal(1, await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.PricePublicationAudits WHERE ProductId=@Product AND BusinessId=@Business AND PublishedSalePrice=14000",
                productId));
            await using var verification = new SqlConnection(fixture.ConnectionString);
            await verification.OpenAsync();
            await using var verifyCommand = verification.CreateCommand();
            verifyCommand.CommandText = """
                SELECT COUNT(*) FROM dbo.PricePublicationAudits
                WHERE ProductId=@ProductId AND BusinessId=@OtherBusinessId AND PublishedSalePrice=14000;
                """;
            verifyCommand.Parameters.AddWithValue("@ProductId", productId);
            verifyCommand.Parameters.AddWithValue("@OtherBusinessId", otherBusinessId);
            Assert.Equal(1, Convert.ToInt32(await verifyCommand.ExecuteScalarAsync()));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(firstPath)) File.Delete(firstPath);
            if (File.Exists(secondPath)) File.Delete(secondPath);
            await using var cleanup = new SqlConnection(fixture.ConnectionString);
            await cleanup.OpenAsync();
            await using var command = cleanup.CreateCommand();
            command.CommandText = """
                UPDATE dbo.Businesses SET SharesProductPrices=0 WHERE BusinessId=@BusinessId;
                UPDATE dbo.Businesses SET SharesProductPrices=0,IsActive=0 WHERE BusinessId=@OtherBusinessId;
                UPDATE dbo.Warehouses SET IsActive=0 WHERE BusinessId=@OtherBusinessId;
                DELETE dbo.CatalogSyncSessions WHERE DeviceId=@OtherDeviceId;
                DELETE dbo.EnrolledDevices WHERE DeviceId=@OtherDeviceId;
                """;
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@OtherBusinessId", otherBusinessId);
            command.Parameters.AddWithValue("@OtherDeviceId", otherDeviceId);
            await command.ExecuteNonQueryAsync();
        }
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
              (TaxProfileId,TenantId,Code,Name,Rate,IsActive,CreatedAt)
            VALUES
              (@TaxProfileId,@TenantId,@TaxCode,N'Sin impuesto',0,1,SYSDATETIMEOFFSET());
            INSERT dbo.Products
              (ProductId,TenantId,ProductCode,Reference,Sku,Name,Description,
               BaseUnitCode,TaxProfileId,ManageStock,IsWeighable,IsActive,Source,
               Currency,CreatedAt)
            VALUES
              (@ProductId,@TenantId,@ProductCode,@ProductCode,@ProductCode,
               N'Producto pricing',N'Producto de prueba de publicacion',N'EA',
               @TaxProfileId,1,0,1,0,N'COP',SYSDATETIMEOFFSET());
            INSERT dbo.ProductBarcodes
              (ProductBarcodeId,TenantId,ProductId,Barcode,IsPrimary,IsActive,CreatedAt)
            VALUES
              (NEWID(),@TenantId,@ProductId,@Barcode,1,1,SYSDATETIMEOFFSET());
            INSERT dbo.ProductPrices
              (ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,ValidFrom,
               TargetMarginPercent,RoundingIncrement,RoundingMode,IsActive,CreatedAt)
            VALUES
              (NEWID(),@BusinessId,@ProductId,@Price,N'COP',SYSDATETIMEOFFSET(),
               @TargetMarginPercent,1,N'Nearest',1,SYSDATETIMEOFFSET());
            INSERT dbo.SupplierProducts
              (SupplierProductId,TenantId,ProductId,SupplierId,SupplierProductCode,IsPrimary,IsActive,CreatedAt)
            VALUES
              (NEWID(),@TenantId,@ProductId,@SupplierId,@ProductCode,1,1,SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@SupplierId", fixture.SupplierId);
        command.Parameters.AddWithValue("@ProductCode", $"PR-{productId:N}");
        command.Parameters.AddWithValue("@TaxCode", $"T-{productId:N}"[..32]);
        command.Parameters.AddWithValue("@Barcode", barcode);
        command.Parameters.AddWithValue("@Price", price);
        command.Parameters.AddWithValue(
            "@TargetMarginPercent",
            targetMarginPercent is { } margin ? margin : DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ConfirmReceiptAsync(Guid productId, decimal unitCost)
    {
        var documentId = Guid.NewGuid();
        var receivedAt = new DateTimeOffset(2026, 9, 11, 9, 0, 0, TimeSpan.FromHours(-5));
        var receipt = new ConfirmGoodsReceiptRequest(
            documentId, fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            $"PRICE-{documentId:N}", receivedAt, receivedAt, false, null, "COP",
            "Secuencia de preparación",
            [new GoodsReceiptLineRequest(
                1, productId, "Producto pricing", 1m, unitCost, 0m,
                "00", 0m, PurchasingTaxTreatments.NotApplicable)]);
        using var purchasing = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        using var message = new HttpRequestMessage(
            HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
        { Content = JsonContent.Create(receipt) };
        message.Headers.Add("Idempotency-Key", $"pricing-sequence-{documentId:N}");
        using var response = await purchasing.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static async Task PrepareAndPublishAsync(
        HttpClient pricing,
        Guid productId,
        decimal salePrice)
    {
        using (var prepare = await pricing.PutAsJsonAsync(
                   $"/api/commerce/v1/pricing/products/{productId:D}/prepared-price",
                   new PublishProductPriceRequest(
                       PriceInputModes.SalePrice, null, salePrice, 1m,
                       PricingRoundingModes.Nearest)))
            prepare.EnsureSuccessStatusCode();
        var candidates = await pricing.GetFromJsonAsync<PriceRevisionPage>(
            "/api/commerce/v1/pricing/proposals?page=1&pageSize=100&status=Pending");
        var candidate = Assert.Single(candidates!.Items.Where(item => item.ProductId == productId));
        using var publish = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/publish",
            new PublishPricesRequest([candidate.ProductId]));
        publish.EnsureSuccessStatusCode();
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

    private async Task<IAsyncDisposable> GrantTenantBusinessAccessAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
            OUTPUT inserted.UserRoleId
            SELECT NEWID(),@UserId,@RoleId,businessValue.BusinessId,SYSUTCDATETIME()
            FROM dbo.Businesses businessValue
            WHERE businessValue.TenantId=@TenantId AND businessValue.IsActive=1
              AND NOT EXISTS(SELECT 1 FROM dbo.UserRoles existing
                WHERE existing.UserId=@UserId AND existing.RoleId=@RoleId
                  AND existing.BusinessId=businessValue.BusinessId);
            """, connection);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        command.Parameters.AddWithValue("@RoleId", fixture.RoleId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        var added = new List<Guid>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) added.Add(reader.GetGuid(0));
        return new TenantBusinessAccessGrant(fixture.ConnectionString, added);
    }

    private sealed class TenantBusinessAccessGrant(
        string connectionString, IReadOnlyCollection<Guid> roleIds) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (roleIds.Count == 0) return;
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                DELETE dbo.UserRoles WHERE UserRoleId IN (
                  SELECT TRY_CONVERT(uniqueidentifier,[value]) FROM OPENJSON(@RoleIdsJson));
                """, connection);
            command.Parameters.AddWithValue("@RoleIdsJson",
                System.Text.Json.JsonSerializer.Serialize(roleIds));
            await command.ExecuteNonQueryAsync();
        }
    }
}
