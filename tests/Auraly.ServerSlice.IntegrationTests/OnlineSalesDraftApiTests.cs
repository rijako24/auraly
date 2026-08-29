using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class OnlineSalesDraftApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Generic_product_accepts_document_only_name_cost_price_and_discount()
    {
        var productId = Guid.NewGuid();
        var priceId = Guid.NewGuid();
        var code = $"GEN-{productId:N}"[..20];
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = new SqlCommand(
                """
                INSERT dbo.Products
                  (ProductId,TenantId,BusinessId,Source,Sku,Name,
                   Currency,ManageStock,IsActive,CreatedAt)
                VALUES
                  (@ProductId,@TenantId,@BusinessId,0,@Code,N'Producto genérico',
                   N'COP',0,1,SYSUTCDATETIME());
                INSERT dbo.ProductPrices
                  (ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,
                   CostBasisType,CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,
                   InputMode,RoundingIncrement,RoundingMode,ValidFrom,IsActive,CreatedAt)
                VALUES
                  (@PriceId,@BusinessId,@ProductId,10000,10000,N'COP',
                   N'AverageCost',4000,60,60,N'SalePrice',1,N'Nearest',
                   DATEADD(day,-1,SYSDATETIMEOFFSET()),1,SYSDATETIMEOFFSET());
                """, connection);
            seed.Parameters.AddWithValue("@ProductId", productId);
            seed.Parameters.AddWithValue("@PriceId", priceId);
            seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@Code", code);
            await seed.ExecuteNonQueryAsync();
        }

        using var client = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate,
            CommercePermissionCodes.SalesChangePrice,
            CommercePermissionCodes.SalesRestartDraft);
        var opened = await OpenAsync(client, new(
            fixture.BusinessId, fixture.WarehouseId, fixture.WorkSessionId));
        if (opened.Lines.Count > 0)
        {
            using var reset = Mutation(
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/reset",
                new ResetOnlineSalesDraftRequest(opened.Version),
                Guid.NewGuid().ToString("D"));
            using var resetResponse = await client.SendAsync(reset);
            resetResponse.EnsureSuccessStatusCode();
            opened = await resetResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>()
                ?? throw new InvalidOperationException("The reset draft response was empty.");
        }
        using var add = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/items",
            new AddOnlineSalesDraftItemRequest(code, 2m, opened.Version),
            $"generic-add-{Guid.NewGuid():N}");
        using var addResponse = await client.SendAsync(add);
        addResponse.EnsureSuccessStatusCode();
        var captured = await addResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>();
        var line = Assert.Single(captured!.Lines);
        Assert.True(line.AllowsDocumentCostOverride);
        Assert.Equal(4_000m, line.DocumentUnitCost);

        using var update = Mutation(
            HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/lines",
            new UpdateOnlineSalesDraftLinesRequest(
                [new(line.LineId, "Servicio puntual", 12_000m, 2_000m, 4_500m)],
                captured.Version),
            Guid.NewGuid().ToString("D"));
        using var updateResponse = await client.SendAsync(update);
        updateResponse.EnsureSuccessStatusCode();
        var changed = await updateResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>();
        var changedLine = Assert.Single(changed!.Lines);
        Assert.Equal("Servicio puntual", changedLine.Description);
        Assert.Equal(12_000m, changedLine.UnitPrice);
        Assert.Equal(2_000m, changedLine.Discount);
        Assert.Equal(4_500m, changedLine.DocumentUnitCost);

        await using var checkConnection = new SqlConnection(fixture.ConnectionString);
        await checkConnection.OpenAsync();
        await using var check = new SqlCommand(
            """
            SELECT p.Name,pp.Amount,pp.CostBasisAmount,l.Description,l.UnitPrice,
                   l.DiscountAmount,l.DocumentUnitCost
            FROM dbo.Products p
            INNER JOIN dbo.ProductPrices pp ON pp.ProductId=p.ProductId AND pp.IsActive=1
            INNER JOIN dbo.SalesDraftLines l ON l.ProductId=p.ProductId
            WHERE p.ProductId=@ProductId AND l.SalesDraftId=@DraftId;
            """, checkConnection);
        check.Parameters.AddWithValue("@ProductId", productId);
        check.Parameters.AddWithValue("@DraftId", opened.DraftId);
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Producto genérico", reader.GetString(0));
        Assert.Equal(10_000m, reader.GetDecimal(1));
        Assert.Equal(4_000m, reader.GetDecimal(2));
        Assert.Equal("Servicio puntual", reader.GetString(3));
        Assert.Equal(12_000m, reader.GetDecimal(4));
        Assert.Equal(2_000m, reader.GetDecimal(5));
        Assert.Equal(4_500m, reader.GetDecimal(6));
        await reader.DisposeAsync();

        using var cleanup = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{changed.DraftId:D}/reset",
            new ResetOnlineSalesDraftRequest(changed.Version),
            Guid.NewGuid().ToString("D"));
        using var cleanupResponse = await client.SendAsync(cleanup);
        cleanupResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Stocked_product_preserves_inventory_cost_when_document_lines_are_applied()
    {
        using var client = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate,
            CommercePermissionCodes.SalesChangePrice,
            CommercePermissionCodes.SalesRestartDraft);
        var opened = await OpenAsync(client, new(
            fixture.BusinessId, fixture.WarehouseId, fixture.WorkSessionId));
        if (opened.Lines.Count > 0)
        {
            using var reset = Mutation(
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/reset",
                new ResetOnlineSalesDraftRequest(opened.Version),
                Guid.NewGuid().ToString("D"));
            using var resetResponse = await client.SendAsync(reset);
            resetResponse.EnsureSuccessStatusCode();
            opened = await resetResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>()
                ?? throw new InvalidOperationException("The reset draft response was empty.");
        }

        using var add = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/items",
            new AddOnlineSalesDraftItemRequest(fixture.ProductId.ToString("D"), 1m, opened.Version),
            $"stock-cost-add-{Guid.NewGuid():N}");
        using var addResponse = await client.SendAsync(add);
        addResponse.EnsureSuccessStatusCode();
        var captured = await addResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("The add item response was empty.");
        var line = Assert.Single(captured.Lines);
        Assert.False(line.AllowsDocumentCostOverride);

        using var update = Mutation(
            HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{captured.DraftId:D}/lines",
            new UpdateOnlineSalesDraftLinesRequest(
                [new(
                    line.LineId,
                    line.Description,
                    line.UnitPrice,
                    line.Discount,
                    line.DocumentUnitCost + 1_000m)],
                captured.Version),
            Guid.NewGuid().ToString("D"));
        using var updateResponse = await client.SendAsync(update);
        updateResponse.EnsureSuccessStatusCode();
        var changed = await updateResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("The update lines response was empty.");
        Assert.Equal(line.DocumentUnitCost, Assert.Single(changed.Lines).DocumentUnitCost);

        using var cleanup = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{changed.DraftId:D}/reset",
            new ResetOnlineSalesDraftRequest(changed.Version),
            Guid.NewGuid().ToString("D"));
        using var cleanupResponse = await client.SendAsync(cleanup);
        cleanupResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Draft_survives_client_restart_and_mutations_are_idempotent()
    {
        var context = new OnlineSalesDraftContext(
            fixture.BusinessId,
            fixture.WarehouseId,
            fixture.WorkSessionId);
        Guid draftId;
        Guid lineId;
        using (var firstClient = fixture.CreateAdminClient(
                   CommercePermissionCodes.SalesCreate))
        {
            var opened = await OpenAsync(firstClient, context);
            Assert.Equal(1, opened.Version);
            Assert.Empty(opened.Lines);

            using var add = Mutation(
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/items",
                new AddOnlineSalesDraftItemRequest(fixture.ProductId.ToString("D"), 1m, opened.Version),
                "add-product-once");
            using var addedResponse = await firstClient.SendAsync(add);
            addedResponse.EnsureSuccessStatusCode();
            var added = await addedResponse.Content
                .ReadFromJsonAsync<OnlineSalesDraft>();
            Assert.NotNull(added);
            Assert.Equal(2, added.Version);
            var line = Assert.Single(added.Lines);
            Assert.Equal(1m, line.Quantity);
            Assert.Equal(10_000m, added.PayableAmount);
            draftId = added.DraftId;
            lineId = line.LineId;

            using var duplicate = Mutation(
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/items",
                new AddOnlineSalesDraftItemRequest(fixture.ProductId.ToString("D"), 1m, opened.Version),
                "add-product-once");
            using var duplicateResponse = await firstClient.SendAsync(duplicate);
            duplicateResponse.EnsureSuccessStatusCode();
            var replayed = await duplicateResponse.Content
                .ReadFromJsonAsync<OnlineSalesDraft>();
            Assert.NotNull(replayed);
            Assert.Equal(2, replayed.Version);
            Assert.Equal(1m, Assert.Single(replayed.Lines).Quantity);
        }

        using var reopenedClient = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate,
            CommercePermissionCodes.SalesRestartDraft);
        var recovered = await OpenAsync(reopenedClient, context);
        Assert.Equal(draftId, recovered.DraftId);
        Assert.Equal(1m, Assert.Single(recovered.Lines).Quantity);

        using var quantity = Mutation(
            HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{draftId:D}/lines/{lineId:D}/quantity",
            new ChangeOnlineSalesDraftQuantityRequest(3m, recovered.Version),
            "quantity-three");
        using var quantityResponse = await reopenedClient.SendAsync(quantity);
        quantityResponse.EnsureSuccessStatusCode();
        var changed = await quantityResponse.Content
            .ReadFromJsonAsync<OnlineSalesDraft>();
        Assert.NotNull(changed);
        Assert.Equal(3m, Assert.Single(changed.Lines).Quantity);
        Assert.Equal(30_000m, changed.PayableAmount);

        using var reset = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draftId:D}/reset",
            new ResetOnlineSalesDraftRequest(changed.Version),
            Guid.NewGuid().ToString("D"));
        using var resetResponse = await reopenedClient.SendAsync(reset);
        resetResponse.EnsureSuccessStatusCode();
        var next = await resetResponse.Content
            .ReadFromJsonAsync<OnlineSalesDraft>();
        Assert.NotNull(next);
        Assert.NotEqual(draftId, next.DraftId);
        Assert.Empty(next.Lines);
        Assert.Equal(1, next.Version);

        var reopenedEmpty = await OpenAsync(reopenedClient, context);
        Assert.Equal(next.DraftId, reopenedEmpty.DraftId);
    }

    [Fact]
    public async Task Two_tabs_cannot_silently_overwrite_the_same_version()
    {
        using var client = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate);
        var opened = await OpenAsync(
            client,
            new(
                fixture.BusinessId,
                fixture.WarehouseId,
                fixture.WorkSessionId));

        using var first = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/items",
            new AddOnlineSalesDraftItemRequest(fixture.ProductId.ToString("D"), 1m, opened.Version),
            $"tab-a-{Guid.NewGuid():N}");
        using var second = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/items",
            new AddOnlineSalesDraftItemRequest(fixture.ProductId.ToString("D"), 2m, opened.Version),
            $"tab-b-{Guid.NewGuid():N}");

        var responses = await Task.WhenAll(
            client.SendAsync(first),
            client.SendAsync(second));
        try
        {
            Assert.Single(responses, response => response.IsSuccessStatusCode);
            Assert.Single(
                responses,
                response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task Draft_scope_and_permission_are_enforced_by_the_server()
    {
        using var denied = fixture.CreateAdminClient();
        using var permissionResponse = await denied.PostAsJsonAsync(
            "/api/commerce/v1/pos/drafts/active",
            new OpenOnlineSalesDraftRequest(new(
                fixture.BusinessId,
                fixture.WarehouseId,
                fixture.WorkSessionId)));
        Assert.Equal(HttpStatusCode.Forbidden, permissionResponse.StatusCode);

        using var allowed = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate);
        using var scopeResponse = await allowed.PostAsJsonAsync(
            "/api/commerce/v1/pos/drafts/active",
            new OpenOnlineSalesDraftRequest(new(
                Guid.NewGuid(),
                fixture.WarehouseId,
                fixture.WorkSessionId)));
        Assert.Equal(HttpStatusCode.Forbidden, scopeResponse.StatusCode);
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
            ?? throw new InvalidOperationException(
                "The online draft response was empty.");
    }

    private static HttpRequestMessage Mutation<T>(
        HttpMethod method,
        string path,
        T body,
        string idempotencyKey)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}
