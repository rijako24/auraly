namespace Auraly.Pos.Edge.Host;

public sealed class PosEdgeAuthenticationService(
    PosLocalIdentityStore identities,
    PosIdentitySynchronizer identitySynchronization,
    PosWorkSessionOpenServerClient workSessions,
    ILogger<PosEdgeAuthenticationService> logger)
{
    public async Task<PosLocalUserSession> LoginAsync(
        PosLocalLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await LoginLocalAsync(request, cancellationToken);
        try
        {
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
                "The server work session could not be activated; local offline login remains available.");
        }
        return session;
    }

    private async Task<PosLocalUserSession> LoginLocalAsync(
        PosLocalLoginRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await identities.LoginAsync(request, cancellationToken);
        }
        catch (PosLocalLoginException exception) when (
            exception.Code is "InvalidCredentials" or "IdentityUnavailable")
        {
            if (exception.Code == "InvalidCredentials" &&
                await identities.ContainsUserAsync(request.Username, cancellationToken))
                throw;
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
                return await identities.LoginAsync(request, cancellationToken);
            }

            if (!await identities.ContainsUserAsync(request.Username, cancellationToken))
                throw new PosLocalLoginException(
                    "CloudLoginRequired",
                    "Este usuario no tiene acceso local en el equipo. Auraly intentará iniciar la sesión administrativa en el servidor.");

            return await identities.LoginAsync(request, cancellationToken);
        }
    }

    public async Task LogoutAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        await identities.LogoutAsync(token, cancellationToken);
    }
}
