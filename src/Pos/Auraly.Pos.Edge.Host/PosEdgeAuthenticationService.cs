namespace Auraly.Pos.Edge.Host;

public sealed class PosEdgeAuthenticationService(
    PosLocalIdentityStore identities,
    PosIdentitySynchronizer identitySynchronization)
{
    public async Task<PosLocalUserSession> LoginAsync(
        PosLocalLoginRequest request,
        CancellationToken cancellationToken = default)
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
