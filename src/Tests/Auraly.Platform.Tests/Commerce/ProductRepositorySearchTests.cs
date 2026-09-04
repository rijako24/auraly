using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;
using Auraly.Platform.Infrastructure.Data.ReadModels;
using Auraly.Platform.Infrastructure.Repositories;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public sealed class ProductRepositorySearchTests
{
    [Fact]
    public async Task ProductModel_DoesNotPersistTheProjectedUnitPrice()
    {
        await using var context = CreateContext();

        context.Model.FindEntityType(typeof(Auraly.Platform.Domain.Entities.Product))!
            .FindProperty(nameof(Auraly.Platform.Domain.Entities.Product.UnitPrice))
            .Should().BeNull();
    }

    [Fact]
    public async Task SearchByIndexTerms_IncludesActiveProductWithoutIndexRows()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();
        AddBusinessScope(context, businessId);
        var jamon = Product(businessId, "JAMON CUNIT X 500GR", "CF17", active: true);
        context.Products.Add(jamon);
        Publish(context, jamon, 10m);
        await context.SaveChangesAsync();

        var result = await new ProductRepository(context).SearchByIndexTermsAsync(
            businessId, ["jamon", "cuni"], 20);

        result.Should().ContainSingle(product => product.ProductId == jamon.ProductId);
    }

    [Fact]
    public async Task SearchByIndexTerms_ReturnsInactiveIdentityWithoutHidingActiveProduct()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();
        AddBusinessScope(context, businessId);
        var inactiveExternal = Product(businessId, "TOCINETA IMPORTADA", "EXT-1", active: false);
        var activeLocal = Product(businessId, "Tocineta ahumada 500 g", "LOCAL-1", active: true);
        context.Products.AddRange(inactiveExternal, activeLocal);
        Publish(context, inactiveExternal, 10m);
        Publish(context, activeLocal, 10m);
        context.ProductSearchTerms.Add(new ProductSearchTerm
        {
            BusinessId = businessId,
            ProductId = inactiveExternal.ProductId,
            Product = inactiveExternal,
            Term = "tocineta"
        });
        await context.SaveChangesAsync();

        var result = await new ProductRepository(context).SearchByIndexTermsAsync(
            businessId, ["tocineta"], 20);

        result.Select(product => product.ProductId).Should().Contain([
            activeLocal.ProductId, inactiveExternal.ProductId]);
        result[0].ProductId.Should().Be(activeLocal.ProductId);
    }

    [Fact]
    public async Task SearchByIndexTerms_UnionsConfiguredTermsAndNativeIdentity()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();
        AddBusinessScope(context, businessId);
        var keywordProduct = Product(businessId, "PRESENTACION ESPECIAL", "KEY-1", active: true);
        var nativeProduct = Product(businessId, "Papa a la francesa 2.5 kg", "PAPA-1", active: true);
        context.Products.AddRange(keywordProduct, nativeProduct);
        Publish(context, keywordProduct, 10m);
        Publish(context, nativeProduct, 10m);
        context.ProductSearchTerms.Add(new ProductSearchTerm
        {
            BusinessId = businessId,
            ProductId = keywordProduct.ProductId,
            Product = keywordProduct,
            Term = "papa"
        });
        await context.SaveChangesAsync();

        var result = await new ProductRepository(context).SearchByIndexTermsAsync(
            businessId, ["papa"], 20);

        result.Select(product => product.ProductId)
            .Should().BeEquivalentTo([keywordProduct.ProductId, nativeProduct.ProductId]);
    }

    [Fact]
    public async Task ReplaceSearchTerms_IndexesStableIdentityWithoutProviderDescriptionNoise()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();
        AddBusinessScope(context, businessId);
        var product = Product(businessId, "ALKAPARRAS VINAGRE x500gr", "PV48", active: true);
        product.ExternalProductId = "PV48";
        product.CategoryName = "VINAGRES";
        product.Description = "UNIDAD PRODUCTO GENERAL VARIOS";
        context.Products.Add(product);
        Publish(context, product, 10m);
        await context.SaveChangesAsync();

        var repository = new ProductRepository(context);
        await repository.ReplaceSearchTermsAsync(product);
        await context.SaveChangesAsync();

        var terms = await repository.GetSearchTermsAsync(businessId, product.ProductId);
        terms.Should().Contain(["alkaparra", "vinagre", "500", "gr", "48"]);
        terms.Should().NotContain(["producto", "general", "unidad", "vario", "pv"]);
        terms.Should().NotContain(term => term.Contains("produkto", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetById_ReturnsNullWhenProductDoesNotExist()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();
        AddBusinessScope(context, businessId);
        await context.SaveChangesAsync();

        var result = await new ProductRepository(context).GetByIdAsync(
            businessId, Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Product_list_uses_negative_balance_from_every_active_public_warehouse()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();
        AddBusinessScope(context, businessId);
        var product = Product(businessId, "PRODUCTO CON SALDO NEGATIVO", "NEG-3", active: true);
        product.ManageStock = true;
        var salesWarehouseId = Guid.NewGuid();
        var secondPublicWarehouseId = Guid.NewGuid();
        var systemWarehouseId = Guid.NewGuid();
        var inactiveWarehouseId = Guid.NewGuid();
        context.Products.Add(product);
        Publish(context, product, 10m);
        context.InventoryWarehouseScopes.AddRange(
            Warehouse(businessId, salesWarehouseId, isSystem: false, isActive: true),
            Warehouse(businessId, secondPublicWarehouseId, isSystem: false, isActive: true),
            Warehouse(businessId, systemWarehouseId, isSystem: true, isActive: true),
            Warehouse(businessId, inactiveWarehouseId, isSystem: false, isActive: false));
        context.InventoryBalances.AddRange(
            Balance(businessId, salesWarehouseId, product.ProductId, -5m),
            Balance(businessId, secondPublicWarehouseId, product.ProductId, 2m),
            Balance(businessId, systemWarehouseId, product.ProductId, 3m),
            Balance(businessId, inactiveWarehouseId, product.ProductId, 10m));
        await context.SaveChangesAsync();

        var result = await new ProductRepository(context).GetPagedByBusinessIdAsync(
            businessId, 1, 20, includeInactive: true);

        result.Items.Should().ContainSingle();
        result.Items[0].StockQuantity.Should().Be(-3m);
    }

    [Fact]
    public async Task Product_list_applies_classification_brand_and_tri_state_filters_before_paging()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        AddBusinessScope(context, businessId);
        var matching = Product(businessId, "PRODUCTO FILTRADO", "FILTER-1", active: true);
        matching.ProductCategoryId = categoryId;
        matching.ProductBrandId = brandId;
        matching.ManageStock = true;
        matching.AllowsFractionalSale = false;
        matching.IsWeighable = true;
        var excluded = Product(businessId, "PRODUCTO EXCLUIDO", "FILTER-2", active: true);
        excluded.ProductCategoryId = categoryId;
        excluded.ProductBrandId = brandId;
        excluded.ManageStock = false;
        excluded.AllowsFractionalSale = false;
        excluded.IsWeighable = true;
        context.Products.AddRange(matching, excluded);
        Publish(context, matching, 10m);
        Publish(context, excluded, 10m);
        await context.SaveChangesAsync();

        var result = await new ProductRepository(context).GetPagedByBusinessIdAsync(
            businessId, 1, 20, includeInactive: true,
            filter: new ProductListFilter(
                [categoryId], BrandId: brandId, ManagesInventory: true,
                AllowsFractionalSale: false, IsWeighable: true));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(product => product.ProductId == matching.ProductId);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Product Product(Guid businessId, string name, string sku, bool active) => new()
    {
        ProductId = Guid.NewGuid(),
        TenantId = businessId,
        BusinessId = businessId,
        Name = name,
        Sku = sku,
        UnitPrice = 10m,
        Currency = "COP",
        IsActive = active
    };

    private static void AddBusinessScope(ApplicationDbContext context, Guid businessId) =>
        context.Businesses.Add(new Business
        {
            BusinessId = businessId,
            TenantId = businessId,
            Name = "Test business",
            IsActive = true
        });

    private static InventoryWarehouseScopeRow Warehouse(
        Guid businessId, Guid warehouseId, bool isSystem, bool isActive) => new()
    {
        BusinessId = businessId,
        WarehouseId = warehouseId,
        IsSystem = isSystem,
        IsActive = isActive
    };

    private static InventoryBalanceRow Balance(
        Guid businessId, Guid warehouseId, Guid productId, decimal quantity) => new()
    {
        BusinessId = businessId,
        WarehouseId = warehouseId,
        ProductId = productId,
        QuantityOnHand = quantity
    };

    [Fact]
    public async Task SearchAsync_DoesNotExposeLegacyUnitPriceWithoutPublishedPrice()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();
        AddBusinessScope(context, businessId);
        var product = Product(businessId, "PRODUCTO SIN PUBLICAR", "NO-PUBLICADO", active: true);
        product.UnitPrice = 98765m;
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var result = await new ProductRepository(context).SearchAsync(
            businessId, null, null, 10);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_UsesOnlyTheCanonicalPublishedPrice()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();
        AddBusinessScope(context, businessId);
        var product = Product(businessId, "PRODUCTO PUBLICADO", "PUBLICADO", active: true);
        product.UnitPrice = 111m;
        context.Products.Add(product);
        Publish(context, product, 25900m);
        await context.SaveChangesAsync();

        var result = await new ProductRepository(context).SearchAsync(
            businessId, null, null, 10);

        result.Should().ContainSingle();
        result[0].UnitPrice.Should().Be(25900m);
        result[0].HasPublishedPrice.Should().BeTrue();
    }

    [Fact]
    public async Task Product_master_is_visible_in_another_business_of_the_same_tenant_with_that_business_price()
    {
        await using var context = CreateContext();
        var tenantId = Guid.NewGuid();
        var originBusinessId = Guid.NewGuid();
        var targetBusinessId = Guid.NewGuid();
        context.Businesses.AddRange(
            new Business { BusinessId = originBusinessId, TenantId = tenantId, Name = "Origin", IsActive = true },
            new Business { BusinessId = targetBusinessId, TenantId = tenantId, Name = "Target", IsActive = true });
        var product = Product(originBusinessId, "PRODUCTO DEL TENANT", "TENANT-1", active: true);
        product.TenantId = tenantId;
        context.Products.Add(product);
        context.PublishedProductPrices.Add(new PublishedProductPriceRow
        {
            ProductPriceId = Guid.NewGuid(),
            BusinessId = targetBusinessId,
            ProductId = product.ProductId,
            Amount = 42_500m,
            CurrencyCode = "COP",
            ValidFrom = DateTimeOffset.UtcNow.AddMinutes(-1),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var result = await new ProductRepository(context).SearchAsync(
            targetBusinessId, null, null, 10);

        result.Should().ContainSingle();
        result[0].ProductId.Should().Be(product.ProductId);
        result[0].UnitPrice.Should().Be(42_500m);
    }

    [Fact]
    public async Task GetLinkedFamily_ReturnsEveryOptionWithItsIndependentPriceAndStock()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();
        AddBusinessScope(context, businessId);
        var model = Product(businessId, "Tenis Runner", "RUNNER", active: true);
        var black40 = Product(businessId, "Tenis Runner negro talla 40", "RUN-N40", active: true);
        var white41 = Product(businessId, "Tenis Runner blanco talla 41", "RUN-B41", active: true);
        model.StockQuantity = 0;
        black40.StockQuantity = 4;
        white41.StockQuantity = 7;
        context.Products.AddRange(model, black40, white41);
        Publish(context, model, 100_000m);
        Publish(context, black40, 110_000m);
        Publish(context, white41, 120_000m);
        context.ProductLinks.AddRange(
            FamilyLink(businessId, model.ProductId, black40.ProductId),
            FamilyLink(businessId, model.ProductId, white41.ProductId));
        await context.SaveChangesAsync();

        var result = await new ProductRepository(context).GetLinkedFamilyAsync(
            businessId, [black40.ProductId]);

        result.Select(product => product.ProductId).Should().BeEquivalentTo([
            model.ProductId, black40.ProductId, white41.ProductId]);
        result.Single(product => product.ProductId == black40.ProductId).StockQuantity.Should().Be(4);
        result.Single(product => product.ProductId == white41.ProductId).UnitPrice.Should().Be(120_000m);
    }

    private static ProductLink FamilyLink(Guid businessId, Guid parentId, Guid childId) => new()
    {
        ProductLinkId = Guid.NewGuid(),
        BusinessId = businessId,
        ParentProductId = parentId,
        ChildProductId = childId,
        SharesInventory = false,
        SharesPrice = false,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static void Publish(ApplicationDbContext context, Product product, decimal amount)
    {
        var now = DateTimeOffset.UtcNow;
        context.PublishedProductPrices.Add(new PublishedProductPriceRow
        {
            ProductPriceId = Guid.NewGuid(),
            BusinessId = product.BusinessId,
            ProductId = product.ProductId,
            Amount = amount,
            CurrencyCode = "COP",
            ValidFrom = now.AddMinutes(-1),
            IsActive = true,
            CreatedAt = now
        });
    }
}
