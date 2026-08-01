namespace Auraly.Pos.Edge.Host;

public sealed class PosEdgeAuthenticationService(
    PosLocalIdentityStore identities,
    PosOfflineLeaseStore leases,
    PosOfflineLeaseClient server)
{
    public async Task<PosLocalUserSession> LoginAsync(
        PosLocalLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        PosValidatedOfflineLease lease;
        try
        {
            var response = await server.AcquireAsync(request, cancellationToken);
            await identities.ApplyLeaseUserAsync(response.User, cancellationToken);
            lease = await leases.SaveAsync(response, cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is null)
        {
            lease = await leases.RequireForUserAsync(
                request.Username, cancellationToken);
        }

        return await identities.LoginAsync(
            request,
            lease.Payload.UserId,
            lease.Payload.ExpiresAt,
            cancellationToken);
    }

    public async Task LogoutAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        var session = await identities.ResolveAsync(token, cancellationToken);
        await identities.LogoutAsync(token, cancellationToken);
        if (session is null) return;

        await leases.QueueReleaseAsync(session.UserId, cancellationToken);
        try
        {
            await ReleasePendingAsync(cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is null)
        {
        }
    }

    public async Task<bool> ReleasePendingAsync(
        CancellationToken cancellationToken = default)
    {
        var leaseId = await leases.PendingReleaseAsync(cancellationToken);
        if (leaseId is null) return false;
        await server.ReleaseAsync(leaseId.Value, cancellationToken);
        await leases.MarkReleasedAsync(leaseId.Value, cancellationToken);
        return true;
    }
}
