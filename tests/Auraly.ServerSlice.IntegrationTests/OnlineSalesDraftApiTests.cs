using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class OnlineSalesDraftApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Draft_survives_client_restart_and_mutations_are_idempotent()
    {
        var context = new OnlineSalesDraftContext(
            fixture.BusinessId,
            fixture.LocationId,
            fixture.OnlineRegisterId);
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
                $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/lines",
                new AddOnlineSalesDraftProductRequest(
                    fixture.ProductId, 1m, opened.Version),
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
                $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/lines",
                new AddOnlineSalesDraftProductRequest(
                    fixture.ProductId, 1m, opened.Version),
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
            CommercePermissionCodes.SalesCreate);
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
            "reset-draft");
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
                fixture.LocationId,
                fixture.OnlineRegisterId));

        using var first = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/lines",
            new AddOnlineSalesDraftProductRequest(
                fixture.ProductId, 1m, opened.Version),
            $"tab-a-{Guid.NewGuid():N}");
        using var second = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/lines",
            new AddOnlineSalesDraftProductRequest(
                fixture.ProductId, 2m, opened.Version),
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
                fixture.LocationId,
                fixture.OnlineRegisterId)));
        Assert.Equal(HttpStatusCode.Forbidden, permissionResponse.StatusCode);

        using var allowed = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate);
        using var scopeResponse = await allowed.PostAsJsonAsync(
            "/api/commerce/v1/pos/drafts/active",
            new OpenOnlineSalesDraftRequest(new(
                Guid.NewGuid(),
                fixture.LocationId,
                fixture.OnlineRegisterId)));
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
