using Auraly.Contracts.Authentication;

namespace Auraly.Pos.Edge.Host;

public sealed class PosEdgeAuthenticationService(
    PosLocalIdentityStore identities,
    PosIdentitySynchronizer identitySynchronization,
    PosOfflineLeaseClient offlineLeases,
    PosOfflineLeaseStore offlineLeaseStore,
    PosWorkSessionOpenServerClient workSessions,
    ILogger<PosEdgeAuthenticationService> logger)
{
    public async Task<PosLocalUserSession> LoginAsync(
        PosLocalLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var login = await LoginLocalAsync(request, cancellationToken);
        var session = login.Session;
        try
        {
            // A connected login on an enrolled POS is the canonical handoff to
            // this device. The server acquisition revokes every online browser
            // session for the same user before the POS starts operating.
            if (login.AcquiredLease is null)
            {
                var lease = await offlineLeases.AcquireAsync(request, cancellationToken);
                await offlineLeaseStore.SaveAsync(lease, cancellationToken);
                await identities.ApplyLeaseUserAsync(lease.User, cancellationToken);
            }
            var active = await workSessions.OpenOrResumeAsync(
                session, cancellationToken);
            if (active.WorkSessionId != session.WorkSessionId)
            {
                await identities.AssignWorkSessionAsync(
                    session.SessionId, active.WorkSessionId, cancellationToken);
                session = session with { WorkSessionId = active.WorkSessionId };
            }
        }
        catch (HttpRequestException error)
        {
            logger.LogWarning(
                error,
                "The server authentication handoff or work session could not be activated; local offline login remains available.");
        }
        return session;
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
