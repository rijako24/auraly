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
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
              FirstName,LastName,IsActive,CreatedAt)
            VALUES(
              @UserId,@TenantId,@Username,UPPER(@Username),
              CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),
              N'Cambio',N'Pedido',1,SYSDATETIMEOFFSET());

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
            new("@TenantId", fixture.TenantId),
            new("@Username", $"order-switch-{userId:N}"),
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
        response.EnsureSuccessStatusCode();
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
