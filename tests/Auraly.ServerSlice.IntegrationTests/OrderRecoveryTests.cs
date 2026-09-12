using System.Net.Http.Json;
using Auraly.Api;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Orders;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class OrderRecoveryTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task User_with_create_permission_can_create_an_order_without_being_a_commercial_seller()
    {
        var userId = Guid.NewGuid();
        var customerPartyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var taxProfileId = Guid.NewGuid();
        await EnsureOrdersWarehouseAsync();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
              FirstName,LastName,IsActive,CreatedAt)
            VALUES(@UserId,@TenantId,@Username,UPPER(@Username),
              CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),
              N'Administrador',N'Pedidos',1,SYSDATETIMEOFFSET());
            INSERT dbo.Parties(
              PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES(@CustomerPartyId,@TenantId,N'Organization',N'Cliente pedido administrativo',
              N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.Customers(CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
            VALUES(@CustomerId,@CustomerPartyId,@BusinessId,1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.TaxProfiles(TaxProfileId,BusinessId,Code,Name,Rate,IsActive,CreatedAt)
            VALUES(@TaxProfileId,@BusinessId,@TaxCode,N'Sin impuesto pedido administrativo',0,1,SYSDATETIMEOFFSET());
            INSERT dbo.Products(
              ProductId,TenantId,BusinessId,ProductCode,Sku,Name,BaseUnitCode,TaxProfileId,
              ManageStock,IsWeighable,IsActive,Source,Currency,CreatedAt)
            VALUES(@ProductId,@TenantId,@BusinessId,@ProductCode,@ProductCode,
              N'Producto pedido administrativo',N'EA',@TaxProfileId,0,0,1,0,N'COP',SYSDATETIMEOFFSET());
            INSERT dbo.ProductPrices(
              ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,ValidFrom,
              RoundingIncrement,RoundingMode,IsActive,CreatedAt)
            VALUES(NEWID(),@BusinessId,@ProductId,12500,N'COP',
              DATEADD(day,-1,SYSDATETIMEOFFSET()),1,N'Nearest',1,SYSDATETIMEOFFSET());
            """,
            new("@UserId", userId), new("@TenantId", fixture.TenantId),
            new("@Username", $"admin-order-{userId:N}"),
            new("@CustomerPartyId", customerPartyId), new("@CustomerId", customerId),
            new("@BusinessId", fixture.BusinessId), new("@ProductId", productId),
            new("@TaxProfileId", taxProfileId), new("@TaxCode", $"OA-{taxProfileId:N}"[..32]),
            new("@ProductCode", $"ADM-{productId:N}"[..24]));

        using var client = fixture.CreateUserClient(userId, OrderPermissionCodes.Create);
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/seller-orders",
            new
            {
                businessId = fixture.BusinessId,
                warehouseId = fixture.WarehouseId,
                customerId,
                partySiteId = (Guid?)null,
                routeId = (Guid?)null,
                routeStopId = (Guid?)null,
                capturedOffline = false,
                notes = "Creado por permiso sin rol comercial",
                idempotencyKey = Guid.NewGuid().ToString("N"),
                lines = new[]
                {
                    new
                    {
                        productId,
                        quantity = 1m,
                        unitPrice = 12500m,
                        discountAmount = 0m,
                        priceSource = "Public"
                    }
                }
            });

        Assert.True(response.IsSuccessStatusCode,
            $"El pedido respondió {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var orderId = body.GetProperty("orderId").GetGuid();

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CapturedByUserId,SellerId
            FROM dbo.Orders
            WHERE OrderId=@OrderId AND BusinessId=@BusinessId;
            """;
        command.Parameters.AddWithValue("@OrderId", orderId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(userId, reader.GetGuid(0));
        Assert.True(reader.IsDBNull(1));
    }

    [Fact]
    public async Task Seller_order_creation_preserves_the_prices_captured_by_the_shared_resolver()
    {
        var userId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var sellerPartyId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var firstProductId = Guid.NewGuid();
        var secondProductId = Guid.NewGuid();
        var taxProfileId = Guid.NewGuid();
        var promotionId = Guid.NewGuid();
        await EnsureOrdersWarehouseAsync();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
              FirstName,LastName,IsActive,CreatedAt)
            VALUES(@UserId,@TenantId,@Username,UPPER(@Username),
              CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),
              N'Precio',N'Pedido',1,SYSDATETIMEOFFSET());
            INSERT dbo.Parties(
              PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES
              (@PartyId,@TenantId,N'Organization',N'Cliente precios pedido',
               N'Incomplete',1,@UserId,SYSDATETIMEOFFSET()),
              (@SellerPartyId,@TenantId,N'NaturalPerson',N'Vendedor precios pedido',
               N'Complete',1,@UserId,SYSDATETIMEOFFSET());
            UPDATE dbo.AppUsers SET PartyId=@SellerPartyId WHERE UserId=@UserId;
            INSERT dbo.Customers(CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
            VALUES(@CustomerId,@PartyId,@BusinessId,1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.CommerceSellers(
              SellerId,BusinessId,PartyId,Code,CommissionBasis,CommissionTrigger,IsActive,CreatedAt)
            VALUES(@SellerId,@BusinessId,@SellerPartyId,@SellerCode,
              N'SaleAfterTax',N'Sale',1,SYSDATETIMEOFFSET());

            INSERT dbo.TaxProfiles(TaxProfileId,BusinessId,Code,Name,Rate,IsActive,CreatedAt)
            VALUES(@TaxProfileId,@BusinessId,@TaxCode,N'Sin impuesto pedido',0,1,SYSDATETIMEOFFSET());

            INSERT dbo.Products(
              ProductId,TenantId,BusinessId,ProductCode,Sku,Name,BaseUnitCode,TaxProfileId,
              ManageStock,IsWeighable,IsActive,Source,Currency,CreatedAt)
            VALUES
              (@FirstProductId,@TenantId,@BusinessId,@FirstCode,@FirstCode,N'Producto disparador',N'EA',@TaxProfileId,0,0,1,0,N'COP',SYSDATETIMEOFFSET()),
              (@SecondProductId,@TenantId,@BusinessId,@SecondCode,@SecondCode,N'Producto beneficiado',N'EA',@TaxProfileId,0,0,1,0,N'COP',SYSDATETIMEOFFSET());
            INSERT dbo.ProductPrices(
              ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,ValidFrom,
              RoundingIncrement,RoundingMode,IsActive,CreatedAt)
            VALUES
              (NEWID(),@BusinessId,@FirstProductId,100,N'COP',DATEADD(day,-1,SYSDATETIMEOFFSET()),1,N'Nearest',1,SYSDATETIMEOFFSET()),
              (NEWID(),@BusinessId,@SecondProductId,200,N'COP',DATEADD(day,-1,SYSDATETIMEOFFSET()),1,N'Nearest',1,SYSDATETIMEOFFSET());

            INSERT dbo.Promotions(
              PromotionId,TenantId,Name,IsActive,Priority,IsCombinable,CreatedAt)
            VALUES(@PromotionId,@TenantId,N'Pedido compra A y B al 50',1,100,0,SYSUTCDATETIME());
            INSERT pricing.PromotionBusinessScopes(PromotionId,BusinessId,TenantId)
            VALUES(@PromotionId,@BusinessId,@TenantId);
            INSERT dbo.PromotionConditions(
              PromotionConditionId,PromotionId,TenantId,ItemType,ProductId,MinQuantity,CreatedAt)
            VALUES(NEWID(),@PromotionId,@TenantId,1,@FirstProductId,1,SYSUTCDATETIME());
            INSERT dbo.PromotionBenefits(
              PromotionBenefitId,PromotionId,TenantId,BenefitType,TargetItemType,ProductId,
              DiscountPercentage,AppliesToQuantity,CreatedAt)
            VALUES(NEWID(),@PromotionId,@TenantId,0,1,@SecondProductId,50,1,SYSUTCDATETIME());
            """,
            new("@UserId", userId), new("@TenantId", fixture.TenantId),
            new("@Username", $"seller-price-{userId:N}"), new("@PartyId", partyId),
            new("@SellerPartyId", sellerPartyId), new("@SellerId", sellerId),
            new("@SellerCode", $"SP-{sellerId:N}"[..20]),
            new("@CustomerId", customerId), new("@BusinessId", fixture.BusinessId),
            new("@FirstProductId", firstProductId), new("@SecondProductId", secondProductId),
            new("@FirstCode", $"SP-A-{firstProductId:N}"[..24]),
            new("@SecondCode", $"SP-B-{secondProductId:N}"[..24]),
            new("@TaxProfileId", taxProfileId), new("@TaxCode", $"SO-{taxProfileId:N}"[..32]),
            new("@PromotionId", promotionId));

        using var client = fixture.CreateUserClient(
            userId, OrderPermissionCodes.Create, OrderPermissionCodes.Read);
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/seller-orders",
            new
            {
                businessId = fixture.BusinessId,
                warehouseId = fixture.WarehouseId,
                customerId,
                partySiteId = (Guid?)null,
                routeId = (Guid?)null,
                routeStopId = (Guid?)null,
                capturedOffline = false,
                notes = "Prueba de paridad documental",
                idempotencyKey = Guid.NewGuid().ToString("N"),
                lines = new[]
                {
                    new { productId = firstProductId, quantity = 1m, unitPrice = 111m, discountAmount = 1m, priceSource = "Promotion" },
                    new { productId = secondProductId, quantity = 1m, unitPrice = 99m, discountAmount = 4m, priceSource = "Promotion" }
                }
            });
        Assert.True(response.IsSuccessStatusCode,
            $"El pedido respondió {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(205m, body.GetProperty("total").GetDecimal());
        var orderId = body.GetProperty("orderId").GetGuid();

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ProductId,UnitPrice
            FROM dbo.OrderItems
            WHERE OrderId=@OrderId
            ORDER BY ProductId;
            """;
        command.Parameters.AddWithValue("@OrderId", orderId);
        await using var reader = await command.ExecuteReaderAsync();
        var prices = new Dictionary<Guid, decimal>();
        while (await reader.ReadAsync()) prices.Add(reader.GetGuid(0), reader.GetDecimal(1));
        Assert.Equal(111m, prices[firstProductId]);
        Assert.Equal(99m, prices[secondProductId]);

        using var printResponse = await client.PostAsJsonAsync(
            "/api/commerce/v1/orders/print-batch",
            new OrderPrintBatchRequest([orderId, orderId]));
        printResponse.EnsureSuccessStatusCode();
        var printable = Assert.Single(Assert.IsType<List<OrderPrintDocument>>(
            await printResponse.Content.ReadFromJsonAsync<List<OrderPrintDocument>>()));
        Assert.Equal(orderId, printable.OrderId);
        Assert.Equal(205m, printable.Total);
        Assert.Equal("Cliente precios pedido", printable.CustomerName);
        Assert.Collection(
            printable.Lines.OrderBy(line => line.UnitPrice),
            line =>
            {
                Assert.Equal(99m, line.UnitPrice);
                Assert.Equal(4m, line.DiscountAmount);
                Assert.Equal(95m, line.LineTotal);
            },
            line =>
            {
                Assert.Equal(111m, line.UnitPrice);
                Assert.Equal(1m, line.DiscountAmount);
                Assert.Equal(110m, line.LineTotal);
            });

        using var missingPrint = await client.PostAsJsonAsync(
            "/api/commerce/v1/orders/print-batch",
            new OrderPrintBatchRequest([orderId, Guid.NewGuid()]));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, missingPrint.StatusCode);
    }

    [Fact]
    public async Task Insufficient_seller_order_reserves_sufficient_lines_then_review_completes_only_the_pending_inventory()
    {
        var userId = Guid.NewGuid();
        var customerPartyId = Guid.NewGuid();
        var sellerPartyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var taxProfileId = Guid.NewGuid();
        var firstProductId = Guid.NewGuid();
        var secondProductId = Guid.NewGuid();
        var ordersWarehouseId = await EnsureOrdersWarehouseAsync();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,IsActive,CreatedAt)
            VALUES(@UserId,@TenantId,@Username,UPPER(@Username),CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),N'Revisión',N'Pedido',1,SYSDATETIMEOFFSET());
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES
              (@CustomerPartyId,@TenantId,N'Organization',N'Cliente revisión',N'Incomplete',1,@UserId,SYSDATETIMEOFFSET()),
              (@SellerPartyId,@TenantId,N'NaturalPerson',N'Vendedor revisión',N'Complete',1,@UserId,SYSDATETIMEOFFSET());
            UPDATE dbo.AppUsers SET PartyId=@SellerPartyId WHERE UserId=@UserId;
            INSERT dbo.Customers(CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
            VALUES(@CustomerId,@CustomerPartyId,@BusinessId,1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.CommerceSellers(SellerId,BusinessId,PartyId,Code,CommissionBasis,CommissionTrigger,IsActive,CreatedAt)
            VALUES(@SellerId,@BusinessId,@SellerPartyId,@SellerCode,N'SaleAfterTax',N'Sale',1,SYSDATETIMEOFFSET());
            INSERT dbo.TaxProfiles(TaxProfileId,BusinessId,Code,Name,Rate,IsActive,CreatedAt)
            VALUES(@TaxProfileId,@BusinessId,@TaxCode,N'IVA revisión',0,1,SYSDATETIMEOFFSET());
            INSERT dbo.Products(ProductId,TenantId,BusinessId,ProductCode,Sku,Name,BaseUnitCode,TaxProfileId,ManageStock,IsWeighable,IsActive,Source,Currency,CreatedAt)
            VALUES
              (@FirstProductId,@TenantId,@BusinessId,@FirstCode,@FirstCode,N'Producto suficiente',N'EA',@TaxProfileId,1,0,1,0,N'COP',SYSDATETIMEOFFSET()),
              (@SecondProductId,@TenantId,@BusinessId,@SecondCode,@SecondCode,N'Producto insuficiente',N'EA',@TaxProfileId,1,0,1,0,N'COP',SYSDATETIMEOFFSET());
            INSERT dbo.ProductPrices(ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,ValidFrom,RoundingIncrement,RoundingMode,IsActive,CreatedAt)
            VALUES
              (NEWID(),@BusinessId,@FirstProductId,1000,N'COP',DATEADD(day,-1,SYSDATETIMEOFFSET()),1,N'Nearest',1,SYSDATETIMEOFFSET()),
              (NEWID(),@BusinessId,@SecondProductId,2000,N'COP',DATEADD(day,-1,SYSDATETIMEOFFSET()),1,N'Nearest',1,SYSDATETIMEOFFSET());
            INSERT dbo.InventoryBalances(BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,InventoryValue,LastProcessingSequence,UpdatedAt)
            VALUES
              (@BusinessId,@WarehouseId,@FirstProductId,7,500,3500,0,SYSDATETIMEOFFSET()),
              (@BusinessId,@WarehouseId,@SecondProductId,2,1000,2000,0,SYSDATETIMEOFFSET());
            """,
            new("@UserId", userId), new("@TenantId", fixture.TenantId),
            new("@Username", $"review-{userId:N}"), new("@CustomerPartyId", customerPartyId),
            new("@SellerPartyId", sellerPartyId), new("@CustomerId", customerId),
            new("@SellerId", sellerId), new("@SellerCode", $"RV-{sellerId:N}"[..20]),
            new("@BusinessId", fixture.BusinessId), new("@WarehouseId", fixture.WarehouseId),
            new("@TaxProfileId", taxProfileId), new("@TaxCode", $"RV-{taxProfileId:N}"[..16]),
            new("@FirstProductId", firstProductId), new("@SecondProductId", secondProductId),
            new("@FirstCode", $"RV-A-{firstProductId:N}"[..24]),
            new("@SecondCode", $"RV-B-{secondProductId:N}"[..24]));

        using var client = fixture.CreateUserClient(userId,
            OrderPermissionCodes.Read, OrderPermissionCodes.Create, OrderPermissionCodes.Update);
        using var create = await client.PostAsJsonAsync("/api/commerce/v1/seller-orders", new
        {
            businessId = fixture.BusinessId,
            warehouseId = fixture.WarehouseId,
            customerId,
            capturedOffline = false,
            idempotencyKey = Guid.NewGuid().ToString("N"),
            lines = new[]
            {
                new { productId = firstProductId, quantity = 5m, unitPrice = 1111m, discountAmount = 0m, priceSource = "PriceChannel" },
                new { productId = secondProductId, quantity = 10m, unitPrice = 2222m, discountAmount = 0m, priceSource = "Promotion" },
            },
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(created.GetProperty("requiresReview").GetBoolean());
        Assert.Equal("InReview", created.GetProperty("status").GetString());
        var orderId = created.GetProperty("orderId").GetGuid();
        Assert.Equal((2m, 2m, 5m, 0m), await ReadBalancesAsync(firstProductId, secondProductId, ordersWarehouseId));

        var reviews = Assert.IsType<OrderPage>(await client.GetFromJsonAsync<OrderPage>(
            "/api/commerce/v1/orders?page=1&pageSize=20&status=InReview"));
        Assert.Contains(reviews.Items, order => order.OrderId == orderId && order.Status == "InReview");

        using var detailResponse = await client.GetAsync($"/api/commerce/v1/orders/{orderId:D}");
        Assert.True(detailResponse.IsSuccessStatusCode,
            $"El detalle respondió {(int)detailResponse.StatusCode}: {await detailResponse.Content.ReadAsStringAsync()}");
        var detail = Assert.IsType<OrderDetail>(await detailResponse.Content.ReadFromJsonAsync<OrderDetail>());
        Assert.Equal("InReview", detail.Status);
        var sufficientLine = Assert.Single(detail.Lines, line => line.ProductId == firstProductId);
        Assert.Equal(2m, sufficientLine.QuantityOnHand);
        Assert.Equal(5m, sufficientLine.ReservedQuantity);
        var insufficientLine = Assert.Single(detail.Lines, line => line.ProductId == secondProductId);
        Assert.Equal(2m, insufficientLine.QuantityOnHand);
        Assert.Equal(0m, insufficientLine.ReservedQuantity);

        using var readOnly = fixture.CreateUserClient(userId, OrderPermissionCodes.Read);
        using (var forbiddenReview = await readOnly.PutAsJsonAsync($"/api/commerce/v1/seller-orders/{orderId:D}", new
        {
            customerId,
            notes = "Sin permiso de revisión",
            idempotencyKey = Guid.NewGuid().ToString("N"),
            lines = new[]
            {
                new { productId = firstProductId, quantity = 5m, unitPrice = 1111m, discountAmount = 0m, priceSource = "PriceChannel" },
                new { productId = secondProductId, quantity = 2m, unitPrice = 2222m, discountAmount = 0m, priceSource = "Promotion" },
            },
        }))
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, forbiddenReview.StatusCode);

        using var reviewer = fixture.CreateUserClient(userId,
            OrderPermissionCodes.Read, OrderPermissionCodes.Review);
        using (var forbiddenChange = await reviewer.PutAsJsonAsync($"/api/commerce/v1/seller-orders/{orderId:D}", new
        {
            customerId,
            notes = "Intento de cambiar una línea ya reservada",
            idempotencyKey = Guid.NewGuid().ToString("N"),
            lines = new[]
            {
                new { productId = firstProductId, quantity = 4m, unitPrice = 1111m, discountAmount = 0m, priceSource = "PriceChannel" },
                new { productId = secondProductId, quantity = 2m, unitPrice = 2222m, discountAmount = 0m, priceSource = "Promotion" },
            },
        }))
            Assert.Equal(System.Net.HttpStatusCode.Conflict, forbiddenChange.StatusCode);

        var reviewKey = Guid.NewGuid().ToString("N");
        var reviewRequest = new
        {
            customerId,
            notes = "Ajustado a existencia disponible",
            idempotencyKey = reviewKey,
            lines = new[]
            {
                new { productId = firstProductId, quantity = 5m, unitPrice = 1111m, discountAmount = 0m, priceSource = "PriceChannel" },
                new { productId = secondProductId, quantity = 2m, unitPrice = 2222m, discountAmount = 0m, priceSource = "Promotion" },
            },
        };
        using var confirm = await reviewer.PutAsJsonAsync($"/api/commerce/v1/seller-orders/{orderId:D}", reviewRequest);
        confirm.EnsureSuccessStatusCode();
        Assert.Equal((2m, 0m, 5m, 2m), await ReadBalancesAsync(firstProductId, secondProductId, ordersWarehouseId));
        using (var replay = await reviewer.PutAsJsonAsync($"/api/commerce/v1/seller-orders/{orderId:D}", reviewRequest))
            replay.EnsureSuccessStatusCode();
        Assert.Equal((2m, 0m, 5m, 2m), await ReadBalancesAsync(firstProductId, secondProductId, ordersWarehouseId));

        using (var reduceAndRemove = await client.PutAsJsonAsync($"/api/commerce/v1/seller-orders/{orderId:D}", new
        {
            customerId,
            notes = "Edición completa con devolución a ventas",
            idempotencyKey = Guid.NewGuid().ToString("N"),
            lines = new[]
            {
                new { productId = firstProductId, quantity = 3m, unitPrice = 1111m, discountAmount = 0m, priceSource = "PriceChannel" },
            },
        }))
            reduceAndRemove.EnsureSuccessStatusCode();
        Assert.Equal((4m, 2m, 3m, 0m), await ReadBalancesAsync(firstProductId, secondProductId, ordersWarehouseId));

        using (var increase = await client.PutAsJsonAsync($"/api/commerce/v1/seller-orders/{orderId:D}", new
        {
            customerId,
            notes = "Edición completa con reserva incremental",
            idempotencyKey = Guid.NewGuid().ToString("N"),
            lines = new[]
            {
                new { productId = firstProductId, quantity = 6m, unitPrice = 1111m, discountAmount = 0m, priceSource = "PriceChannel" },
            },
        }))
            increase.EnsureSuccessStatusCode();
        Assert.Equal((1m, 2m, 6m, 0m), await ReadBalancesAsync(firstProductId, secondProductId, ordersWarehouseId));

        using var createForRemoval = await client.PostAsJsonAsync("/api/commerce/v1/seller-orders", new
        {
            businessId = fixture.BusinessId,
            warehouseId = fixture.WarehouseId,
            customerId,
            capturedOffline = false,
            idempotencyKey = Guid.NewGuid().ToString("N"),
            lines = new[]
            {
                new { productId = firstProductId, quantity = 1m, unitPrice = 1111m, discountAmount = 0m, priceSource = "PriceChannel" },
                new { productId = secondProductId, quantity = 10m, unitPrice = 2222m, discountAmount = 0m, priceSource = "Promotion" },
            },
        });
        createForRemoval.EnsureSuccessStatusCode();
        var removalReview = await createForRemoval.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("InReview", removalReview.GetProperty("status").GetString());
        var removalOrderId = removalReview.GetProperty("orderId").GetGuid();
        Assert.Equal((0m, 2m, 7m, 0m), await ReadBalancesAsync(firstProductId, secondProductId, ordersWarehouseId));

        using (var removePendingLine = await reviewer.PutAsJsonAsync($"/api/commerce/v1/seller-orders/{removalOrderId:D}", new
        {
            customerId,
            notes = "Línea faltante eliminada durante revisión",
            idempotencyKey = Guid.NewGuid().ToString("N"),
            lines = new[]
            {
                new { productId = firstProductId, quantity = 1m, unitPrice = 999999m, discountAmount = 0m, priceSource = "Manipulated" },
            },
        }))
            removePendingLine.EnsureSuccessStatusCode();
        Assert.Equal((0m, 2m, 7m, 0m), await ReadBalancesAsync(firstProductId, secondProductId, ordersWarehouseId));

        using var removedDetailResponse = await client.GetAsync($"/api/commerce/v1/orders/{removalOrderId:D}");
        removedDetailResponse.EnsureSuccessStatusCode();
        var removedDetail = Assert.IsType<OrderDetail>(await removedDetailResponse.Content.ReadFromJsonAsync<OrderDetail>());
        var preservedLine = Assert.Single(removedDetail.Lines);
        Assert.Equal(firstProductId, preservedLine.ProductId);
        Assert.Equal(1111m, preservedLine.UnitPrice);

        async Task<(decimal FirstSource, decimal SecondSource, decimal FirstReserved, decimal SecondReserved)> ReadBalancesAsync(
            Guid first, Guid second, Guid reservedWarehouse)
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                  COALESCE((SELECT QuantityOnHand FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@SourceWarehouseId AND ProductId=@FirstProductId),0),
                  COALESCE((SELECT QuantityOnHand FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@SourceWarehouseId AND ProductId=@SecondProductId),0),
                  COALESCE((SELECT QuantityOnHand FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@ReservedWarehouseId AND ProductId=@FirstProductId),0),
                  COALESCE((SELECT QuantityOnHand FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@ReservedWarehouseId AND ProductId=@SecondProductId),0);
                """;
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@SourceWarehouseId", fixture.WarehouseId);
            command.Parameters.AddWithValue("@ReservedWarehouseId", reservedWarehouse);
            command.Parameters.AddWithValue("@FirstProductId", first);
            command.Parameters.AddWithValue("@SecondProductId", second);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetDecimal(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3));
        }
    }

    [Fact]
    public async Task Linked_inventory_family_is_allocated_in_parent_units_and_review_moves_only_the_pending_quantity()
    {
        var userId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var sellerPartyId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var taxProfileId = Guid.NewGuid();
        var parentProductId = Guid.NewGuid();
        var childProductId = Guid.NewGuid();
        var ordersWarehouseId = await EnsureOrdersWarehouseAsync();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,IsActive,CreatedAt)
            VALUES(@UserId,@TenantId,@Username,UPPER(@Username),CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),N'Familia',N'Inventario',1,SYSDATETIMEOFFSET());
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES
              (@PartyId,@TenantId,N'Organization',N'Cliente familia inventario',N'Incomplete',1,@UserId,SYSDATETIMEOFFSET()),
              (@SellerPartyId,@TenantId,N'NaturalPerson',N'Vendedor familia inventario',N'Complete',1,@UserId,SYSDATETIMEOFFSET());
            UPDATE dbo.AppUsers SET PartyId=@SellerPartyId WHERE UserId=@UserId;
            INSERT dbo.Customers(CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
            VALUES(@CustomerId,@PartyId,@BusinessId,1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.CommerceSellers(SellerId,BusinessId,PartyId,Code,CommissionBasis,CommissionTrigger,IsActive,CreatedAt)
            VALUES(@SellerId,@BusinessId,@SellerPartyId,@SellerCode,N'SaleAfterTax',N'Sale',1,SYSDATETIMEOFFSET());
            INSERT dbo.TaxProfiles(TaxProfileId,BusinessId,Code,Name,Rate,IsActive,CreatedAt)
            VALUES(@TaxProfileId,@BusinessId,@TaxCode,N'Sin impuesto familia',0,1,SYSDATETIMEOFFSET());
            INSERT dbo.Products(ProductId,TenantId,BusinessId,ProductCode,Sku,Name,BaseUnitCode,TaxProfileId,ManageStock,IsWeighable,IsActive,Source,Currency,CreatedAt)
            VALUES
              (@ParentProductId,@TenantId,@BusinessId,@ParentCode,@ParentCode,N'Unidad padre',N'EA',@TaxProfileId,1,0,1,0,N'COP',SYSDATETIMEOFFSET()),
              (@ChildProductId,@TenantId,@BusinessId,@ChildCode,@ChildCode,N'Media unidad',N'EA',@TaxProfileId,0,0,1,0,N'COP',SYSDATETIMEOFFSET());
            INSERT dbo.ProductLinks(ProductLinkId,BusinessId,ChildProductId,ParentProductId,InventoryFactor,SharesInventory,SharesPrice,AllowsConversion,IsActive,CreatedAt)
            VALUES(NEWID(),@BusinessId,@ChildProductId,@ParentProductId,0.5,1,0,0,1,SYSDATETIMEOFFSET());
            INSERT dbo.ProductPrices(ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,ValidFrom,RoundingIncrement,RoundingMode,IsActive,CreatedAt)
            VALUES
              (NEWID(),@BusinessId,@ParentProductId,1000,N'COP',DATEADD(day,-1,SYSDATETIMEOFFSET()),1,N'Nearest',1,SYSDATETIMEOFFSET()),
              (NEWID(),@BusinessId,@ChildProductId,550,N'COP',DATEADD(day,-1,SYSDATETIMEOFFSET()),1,N'Nearest',1,SYSDATETIMEOFFSET());
            INSERT dbo.InventoryBalances(BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,InventoryValue,LastProcessingSequence,UpdatedAt)
            VALUES(@BusinessId,@WarehouseId,@ParentProductId,5,400,2000,0,SYSDATETIMEOFFSET());
            """,
            new("@UserId", userId), new("@TenantId", fixture.TenantId),
            new("@Username", $"inventory-family-{userId:N}"), new("@PartyId", partyId),
            new("@SellerPartyId", sellerPartyId), new("@SellerId", sellerId),
            new("@SellerCode", $"IF-{sellerId:N}"[..20]),
            new("@CustomerId", customerId), new("@BusinessId", fixture.BusinessId),
            new("@WarehouseId", fixture.WarehouseId), new("@TaxProfileId", taxProfileId),
            new("@TaxCode", $"IF-{taxProfileId:N}"[..12]),
            new("@ParentProductId", parentProductId), new("@ChildProductId", childProductId),
            new("@ParentCode", $"IF-P-{parentProductId:N}"[..16]),
            new("@ChildCode", $"IF-C-{childProductId:N}"[..16]));

        using var client = fixture.CreateUserClient(userId,
            OrderPermissionCodes.Read, OrderPermissionCodes.Create, OrderPermissionCodes.Review);
        using var create = await client.PostAsJsonAsync("/api/commerce/v1/seller-orders", new
        {
            businessId = fixture.BusinessId,
            warehouseId = fixture.WarehouseId,
            customerId,
            capturedOffline = false,
            idempotencyKey = Guid.NewGuid().ToString("N"),
            lines = new[]
            {
                new { productId = childProductId, quantity = 8m, unitPrice = 550m, discountAmount = 0m, priceSource = "Captured" },
                new { productId = parentProductId, quantity = 3m, unitPrice = 1000m, discountAmount = 0m, priceSource = "Captured" },
            },
        });
        Assert.True(create.IsSuccessStatusCode,
            $"El pedido vinculado respondió {(int)create.StatusCode}: {await create.Content.ReadAsStringAsync()}");
        var created = await create.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("InReview", created.GetProperty("status").GetString());
        var orderId = created.GetProperty("orderId").GetGuid();
        Assert.Equal((1m, 4m), await ReadParentBalancesAsync());

        var detail = Assert.IsType<OrderDetail>(await client.GetFromJsonAsync<OrderDetail>(
            $"/api/commerce/v1/orders/{orderId:D}"));
        Assert.Equal(8m, Assert.Single(detail.Lines, line => line.ProductId == childProductId).ReservedQuantity);
        Assert.Equal(0m, Assert.Single(detail.Lines, line => line.ProductId == parentProductId).ReservedQuantity);

        using var review = await client.PutAsJsonAsync($"/api/commerce/v1/seller-orders/{orderId:D}", new
        {
            customerId,
            notes = "Cantidad padre ajustada a la existencia restante",
            idempotencyKey = Guid.NewGuid().ToString("N"),
            lines = new[]
            {
                new { productId = childProductId, quantity = 8m, unitPrice = 550m, discountAmount = 0m, priceSource = "Captured" },
                new { productId = parentProductId, quantity = 1m, unitPrice = 1000m, discountAmount = 0m, priceSource = "Captured" },
            },
        });
        Assert.True(review.IsSuccessStatusCode,
            $"La revisión vinculada respondió {(int)review.StatusCode}: {await review.Content.ReadAsStringAsync()}");
        var reviewed = await review.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("Confirmed", reviewed.GetProperty("status").GetString());
        Assert.Equal((0m, 5m), await ReadParentBalancesAsync());

        async Task<(decimal Sales, decimal Orders)> ReadParentBalancesAsync()
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                  COALESCE((SELECT QuantityOnHand FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@SalesWarehouseId AND ProductId=@ProductId),0),
                  COALESCE((SELECT QuantityOnHand FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@OrdersWarehouseId AND ProductId=@ProductId),0);
                """;
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@SalesWarehouseId", fixture.WarehouseId);
            command.Parameters.AddWithValue("@OrdersWarehouseId", ordersWarehouseId);
            command.Parameters.AddWithValue("@ProductId", parentProductId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetDecimal(0), reader.GetDecimal(1));
        }
    }

    [Fact]
    public async Task Seller_route_can_list_todays_orders_and_open_detail_with_legacy_non_json_attributes()
    {
        var orderId = Guid.NewGuid();
        var orderNumber = $"PED-RUTA-{orderId:N}";
        await ExecuteAsync(
            """
            INSERT dbo.Orders(
              OrderId,BusinessId,Source,FulfillmentMode,Status,CustomerId,
              WarehouseId,CapturedByUserId,CustomerNameSnapshot,Currency,
              Subtotal,DiscountTotal,Total,CustomerConfirmed,
              ExternalDocumentNumber,CustomAttributesJson,CreatedAt)
            VALUES(
              @OrderId,@BusinessId,1,0,3,NULL,
              @WarehouseId,@UserId,N'Cliente de ruta',N'COP',
              12500,0,12500,1,@OrderNumber,
              N'<!doctype html><meta name="viewport" content="width=device-width"><h1>legacy</h1>',
              SYSUTCDATETIME());

            INSERT dbo.OrderItems(
              OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,
              ProductNameSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,
              DiscountAmount,LineTotal,CreatedAt)
            VALUES(
              NEWID(),@OrderId,@BusinessId,@ProductId,N'P-RUTA',N'P-RUTA',
              N'Producto tomado en ruta',N'EA',1,12500,0,12500,SYSUTCDATETIME());
            """,
            new("@OrderId", orderId),
            new("@BusinessId", fixture.BusinessId),
            new("@WarehouseId", fixture.WarehouseId),
            new("@UserId", fixture.UserId),
            new("@OrderNumber", orderNumber),
            new("@ProductId", fixture.ProductId));

        using var client = fixture.CreateAdminClient(OrderPermissionCodes.Read);
        using (var response = await client.GetAsync(
                   $"/api/commerce/v1/orders?page=1&pageSize=100&source=1&warehouseId={fixture.WarehouseId:D}&onlyMine=true&createdFrom=2020-01-01T00:00:00Z&createdTo=2035-01-01T00:00:00Z"))
        {
            Assert.True(response.IsSuccessStatusCode,
                $"La pestaña Pedidos respondió {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            var page = Assert.IsType<OrderPage>(await response.Content.ReadFromJsonAsync<OrderPage>());
            Assert.Contains(page.Items, item => item.OrderId == orderId && item.OrderNumber == orderNumber);
        }

        using (var response = await client.GetAsync($"/api/commerce/v1/orders/{orderId:D}"))
        {
            Assert.True(response.IsSuccessStatusCode,
                $"El detalle respondió {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            var detail = Assert.IsType<OrderDetail>(await response.Content.ReadFromJsonAsync<OrderDetail>());
            Assert.Equal(orderId, detail.OrderId);
            Assert.Equal("Producto tomado en ruta", Assert.Single(detail.Lines).ProductName);
        }
    }

    [Fact]
    public async Task Recovered_order_update_preserves_price_discount_and_reserved_quantity()
    {
        var userId = Guid.NewGuid();
        var customerPartyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var updatedCustomerPartyId = Guid.NewGuid();
        var updatedCustomerId = Guid.NewGuid();
        var sellerPartyId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var ordersWarehouseId = await EnsureOrdersWarehouseAsync();
        var attributes = System.Text.Json.JsonSerializer.Serialize(new
        {
            WarehouseId = fixture.WarehouseId,
            ordersWarehouseId,
            createdBy = userId,
        });
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
              FirstName,LastName,IsActive,CreatedAt)
            VALUES(
              @UserId,@TenantId,@Username,UPPER(@Username),
              CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),
              N'Edición',N'Pedido',1,SYSDATETIMEOFFSET());

            INSERT dbo.Parties(
              PartyId,TenantId,PartyType,DisplayName,LegalName,CompletionStatus,
              IsActive,CreatedBy,CreatedAt)
            VALUES
              (@PartyId,@TenantId,N'Organization',N'Cliente edición',N'Cliente edición',
               N'Incomplete',1,@UserId,SYSDATETIMEOFFSET()),
              (@UpdatedPartyId,@TenantId,N'Organization',N'Cliente actualizado',N'Cliente actualizado',
               N'Incomplete',1,@UserId,SYSDATETIMEOFFSET()),
              (@SellerPartyId,@TenantId,N'NaturalPerson',N'Vendedor edición',N'Vendedor edición',
               N'Complete',1,@UserId,SYSDATETIMEOFFSET());

            INSERT dbo.Customers(CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
            VALUES
              (@CustomerId,@PartyId,@BusinessId,1,@UserId,SYSDATETIMEOFFSET()),
              (@UpdatedCustomerId,@UpdatedPartyId,@BusinessId,1,@UserId,SYSDATETIMEOFFSET());

            INSERT dbo.CommerceSellers(
              SellerId,BusinessId,PartyId,Code,CommissionBasis,CommissionTrigger,IsActive,CreatedAt)
            VALUES(@SellerId,@BusinessId,@SellerPartyId,@SellerCode,
              N'SaleAfterTax',N'Sale',1,SYSDATETIMEOFFSET());

            MERGE dbo.InventoryBalances AS target
            USING (SELECT @BusinessId BusinessId,@OrdersWarehouseId WarehouseId,@ProductId ProductId) AS source
              ON target.BusinessId=source.BusinessId AND target.WarehouseId=source.WarehouseId
             AND target.ProductId=source.ProductId
            WHEN MATCHED THEN UPDATE SET QuantityOnHand=2,AverageUnitCost=5000,
              InventoryValue=10000,LastProcessingSequence=1,UpdatedAt=SYSDATETIMEOFFSET()
            WHEN NOT MATCHED THEN INSERT(
              BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
              InventoryValue,LastProcessingSequence,UpdatedAt)
            VALUES(source.BusinessId,source.WarehouseId,source.ProductId,2,5000,10000,1,SYSDATETIMEOFFSET());

            UPDATE dbo.InventoryBalances
            SET QuantityOnHand=100,AverageUnitCost=5000,InventoryValue=500000,
                UpdatedAt=SYSDATETIMEOFFSET()
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;

            INSERT dbo.Orders(
              OrderId,BusinessId,Source,FulfillmentMode,Status,CustomerId,SellerId,WarehouseId,
              CustomerNameSnapshot,CustomerDocumentSnapshot,Currency,
              Subtotal,DiscountTotal,Total,CustomerConfirmed,
              ExternalDocumentNumber,CustomAttributesJson,CreatedAt)
            VALUES(
              @OrderId,@BusinessId,1,0,2,@CustomerId,@SellerId,@WarehouseId,
              N'Cliente edición',N'900100200',N'COP',
              20000,1000,19000,1,@OrderNumber,@Attributes,SYSUTCDATETIME());

            INSERT dbo.OrderItems(
              OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,
              ProductNameSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,
              DiscountAmount,LineTotal,CreatedAt)
            VALUES(
              NEWID(),@OrderId,@BusinessId,@ProductId,N'P-E2E',N'P-E2E',
              N'Producto edición',N'EA',2,10000,1000,19000,SYSUTCDATETIME());
            """,
            new("@UserId", userId),
            new("@TenantId", fixture.TenantId),
            new("@Username", $"order-edit-{userId:N}"),
            new("@PartyId", customerPartyId),
            new("@CustomerId", customerId),
            new("@UpdatedPartyId", updatedCustomerPartyId),
            new("@UpdatedCustomerId", updatedCustomerId),
            new("@SellerPartyId", sellerPartyId),
            new("@SellerId", sellerId),
            new("@SellerCode", $"SE-{sellerId:N}"[..20]),
            new("@BusinessId", fixture.BusinessId),
            new("@WarehouseId", fixture.WarehouseId),
            new("@OrdersWarehouseId", ordersWarehouseId),
            new("@ProductId", fixture.ProductId),
            new("@OrderId", orderId),
            new("@OrderNumber", $"PED-EDIT-{orderId:N}"),
            new("@Attributes", attributes));

        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            OrderPermissionCodes.Read,
            OrderPermissionCodes.Recover,
            OrderPermissionCodes.Update,
            WorkSessionPermissionCodes.Open);
        using (var filteredResponse = await client.GetAsync(
                   $"/api/commerce/v1/orders?page=1&pageSize=20&sellerId={sellerId:D}&customerId={customerId:D}"))
        {
            filteredResponse.EnsureSuccessStatusCode();
            var filteredPage = Assert.IsType<OrderPage>(
                await filteredResponse.Content.ReadFromJsonAsync<OrderPage>());
            Assert.Contains(filteredPage.Items, item => item.OrderId == orderId);
        }
        using (var otherSellerResponse = await client.GetAsync(
                   $"/api/commerce/v1/orders?page=1&pageSize=20&sellerId={Guid.NewGuid():D}&customerId={customerId:D}"))
        {
            otherSellerResponse.EnsureSuccessStatusCode();
            var filteredPage = Assert.IsType<OrderPage>(
                await otherSellerResponse.Content.ReadFromJsonAsync<OrderPage>());
            Assert.DoesNotContain(filteredPage.Items, item => item.OrderId == orderId);
        }
        var workSession = await fixture.OpenWorkSessionAsync(client);
        var draft = await OpenDraftAsync(client, workSession.WorkSessionId);
        await RecoverAsync(client, userId, workSession.WorkSessionId, orderId, draft);

        using var response = await client.PutAsJsonAsync(
            $"/api/commerce/v1/seller-orders/{orderId:D}",
            new
            {
                customerId = updatedCustomerId,
                notes = "Pedido actualizado desde el POS",
                idempotencyKey = Guid.NewGuid().ToString("N"),
                workSessionId = workSession.WorkSessionId,
                lines = new[]
                {
                    new
                    {
                        productId = fixture.ProductId,
                        quantity = 2m,
                        unitPrice = 12_500m,
                        discountAmount = 2_500m,
                    },
                },
            });
        Assert.True(
            response.IsSuccessStatusCode,
            $"La actualización del pedido respondió {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT o.CustomerId,o.CustomerNameSnapshot,o.Subtotal,o.DiscountTotal,o.Total,
                   i.Quantity,i.UnitPrice,i.DiscountAmount,i.LineTotal,
                   b.QuantityOnHand,o.Notes
            FROM dbo.Orders o
            JOIN dbo.OrderItems i ON i.OrderId=o.OrderId
            JOIN dbo.InventoryBalances b
              ON b.BusinessId=o.BusinessId AND b.WarehouseId=@OrdersWarehouseId
             AND b.ProductId=i.ProductId
            WHERE o.OrderId=@OrderId;
            """;
        command.Parameters.AddWithValue("@OrderId", orderId);
        command.Parameters.AddWithValue("@OrdersWarehouseId", ordersWarehouseId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(updatedCustomerId, reader.GetGuid(0));
        Assert.Equal("Cliente actualizado", reader.GetString(1));
        Assert.Equal(25_000m, reader.GetDecimal(2));
        Assert.Equal(2_500m, reader.GetDecimal(3));
        Assert.Equal(22_500m, reader.GetDecimal(4));
        Assert.Equal(2m, reader.GetDecimal(5));
        Assert.Equal(12_500m, reader.GetDecimal(6));
        Assert.Equal(2_500m, reader.GetDecimal(7));
        Assert.Equal(22_500m, reader.GetDecimal(8));
        Assert.Equal(2m, reader.GetDecimal(9));
        Assert.Equal("Pedido actualizado desde el POS", reader.GetString(10));
    }

    private async Task<Guid> EnsureOrdersWarehouseAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var command = new SqlCommand(
            """
            DECLARE @WarehouseId uniqueidentifier=(
              SELECT WarehouseId FROM dbo.Warehouses WITH(UPDLOCK,HOLDLOCK)
              WHERE BusinessId=@BusinessId AND Code=N'PED');
            IF @WarehouseId IS NULL
            BEGIN
              SET @WarehouseId=NEWID();
              INSERT dbo.Warehouses(
                WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,
                IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive,CreatedAt)
              VALUES(@WarehouseId,@BusinessId,N'PED',N'Pedidos',0,1,0,0,0,1,SYSDATETIMEOFFSET());
            END;
            SELECT @WarehouseId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        var warehouseId = (Guid)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("No fue posible aprovisionar la bodega PED para la prueba."));
        await transaction.CommitAsync();
        return warehouseId;
    }

    [Fact]
    public async Task Recovering_order_preserves_commercial_values_but_uses_current_product_tax()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var taxProfileId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
              FirstName,LastName,IsActive,CreatedAt)
            VALUES(
              @UserId,@TenantId,@Username,UPPER(@Username),
              CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),
              N'Pedidos',N'Prueba',1,SYSDATETIMEOFFSET());

            INSERT dbo.Orders(
              OrderId,BusinessId,Source,FulfillmentMode,Status,WarehouseId,
              CustomerNameSnapshot,CustomerDocumentSnapshot,Currency,
              Subtotal,DiscountTotal,Total,CustomerConfirmed,
              ExternalDocumentNumber,CreatedAt)
            VALUES(
              @OrderId,@BusinessId,0,0,2,@WarehouseId,
              N'Cliente pedido',N'123456789',N'COP',
              15554,777,14777,1,N'PED-PRUEBA-01',DATEADD(day,-4,SYSUTCDATETIME()));

            INSERT dbo.OrderItems(
              OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,
              ProductNameSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,
              DiscountAmount,LineTotal,CreatedAt)
            VALUES(
              @ItemId,@OrderId,@BusinessId,@ProductId,N'P-E2E',N'P-E2E',
              N'Producto del pedido',N'EA',2,7777,
              777,14777,DATEADD(day,-4,SYSUTCDATETIME()));

            INSERT dbo.TaxProfiles(
              TaxProfileId,BusinessId,Code,Name,Rate,IsActive,CreatedAt)
            VALUES(
              @TaxProfileId,@BusinessId,N'IVA-5-PEDIDO',N'IVA vigente al facturar',5,1,SYSDATETIMEOFFSET());
            UPDATE dbo.Products
            SET TaxProfileId=@TaxProfileId
            WHERE ProductId=@ProductId AND BusinessId=@BusinessId;
            """,
            new("@UserId", userId),
            new("@TenantId", fixture.TenantId),
            new("@Username", $"orders-{userId:N}"),
            new("@OrderId", orderId),
            new("@BusinessId", fixture.BusinessId),
            new("@WarehouseId", fixture.WarehouseId),
            new("@ItemId", itemId),
            new("@ProductId", fixture.ProductId),
            new("@TaxProfileId", taxProfileId));

        try
        {
            using var client = fixture.CreateUserClient(
                userId,
                CommercePermissionCodes.SalesCreate,
                OrderPermissionCodes.Read,
                OrderPermissionCodes.Recover,
                WorkSessionPermissionCodes.Open);
            var workSession = await fixture.OpenWorkSessionAsync(client);
            var draft = await OpenDraftAsync(client, workSession.WorkSessionId);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/commerce/v1/orders/{orderId:D}/recover")
            {
                Content = JsonContent.Create(new RecoverOrderIntoSaleRequest(
                    workSession.WorkSessionId,
                    userId,
                    draft.DraftId,
                    draft.Version))
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var recovered = await OpenDraftAsync(client, workSession.WorkSessionId);
            var line = Assert.Single(recovered.Lines);
            Assert.Equal(2m, line.Quantity);
            Assert.Equal(7_777m, line.UnitPrice);
            Assert.Equal(777m, line.Discount);
            Assert.Equal("Order", line.PriceSource);
            Assert.Equal(5m, line.TaxRate);
            Assert.Equal(738.85m, line.Tax);
            Assert.Equal(15_515.85m, recovered.PayableAmount);

            using var quantityRequest = new HttpRequestMessage(
                HttpMethod.Put,
                $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/lines/{line.LineId:D}/quantity")
            {
                Content = JsonContent.Create(new ChangeOnlineSalesDraftQuantityRequest(3m, recovered.Version))
            };
            quantityRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using (var quantityResponse = await client.SendAsync(quantityRequest))
                Assert.True(quantityResponse.IsSuccessStatusCode,
                    $"El cambio de cantidad respondió {(int)quantityResponse.StatusCode}: {await quantityResponse.Content.ReadAsStringAsync()}");
            var repriced = await OpenDraftAsync(client, workSession.WorkSessionId);
            var repricedLine = Assert.Single(repriced.Lines);
            Assert.Equal(3m, repricedLine.Quantity);
            Assert.NotEqual(7_777m, repricedLine.UnitPrice);
            Assert.Equal("Base", repricedLine.PriceSource);

            var page = await client.GetFromJsonAsync<OrderPage>(
                $"/api/commerce/v1/orders?orderNumber={orderId:D}");
            var listed = Assert.Single(page!.Items);
            Assert.NotNull(listed.Claim);
            Assert.False(listed.Claim.IsOwnedByCurrentActor);

            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT SourceOrderId
                FROM dbo.SalesDrafts
                WHERE SalesDraftId=@DraftId;
                """;
            command.Parameters.AddWithValue("@DraftId", draft.DraftId);
            Assert.Equal(orderId, Assert.IsType<Guid>(await command.ExecuteScalarAsync()));
        }
        finally
        {
            await ExecuteAsync(
                "UPDATE dbo.Products SET ProductCode=NULL,TaxProfileId=NULL WHERE ProductId=@ProductId; DELETE dbo.TaxProfiles WHERE TaxProfileId=@TaxProfileId;",
                new SqlParameter("@ProductId", fixture.ProductId),
                new SqlParameter("@TaxProfileId", taxProfileId));
        }
    }

    [Fact]
    public async Task Recovering_an_order_replaces_the_active_claim_for_the_work_session()
    {
        var userId = Guid.NewGuid();
        var replacementUserId = Guid.NewGuid();
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
              FirstName,LastName,IsActive,CreatedAt)
            VALUES
              (
              @UserId,@TenantId,@Username,UPPER(@Username),
              CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),
              N'Cambio',N'Pedido',1,SYSDATETIMEOFFSET()),
              (
              @ReplacementUserId,@TenantId,@ReplacementUsername,UPPER(@ReplacementUsername),
              CONCAT(@ReplacementUsername,N'@test.local'),UPPER(CONCAT(@ReplacementUsername,N'@test.local')),
              N'Relevo',N'Pedido',1,SYSDATETIMEOFFSET());

            INSERT dbo.Orders(
              OrderId,BusinessId,Source,FulfillmentMode,Status,WarehouseId,
              CustomerNameSnapshot,CustomerDocumentSnapshot,Currency,
              Subtotal,DiscountTotal,Total,CustomerConfirmed,
              ExternalDocumentNumber,CreatedAt)
            VALUES
              (@FirstOrderId,@BusinessId,0,0,2,@WarehouseId,N'Cliente A',N'1001',N'COP',
               10000,0,10000,1,N'PED-CAMBIO-A',SYSUTCDATETIME()),
              (@SecondOrderId,@BusinessId,0,0,2,@WarehouseId,N'Cliente B',N'1002',N'COP',
               12000,0,12000,1,N'PED-CAMBIO-B',SYSUTCDATETIME());

            INSERT dbo.OrderItems(
              OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,
              ProductNameSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,
              DiscountAmount,LineTotal,CreatedAt)
            VALUES
              (NEWID(),@FirstOrderId,@BusinessId,@ProductId,N'P-A',N'P-A',
               N'Producto A',N'EA',1,10000,0,10000,SYSUTCDATETIME()),
              (NEWID(),@SecondOrderId,@BusinessId,@ProductId,N'P-B',N'P-B',
               N'Producto B',N'EA',1,12000,0,12000,SYSUTCDATETIME());
            """,
            new("@UserId", userId),
            new("@ReplacementUserId", replacementUserId),
            new("@TenantId", fixture.TenantId),
            new("@Username", $"order-switch-{userId:N}"),
            new("@ReplacementUsername", $"order-replacement-{replacementUserId:N}"),
            new("@FirstOrderId", firstOrderId),
            new("@SecondOrderId", secondOrderId),
            new("@BusinessId", fixture.BusinessId),
            new("@WarehouseId", fixture.WarehouseId),
            new("@ProductId", fixture.ProductId));

        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            OrderPermissionCodes.Read,
            OrderPermissionCodes.Recover,
            WorkSessionPermissionCodes.Open);
        var workSession = await fixture.OpenWorkSessionAsync(client);
        var draft = await OpenDraftAsync(client, workSession.WorkSessionId);

        await RecoverAsync(client, userId, workSession.WorkSessionId, firstOrderId, draft);
        draft = await OpenDraftAsync(client, workSession.WorkSessionId);

        // Recuperar el mismo pedido renueva su reclamo sin abrir uno adicional.
        await RecoverAsync(client, userId, workSession.WorkSessionId, firstOrderId, draft);
        Assert.Equal(
            new[] { firstOrderId },
            await ActiveClaimOrderIdsAsync(userId, workSession.WorkSessionId));

        draft = await OpenDraftAsync(client, workSession.WorkSessionId);
        await RecoverAsync(client, userId, workSession.WorkSessionId, secondOrderId, draft);

        Assert.Equal(
            new[] { secondOrderId },
            await ActiveClaimOrderIdsAsync(userId, workSession.WorkSessionId));
        var switchedDraft = await OpenDraftAsync(client, workSession.WorkSessionId);
        Assert.Equal(secondOrderId, switchedDraft.SourceOrderId);

        await ExecuteAsync(
            "UPDATE dbo.WorkSessions SET Status=N'Closed',ClosedAt=SYSDATETIMEOFFSET() WHERE WorkSessionId=@WorkSessionId;",
            new SqlParameter("@WorkSessionId", workSession.WorkSessionId));

        using var replacementClient = fixture.CreateUserClient(
            replacementUserId,
            CommercePermissionCodes.SalesCreate,
            OrderPermissionCodes.Read,
            OrderPermissionCodes.Recover,
            WorkSessionPermissionCodes.Open);
        var replacementSession = await fixture.OpenWorkSessionAsync(replacementClient);
        var replacementDraft = await OpenDraftAsync(
            replacementClient,
            replacementSession.WorkSessionId);

        await RecoverAsync(
            replacementClient,
            replacementUserId,
            replacementSession.WorkSessionId,
            secondOrderId,
            replacementDraft);
        Assert.Equal(
            new[] { secondOrderId },
            await ActiveClaimOrderIdsAsync(
                replacementUserId,
                replacementSession.WorkSessionId));
    }

    [Fact]
    public async Task Orders_api_requires_explicit_read_permission()
    {
        using var client = fixture.CreateAdminClient();
        using var response = await client.GetAsync("/api/commerce/v1/orders");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pos_orders_require_both_device_and_logged_in_user_permissions()
    {
        var roleId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.AppRoles(
              RoleId,TenantId,Name,NormalizedName,Description,IsSystemRole,IsActive,CreatedAt)
            VALUES(
              @RoleId,@TenantId,@Name,UPPER(@Name),N'POS orders integration role',0,1,SYSDATETIMEOFFSET());

            INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
            SELECT NEWID(),@RoleId,PermissionId,SYSDATETIMEOFFSET()
            FROM dbo.Permissions WHERE Resource=N'orders.read';

            INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
            VALUES(NEWID(),@UserId,@RoleId,@BusinessId,SYSDATETIMEOFFSET());
            """,
            new("@RoleId", roleId),
            new("@TenantId", fixture.TenantId),
            new("@Name", $"POS orders {roleId:N}"),
            new("@UserId", fixture.UserId),
            new("@BusinessId", fixture.BusinessId));

        try
        {
            using var client = fixture.CreateClient();
            client.DefaultRequestHeaders.Add(
                "X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
            client.DefaultRequestHeaders.Add(
                "X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);

            using var allowed = await client.GetAsync(
                $"/api/pos/v1/orders?userId={fixture.UserId:D}&businessId={fixture.BusinessId:D}&warehouseId={fixture.WarehouseId:D}&workSessionId={fixture.WorkSessionId:D}&page=1&pageSize=50");
            Assert.Equal(System.Net.HttpStatusCode.OK, allowed.StatusCode);

            using var printRoute = await client.PostAsJsonAsync(
                "/api/pos/v1/orders/print-batch",
                new PosPrintOrdersRequest(
                    fixture.UserId,
                    fixture.BusinessId,
                    fixture.WarehouseId,
                    fixture.WorkSessionId,
                    [Guid.NewGuid()]));
            Assert.Equal(System.Net.HttpStatusCode.NotFound, printRoute.StatusCode);
            Assert.NotEqual(System.Net.HttpStatusCode.MethodNotAllowed, printRoute.StatusCode);

            using var unknownUser = await client.GetAsync(
                $"/api/pos/v1/orders?userId={Guid.NewGuid():D}&businessId={fixture.BusinessId:D}&warehouseId={fixture.WarehouseId:D}&workSessionId={fixture.WorkSessionId:D}&page=1&pageSize=50");
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, unknownUser.StatusCode);
        }
        finally
        {
            await ExecuteAsync(
                """
                DELETE dbo.UserRoles WHERE RoleId=@RoleId;
                DELETE dbo.RolePermissions WHERE RoleId=@RoleId;
                DELETE dbo.AppRoles WHERE RoleId=@RoleId;
                """,
                new SqlParameter("@RoleId", roleId));
        }
    }
    private async Task<OnlineSalesDraft> OpenDraftAsync(HttpClient client, Guid workSessionId)
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

    private static async Task RecoverAsync(
        HttpClient client,
        Guid userId,
        Guid workSessionId,
        Guid orderId,
        OnlineSalesDraft draft)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/commerce/v1/orders/{orderId:D}/recover")
        {
            Content = JsonContent.Create(new RecoverOrderIntoSaleRequest(
                workSessionId,
                userId,
                draft.DraftId,
                draft.Version))
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        Assert.True(
            response.IsSuccessStatusCode,
            $"La recuperación del pedido respondió {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private async Task<Guid[]> ActiveClaimOrderIdsAsync(
        Guid userId,
        Guid workSessionId)
    {
        var result = new List<Guid>();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OrderId
            FROM dbo.OrderClaims
            WHERE BusinessId=@BusinessId AND UserId=@UserId
              AND WorkSessionId=@WorkSessionId AND ReleasedAt IS NULL
            ORDER BY OrderId;
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetGuid(0));
        return result.ToArray();
    }

    private async Task ExecuteAsync(
        string sql,
        params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }
}
