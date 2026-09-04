using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Domain.Enums;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class OnlineSalesDraftCommandTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Enrolled_pos_downloads_scoped_promotion_and_applies_it_from_sqlite_offline()
    {
        var promotionId = Guid.Empty;
        var productId = Guid.NewGuid();
        var taxProfileId = Guid.NewGuid();
        var sqlitePath = Path.Combine(
            Path.GetTempPath(), $"auraly-promotion-sync-{Guid.NewGuid():N}.db");
        var configurationCursor = await ScalarAsync<long>(
            "SELECT ISNULL(MAX(AvailableThroughCursor),0) FROM dbo.PosSynchronizationOutboxMessages WHERE BusinessId=@BusinessId AND Stream=N'Configuration';",
            new SqlParameter("@BusinessId", fixture.BusinessId));
        await ExecuteAsync(
            """
            INSERT dbo.TaxProfiles(TaxProfileId,BusinessId,Code,Name,Rate,IsActive,CreatedAt)
            VALUES(@TaxProfileId,@BusinessId,@TaxCode,N'Sin impuesto promoción offline',0,1,SYSDATETIMEOFFSET());
            INSERT dbo.Products(
              ProductId,TenantId,BusinessId,ProductCode,Reference,Sku,Name,
              BaseUnitCode,TaxProfileId,ManageStock,IsWeighable,IsActive,Source,Currency,CreatedAt)
            VALUES(
              @ProductId,@TenantId,@BusinessId,@ProductCode,@ProductCode,@ProductCode,
              N'Producto promoción offline',N'EA',@TaxProfileId,0,0,1,0,N'COP',SYSDATETIMEOFFSET());
            INSERT dbo.ProductPrices(
              ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,ValidFrom,
              RoundingIncrement,RoundingMode,IsActive,CreatedAt)
            VALUES(
              NEWID(),@BusinessId,@ProductId,10000,N'COP',DATEADD(day,-1,SYSDATETIMEOFFSET()),
              1,N'Nearest',1,SYSDATETIMEOFFSET());
            """,
            new("@TenantId", fixture.TenantId),
            new("@BusinessId", fixture.BusinessId), new("@ProductId", productId),
            new("@ProductCode", $"PO-{productId:N}"),
            new("@TaxProfileId", taxProfileId), new("@TaxCode", $"T-{taxProfileId:N}"[..32]));
        try
        {
            using var admin = fixture.CreateAdminClient(
                "promotions.create", "promotions.update", "promotions.delete");
            using var createdResponse = await admin.PostAsJsonAsync(
                $"/api/v1/businesses/{fixture.BusinessId:D}/promotions",
                new CreatePromotionRequest(
                    "Promoción descargable 10", null, true, null, null, 100, false, null,
                    [],
                    [new PromotionBenefitDto(
                        null, PromotionBenefitType.PercentageDiscount,
                        PromotionItemType.Product, productId, null, null,
                        10m, null, null, null)],
                    false, [fixture.BusinessId]));
            createdResponse.EnsureSuccessStatusCode();
            promotionId = (await createdResponse.Content.ReadFromJsonAsync<PromotionDto>())!.PromotionId;
            var createdCursor = await ScalarAsync<long>(
                "SELECT ISNULL(MAX(AvailableThroughCursor),0) FROM dbo.PosSynchronizationOutboxMessages WHERE BusinessId=@BusinessId AND Stream=N'Configuration';",
                new SqlParameter("@BusinessId", fixture.BusinessId));
            Assert.True(createdCursor > configurationCursor);

            var local = new PosCatalogStore($"Data Source={sqlitePath}");
            using (var server = fixture.CreateClient())
            {
                var synchronization = new PosCatalogSynchronizer(
                    server,
                    local,
                    new PosDeviceCredentials(
                        fixture.DeviceId, ServerSliceFixture.DeviceSecret),
                    new PosOperationalScope(fixture.BusinessId, fixture.WarehouseId));
                await synchronization.SynchronizeAsync();
            }

            var downloaded = await local.ReadPricingSnapshotAsync();
            Assert.Contains(
                downloaded.Promotions ?? [], item => item.PromotionId == promotionId);

            // From this point there is no server client: pricing is resolved only from SQLite.
            var resolved = await local.ResolvePriceAsync(productId, null, 1m);
            Assert.Equal(9_000m, resolved.Amount);
            Assert.Equal("Promotion", resolved.Source);
            Assert.Equal(promotionId, Assert.Single(resolved.PromotionIds!));

            using var updatedResponse = await admin.PutAsJsonAsync(
                $"/api/v1/businesses/{fixture.BusinessId:D}/promotions/{promotionId:D}",
                new UpdatePromotionRequest(
                    null, null, null, null, null, null, null, null, null,
                    [new PromotionBenefitDto(
                        null, PromotionBenefitType.PercentageDiscount,
                        PromotionItemType.Product, productId, null, null,
                        20m, null, null, null)]));
            updatedResponse.EnsureSuccessStatusCode();
            var updatedCursor = await ScalarAsync<long>(
                "SELECT ISNULL(MAX(AvailableThroughCursor),0) FROM dbo.PosSynchronizationOutboxMessages WHERE BusinessId=@BusinessId AND Stream=N'Configuration';",
                new SqlParameter("@BusinessId", fixture.BusinessId));
            Assert.True(updatedCursor > createdCursor);
            using (var server = fixture.CreateClient())
            {
                var synchronization = new PosCatalogSynchronizer(
                    server, local,
                    new PosDeviceCredentials(fixture.DeviceId, ServerSliceFixture.DeviceSecret),
                    new PosOperationalScope(fixture.BusinessId, fixture.WarehouseId));
                await synchronization.SynchronizeAsync();
            }
            Assert.Equal(8_000m, (await local.ResolvePriceAsync(productId, null, 1m)).Amount);

            using var deactivatedResponse = await admin.DeleteAsync(
                $"/api/v1/businesses/{fixture.BusinessId:D}/promotions/{promotionId:D}");
            Assert.Equal(HttpStatusCode.NoContent, deactivatedResponse.StatusCode);
            var deactivatedCursor = await ScalarAsync<long>(
                "SELECT ISNULL(MAX(AvailableThroughCursor),0) FROM dbo.PosSynchronizationOutboxMessages WHERE BusinessId=@BusinessId AND Stream=N'Configuration';",
                new SqlParameter("@BusinessId", fixture.BusinessId));
            Assert.True(deactivatedCursor > updatedCursor);
            using (var server = fixture.CreateClient())
            {
                var synchronization = new PosCatalogSynchronizer(
                    server, local,
                    new PosDeviceCredentials(fixture.DeviceId, ServerSliceFixture.DeviceSecret),
                    new PosOperationalScope(fixture.BusinessId, fixture.WarehouseId));
                await synchronization.SynchronizeAsync();
            }
            var withoutPromotion = await local.ResolvePriceAsync(productId, null, 1m);
            Assert.Equal(10_000m, withoutPromotion.Amount);
            Assert.Equal("Base", withoutPromotion.Source);
        }
        finally
        {
            if (promotionId != Guid.Empty)
                await ExecuteAsync(
                    "DELETE dbo.Promotions WHERE PromotionId=@PromotionId;",
                    new SqlParameter("@PromotionId", promotionId));
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var path in new[] { sqlitePath, sqlitePath + "-wal", sqlitePath + "-shm" })
                if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Promotion_scope_isolated_by_tenant_and_selected_business()
    {
        var userId = Guid.NewGuid();
        var otherBusinessId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var foreignBusinessId = Guid.NewGuid();
        var scopedPromotionId = Guid.NewGuid();
        var foreignPromotionId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
              FirstName,LastName,IsActive,CreatedAt)
            VALUES(@UserId,@TenantId,@Username,UPPER(@Username),CONCAT(@Username,N'@test.local'),
              UPPER(CONCAT(@Username,N'@test.local')),N'Scope',N'Promotion',1,SYSDATETIMEOFFSET());
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,IsActive,CreatedAt)
            VALUES(@OtherBusinessId,@TenantId,N'Otra sede',N'',N'',N'',CONCAT(@OtherBusinessId,N'@test.local'),N'',1,SYSUTCDATETIME());
            INSERT dbo.Tenants(TenantId,TenantKey,Name,Email,IsActive,CreatedAt)
            VALUES(@ForeignTenantId,CONCAT(N'@foreign-',REPLACE(CONVERT(NVARCHAR(36),@ForeignTenantId),N'-',N'')),
              N'Empresa ajena',CONCAT(@ForeignTenantId,N'@test.local'),1,SYSUTCDATETIME());
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,IsActive,CreatedAt)
            VALUES(@ForeignBusinessId,@ForeignTenantId,N'Sede ajena',N'',N'',N'',CONCAT(@ForeignBusinessId,N'@test.local'),N'',1,SYSUTCDATETIME());

            INSERT dbo.Promotions(PromotionId,TenantId,Name,IsActive,Priority,IsCombinable,CreatedAt)
            VALUES(@ScopedPromotionId,@TenantId,N'Solo otra sede',1,500,0,SYSUTCDATETIME());
            INSERT pricing.PromotionBusinessScopes(PromotionId,BusinessId,TenantId)
            VALUES(@ScopedPromotionId,@OtherBusinessId,@TenantId);
            INSERT dbo.PromotionBenefits(PromotionId,TenantId,BenefitType,TargetItemType,ProductId,DiscountPercentage,CreatedAt)
            VALUES(@ScopedPromotionId,@TenantId,0,1,@ProductId,50,SYSUTCDATETIME());

            INSERT dbo.Promotions(PromotionId,TenantId,Name,IsActive,Priority,IsCombinable,AppliesToAllBusinesses,CreatedAt)
            VALUES(@ForeignPromotionId,@ForeignTenantId,N'Promoción ajena',1,1000,0,1,SYSUTCDATETIME());
            INSERT dbo.PromotionBenefits(PromotionId,TenantId,BenefitType,TargetItemType,ProductId,DiscountPercentage,CreatedAt)
            VALUES(@ForeignPromotionId,@ForeignTenantId,0,1,@ProductId,90,SYSUTCDATETIME());
            """,
            new("@UserId", userId), new("@TenantId", fixture.TenantId),
            new("@Username", $"scope-{userId:N}"), new("@OtherBusinessId", otherBusinessId),
            new("@ForeignTenantId", foreignTenantId), new("@ForeignBusinessId", foreignBusinessId),
            new("@ScopedPromotionId", scopedPromotionId), new("@ForeignPromotionId", foreignPromotionId),
            new("@BusinessId", fixture.BusinessId), new("@ProductId", fixture.ProductId));

        using var client = fixture.CreateUserClient(
            userId, CommercePermissionCodes.SalesCreate, WorkSessionPermissionCodes.Open,
            "promotions.read");
        using var listResponse = await client.GetAsync(
            $"/api/v1/businesses/{fixture.BusinessId:D}/promotions?page=1&pageSize=100");
        listResponse.EnsureSuccessStatusCode();
        var listed = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var listedIds = listed.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("promotionId").GetGuid())
            .ToArray();
        Assert.Contains(scopedPromotionId, listedIds);
        Assert.DoesNotContain(foreignPromotionId, listedIds);

        var workSession = await fixture.OpenWorkSessionAsync(client);
        var context = new OnlineSalesDraftContext(
            fixture.BusinessId, fixture.WarehouseId, workSession.WorkSessionId);

        var isolated = await SearchAsync(client, context);
        Assert.Equal(10_000m, isolated.UnitPrice);
        Assert.Equal("Base", isolated.PriceSource);

        await ExecuteAsync(
            "INSERT pricing.PromotionBusinessScopes(PromotionId,BusinessId,TenantId) VALUES(@PromotionId,@BusinessId,@TenantId);",
            new("@PromotionId", scopedPromotionId), new("@BusinessId", fixture.BusinessId),
            new("@TenantId", fixture.TenantId));
        var included = await SearchAsync(client, context);
        Assert.Equal(5_000m, included.UnitPrice);
        Assert.Equal("Promotion", included.PriceSource);

        await ExecuteAsync(
            "DELETE dbo.Promotions WHERE PromotionId IN (@ScopedPromotionId,@ForeignPromotionId);",
            new("@ScopedPromotionId", scopedPromotionId), new("@ForeignPromotionId", foreignPromotionId));
    }

    [Fact]
    public async Task Online_capture_uses_customer_pricing_and_supports_line_commands()
    {
        var userId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var priceChannelId = Guid.NewGuid();
        var priceChannelItemId = Guid.NewGuid();
        var volumePriceChannelItemId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var subgroupId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var taxProfileId = Guid.NewGuid();
        var categoryExclusionId = Guid.NewGuid();
        var barcodeId = Guid.NewGuid();
        var promotionId = Guid.NewGuid();
        var promotionConditionId = Guid.NewGuid();
        var promotionBenefitId = Guid.NewGuid();
        var barcode = $"770{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}";
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,
              IsActive,CreatedAt)
            VALUES(
              @UserId,@TenantId,@Username,@NormalizedUsername,
              CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),N'Venta',N'Online',
              1,SYSDATETIMEOFFSET());
            INSERT dbo.Parties(
              PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,
              CreatedBy,CreatedAt)
            VALUES(
              @PartyId,@TenantId,N'NaturalPerson',N'Cliente canal online',
              N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.Customers(
              CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
            VALUES(
              @CustomerId,@PartyId,@BusinessId,1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.PriceChannels(
              PriceChannelId,BusinessId,Code,Name,Strategy,IsActive,CreatedAt)
            VALUES(
              @PriceChannelId,@BusinessId,@ChannelCode,N'Canal online',N'TieredProductPrice',1,SYSDATETIMEOFFSET());
            INSERT dbo.PriceChannelItems(
              PriceChannelItemId,PriceChannelId,ProductId,MinimumQuantity,Amount,
              CurrencyCode,ValidFrom,ValidUntil,IsActive,CreatedAt)
            VALUES(
              @PriceChannelItemId,@PriceChannelId,@ProductId,1,8000,N'COP',
              DATEADD(day,-2,SYSDATETIMEOFFSET()),DATEADD(day,-1,SYSDATETIMEOFFSET()),1,SYSDATETIMEOFFSET());
            INSERT dbo.PriceChannelItems(
              PriceChannelItemId,PriceChannelId,ProductId,MinimumQuantity,Amount,
              CurrencyCode,ValidFrom,IsActive,CreatedAt)
            VALUES(
              @VolumePriceChannelItemId,@PriceChannelId,@ProductId,3,7000,N'COP',
              DATEADD(day,-1,SYSDATETIMEOFFSET()),1,SYSDATETIMEOFFSET());
            INSERT dbo.CustomerPricingSettings(
              CustomerId,PriceChannelId,UpdatedBy,UpdatedAt)
            VALUES(
              @CustomerId,@PriceChannelId,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.ProductCategories(ProductCategoryId,BusinessId,Name,CreatedAt)
            VALUES(@AreaId,@BusinessId,N'Área excluible',SYSUTCDATETIME());
            INSERT dbo.ProductCategories(
              ProductCategoryId,BusinessId,ParentProductCategoryId,Name,CreatedAt)
            VALUES(@SubgroupId,@BusinessId,@AreaId,N'Subgrupo excluible',SYSUTCDATETIME());
            INSERT dbo.ProductBrands(ProductBrandId,BusinessId,Name,IsActive,CreatedAt)
            VALUES(@BrandId,@BusinessId,N'Marca excluible',1,SYSDATETIMEOFFSET());
            INSERT dbo.TaxProfiles(TaxProfileId,BusinessId,Code,Name,Rate,IsActive,CreatedAt)
            VALUES(@TaxProfileId,@BusinessId,@TaxCode,N'IVA prueba canal',0,1,SYSDATETIMEOFFSET());
            UPDATE dbo.Products
            SET ProductCode=N'P-E2E',BaseUnitCode=N'EA',TaxProfileId=@TaxProfileId,
                ProductCategoryId=@SubgroupId, ProductBrandId=@BrandId
            WHERE ProductId=@ProductId AND BusinessId=@BusinessId;
            INSERT dbo.ProductBarcodes(
              ProductBarcodeId,BusinessId,ProductId,Barcode,IsPrimary,IsActive,CreatedAt)
            VALUES(
              @BarcodeId,@BusinessId,@ProductId,@Barcode,1,1,SYSDATETIMEOFFSET());
            UPDATE dbo.Tenants SET AllowPromotionChannelCombination=0 WHERE TenantId=@TenantId;
            INSERT dbo.Promotions(PromotionId,TenantId,Name,IsActive,Priority,IsCombinable,CreatedAt)
            VALUES(@PromotionId,@TenantId,N'Promoción online 10',1,100,0,SYSUTCDATETIME());
            INSERT pricing.PromotionBusinessScopes(PromotionId,BusinessId,TenantId)
            VALUES(@PromotionId,@BusinessId,@TenantId);
            INSERT dbo.PromotionConditions(
              PromotionConditionId,PromotionId,TenantId,ItemType,ProductId,MinQuantity,CreatedAt)
            VALUES(@PromotionConditionId,@PromotionId,@TenantId,1,@ProductId,1,SYSUTCDATETIME());
            INSERT dbo.PromotionBenefits(
              PromotionBenefitId,PromotionId,TenantId,BenefitType,TargetItemType,ProductId,
              DiscountPercentage,CreatedAt)
            VALUES(@PromotionBenefitId,@PromotionId,@TenantId,0,1,@ProductId,10,SYSUTCDATETIME());
            """,
            new("@UserId", userId),
            new("@TenantId", fixture.TenantId),
            new("@Username", $"online-{userId:N}"),
            new("@NormalizedUsername", $"ONLINE-{userId:N}".ToUpperInvariant()),
            new("@PartyId", partyId),
            new("@CustomerId", customerId),
            new("@BusinessId", fixture.BusinessId),
            new("@PriceChannelId", priceChannelId),
            new("@ChannelCode", $"C-{priceChannelId:N}"[..20]),
            new("@PriceChannelItemId", priceChannelItemId),
            new("@VolumePriceChannelItemId", volumePriceChannelItemId),
            new("@AreaId", areaId),
            new("@SubgroupId", subgroupId),
            new("@BrandId", brandId),
            new("@TaxProfileId", taxProfileId),
            new("@TaxCode", $"C-{taxProfileId:N}"[..16]),
            new("@ProductId", fixture.ProductId),
            new("@BarcodeId", barcodeId),
            new("@Barcode", barcode),
            new("@PromotionId", promotionId),
            new("@PromotionConditionId", promotionConditionId),
            new("@PromotionBenefitId", promotionBenefitId));

        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            CommercePermissionCodes.SalesDiscount,
            CommercePermissionCodes.SalesRemoveLine,
            "orders.create",
            WorkSessionPermissionCodes.Open);
        var workSession = await fixture.OpenWorkSessionAsync(client);
        var draft = await OpenAsync(client, workSession.WorkSessionId);
        var context = new OnlineSalesDraftContext(
            fixture.BusinessId, fixture.WarehouseId, workSession.WorkSessionId);

        using (var searchResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/pos/drafts/products/search",
                   new SearchOnlineSalesRequest(context, "P-E2E", 0, 50, customerId)))
        {
            searchResponse.EnsureSuccessStatusCode();
            var products = await searchResponse.Content.ReadFromJsonAsync<OnlineSalesProductPage>();
            var product = Assert.Single(products!.Items, item => item.ProductId == fixture.ProductId);
            Assert.Equal(9_000m, product.UnitPrice);
            Assert.Equal("Promotion", product.PriceSource);
        }

        await ExecuteAsync(
            "UPDATE dbo.Tenants SET AllowPromotionChannelCombination=1 WHERE TenantId=@TenantId;",
            new SqlParameter("@TenantId", fixture.TenantId));
        using (var combinedSearchResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/pos/drafts/products/search",
                   new SearchOnlineSalesRequest(context, "P-E2E", 0, 50, customerId)))
        {
            combinedSearchResponse.EnsureSuccessStatusCode();
            var products = await combinedSearchResponse.Content.ReadFromJsonAsync<OnlineSalesProductPage>();
            var product = Assert.Single(products!.Items, item => item.ProductId == fixture.ProductId);
            Assert.Equal(7_200m, product.UnitPrice);
            Assert.Equal("Promotion+PriceChannel", product.PriceSource);
        }
        await ExecuteAsync(
            "DELETE dbo.Promotions WHERE PromotionId=@PromotionId; UPDATE dbo.Tenants SET AllowPromotionChannelCombination=0 WHERE TenantId=@TenantId;",
            new SqlParameter("@PromotionId", promotionId),
            new SqlParameter("@TenantId", fixture.TenantId));
        using (var channelSearchResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/pos/drafts/products/search",
                   new SearchOnlineSalesRequest(context, "P-E2E", 0, 50, customerId)))
        {
            channelSearchResponse.EnsureSuccessStatusCode();
            var products = await channelSearchResponse.Content.ReadFromJsonAsync<OnlineSalesProductPage>();
            var product = Assert.Single(products!.Items, item => item.ProductId == fixture.ProductId);
            Assert.Equal(8_000m, product.UnitPrice);
            Assert.Equal("PriceChannel", product.PriceSource);
        }

        using (var catalogResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/seller-orders/catalog",
                   new { businessId = fixture.BusinessId, warehouseId = fixture.WarehouseId,
                       customerId, search = "P-E2E", skip = 0, take = 50 }))
        {
            catalogResponse.EnsureSuccessStatusCode();
            var catalog = await catalogResponse.Content.ReadFromJsonAsync<JsonElement>();
            var product = Assert.Single(catalog.GetProperty("items").EnumerateArray()
                .Where(item => item.GetProperty("productId").GetGuid() == fixture.ProductId));
            Assert.Equal(8_000m, product.GetProperty("unitPrice").GetDecimal());
            Assert.Equal("PriceChannel", product.GetProperty("priceSource").GetString());
        }

        await ExecuteAsync(
            "UPDATE dbo.PriceChannels SET Strategy=N'ProductMarginAdjustment',Value=10 WHERE PriceChannelId=@PriceChannelId;",
            new SqlParameter("@PriceChannelId", priceChannelId));
        decimal salesAdjustmentPrice;
        using (var adjustedSearchResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/pos/drafts/products/search",
                   new SearchOnlineSalesRequest(context, "P-E2E", 0, 50, customerId)))
        {
            adjustedSearchResponse.EnsureSuccessStatusCode();
            var products = await adjustedSearchResponse.Content.ReadFromJsonAsync<OnlineSalesProductPage>();
            var product = Assert.Single(products!.Items, item => item.ProductId == fixture.ProductId);
            salesAdjustmentPrice = product.UnitPrice;
            Assert.True(salesAdjustmentPrice > 0);
            Assert.Equal("PriceChannel", product.PriceSource);
        }
        using (var adjustedCatalogResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/seller-orders/catalog",
                   new { businessId = fixture.BusinessId, warehouseId = fixture.WarehouseId,
                       customerId, search = "P-E2E", skip = 0, take = 50 }))
        {
            adjustedCatalogResponse.EnsureSuccessStatusCode();
            var catalog = await adjustedCatalogResponse.Content.ReadFromJsonAsync<JsonElement>();
            var product = Assert.Single(catalog.GetProperty("items").EnumerateArray()
                .Where(item => item.GetProperty("productId").GetGuid() == fixture.ProductId));
            Assert.Equal(salesAdjustmentPrice, product.GetProperty("unitPrice").GetDecimal());
            Assert.Equal("PriceChannel", product.GetProperty("priceSource").GetString());
        }
        await ExecuteAsync(
            "UPDATE dbo.PriceChannels SET Strategy=N'TieredProductPrice',Value=NULL WHERE PriceChannelId=@PriceChannelId;",
            new SqlParameter("@PriceChannelId", priceChannelId));

        await ExecuteAsync(
            """
            INSERT dbo.PriceChannelExclusions(
              PriceChannelExclusionId,PriceChannelId,ScopeType,ProductCategoryId,CreatedAt)
            VALUES(@ExclusionId,@PriceChannelId,N'Category',@AreaId,SYSDATETIMEOFFSET());
            """,
            new("@ExclusionId", categoryExclusionId),
            new("@PriceChannelId", priceChannelId),
            new("@AreaId", areaId));

        using (var excludedSearchResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/pos/drafts/products/search",
                   new SearchOnlineSalesRequest(context, "P-E2E", 0, 50, customerId)))
        {
            excludedSearchResponse.EnsureSuccessStatusCode();
            var products = await excludedSearchResponse.Content.ReadFromJsonAsync<OnlineSalesProductPage>();
            var product = Assert.Single(products!.Items, item => item.ProductId == fixture.ProductId);
            Assert.Equal(10_000m, product.UnitPrice);
            Assert.Equal("Base", product.PriceSource);
        }

        using (var excludedCatalogResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/seller-orders/catalog",
                   new { businessId = fixture.BusinessId, warehouseId = fixture.WarehouseId,
                       customerId, search = "P-E2E", skip = 0, take = 50 }))
        {
            excludedCatalogResponse.EnsureSuccessStatusCode();
            var catalog = await excludedCatalogResponse.Content.ReadFromJsonAsync<JsonElement>();
            var product = Assert.Single(catalog.GetProperty("items").EnumerateArray()
                .Where(item => item.GetProperty("productId").GetGuid() == fixture.ProductId));
            Assert.Equal(10_000m, product.GetProperty("unitPrice").GetDecimal());
            Assert.Equal("Base", product.GetProperty("priceSource").GetString());
        }

        var localPath = Path.Combine(Path.GetTempPath(),$"auraly-channel-rules-{Guid.NewGuid():N}.db");
        try
        {
            var local = new PosCatalogStore($"Data Source={localPath}");
            using (var server = fixture.CreateClient())
                await new PosCatalogSynchronizer(
                    server,local,
                    new PosDeviceCredentials(fixture.DeviceId,ServerSliceFixture.DeviceSecret),
                    new PosOperationalScope(fixture.BusinessId,fixture.WarehouseId))
                    .SynchronizeAsync();

            var downloaded = await local.ReadPricingSnapshotAsync();
            Assert.Single(downloaded.PriceChannels,value => value.PriceChannelId==priceChannelId);
            Assert.Equal(2,downloaded.PriceChannelTiers.Count(value => value.PriceChannelId==priceChannelId));
            Assert.Contains(downloaded.PriceChannelExclusions,value =>
                value.PriceChannelId==priceChannelId && value.ProductCategoryId==areaId);
            var excludedOffline = await local.ResolvePriceAsync(fixture.ProductId,customerId,1m);
            Assert.Equal(10_000m,excludedOffline.Amount);
            Assert.Equal("Base",excludedOffline.Source);

            await ExecuteAsync(
                "DELETE dbo.PriceChannelExclusions WHERE PriceChannelExclusionId=@ExclusionId;",
                new SqlParameter("@ExclusionId", categoryExclusionId));
            using (var server = fixture.CreateClient())
                await new PosCatalogSynchronizer(
                    server,local,
                    new PosDeviceCredentials(fixture.DeviceId,ServerSliceFixture.DeviceSecret),
                    new PosOperationalScope(fixture.BusinessId,fixture.WarehouseId))
                    .SynchronizeAsync();
            var oneOffline = await local.ResolvePriceAsync(fixture.ProductId,customerId,1m);
            var threeOffline = await local.ResolvePriceAsync(fixture.ProductId,customerId,3m);
            Assert.Equal(8_000m,oneOffline.Amount);
            Assert.Equal(7_000m,threeOffline.Amount);
            Assert.All(new[] { oneOffline,threeOffline },value => Assert.Equal("PriceChannel",value.Source));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var path in new[] { localPath,localPath+"-wal",localPath+"-shm" })
                if (File.Exists(path)) File.Delete(path);
        }

        var captured = await MutateAsync<OnlineSalesDraft>(
            client,
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items",
            new AddOnlineSalesDraftItemRequest(barcode, 1m, draft.Version));
        var baseLine = Assert.Single(captured.Lines);
        Assert.Equal(10_000m, baseLine.UnitPrice);
        Assert.Equal("Base", baseLine.PriceSource);

        var selected = await MutateAsync<OnlineSalesCustomerSelection>(
            client,
            HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/customer",
            new SelectOnlineSalesDraftCustomerRequest(customerId, captured.Version));
        Assert.NotNull(selected.Customer);
        Assert.Equal(customerId, selected.Customer.CustomerId);
        var customerLine = Assert.Single(selected.Draft.Lines);
        Assert.Equal(8_000m, customerLine.UnitPrice);
        Assert.Equal("PriceChannel", customerLine.PriceSource);

        var discounted = await MutateAsync<OnlineSalesDraft>(
            client,
            HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/lines/{customerLine.LineId:D}/discount",
            new SetOnlineSalesDraftDiscountRequest(1_000m, selected.Draft.Version));
        Assert.Equal(7_000m, discounted.PayableAmount);

        var changed = await MutateAsync<OnlineSalesDraft>(
            client,
            HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/lines/{customerLine.LineId:D}/quantity",
            new ChangeOnlineSalesDraftQuantityRequest(2m, discounted.Version));
        Assert.Equal(15_000m, changed.PayableAmount);

        var withAnotherLine = await MutateAsync<OnlineSalesDraft>(
            client,
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items",
            new AddOnlineSalesDraftItemRequest(barcode, 1m, changed.Version));
        Assert.Equal(2, withAnotherLine.Lines.Count);
        Assert.Equal(7_000m, withAnotherLine.Lines.Single(line => line.LineId == customerLine.LineId).UnitPrice);
        Assert.Equal(7_000m, withAnotherLine.Lines.Single(line => line.LineId != customerLine.LineId).UnitPrice);
        Assert.Equal(20_000m, withAnotherLine.PayableAmount);

        var removed = await MutateAsync<OnlineSalesDraft>(
            client,
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/lines/{customerLine.LineId:D}/remove",
            new RemoveOnlineSalesDraftLineRequest(withAnotherLine.Version));
        var remaining = Assert.Single(removed.Lines);
        Assert.Equal(8_000m, remaining.UnitPrice);
        Assert.Equal(8_000m, removed.PayableAmount);

        var emptied = await MutateAsync<OnlineSalesDraft>(
            client,
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/lines/{remaining.LineId:D}/remove",
            new RemoveOnlineSalesDraftLineRequest(removed.Version));
        Assert.Empty(emptied.Lines);
        Assert.Equal(0m, emptied.PayableAmount);
    }

    [Fact]
    public async Task Online_draft_reprices_when_buy_two_get_one_enters_and_leaves_eligibility()
    {
        var userId = Guid.NewGuid();
        var promotionId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,
              IsActive,CreatedAt)
            VALUES(@UserId,@TenantId,@Username,UPPER(@Username),CONCAT(@Username,N'@test.local'),
              UPPER(CONCAT(@Username,N'@test.local')),N'Promo',N'Online',1,SYSDATETIMEOFFSET());
            UPDATE dbo.Tenants SET AllowPromotionChannelCombination=0 WHERE TenantId=@TenantId;
            INSERT dbo.Promotions(PromotionId,TenantId,Name,IsActive,Priority,IsCombinable,CreatedAt)
            VALUES(@PromotionId,@TenantId,N'Compra dos y recibe uno',1,100,0,SYSUTCDATETIME());
            INSERT pricing.PromotionBusinessScopes(PromotionId,BusinessId,TenantId)
            VALUES(@PromotionId,@BusinessId,@TenantId);
            INSERT dbo.PromotionConditions(PromotionId,TenantId,ItemType,ProductId,MinQuantity,CreatedAt)
            VALUES(@PromotionId,@TenantId,1,@ProductId,3,SYSUTCDATETIME());
            INSERT dbo.PromotionBenefits(
              PromotionId,TenantId,BenefitType,TargetItemType,ProductId,AppliesToQuantity,CreatedAt)
            VALUES(@PromotionId,@TenantId,3,1,@ProductId,1,SYSUTCDATETIME());
            """,
            new("@UserId", userId), new("@TenantId", fixture.TenantId),
            new("@Username", $"promo-{userId:N}"), new("@PromotionId", promotionId),
            new("@BusinessId", fixture.BusinessId), new("@ProductId", fixture.ProductId));
        try
        {
            using var client = fixture.CreateUserClient(
                userId, CommercePermissionCodes.SalesCreate, WorkSessionPermissionCodes.Open);
            var session = await fixture.OpenWorkSessionAsync(client);
            var draft = await OpenAsync(client, session.WorkSessionId);
            var captured = await MutateAsync<OnlineSalesDraft>(
                client, HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items",
                new AddOnlineSalesDraftItemRequest("P-E2E", 1m, draft.Version));
            var line = Assert.Single(captured.Lines);
            Assert.Equal(10_000m, line.UnitPrice);

            var eligible = await MutateAsync<OnlineSalesDraft>(
                client, HttpMethod.Put,
                $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/lines/{line.LineId:D}/quantity",
                new ChangeOnlineSalesDraftQuantityRequest(3m, captured.Version));
            var promoted = Assert.Single(eligible.Lines);
            Assert.Equal(20_000m, eligible.PayableAmount, 2);
            Assert.Equal("Promotion", promoted.PriceSource);

            var noLongerEligible = await MutateAsync<OnlineSalesDraft>(
                client, HttpMethod.Put,
                $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/lines/{line.LineId:D}/quantity",
                new ChangeOnlineSalesDraftQuantityRequest(2m, eligible.Version));
            Assert.Equal(20_000m, noLongerEligible.PayableAmount, 2);
            Assert.Equal("Base", Assert.Single(noLongerEligible.Lines).PriceSource);
        }
        finally
        {
            await ExecuteAsync(
                "DELETE dbo.Promotions WHERE PromotionId=@PromotionId;",
                new SqlParameter("@PromotionId", promotionId));
        }
    }

    [Fact]
    public async Task Online_capture_respects_warehouse_negative_policy()
    {
        var userId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,
              IsActive,CreatedAt)
            VALUES(
              @UserId,@TenantId,@Username,@NormalizedUsername,
              CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),N'Venta',N'Inventario',
              1,SYSDATETIMEOFFSET());
            UPDATE dbo.Warehouses
            SET AllowNegativeStockSales=1
            WHERE WarehouseId=@WarehouseId;
            """,
            new("@UserId", userId),
            new("@TenantId", fixture.TenantId),
            new("@Username", $"stock-{userId:N}"),
            new("@NormalizedUsername", $"STOCK-{userId:N}".ToUpperInvariant()),
            new("@WarehouseId", fixture.WarehouseId));
        try
        {
            using var client = fixture.CreateUserClient(
                userId,
                CommercePermissionCodes.SalesCreate,
                WorkSessionPermissionCodes.Open);
            var workSession = await fixture.OpenWorkSessionAsync(client);
            var draft = await OpenAsync(client, workSession.WorkSessionId);
            var captured = await MutateAsync<OnlineSalesDraft>(
                client,
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items",
                new AddOnlineSalesDraftItemRequest("P-E2E", 1m, draft.Version));
            var line = Assert.Single(captured.Lines);
            await ExecuteAsync(
                "UPDATE dbo.Warehouses SET AllowNegativeStockSales=0 WHERE WarehouseId=@WarehouseId;",
                new SqlParameter("@WarehouseId", fixture.WarehouseId));
            using var request = Mutation(
                HttpMethod.Put,
                $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/lines/{line.LineId:D}/quantity",
                new ChangeOnlineSalesDraftQuantityRequest(999_999m, captured.Version));
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadAsStringAsync();
            Assert.Contains("Inventario insuficiente", problem, StringComparison.Ordinal);
            Assert.Contains("Disponible:", problem, StringComparison.Ordinal);
            var unchanged = await OpenAsync(client, workSession.WorkSessionId);
            Assert.Equal(1m, Assert.Single(unchanged.Lines).Quantity);
        }
        finally
        {
            await ExecuteAsync(
                """
                UPDATE dbo.Warehouses
                SET AllowNegativeStockSales=1
                WHERE WarehouseId=@WarehouseId;
                """,
                new SqlParameter("@WarehouseId", fixture.WarehouseId));
        }
    }

    [Fact]
    public async Task Online_capture_does_not_validate_inventory_for_non_stock_products()
    {
        var userId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,
              IsActive,CreatedAt)
            VALUES(
              @UserId,@TenantId,@Username,@NormalizedUsername,
              CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),N'Venta',N'Sin inventario',
              1,SYSDATETIMEOFFSET());
            UPDATE dbo.Warehouses SET AllowNegativeStockSales=0 WHERE WarehouseId=@WarehouseId;
            UPDATE dbo.Products SET ManageStock=0 WHERE ProductId=@ProductId;
            """,
            new("@UserId", userId),
            new("@TenantId", fixture.TenantId),
            new("@Username", $"no-stock-{userId:N}"),
            new("@NormalizedUsername", $"NO-STOCK-{userId:N}".ToUpperInvariant()),
            new("@WarehouseId", fixture.WarehouseId),
            new("@ProductId", fixture.ProductId));
        try
        {
            using var client = fixture.CreateUserClient(
                userId, CommercePermissionCodes.SalesCreate, WorkSessionPermissionCodes.Open);
            var workSession = await fixture.OpenWorkSessionAsync(client);
            var draft = await OpenAsync(client, workSession.WorkSessionId);
            var captured = await MutateAsync<OnlineSalesDraft>(
                client,
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items",
                new AddOnlineSalesDraftItemRequest("P-E2E", 999_999m, draft.Version));
            var line = Assert.Single(captured.Lines);
            Assert.Equal(999_999m, line.Quantity);

            var changed = await MutateAsync<OnlineSalesDraft>(
                client,
                HttpMethod.Put,
                $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/lines/{line.LineId:D}/quantity",
                new ChangeOnlineSalesDraftQuantityRequest(1_000_000m, captured.Version));
            Assert.Equal(1_000_000m, Assert.Single(changed.Lines).Quantity);
        }
        finally
        {
            await ExecuteAsync(
                """
                UPDATE dbo.Products SET ManageStock=1 WHERE ProductId=@ProductId;
                UPDATE dbo.Warehouses SET AllowNegativeStockSales=1 WHERE WarehouseId=@WarehouseId;
                """,
                new("@ProductId", fixture.ProductId),
                new("@WarehouseId", fixture.WarehouseId));
        }
    }

    [Fact]
    public async Task Online_capture_allows_decimals_only_for_fractional_products()
    {
        var userId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,
              IsActive,CreatedAt)
            VALUES(
              @UserId,@TenantId,@Username,@NormalizedUsername,
              CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),N'Venta',N'Fraccionada',
              1,SYSDATETIMEOFFSET());
            UPDATE dbo.Products SET AllowsFractionalSale=0 WHERE ProductId=@ProductId;
            """,
            new("@UserId", userId), new("@TenantId", fixture.TenantId),
            new("@Username", $"fraction-{userId:N}"),
            new("@NormalizedUsername", $"FRACTION-{userId:N}".ToUpperInvariant()),
            new("@ProductId", fixture.ProductId));
        try
        {
            using var client = fixture.CreateUserClient(
                userId, CommercePermissionCodes.SalesCreate, WorkSessionPermissionCodes.Open);
            var workSession = await fixture.OpenWorkSessionAsync(client);
            var draft = await OpenAsync(client, workSession.WorkSessionId);
            using var rejectedRequest = Mutation(
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items",
                new AddOnlineSalesDraftItemRequest("P-E2E", 1.5m, draft.Version));
            using var rejected = await client.SendAsync(rejectedRequest);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

            await ExecuteAsync(
                "UPDATE dbo.Products SET AllowsFractionalSale=1 WHERE ProductId=@ProductId;",
                new SqlParameter("@ProductId", fixture.ProductId));
            var accepted = await MutateAsync<OnlineSalesDraft>(
                client,
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items",
                new AddOnlineSalesDraftItemRequest("P-E2E", 1.5m, draft.Version));
            Assert.Equal(1.5m, Assert.Single(accepted.Lines).Quantity);
        }
        finally
        {
            await ExecuteAsync(
                "UPDATE dbo.Products SET AllowsFractionalSale=0 WHERE ProductId=@ProductId;",
                new SqlParameter("@ProductId", fixture.ProductId));
        }
    }

    private async Task<OnlineSalesDraft> OpenAsync(HttpClient client, Guid workSessionId)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/drafts/active",
            new OpenOnlineSalesDraftRequest(new(
                fixture.BusinessId,
                fixture.WarehouseId,
                workSessionId)));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("Empty draft response.");
    }

    private async Task<OnlineSalesProduct> SearchAsync(
        HttpClient client,
        OnlineSalesDraftContext context)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/drafts/products/search",
            new SearchOnlineSalesRequest(context, "P-E2E", 0, 50));
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<OnlineSalesProductPage>()
            ?? throw new InvalidOperationException("Empty product search response.");
        return Assert.Single(page.Items, item => item.ProductId == fixture.ProductId);
    }

    private static async Task<T> MutateAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        object body)
    {
        using var request = Mutation(method, path, body);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"{(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException("Empty mutation response.");
    }

    private static HttpRequestMessage Mutation(
        HttpMethod method,
        string path,
        object body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return request;
    }

    private async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Expected SQL scalar was not returned.");
        return (T)Convert.ChangeType(value, typeof(T));
    }
}
