using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed class PosIdentitySynchronizer(
    HttpClient http,
    PosDeviceCredentials credentials,
    PosLocalIdentityStore identities)
{
    public async Task SynchronizeAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "/api/pos/v1/identity/snapshot");
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var snapshot = await response.Content.ReadFromJsonAsync<PosOfflineIdentitySnapshot>(
            cancellationToken)
            ?? throw new InvalidDataException(
                "Auraly Server returned an empty POS identity snapshot.");
        await identities.ApplySnapshotAsync(snapshot, cancellationToken);
    }
}
