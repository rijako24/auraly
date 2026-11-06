using System.Net;
using Auraly.Api;
using Auraly.BuildingBlocks.Application.Synchronization;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Messaging.WebPubSub;
using Microsoft.AspNetCore.WebUtilities;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed class AzureWebPubSubSynchronizationGatewayTests
{
    [Theory]
    [InlineData("User")]
    [InlineData("Device")]
    [InlineData("RestaurantCommands")]
    public async Task Managed_identity_negotiation_preserves_exact_authorized_groups(string scope)
    {
        var tenant = Guid.NewGuid();
        var business = Guid.NewGuid();
        var actor = Guid.NewGuid();
        using var transport = new TokenRequestHandler();
        using var http = new HttpClient(transport);
        var gateway = new AzureWebPubSubSynchronizationGateway(new WebPubSubServiceClient(
            new Uri("https://push.test"), "auraly_pos", new TestCredential(),
            new WebPubSubServiceClientOptions { Transport = new HttpClientTransport(http) }));

        var uri = scope switch
        {
            "User" => await gateway.CreateUserClientAccessUriAsync(tenant, business, actor),
            "Device" => await gateway.CreateClientAccessUriAsync(tenant, business, actor),
            _ => await gateway.CreateRestaurantCommandsClientAccessUriAsync(tenant, business, actor)
        };

        Assert.Equal("wss", uri.Scheme);
        Assert.Equal(1, transport.RequestCount);
        var query = QueryHelpers.ParseQuery(transport.RequestUri!.Query);
        string[] expected = scope switch
        {
            "User" => [PosSynchronizationGroups.Business(tenant, business), PosSynchronizationGroups.User(tenant, actor)],
            "Device" => [PosSynchronizationGroups.Business(tenant, business), PosSynchronizationGroups.Device(tenant, actor)],
            _ => [PosSynchronizationGroups.RestaurantCommands(tenant, business)]
        };
        Assert.Equal(expected.Order(), query["group"].ToArray().Order());
        Assert.Equal(expected.Select(group => $"webpubsub.joinLeaveGroup.{group}").Order(), query["role"].ToArray().Order());
        Assert.Equal(actor.ToString("D"), query["userId"].ToString());
        // The SDK rounds the remaining duration down to whole minutes.
        Assert.InRange(int.Parse(query["minutesToExpire"].ToString()), 14, 15);
    }

    private sealed class TestCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("test-only-credential", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class TokenRequestHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public int RequestCount { get; private set; }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestCount++;
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"token\":\"test-only-client-token\"}") };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }
}
