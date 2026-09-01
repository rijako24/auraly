using Auraly.Pos.Edge.Host;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosWorkSessionClosureFallbackTests
{
    [Theory]
    [InlineData(404)]
    [InlineData(408)]
    [InlineData(409)]
    [InlineData(500)]
    [InlineData(503)]
    public void Missing_stale_or_unavailable_server_session_uses_the_local_closure(
        int statusCode)
    {
        Assert.True(PosWorkSessionClosureEndpoints.CanUseOfflineFallback(
            new PosWorkSessionClosureException(statusCode, "remote closure unavailable"),
            CancellationToken.None));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(422)]
    public void Invalid_or_unauthorized_requests_do_not_bypass_the_server_rule(
        int statusCode)
    {
        Assert.False(PosWorkSessionClosureEndpoints.CanUseOfflineFallback(
            new PosWorkSessionClosureException(statusCode, "request rejected"),
            CancellationToken.None));
    }
}
