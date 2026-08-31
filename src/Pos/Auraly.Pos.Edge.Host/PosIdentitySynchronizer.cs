using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed class PosIdentitySynchronizer(
    HttpClient http,
    PosDeviceCredentials credentials,
    PosOperationalScope scope,
    PosLocalIdentityStore identities,
    PosSynchronizationEventLog events)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task SynchronizeAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await SynchronizeCoreAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SynchronizeIfUserMissingAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!await identities.ContainsUserAsync(username, cancellationToken))
                await SynchronizeCoreAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SynchronizeCoreAsync(CancellationToken cancellationToken)
    {
        var previous = (await identities.ReadIdentitySummariesAsync(cancellationToken))
            .ToDictionary(user => user.UserId);
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/pos/v1/identity/snapshot?businessId={scope.BusinessId:D}");
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var snapshot = await response.Content.ReadFromJsonAsync<PosOfflineIdentitySnapshot>(
            cancellationToken)
            ?? throw new InvalidDataException(
                "Auraly Server returned an empty POS identity snapshot.");
        await identities.ApplySnapshotAsync(snapshot, cancellationToken);
        var receivedIds = snapshot.Users.Select(user => user.UserId).ToHashSet();
        foreach (var user in snapshot.Users)
        {
            previous.TryGetValue(user.UserId, out var prior);
            var changed = prior is null ||
                !string.Equals(prior.Username, user.Username, StringComparison.Ordinal) ||
                !string.Equals(prior.DisplayName, user.DisplayName, StringComparison.Ordinal) ||
                prior.PasswordChangedAt != user.PasswordVerifier.ChangedAt ||
                !prior.Permissions.SequenceEqual(
                    user.Permissions.Order(StringComparer.Ordinal), StringComparer.Ordinal);
            if (changed) events.UserReceived(user, prior);
        }
        foreach (var removed in previous.Values.Where(user => !receivedIds.Contains(user.UserId)))
            events.UserRemoved(removed);
    }
}
