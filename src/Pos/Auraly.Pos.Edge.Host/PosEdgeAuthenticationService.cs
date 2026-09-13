using Auraly.Contracts.Authentication;

namespace Auraly.Pos.Edge.Host;

public sealed class PosEdgeAuthenticationService(PosLocalIdentityStore identities)
{
    public async Task<PosLocalUserSession> LoginAsync(
        PosLocalLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        return await identities.LoginAsync(request, cancellationToken);
    }

    public async Task LogoutAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        await identities.LogoutAsync(token, cancellationToken);
    }
}
