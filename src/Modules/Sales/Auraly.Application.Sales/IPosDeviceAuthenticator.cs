namespace Auraly.Application.Sales;

public interface IPosDeviceAuthenticator
{
    Task<PosDeviceIdentity?> AuthenticateAsync(
        Guid deviceId,
        string secret,
        CancellationToken cancellationToken = default);
}

