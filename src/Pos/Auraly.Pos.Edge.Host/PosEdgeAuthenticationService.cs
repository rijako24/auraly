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
        return (await LoginLocalAsync(request, cancellationToken)).Session;
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
            if (exception.Code == "IdentityUnavailable")
            {
                try
                {
                    await identitySynchronization.SynchronizeAsync(cancellationToken);
                    return new LocalLoginResult(
                        await identities.LoginAsync(request, cancellationToken),
                        null);
                }
                catch (HttpRequestException)
                {
                    return await LoginWithTargetedLeaseAsync(
                        request, exception, cancellationToken);
                }
            }
            var localUserExists = await identities.ContainsUserAsync(
                request.Username, cancellationToken);
            if (!localUserExists &&
                await identities.SecurityCursorAsync(cancellationToken) is null)
            {
                try
                {
                    await identitySynchronization.SynchronizeAsync(cancellationToken);
                    if (await identities.ContainsUserAsync(
                        request.Username, cancellationToken))
                    {
                        return new LocalLoginResult(
                            await identities.LoginAsync(request, cancellationToken),
                            null);
                    }
                }
                catch (HttpRequestException error)
                {
                    logger.LogWarning(
                        error,
                        "Legacy local identity initialization failed; a targeted user lease will be attempted.");
                }
            }
            try
            {
                return await LoginWithTargetedLeaseAsync(
                    request, exception, cancellationToken);
            }
            catch (PosLocalLoginException) when (!localUserExists)
            {
                throw new PosLocalLoginException(
                    "CloudLoginRequired",
                    "Este usuario no tiene acceso local en el equipo. Auraly intentará iniciar la sesión administrativa en el servidor.");
            }
        }
    }

    private async Task<LocalLoginResult> LoginWithTargetedLeaseAsync(
        PosLocalLoginRequest request,
        PosLocalLoginException originalException,
        CancellationToken cancellationToken)
    {
        try
        {
            var lease = await offlineLeases.AcquireAsync(request, cancellationToken);
            await offlineLeaseStore.SaveAsync(lease, cancellationToken);
            await identities.ApplyLeaseUserAsync(lease.User, cancellationToken);
            return new LocalLoginResult(
                await identities.LoginAsync(request, cancellationToken),
                lease);
        }
        catch (HttpRequestException)
        {
            throw originalException;
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
