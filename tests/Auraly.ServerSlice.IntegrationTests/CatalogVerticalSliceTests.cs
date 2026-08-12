using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Pricing;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class CatalogVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Product_workspace_creates_classification_brand_and_link_atomically()
    {
        var (taxProfileId, _, _) = await ConfigureCatalogAsync();
        var categoryId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.ProductCategories(ProductCategoryId,BusinessId,Name,DisplayOrder,IsActive,IsBrowsable,CreatedAt)
              VALUES(@Category,@Business,N'Aceites',0,1,1,SYSUTCDATETIME());
            INSERT dbo.ProductBrands(ProductBrandId,BusinessId,Name,IsActive,CreatedAt)
              VALUES(@Brand,@Business,N'Marca prueba',1,SYSDATETIMEOFFSET());
            """,
            new SqlParameter("@Category", categoryId),
            new SqlParameter("@Brand", brandId),
            new SqlParameter("@Business", fixture.BusinessId));

        using var admin = fixture.CreateAdminClient(
            CatalogPermissionCodes.Create,
            CatalogPermissionCodes.ManagePrices,
            CatalogPermissionCodes.ManageCosts);
        using var parentResponse = await admin.PostAsJsonAsync(
            "/api/commerce/v1/products",
            ProductRequest(taxProfileId, [new ProductPriceInput(10_000m)], []));
        parentResponse.EnsureSuccessStatusCode();
        var parent = await parentResponse.Content.ReadFromJsonAsync<ProductDetail>();
        Assert.NotNull(parent);

        var request = ProductRequest(
            taxProfileId,
            [new ProductPriceInput(20_000m)],
            [new ProductBarcodeInput($"ATOMIC-{Guid.NewGuid():N}", true)]) with
        {
            ProductCategoryId = categoryId,
            ProductBrandId = brandId,
            AllowsFractionalSale = true,
            Link = new ProductLinkInput(parent.ProductId, true, 2m, true, 2m)
        };
        using var childResponse = await admin.PostAsJsonAsync("/api/commerce/v1/products", request);
        Assert.Equal(HttpStatusCode.Created, childResponse.StatusCode);
        var child = await childResponse.Content.ReadFromJsonAsync<ProductDetail>();
        Assert.NotNull(child);

        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Products WHERE ProductId=@Id AND ProductCategoryId=@Category AND ProductBrandId=@Brand AND AllowsFractionalSale=1;",
            new SqlParameter("@Id", child.ProductId), new SqlParameter("@Category", categoryId), new SqlParameter("@Brand", brandId)));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ProductLinks WHERE ChildProductId=@Child AND ParentProductId=@Parent AND InventoryFactor=2 AND PriceFactor=2 AND IsActive=1;",
            new SqlParameter("@Child", child.ProductId), new SqlParameter("@Parent", parent.ProductId)));
    }

    [Fact]
    public async Task Catalog_push_failure_remains_durable_and_recovers_without_polling()
    {
        fixture.DrainSynchronizationMessages();
        var (taxProfileId, _, _) = await ConfigureCatalogAsync();
        var request = ProductRequest(
            taxProfileId,
            [new ProductPriceInput(9_900m)],
            [new ProductBarcodeInput(
                $"78{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}",
                true)]);

        fixture.FailNextSynchronizationPublication();
        using var admin = fixture.CreateAdminClient(
            CatalogPermissionCodes.Create,
            CatalogPermissionCodes.ManagePrices,
            CatalogPermissionCodes.ManageCosts);
        using var response = await admin.PostAsJsonAsync(
            "/api/commerce/v1/products",
            request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var recovered = await fixture.ReadSynchronizationMessageAsync();
        Assert.Equal("Catalog", recovered.Stream);
        var durableReceiptObserved = false;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            durableReceiptObserved = await ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM dbo.PosSynchronizationOutboxMessages
                WHERE BusinessId=@Business AND Stream=@Stream
                  AND AvailableThroughCursor=@Cursor
                  AND AttemptCount=2 AND PublishedAt IS NOT NULL
                  AND LastError IS NULL;
                """,
                new SqlParameter("@Business", recovered.BusinessId),
                new SqlParameter("@Stream", recovered.Stream),
                new SqlParameter("@Cursor", recovered.AvailableThroughCursor)) == 1;
            if (durableReceiptObserved) break;
            await Task.Delay(20);
        }

        Assert.True(
            durableReceiptObserved,
            "The failed push must be retried and durably acknowledged exactly once.");
    }

    [Fact]
    public async Task Product_is_administered_once_and_synchronized_to_physical_pos_sqlite()
    {
        fixture.DrainSynchronizationMessages();
        var (taxProfileId, priceChannelId, secondChannelId) = await ConfigureCatalogAsync();
        var request = ProductRequest(
            taxProfileId,
            [
                new ProductPriceInput(12_500m)
            ],
            [new ProductBarcodeInput("7701234500012", true)]);

        using var admin = fixture.CreateAdminClient(
            CatalogPermissionCodes.Create,
            CatalogPermissionCodes.Read,
            CatalogPermissionCodes.Update,
            CatalogPermissionCodes.Deactivate,
            CatalogPermissionCodes.ManagePrices,
            CatalogPermissionCodes.ReadCosts,
            CatalogPermissionCodes.ManageCosts);
        using var create = await admin.PostAsJsonAsync("/api/commerce/v1/products", request);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ProductDetail>();
        Assert.NotNull(created);
        var createdSignal = await fixture.ReadSynchronizationMessageAsync();
        Assert.Equal("Catalog", createdSignal.Stream);
        Assert.Equal(fixture.TenantId, createdSignal.TenantId);
        Assert.Equal(fixture.BusinessId, createdSignal.BusinessId);
        Assert.True(createdSignal.AvailableThroughCursor > 0);
        var suppliers = created.Suppliers!;

        Assert.Equal(0m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@Id AND IsActive=1;",
            new SqlParameter("@Id", created.ProductId)));
        Assert.Equal(12_500m, await ScalarAsync<decimal>(
            "SELECT PreparedAmount FROM dbo.ProductPrices WHERE ProductId=@Id AND IsActive=1;",
            new SqlParameter("@Id", created.ProductId)));

        using var pricing = fixture.CreateAdminClient(
            PricingPermissionCodes.Read,
            PricingPermissionCodes.ReadCostBasis,
            PricingPermissionCodes.PublishPrices);
        var candidates = await pricing.GetFromJsonAsync<PriceRevisionPage>(
            "/api/commerce/v1/pricing/proposals?page=1&pageSize=100&status=Approved");
        var candidate = Assert.Single(candidates!.Items.Where(item => item.ProductId == created.ProductId));
        using var publication = await pricing.PostAsJsonAsync(
            "/api/commerce/v1/pricing/publish",
            new PublishPricesRequest([new PublishPriceItem(
                candidate.ProposalId,
                PriceInputModes.SalePrice,
                null,
                12_500m,
                1m,
                PricingRoundingModes.Nearest,
                candidate.ConcurrencyToken)]));
        publication.EnsureSuccessStatusCode();
        var publishedSignal = await fixture.ReadSynchronizationMessageAsync();
        Assert.True(publishedSignal.AvailableThroughCursor > createdSignal.AvailableThroughCursor);
        var priceListId = Guid.NewGuid();
        var listItemId = Guid.NewGuid();
        var channelItemId = Guid.NewGuid();
        var listCustomerId = Guid.NewGuid();
        var channelCustomerId = Guid.NewGuid();
        var listPartyId = Guid.NewGuid();
        var channelPartyId = Guid.NewGuid();
        var countryId = await ScalarAsync<Guid>(
            "SELECT CountryId FROM dbo.Countries WHERE Code=N'CO';");
        await ExecuteAsync(
            """
            INSERT dbo.PriceLists(PriceListId,BusinessId,Code,Name,IsActive,CreatedAt)
              VALUES(@List,@Business,N'VIP',N'VIP',1,SYSDATETIMEOFFSET());
            INSERT dbo.PriceListItems
              (PriceListItemId,PriceListId,ProductId,MinimumQuantity,Amount,CurrencyCode,ValidFrom,IsActive,CreatedAt)
              VALUES(@ListItem,@List,@Product,1,11000,N'COP',SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET());
            INSERT dbo.ResolvedPriceChannelItems
              (ResolvedPriceChannelItemId,PriceChannelId,ProductId,Amount,CurrencyCode,ValidFrom,IsActive,CreatedAt)
              VALUES(@ChannelItem,@Channel,@Product,11500,N'COP',SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET());
            INSERT dbo.Parties
              (PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,
               Identification,NormalizedIdentification,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
              VALUES
              (@ListParty,@Tenant,N'NaturalPerson',@Country,N'CC',N'1001',N'1001',
               N'List customer',N'Complete',1,@User,SYSDATETIMEOFFSET()),
              (@ChannelParty,@Tenant,N'NaturalPerson',@Country,N'CC',N'1002',N'1002',
               N'Channel customer',N'Complete',1,@User,SYSDATETIMEOFFSET());
            INSERT dbo.Customers(CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
              VALUES
              (@ListCustomer,@ListParty,@Business,1,@User,SYSDATETIMEOFFSET()),
              (@ChannelCustomer,@ChannelParty,@Business,1,@User,SYSDATETIMEOFFSET());
            INSERT dbo.CustomerPricingSettings(CustomerId,PriceListId,PriceChannelId,UpdatedBy,UpdatedAt)
              VALUES
              (@ListCustomer,@List,NULL,@User,SYSDATETIMEOFFSET()),
              (@ChannelCustomer,NULL,@Channel,@User,SYSDATETIMEOFFSET());
            """,
            new SqlParameter("@List", priceListId),
            new SqlParameter("@Business", fixture.BusinessId),
            new SqlParameter("@Tenant", fixture.TenantId),
            new SqlParameter("@User", fixture.UserId),
            new SqlParameter("@Country", countryId),
            new SqlParameter("@ListParty", listPartyId),
            new SqlParameter("@ChannelParty", channelPartyId),
            new SqlParameter("@ListItem", listItemId),
            new SqlParameter("@ChannelItem", channelItemId),
            new SqlParameter("@Channel", priceChannelId),
            new SqlParameter("@Product", created.ProductId),
            new SqlParameter("@ListCustomer", listCustomerId),
            new SqlParameter("@ChannelCustomer", channelCustomerId));
        Assert.Single(suppliers);
        var filterUri = $"/api/commerce/v1/products?barcode=7701234500012&supplierId={suppliers.Single().SupplierId:D}&minimumPrice=12000&maximumPrice=13000&sortDescending=true";
        var page = await admin.GetFromJsonAsync<ProductPage>(filterUri);
        Assert.NotNull(page);
        Assert.Contains(page.Items, product => product.ProductId == created.ProductId);

        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Products WHERE ProductId=@Id;",
            new SqlParameter("@Id", created.ProductId)));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE name=N'CatalogProducts';"));

        var sqlitePath = Path.Combine(Path.GetTempPath(), $"auraly-pos-catalog-{Guid.NewGuid():N}.db");
        try
        {
            var local = new PosCatalogStore($"Data Source={sqlitePath}");
            var sync = new PosCatalogSynchronizer(
                fixture.CreateClient(),
                local,
                new PosDeviceCredentials(fixture.DeviceId, ServerSliceFixture.DeviceSecret),
                new PosOperationalScope(fixture.BusinessId, fixture.WarehouseId));
            await sync.SynchronizeAsync();

            var captured = await local.CaptureAsync("7701234500012");
            Assert.NotNull(captured);
            Assert.Equal(12_500m, captured.Product.UnitPrice);
            Assert.Equal(fixture.BusinessId, await ScalarAsync<Guid>(
                "SELECT BusinessId FROM dbo.ProductPrices WHERE ProductId=@Id AND IsActive=1;",
                new SqlParameter("@Id", created.ProductId)));
            Assert.Equal(0, await LocalScalarAsync(
                sqlitePath,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name LIKE '%Supplier%';"));

            var listPrice = await local.ResolvePriceAsync(created.ProductId, listCustomerId, 1m);
            Assert.Equal("PriceList", listPrice.Source);
            Assert.Equal(11_000m, listPrice.Amount);
            var channelPrice = await local.ResolvePriceAsync(created.ProductId, channelCustomerId, 1m);
            Assert.Equal("PriceChannel", channelPrice.Source);
            Assert.Equal(11_500m, channelPrice.Amount);
            Assert.Equal(12_500m, (await local.ResolvePriceAsync(created.ProductId, null, 1m)).Amount);

            var attempted = request with
            {
                Name = "Coffee updated",
                Barcodes = [new ProductBarcodeInput("7701234500098", true)],
                Prices = [new ProductPriceInput(13_250m)]
            };
            using (var rejected = await admin.PutAsJsonAsync(
                       $"/api/commerce/v1/products/{created.ProductId:D}",
                       attempted))
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

            var updated = attempted with
            {
                Prices = [new ProductPriceInput(12_500m)]
            };
            using var update = await admin.PutAsJsonAsync(
                $"/api/commerce/v1/products/{created.ProductId:D}",
                updated);
            update.EnsureSuccessStatusCode();
            var updatedSignal = await fixture.ReadSynchronizationMessageAsync();
            Assert.Equal("Catalog", updatedSignal.Stream);
            Assert.True(
                updatedSignal.AvailableThroughCursor >
                publishedSignal.AvailableThroughCursor);

            await sync.SynchronizeAsync();
            Assert.Null(await local.CaptureAsync("7701234500012"));
            var changed = await local.CaptureAsync("7701234500098");
            Assert.NotNull(changed);
            Assert.Equal(12_500m, changed.Product.UnitPrice);

            using var deactivate = await admin.PostAsync(
                $"/api/commerce/v1/products/{created.ProductId:D}/deactivate",
                content: null);
            Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
            var deactivatedSignal = await fixture.ReadSynchronizationMessageAsync();
            Assert.True(deactivatedSignal.AvailableThroughCursor >
                        updatedSignal.AvailableThroughCursor);
            await sync.SynchronizeAsync();
            Assert.Null(await local.CaptureAsync("7701234500098"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(sqlitePath)) File.Delete(sqlitePath);
        }
    }

    [Fact]
    public async Task Supplier_cost_changes_are_versioned_without_recreating_the_supplier_product()
    {
        var (taxProfileId, _, _) = await ConfigureCatalogAsync();
        var request = ProductRequest(
            taxProfileId,
            [new ProductPriceInput(12_500m)],
            [new ProductBarcodeInput($"76{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}", true)]);

        using var admin = fixture.CreateAdminClient(
            CatalogPermissionCodes.Create,
            CatalogPermissionCodes.Update,
            CatalogPermissionCodes.ManagePrices,
            CatalogPermissionCodes.ManageCosts);
        using var creation = await admin.PostAsJsonAsync("/api/commerce/v1/products", request);
        creation.EnsureSuccessStatusCode();
        var created = (await creation.Content.ReadFromJsonAsync<ProductDetail>())!;
        var supplier = Assert.Single(created.Suppliers!);
        var supplierProductId = await ScalarAsync<Guid>(
            "SELECT SupplierProductId FROM dbo.SupplierProducts WHERE ProductId=@Product AND SupplierId=@Supplier;",
            new SqlParameter("@Product", created.ProductId),
            new SqlParameter("@Supplier", supplier.SupplierId));

        var changedRequest = request with
        {
            Prices = [new ProductPriceInput(0m)],
            Suppliers = [supplier with { BaseUnitCost = 8_500m }]
        };
        using var changed = await admin.PutAsJsonAsync(
            $"/api/commerce/v1/products/{created.ProductId:D}", changedRequest);
        Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());

        Assert.Equal(supplierProductId, await ScalarAsync<Guid>(
            "SELECT SupplierProductId FROM dbo.SupplierProducts WHERE ProductId=@Product AND SupplierId=@Supplier;",
            new SqlParameter("@Product", created.ProductId),
            new SqlParameter("@Supplier", supplier.SupplierId)));
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.SupplierCostAgreements WHERE SupplierProductId=@Id;",
            new SqlParameter("@Id", supplierProductId)));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.SupplierCostAgreements WHERE SupplierProductId=@Id AND IsActive=1 AND BaseUnitCost=8500;",
            new SqlParameter("@Id", supplierProductId)));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.SupplierCostAgreements WHERE SupplierProductId=@Id AND IsActive=0 AND ValidUntil IS NOT NULL;",
            new SqlParameter("@Id", supplierProductId)));

        using var replay = await admin.PutAsJsonAsync(
            $"/api/commerce/v1/products/{created.ProductId:D}", changedRequest);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.SupplierCostAgreements WHERE SupplierProductId=@Id;",
            new SqlParameter("@Id", supplierProductId)));
    }
    [Fact]
    public async Task Security_uniqueness_and_warehouse_negative_policy_are_enforced()
    {
        var (taxProfileId, priceChannelId, _) = await ConfigureCatalogAsync();
        var barcode = $"77{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";
        var request = ProductRequest(
            taxProfileId,
            [new ProductPriceInput(10_000m)],
            [new ProductBarcodeInput(barcode, true)]) with
        {
            ProductCode = $"SEC-{Guid.NewGuid():N}"
        };

        using var denied = fixture.CreateAdminClient(CatalogPermissionCodes.Read);
        using var deniedResponse = await denied.PostAsJsonAsync("/api/commerce/v1/products", request);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var admin = fixture.CreateAdminClient(
            CatalogPermissionCodes.Create,
            CatalogPermissionCodes.ManagePrices,
            CatalogPermissionCodes.ManageCosts);
        using var createdResponse = await admin.PostAsJsonAsync("/api/commerce/v1/products", request);
        createdResponse.EnsureSuccessStatusCode();
        var created = (await createdResponse.Content.ReadFromJsonAsync<ProductDetail>())!;

        using var wrongScope = await admin.PostAsJsonAsync(
            "/api/commerce/v1/products",
            request with { BusinessId = Guid.NewGuid(), ProductCode = $"OTHER-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Forbidden, wrongScope.StatusCode);

        using var duplicate = await admin.PostAsJsonAsync(
            "/api/commerce/v1/products",
            request with { ProductCode = $"DUP-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var sync = new PosCatalogSynchronizer(
            fixture.CreateClient(),
            new PosCatalogStore($"Data Source={Path.Combine(Path.GetTempPath(), $"unused-{Guid.NewGuid():N}.db")}"),
            new PosDeviceCredentials(fixture.DeviceId, ServerSliceFixture.DeviceSecret),
                new PosOperationalScope(fixture.BusinessId, fixture.WarehouseId));
        var allowed = await sync.CheckAvailabilityAsync(
            new InventoryAvailabilityRequest(created.ProductId, fixture.WarehouseId, 1m, Guid.NewGuid()));
        Assert.False(allowed.ValidationRequired);
        Assert.True(allowed.IsAvailable);

        await ExecuteAsync(
            "UPDATE dbo.Warehouses SET AllowNegativeStockSales=0 WHERE WarehouseId=@Id;",
            new SqlParameter("@Id", fixture.WarehouseId));
        var blocked = await sync.CheckAvailabilityAsync(
            new InventoryAvailabilityRequest(created.ProductId, fixture.WarehouseId, 1m, Guid.NewGuid()));
        Assert.True(blocked.ValidationRequired);
        Assert.False(blocked.IsAvailable);
        await ExecuteAsync(
            "UPDATE dbo.Warehouses SET AllowNegativeStockSales=1 WHERE WarehouseId=@Id;",
            new SqlParameter("@Id", fixture.WarehouseId));
    }

    private async Task<(Guid Tax, Guid Channel, Guid Second)> ConfigureCatalogAsync()
    {
        var tax = fixture.TaxProfileId;
        var channel = fixture.PriceChannelId;
        var second = Guid.Parse("019ad230-6e45-7a28-a71e-25584f52bd65");
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.ProductUnits WHERE BusinessId=@Business AND Code=N'EA')
              INSERT dbo.ProductUnits(ProductUnitId,BusinessId,Code,Name,Symbol,AllowsFractionalQuantity,DecimalPlaces,IsActive,CreatedAt)
              VALUES(NEWID(),@Business,N'EA',N'Unidad',N'und',0,0,1,SYSDATETIMEOFFSET());
            IF NOT EXISTS (SELECT 1 FROM dbo.TaxProfiles WHERE TaxProfileId=@Tax)
              INSERT dbo.TaxProfiles(TaxProfileId,BusinessId,Code,Name,Rate,IsActive,CreatedAt)
              VALUES(@Tax,@Business,N'VAT19',N'IVA 19%',19,1,SYSDATETIMEOFFSET());
            IF NOT EXISTS (SELECT 1 FROM dbo.PriceChannels WHERE PriceChannelId=@Channel)
              INSERT dbo.PriceChannels(PriceChannelId,BusinessId,Code,Name,IsActive,CreatedAt)
              VALUES(@Channel,@Business,N'POS',N'POS',1,SYSDATETIMEOFFSET());
            IF NOT EXISTS (SELECT 1 FROM dbo.PriceChannels WHERE PriceChannelId=@Second)
              INSERT dbo.PriceChannels(PriceChannelId,BusinessId,Code,Name,IsActive,CreatedAt)
              VALUES(@Second,@Business,N'WHOLESALE',N'Wholesale',1,SYSDATETIMEOFFSET());
            IF NOT EXISTS (SELECT 1 FROM dbo.PosDevicePermissions WHERE DeviceId=@Device AND PermissionCode=@Permission)
              INSERT dbo.PosDevicePermissions(DeviceId,PermissionCode,IsGranted,GrantedAt)
              VALUES(@Device,@Permission,1,SYSDATETIMEOFFSET());
            """;
        await ExecuteAsync(
            sql,
            new SqlParameter("@Tax", tax),
            new SqlParameter("@Channel", channel),
            new SqlParameter("@Second", second),
            new SqlParameter("@Business", fixture.BusinessId),
            new SqlParameter("@Device", fixture.DeviceId),
            new SqlParameter("@Permission", CatalogPermissionCodes.Sync));
        return (tax, channel, second);
    }

    private SaveProductRequest ProductRequest(
        Guid taxProfileId,
        IReadOnlyCollection<ProductPriceInput> prices,
        IReadOnlyCollection<ProductBarcodeInput> barcodes) =>
        new(
            fixture.BusinessId,
            $"P-{Guid.NewGuid():N}",
            "REF-COFFEE",
            "Coffee 500 g",
            "Ground coffee",
            "EA",
            taxProfileId,
            true,
            false,
            barcodes,
            [new ProductIdentifierInput("Alternate", $"ALT-{Guid.NewGuid():N}")],
            prices,
            [new SupplierCostInput(Guid.Empty, $"SUP-{Guid.NewGuid():N}", "Supplier", null, 8_000m)],
            null);

    private async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        var value = (await command.ExecuteScalarAsync())!;
        return value is T typed ? typed : (T)Convert.ChangeType(value, typeof(T));
    }

    private static async Task<long> LocalScalarAsync(string path, string sql)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
