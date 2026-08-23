using System.Net.Http.Json;
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
    public async Task Recovered_order_update_preserves_price_discount_and_reserved_quantity()
    {
        var userId = Guid.NewGuid();
        var customerPartyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var updatedCustomerPartyId = Guid.NewGuid();
        var updatedCustomerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var ordersWarehouseId = Guid.NewGuid();
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
               N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());

            INSERT dbo.Customers(CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
            VALUES
              (@CustomerId,@PartyId,@BusinessId,1,@UserId,SYSDATETIMEOFFSET()),
              (@UpdatedCustomerId,@UpdatedPartyId,@BusinessId,1,@UserId,SYSDATETIMEOFFSET());

            INSERT dbo.Warehouses(
              WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,
              IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive,CreatedAt)
            VALUES(
              @OrdersWarehouseId,@BusinessId,N'PED',N'Pedidos edición',0,
              1,0,0,0,1,SYSDATETIMEOFFSET());

            INSERT dbo.InventoryBalances(
              BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
              InventoryValue,LastProcessingSequence,UpdatedAt)
            VALUES(@BusinessId,@OrdersWarehouseId,@ProductId,2,5000,10000,1,SYSDATETIMEOFFSET());

            INSERT dbo.Orders(
              OrderId,BusinessId,Source,FulfillmentMode,Status,CustomerId,
              CustomerNameSnapshot,CustomerDocumentSnapshot,Currency,
              Subtotal,DiscountTotal,Total,CustomerConfirmed,
              ExternalDocumentNumber,CustomAttributesJson,CreatedAt)
            VALUES(
              @OrderId,@BusinessId,1,0,2,@CustomerId,
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
            new("@BusinessId", fixture.BusinessId),
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
              OrderId,BusinessId,Source,FulfillmentMode,Status,
              CustomerNameSnapshot,CustomerDocumentSnapshot,Currency,
              Subtotal,DiscountTotal,Total,CustomerConfirmed,
              ExternalDocumentNumber,CreatedAt)
            VALUES(
              @OrderId,@BusinessId,0,0,2,
              N'Cliente pedido',N'123456789',N'COP',
              20000,1000,19000,1,N'PED-PRUEBA-01',DATEADD(day,-4,SYSUTCDATETIME()));

            INSERT dbo.OrderItems(
              OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,
              ProductNameSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,
              DiscountAmount,LineTotal,CreatedAt)
            VALUES(
              @ItemId,@OrderId,@BusinessId,@ProductId,N'P-E2E',N'P-E2E',
              N'Producto del pedido',N'EA',2,10000,
              1000,19000,DATEADD(day,-4,SYSUTCDATETIME()));

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
            Assert.Equal(10_000m, line.UnitPrice);
            Assert.Equal(1_000m, line.Discount);
            Assert.Equal(5m, line.TaxRate);
            Assert.Equal(950m, line.Tax);
            Assert.Equal(19_950m, recovered.PayableAmount);

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
                "UPDATE dbo.Products SET TaxProfileId=NULL WHERE ProductId=@ProductId; DELETE dbo.TaxProfiles WHERE TaxProfileId=@TaxProfileId;",
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
              OrderId,BusinessId,Source,FulfillmentMode,Status,
              CustomerNameSnapshot,CustomerDocumentSnapshot,Currency,
              Subtotal,DiscountTotal,Total,CustomerConfirmed,
              ExternalDocumentNumber,CreatedAt)
            VALUES
              (@FirstOrderId,@BusinessId,0,0,2,N'Cliente A',N'1001',N'COP',
               10000,0,10000,1,N'PED-CAMBIO-A',SYSUTCDATETIME()),
              (@SecondOrderId,@BusinessId,0,0,2,N'Cliente B',N'1002',N'COP',
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
