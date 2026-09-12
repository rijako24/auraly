using System.Data;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class OnlineSalesTemporaryTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Product_search_returns_a_resolved_page_of_fifty_in_under_one_second()
    {
        var prefix = $"PERF-{Guid.NewGuid():N}";
        var products = Enumerable.Range(1, 60).Select(index => new
        {
            ProductId = Guid.NewGuid(),
            Code = $"{prefix}-{index:D3}",
            Name = $"Producto rendimiento {prefix} {index:D3}",
            Price = 10_000m + index
        }).ToArray();
        var productsJson = System.Text.Json.JsonSerializer.Serialize(products);
        var jsonParameter = new SqlParameter("@ProductsJson", SqlDbType.NVarChar, -1)
        {
            Value = productsJson
        };
        await ExecuteAsync(
            """
            INSERT dbo.Products(
              ProductId,TenantId,BusinessId,Sku,
              Name,Currency,ManageStock,IsActive,CreatedAt)
            SELECT input.ProductId,@TenantId,@BusinessId,input.Code,
                   input.Name,N'COP',0,1,SYSUTCDATETIME()
            FROM OPENJSON(@ProductsJson) WITH(
              ProductId uniqueidentifier '$.ProductId',Code nvarchar(64) '$.Code',
              Name nvarchar(250) '$.Name',Price decimal(19,4) '$.Price') input;
            INSERT dbo.ProductPrices(
              ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,
              ValidFrom,IsActive,CreatedAt)
            SELECT NEWID(),@BusinessId,input.ProductId,input.Price,input.Price,N'COP',
                   DATEADD(day,-1,SYSDATETIMEOFFSET()),1,SYSDATETIMEOFFSET()
            FROM OPENJSON(@ProductsJson) WITH(
              ProductId uniqueidentifier '$.ProductId',Price decimal(19,4) '$.Price') input;
            """,
            new("@TenantId", fixture.TenantId),
            new("@BusinessId", fixture.BusinessId),
            jsonParameter);

        try
        {
            using var client = fixture.CreateAdminClient(CommercePermissionCodes.SalesCreate);
            var context = new OnlineSalesDraftContext(
                fixture.BusinessId, fixture.WarehouseId, fixture.WorkSessionId);
            var stopwatch = Stopwatch.StartNew();
            using var response = await client.PostAsJsonAsync(
                "/api/commerce/v1/pos/drafts/products/search",
                new SearchOnlineSalesRequest(context, prefix, 0, 50));
            response.EnsureSuccessStatusCode();
            var page = await response.Content.ReadFromJsonAsync<OnlineSalesProductPage>();
            stopwatch.Stop();

            Assert.NotNull(page);
            Assert.Equal(50, page.Items.Count);
            Assert.True(page.HasMore);
            Assert.Equal(50, page.NextOffset);
            Assert.All(page.Items, item =>
            {
                Assert.StartsWith(prefix, item.ProductCode, StringComparison.Ordinal);
                Assert.StartsWith($"Producto rendimiento {prefix}", item.Name, StringComparison.Ordinal);
                Assert.True(item.UnitPrice > 0);
                Assert.False(string.IsNullOrWhiteSpace(item.PriceSource));
            });
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(1),
                $"La primera página resuelta tardó {stopwatch.Elapsed.TotalMilliseconds:N0} ms.");
        }
        finally
        {
            await ExecuteAsync(
                """
                DELETE price FROM dbo.ProductPrices price
                JOIN OPENJSON(@ProductsJson) WITH(ProductId uniqueidentifier '$.ProductId') input
                  ON input.ProductId=price.ProductId;
                DELETE product FROM dbo.Products product
                JOIN OPENJSON(@ProductsJson) WITH(ProductId uniqueidentifier '$.ProductId') input
                  ON input.ProductId=product.ProductId;
                """,
                new SqlParameter("@ProductsJson", SqlDbType.NVarChar, -1)
                {
                    Value = productsJson
                });
        }
    }

    [Fact]
    public async Task Search_pause_restart_recover_and_remove_are_durable()
    {
        var userId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
              FirstName,LastName,IsActive,CreatedAt)
            VALUES(
              @UserId,@TenantId,@Username,@NormalizedUsername,@Email,@NormalizedEmail,
              N'Venta',N'Espera',1,SYSDATETIMEOFFSET());
            INSERT dbo.Parties(
              PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,
              CreatedBy,CreatedAt)
            VALUES(
              @PartyId,@TenantId,N'NaturalPerson',N'Cliente espera online',
              N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.Customers(
              CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
            VALUES(
              @CustomerId,@PartyId,@BusinessId,1,@UserId,SYSDATETIMEOFFSET());
            """,
            new("@UserId", userId),
            new("@TenantId", fixture.TenantId),
            new("@Username", $"wait-{userId:N}"),
            new("@NormalizedUsername", $"WAIT-{userId:N}".ToUpperInvariant()),
            new("@Email", $"wait-{userId:N}@test.local"),
            new("@NormalizedEmail", $"WAIT-{userId:N}@TEST.LOCAL"),
            new("@PartyId", partyId),
            new("@CustomerId", customerId),
            new("@BusinessId", fixture.BusinessId));

        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            CommercePermissionCodes.SalesRestartDraft,
            WorkSessionPermissionCodes.Open);
        client.Timeout = TimeSpan.FromSeconds(15);
        var workSession = await fixture.OpenWorkSessionAsync(client);
        var context = new OnlineSalesDraftContext(
            fixture.BusinessId,
            fixture.WarehouseId,
            workSession.WorkSessionId);

        using (var productsResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/pos/drafts/products/search",
                   new SearchOnlineSalesRequest(context, "P-E2E", 0, 50)))
        {
            productsResponse.EnsureSuccessStatusCode();
            var products = await productsResponse.Content
                .ReadFromJsonAsync<OnlineSalesProductPage>();
            Assert.NotNull(products);
            Assert.Contains(products.Items, item => item.ProductId == fixture.ProductId);
        }

        using (var customersResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/pos/drafts/customers/search",
                   new SearchOnlineSalesRequest(context, "Cliente espera", 0, 50)))
        {
            customersResponse.EnsureSuccessStatusCode();
            var customers = await customersResponse.Content
                .ReadFromJsonAsync<OnlineSalesCustomerPage>();
            Assert.NotNull(customers);
            Assert.Contains(customers.Items, item => item.CustomerId == customerId);
        }

        var draft = await OpenAsync(client, context);
        var captured = await MutateAsync<OnlineSalesDraft>(
            client,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items",
            new AddOnlineSalesDraftItemRequest("P-E2E", 1m, draft.Version));
        var pauseKey = Guid.NewGuid().ToString("N");
        var next = await MutateAsync<OnlineSalesDraft>(
            client,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/pause",
            new PauseOnlineSalesDraftRequest(
                "Cliente regresa", "Mesa 4", "Sin observación", captured.Version),
            pauseKey);
        Assert.NotEqual(draft.DraftId, next.DraftId);
        Assert.Empty(next.Lines);

        var replayedNext = await MutateAsync<OnlineSalesDraft>(
            client,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/pause",
            new PauseOnlineSalesDraftRequest(
                "Cliente regresa", "Mesa 4", "Sin observación", captured.Version),
            pauseKey);
        Assert.Equal(next.DraftId, replayedNext.DraftId);

        using var restarted = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            CommercePermissionCodes.SalesRestartDraft,
            WorkSessionPermissionCodes.Open);
        restarted.Timeout = TimeSpan.FromSeconds(15);
        var reopened = await OpenAsync(restarted, context);
        Assert.Equal(next.DraftId, reopened.DraftId);

        var temporaries = await ListAsync(restarted, context);
        var saved = Assert.Single(temporaries);
        Assert.Equal("Cliente regresa", saved.Name);
        Assert.Equal("Mesa 4", saved.Reference);
        Assert.Single(saved.Lines);

        var occupied = await MutateAsync<OnlineSalesDraft>(
            restarted,
            $"/api/commerce/v1/pos/drafts/{next.DraftId:D}/items",
            new AddOnlineSalesDraftItemRequest("P-E2E", 1m, next.Version));
        using (var invalidRecovery = Mutation(
                   $"/api/commerce/v1/pos/drafts/temporaries/{saved.DraftId:D}/recover",
                   new RecoverOnlineSalesDraftRequest(saved.Version, occupied.Version)))
        {
            using var response = await restarted.SendAsync(invalidRecovery);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var clean = await MutateAsync<OnlineSalesDraft>(
            restarted,
            $"/api/commerce/v1/pos/drafts/{occupied.DraftId:D}/reset",
            new ResetOnlineSalesDraftRequest(occupied.Version));
        var recovered = await MutateAsync<OnlineSalesDraft>(
            restarted,
            $"/api/commerce/v1/pos/drafts/temporaries/{saved.DraftId:D}/recover",
            new RecoverOnlineSalesDraftRequest(saved.Version, clean.Version));
        Assert.Equal(saved.DraftId, recovered.DraftId);
        Assert.Equal("Active", recovered.Status);
        Assert.Single(recovered.Lines);

        var balanceExisted = await ScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;",
            new("@BusinessId", fixture.BusinessId), new("@WarehouseId", fixture.WarehouseId),
            new("@ProductId", fixture.ProductId)) > 0;
        var previousStock = balanceExisted
            ? await ScalarAsync<decimal>(
                "SELECT QuantityOnHand FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;",
                new("@BusinessId", fixture.BusinessId), new("@WarehouseId", fixture.WarehouseId),
                new("@ProductId", fixture.ProductId))
            : 0m;
        var previousNegativeStockPolicy = await ScalarAsync<bool>(
            "SELECT AllowNegativeStockSales FROM dbo.Warehouses WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId;",
            new("@BusinessId", fixture.BusinessId), new("@WarehouseId", fixture.WarehouseId));
        try
        {
            await ExecuteAsync(
                """
                UPDATE dbo.Warehouses SET AllowNegativeStockSales=0
                WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId;
                IF EXISTS(SELECT 1 FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId)
                  UPDATE dbo.InventoryBalances SET QuantityOnHand=0,InventoryValue=0,UpdatedAt=SYSDATETIMEOFFSET()
                  WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;
                ELSE
                  INSERT dbo.InventoryBalances(BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,InventoryValue,LastProcessingSequence,UpdatedAt)
                  VALUES(@BusinessId,@WarehouseId,@ProductId,0,0,0,1,SYSDATETIMEOFFSET());
                """,
                new("@BusinessId", fixture.BusinessId), new("@WarehouseId", fixture.WarehouseId),
                new("@ProductId", fixture.ProductId));
            var validation = await restarted.GetFromJsonAsync<OnlineSalesInventoryValidation>(
                $"/api/commerce/v1/pos/drafts/{recovered.DraftId:D}/inventory-validation");
            Assert.NotNull(validation);
            Assert.True(validation.WasValidated);
            Assert.False(validation.IsValid);
            var issue = Assert.Single(validation.Issues);
            Assert.Equal(recovered.Lines[0].LineId, issue.LineId);
            Assert.Equal(1m, issue.RequestedQuantity);
            Assert.Equal(0m, issue.AvailableQuantity);
        }
        finally
        {
            await ExecuteAsync(
                "UPDATE dbo.Warehouses SET AllowNegativeStockSales=@AllowNegative WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId;",
                new("@AllowNegative", previousNegativeStockPolicy), new("@BusinessId", fixture.BusinessId),
                new("@WarehouseId", fixture.WarehouseId));
            if (balanceExisted)
                await ExecuteAsync(
                    "UPDATE dbo.InventoryBalances SET QuantityOnHand=@Quantity,UpdatedAt=SYSDATETIMEOFFSET() WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;",
                    new("@Quantity", previousStock), new("@BusinessId", fixture.BusinessId),
                    new("@WarehouseId", fixture.WarehouseId), new("@ProductId", fixture.ProductId));
            else
                await ExecuteAsync(
                    "DELETE dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;",
                    new("@BusinessId", fixture.BusinessId), new("@WarehouseId", fixture.WarehouseId),
                    new("@ProductId", fixture.ProductId));
        }

        var afterSecondPause = await MutateAsync<OnlineSalesDraft>(
            restarted,
            $"/api/commerce/v1/pos/drafts/{recovered.DraftId:D}/pause",
            new PauseOnlineSalesDraftRequest(
                "Eliminar luego", null, null, recovered.Version));
        var savedAgain = Assert.Single(await ListAsync(restarted, context));
        Assert.Equal("Eliminar luego", savedAgain.Name);

        using (var unauthorizedRemoval = Mutation(
                   $"/api/commerce/v1/pos/drafts/temporaries/{savedAgain.DraftId:D}/remove",
                   new RemoveOnlineSalesTemporaryRequest(savedAgain.Version)))
        using (var unauthorizedResponse = await restarted.SendAsync(unauthorizedRemoval))
            Assert.Equal((HttpStatusCode)428, unauthorizedResponse.StatusCode);

        using var privileged = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            CommercePermissionCodes.SalesRestartDraft,
            CommercePermissionCodes.SalesDeletePausedDraft,
            WorkSessionPermissionCodes.Open);
        var stillActive = await MutateAsync<OnlineSalesDraft>(
            privileged,
            $"/api/commerce/v1/pos/drafts/temporaries/{savedAgain.DraftId:D}/remove",
            new RemoveOnlineSalesTemporaryRequest(savedAgain.Version));
        Assert.Equal(afterSecondPause.DraftId, stillActive.DraftId);
        Assert.Empty(await ListAsync(restarted, context));
    }

    private static async Task<OnlineSalesDraft> OpenAsync(
        HttpClient client,
        OnlineSalesDraftContext context)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/drafts/active",
            new OpenOnlineSalesDraftRequest(context));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("Empty draft response.");
    }

    private static async Task<IReadOnlyList<OnlineSalesDraft>> ListAsync(
        HttpClient client,
        OnlineSalesDraftContext context)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/drafts/temporaries/search",
            new SearchOnlineSalesRequest(context));
        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<IReadOnlyList<OnlineSalesDraft>>()
            ?? throw new InvalidOperationException("Empty temporary response.");
    }

    private static async Task<T> MutateAsync<T>(
        HttpClient client,
        string path,
        object body,
        string? idempotencyKey = null)
    {
        using var request = Mutation(path, body, idempotencyKey);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException("Empty mutation response.");
    }

    private static HttpRequestMessage Mutation(
        string path,
        object body,
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add(
            "Idempotency-Key",
            idempotencyKey ?? Guid.NewGuid().ToString("N"));
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
            ?? throw new InvalidOperationException("Expected scalar value was not returned.");
        return (T)Convert.ChangeType(value, typeof(T));
    }
}
