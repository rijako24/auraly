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
        var cursor = await identities.SecurityCursorAsync(cancellationToken);
        if (cursor is null)
        {
            var snapshot = await GetAsync<PosOfflineIdentitySnapshot>(
                $"/api/pos/v1/identity/snapshot?businessId={scope.BusinessId:D}", cancellationToken);
            await identities.ApplySnapshotAsync(snapshot, cancellationToken);
            RecordSnapshotChanges(previous, snapshot.Users);
            return;
        }

        while (true)
        {
            var page = await GetAsync<PosOfflineIdentityDeltaPage>(
                $"/api/pos/v1/identity/changes?businessId={scope.BusinessId:D}&cursor={cursor.Value}&pageSize=250",
                cancellationToken);
            await identities.ApplyChangesAsync(page, cancellationToken);
            foreach (var change in page.Changes)
            {
                previous.TryGetValue(change.UserId, out var prior);
                if (change.User is { } user) events.UserReceived(user, prior);
                else if (prior is not null) events.UserRemoved(prior);
            }
            cursor = page.ToCursor;
            if (!page.HasMore) break;
        }
    }

    private async Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Auraly Server returned an empty identity response.");
    }

    private void RecordSnapshotChanges(
        IReadOnlyDictionary<Guid, PosLocalIdentitySummary> previous,
        IReadOnlyList<PosOfflineUserProjection> users)
    {
        var receivedIds = users.Select(user => user.UserId).ToHashSet();
        foreach (var user in users)
        {
            previous.TryGetValue(user.UserId, out var prior);
            var changed = prior is null || prior.Username != user.Username ||
                prior.DisplayName != user.DisplayName ||
                prior.PasswordChangedAt != user.PasswordVerifier.ChangedAt ||
                !prior.Permissions.SequenceEqual(user.Permissions.Order(StringComparer.Ordinal), StringComparer.Ordinal);
            if (changed) events.UserReceived(user, prior);
        }
        foreach (var removed in previous.Values.Where(user => !receivedIds.Contains(user.UserId)))
            events.UserRemoved(removed);
    }
}
