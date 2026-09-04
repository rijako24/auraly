using Auraly.Contracts.Authentication;

namespace Auraly.Pos.Edge.Host;

public sealed class PosEdgeAuthenticationService(
    PosLocalIdentityStore identities,
    PosIdentitySynchronizer identitySynchronization,
    PosOfflineLeaseClient offlineLeases,
    PosOfflineLeaseStore offlineLeaseStore,
    ILogger<PosEdgeAuthenticationService> logger)
{
    public async Task<PosLocalUserSession> LoginAsync(
        PosLocalLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var login = await LoginLocalAsync(request, cancellationToken);
        try
        {
            // Authentication may refresh the signed offline lease while connected.
            // Operational WorkSessions are deliberately opened only by the POS
            // entry point and are never acquired or replaced here.
            if (login.AcquiredLease is null)
            {
                var lease = await offlineLeases.AcquireAsync(request, cancellationToken);
                await offlineLeaseStore.SaveAsync(lease, cancellationToken);
                await identities.ApplyLeaseUserAsync(lease.User, cancellationToken);
            }
        }
        catch (HttpRequestException error)
        {
            logger.LogWarning(
                error,
                "The server authentication lease could not be refreshed; local offline login remains available.");
        }
        return login.Session;
    }

    private async Task<LocalLoginResult> LoginLocalAsync(
        PosLocalLoginRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return new LocalLoginResult(
                await identities.LoginAsync(request, cancellationToken),
                null);
        }
        catch (PosLocalLoginException exception) when (
            exception.Code is "InvalidCredentials" or "IdentityUnavailable")
        {
            if (exception.Code == "InvalidCredentials" &&
                await identities.ContainsUserAsync(request.Username, cancellationToken))
            {
                try
                {
                    var lease = await offlineLeases.AcquireAsync(
                        request, cancellationToken);
                    await offlineLeaseStore.SaveAsync(lease, cancellationToken);
                    await identities.ApplyLeaseUserAsync(
                        lease.User, cancellationToken);
                    return new LocalLoginResult(
                        await identities.LoginAsync(request, cancellationToken),
                        lease);
                }
                catch (HttpRequestException)
                {
                    throw exception;
                }
            }
            try
            {
                if (exception.Code == "IdentityUnavailable")
                    await identitySynchronization.SynchronizeAsync(cancellationToken);
                else
                    await identitySynchronization.SynchronizeIfUserMissingAsync(
                        request.Username, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return new LocalLoginResult(
                    await identities.LoginAsync(request, cancellationToken),
                    null);
            }

            if (!await identities.ContainsUserAsync(request.Username, cancellationToken))
                throw new PosLocalLoginException(
                    "CloudLoginRequired",
                    "Este usuario no tiene acceso local en el equipo. Auraly intentará iniciar la sesión administrativa en el servidor.");

            return new LocalLoginResult(
                await identities.LoginAsync(request, cancellationToken),
                null);
        }
    }

    private sealed record LocalLoginResult(
        PosLocalUserSession Session,
        OfflineAuthenticationLeaseAcquireResponse? AcquiredLease);

    public async Task LogoutAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        await identities.LogoutAsync(token, cancellationToken);
    }
}
