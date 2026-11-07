using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Sales;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class CatalogVerticalSliceTests
{
    [Fact]
    public async Task Store_catalog_pages_remain_bounded_with_ten_thousand_products()
    {
        var (taxProfileId, _, _) = await ConfigureCatalogAsync();
        var prefix = $"STOREPERF-{Guid.NewGuid():N}-";
        await ExecuteAsync("""
            ;WITH Digits AS (SELECT n FROM (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) digit(n)),
            Numbers AS (SELECT a.n+10*b.n+100*c.n+1000*d.n Number
                FROM Digits a CROSS JOIN Digits b CROSS JOIN Digits c CROSS JOIN Digits d)
            INSERT dbo.Products(ProductId,TenantId,ProductCode,Name,BaseUnitCode,TaxProfileId,ManageStock)
            SELECT NEWID(),@Tenant,CONCAT(@Prefix,Number),CONCAT(@Prefix,RIGHT('00000'+CONVERT(varchar(5),Number),5)),
                   'EA',@TaxProfileId,0
            FROM Numbers;
            INSERT dbo.ProductPrices(ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,ValidFrom,IsActive,CreatedAt)
            SELECT NEWID(),@Business,ProductId,49900,'COP',DATEADD(DAY,-1,SYSDATETIMEOFFSET()),1,SYSDATETIMEOFFSET()
            FROM dbo.Products WHERE TenantId=@Tenant AND ProductCode LIKE CONCAT(@Prefix,'%');
            """, new SqlParameter("@Tenant", fixture.TenantId), new SqlParameter("@Business", fixture.BusinessId),
            new SqlParameter("@TaxProfileId", taxProfileId), new SqlParameter("@Prefix", prefix));
        try
        {
            using var client = fixture.CreateAdminClient("sales.create");
            var context = new OnlineSalesDraftContext(fixture.BusinessId, fixture.WarehouseId, await fixture.OpenWebWorkSessionAsync());
            var timings = new List<double>();
            for (var round = 0; round < 11; round++)
            {
                Guid? lastProduct = null;
                foreach (var skip in new[] { 0, 50 })
                {
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    using var response = await client.PostAsJsonAsync("/api/commerce/v1/pos/catalog/products/search",
                        new SearchOnlineSalesRequest(context, Search: prefix, Skip: skip));
                    response.EnsureSuccessStatusCode();
                    var page = (await response.Content.ReadFromJsonAsync<OnlineSalesProductPage>())!;
                    timer.Stop();
                    Assert.Equal(50, page.Items.Count);
                    Assert.True(page.HasMore);
                    Assert.NotEqual(lastProduct, page.Items[0].ProductId);
                    lastProduct = page.Items[0].ProductId;
                    if (round > 0) timings.Add(timer.Elapsed.TotalMilliseconds);
                }
            }
            timings.Sort();
            var p95 = timings[(int)Math.Ceiling(timings.Count * 0.95) - 1];
            Console.WriteLine($"Store catalog, 10,000 products, first/next page, 20 warm reads: p95={p95:F1} ms");
            Assert.True(p95 < 500, $"Store catalog p95 was {p95:F1} ms; budget is 500 ms.");
        }
        finally
        {
            await ExecuteAsync("""
                DELETE prices FROM dbo.ProductPrices prices JOIN dbo.Products product ON product.ProductId=prices.ProductId
                WHERE product.TenantId=@Tenant AND product.ProductCode LIKE CONCAT(@Prefix,'%');
                DELETE dbo.Products WHERE TenantId=@Tenant AND ProductCode LIKE CONCAT(@Prefix,'%');
                """, new SqlParameter("@Tenant", fixture.TenantId), new SqlParameter("@Prefix", prefix));
        }
    }

    [Fact]
    public async Task Optional_variant_fields_preserve_manual_name_legacy_updates_and_inactive_history()
    {
        var (tax, _, _) = await ConfigureCatalogAsync();
        using var admin = fixture.CreateAdminClient(CatalogPermissionCodes.Create, CatalogPermissionCodes.Update,
            CatalogPermissionCodes.Read, CatalogPermissionCodes.ManageCosts, CatalogPermissionCodes.ManagePrices);
        var color = await CreateVariantOptionAsync(admin, "colors", "Negro");
        var size = await CreateVariantOptionAsync(admin, "sizes", "M");
        var request = ProductRequest(tax, [new ProductPriceInput(12_500m)], []) with
            { Name = "Nombre escrito por la empresa", Identifiers = [], Variant = new(color.Id, size.Id) };
        using var created = await admin.PostAsJsonAsync("/api/commerce/v1/products", request);
        created.EnsureSuccessStatusCode();
        var product = (await created.Content.ReadFromJsonAsync<ProductDetail>())!;
        request = request with { ProductCode = product.ProductCode };
        var path = $"/api/commerce/v1/products/{product.ProductId}/merchandising";
        var merchandising = (await admin.GetFromJsonAsync<ProductMerchandisingConfiguration>(path))!;
        Assert.Equal(color.Id, merchandising.ProductColorId);
        Assert.Equal(size.Id, merchandising.ProductSizeId);
        Assert.Equal(color.Name, merchandising.ProductColorName);
        Assert.Equal("Nombre escrito por la empresa", product.Name);

        using var deactivated = await admin.PutAsJsonAsync($"/api/commerce/v1/product-colors/{color.Id}",
            new SaveProductVariantOption(color.Code, color.Name, false, 3));
        deactivated.EnsureSuccessStatusCode();
        using var unchanged = await admin.PutAsJsonAsync($"/api/commerce/v1/products/{product.ProductId}", request);
        unchanged.EnsureSuccessStatusCode();
        using var legacy = await admin.PutAsJsonAsync($"/api/commerce/v1/products/{product.ProductId}", request with { Variant = null });
        legacy.EnsureSuccessStatusCode();
        Assert.Equal(color.Id, (await admin.GetFromJsonAsync<ProductMerchandisingConfiguration>(path))!.ProductColorId);
        using var invalidAssignment = await admin.PostAsJsonAsync("/api/commerce/v1/products", request);
        Assert.Equal(HttpStatusCode.BadRequest, invalidAssignment.StatusCode);

        using var cleared = await admin.PutAsJsonAsync($"/api/commerce/v1/products/{product.ProductId}", request with { Variant = new() });
        cleared.EnsureSuccessStatusCode();
        var empty = (await admin.GetFromJsonAsync<ProductMerchandisingConfiguration>(path))!;
        Assert.Null(empty.ProductColorId);
        Assert.Null(empty.ProductSizeId);
        using var readerOnly = fixture.CreateAdminClient(CatalogPermissionCodes.Read);
        using var denied = await readerOnly.PostAsJsonAsync("/api/commerce/v1/product-sizes", new SaveProductVariantOption("DENIED", "No autorizado"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var outsideScope = await admin.PutAsJsonAsync($"/api/commerce/v1/products/{product.ProductId}",
            request with { Variant = new(Guid.NewGuid(), size.Id) });
        Assert.Equal(HttpStatusCode.BadRequest, outsideScope.StatusCode);
    }

    [Fact]
    public async Task Store_family_browse_reuses_catalog_prices_and_preserves_the_direct_sale_draft()
    {
        var (tax, _, _) = await ConfigureCatalogAsync();
        using var admin = fixture.CreateAdminClient(CatalogPermissionCodes.Create, CatalogPermissionCodes.Update,
            CatalogPermissionCodes.Read, CatalogPermissionCodes.ManageCosts, CatalogPermissionCodes.ManagePrices, "sales.create", "products.read");
        var color = await CreateVariantOptionAsync(admin, "colors", "Azul");
        var size = await CreateVariantOptionAsync(admin, "sizes", "38 EU");
        var request = ProductRequest(tax, [new ProductPriceInput(12_500m)], []) with { Identifiers = [], Variant = new(color.Id, size.Id) };
        using var rootResponse = await admin.PostAsJsonAsync("/api/commerce/v1/products", request);
        rootResponse.EnsureSuccessStatusCode();
        var root = (await rootResponse.Content.ReadFromJsonAsync<ProductDetail>())!;
        using var childResponse = await admin.PostAsJsonAsync("/api/commerce/v1/products", request with
            { Name = "Otra talla del modelo", Link = new(root.ProductId, false, null, false, null) });
        childResponse.EnsureSuccessStatusCode();
        var child = (await childResponse.Content.ReadFromJsonAsync<ProductDetail>())!;
        var workSession = await fixture.OpenWebWorkSessionAsync();
        var context = new OnlineSalesDraftContext(fixture.BusinessId, fixture.WarehouseId, workSession);
        using var opened = await admin.PostAsJsonAsync("/api/commerce/v1/pos/drafts/active", new OpenOnlineSalesDraftRequest(context));
        opened.EnsureSuccessStatusCode();
        var draft = (await opened.Content.ReadFromJsonAsync<OnlineSalesDraft>())!;
        var browseRequest = new SearchOnlineSalesRequest(context, Take: 1, FamilyProductId: child.ProductId);
        foreach (var product in new[] { root, child })
        {
            using var photo = await admin.PutAsJsonAsync($"/api/commerce/v1/products/{product.ProductId}", request with
            {
                ProductCode = product.ProductCode, Name = product.Name,
                Link = product.ProductId == child.ProductId ? new(root.ProductId, false, null, false, null) : null,
                Images = [new ProductImageInput(Guid.NewGuid(), null, $"products/{product.ProductId:N}/secondary.png", null, 0, false),
                    new ProductImageInput(Guid.NewGuid(), null, $"products/{product.ProductId:N}/photo.png", null, 10, true)]
            });
            photo.EnsureSuccessStatusCode();
        }
        using var firstResponse = await admin.PostAsJsonAsync("/api/commerce/v1/pos/catalog/products/search", browseRequest);
        firstResponse.EnsureSuccessStatusCode();
        var first = (await firstResponse.Content.ReadFromJsonAsync<OnlineSalesProductPage>())!;
        Assert.True(first.HasMore);
        var item = Assert.Single(first.Items);
        Assert.Equal(root.ProductId, item.Presentation!.FamilyProductId);
        Assert.Equal(color.Id, item.Presentation.ProductColorId);
        Assert.Equal(size.Name, item.Presentation.SizeName);
        Assert.Equal($"https://media.auraly.test/tenant/{fixture.TenantId:D}/products/{item.ProductId:N}/photo.png", item.ImageUrl);
        Assert.Equal(item.ImageUrl, item.Presentation.ImageUrl);
        var productList = await admin.GetFromJsonAsync<Auraly.Platform.Application.Common.DTOs.PagedResponse<Auraly.Platform.Application.Identity.DTOs.ProductDto>>(
            $"/api/v1/businesses/{fixture.BusinessId}/products?page=1&pageSize=1&search={Uri.EscapeDataString(child.Name)}");
        var cardProduct = Assert.Single(productList!.Items);
        Assert.Equal(child.ProductId, cardProduct.ProductId);
        Assert.Equal($"https://media.auraly.test/tenant/{fixture.TenantId:D}/products/{child.ProductId:N}/photo.png", cardProduct.ImageUrl);
        using var nextResponse = await admin.PostAsJsonAsync("/api/commerce/v1/pos/catalog/products/search", browseRequest with { Skip = 1 });
        nextResponse.EnsureSuccessStatusCode();
        var next = (await nextResponse.Content.ReadFromJsonAsync<OnlineSalesProductPage>())!;
        Assert.False(next.HasMore);
        Assert.NotEqual(item.ProductId, Assert.Single(next.Items).ProductId);
        Assert.Equal(Assert.Single(next.Items).ImageUrl, Assert.Single(next.Items).Presentation!.ImageUrl);
        using var publicResponse = await admin.PostAsJsonAsync("/api/commerce/v1/pos/catalog/products/search", browseRequest with { PublicPriceOnly = true });
        publicResponse.EnsureSuccessStatusCode();
        var publicPage = (await publicResponse.Content.ReadFromJsonAsync<OnlineSalesProductPage>())!;
        Assert.Equal(item.ImageUrl, Assert.Single(publicPage.Items).ImageUrl);
        using var regularResponse = await admin.PostAsJsonAsync("/api/commerce/v1/pos/drafts/products/search",
            new SearchOnlineSalesRequest(context, Search: item.ProductCode));
        regularResponse.EnsureSuccessStatusCode();
        var regular = (await regularResponse.Content.ReadFromJsonAsync<OnlineSalesProductPage>())!;
        Assert.Equal(item.UnitPrice, regular.Items.First(product => product.ProductId == item.ProductId).UnitPrice);
        using var unknown = await admin.PostAsJsonAsync("/api/commerce/v1/pos/catalog/products/search", browseRequest with { FamilyProductId = Guid.NewGuid() });
        unknown.EnsureSuccessStatusCode();
        Assert.Empty((await unknown.Content.ReadFromJsonAsync<OnlineSalesProductPage>())!.Items);
        using var reopened = await admin.PostAsJsonAsync("/api/commerce/v1/pos/drafts/active", new OpenOnlineSalesDraftRequest(context));
        reopened.EnsureSuccessStatusCode();
        var sameDraft = (await reopened.Content.ReadFromJsonAsync<OnlineSalesDraft>())!;
        Assert.Equal(draft.DraftId, sameDraft.DraftId);
        Assert.Equal(draft.Version, sameDraft.Version);

        using var device = fixture.CreateClient();
        device.DefaultRequestHeaders.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString());
        device.DefaultRequestHeaders.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        var scope = $"businessId={fixture.BusinessId}&warehouseId={fixture.WarehouseId}";
        using var started = await device.PostAsync($"/api/pos/v1/catalog/sync-sessions?{scope}", null);
        started.EnsureSuccessStatusCode();
        var session = (await started.Content.ReadFromJsonAsync<CatalogSyncSessionResponse>())!;
        using var downloaded = await device.GetAsync(
            $"/api/pos/v1/catalog/sync-sessions/{session.SessionId}/pages?{scope}&pageSize=500");
        downloaded.EnsureSuccessStatusCode();
        var bootstrap = (await downloaded.Content.ReadFromJsonAsync<CatalogBootstrapPage>())!;
        Assert.True(CatalogBootstrapIntegrity.IsValid(bootstrap.Items, bootstrap.IntegrityHash));
        var localChild = Assert.Single(bootstrap.Items, product => product.ProductId == child.ProductId);
        Assert.Equal(root.ProductId, localChild.Presentation!.FamilyProductId);
        Assert.Equal(color.Id, localChild.Presentation.ProductColorId);
        Assert.Equal(size.Id, localChild.Presentation.ProductSizeId);
        Assert.Equal(item.UnitPrice, localChild.UnitPrice);
    }

    private static async Task<ProductVariantOption> CreateVariantOptionAsync(HttpClient admin, string attribute, string name)
    {
        using var response = await admin.PostAsJsonAsync($"/api/commerce/v1/product-{attribute}",
            new SaveProductVariantOption(Guid.NewGuid().ToString("N"), $"{name} {Guid.NewGuid():N}"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductVariantOption>())!;
    }
}
