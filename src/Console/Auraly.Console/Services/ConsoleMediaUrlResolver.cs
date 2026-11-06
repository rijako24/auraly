using Auraly.Platform.Application.Services;

namespace Auraly.Console.Services;

/// <summary>
/// Stub de IMediaUrlResolver para el simulador de consola (sin Azure Blob).
/// </summary>
public sealed class ConsoleMediaUrlResolver : IMediaUrlResolver
{
    public async Task<IReadOnlyDictionary<string, string>> ResolveTenantBatchAsync(
        Guid tenantId, IReadOnlyCollection<string> mediaRefs, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reference in mediaRefs.Distinct(StringComparer.Ordinal))
            result.Add(reference, await ResolveTenantAsync(tenantId, reference, ct));
        return result;
    }

    public Task<string> ResolveAsync(Guid businessId, string mediaRef, CancellationToken ct = default)
    {
        if (Uri.TryCreate(mediaRef, UriKind.Absolute, out var uri) && uri.Scheme == "https")
            return Task.FromResult(mediaRef);

        return Task.FromResult($"console://blob/{businessId:D}/{mediaRef}");
    }

    public Task<string> ResolveTenantAsync(Guid tenantId, string mediaRef, CancellationToken ct = default)
    {
        if (Uri.TryCreate(mediaRef, UriKind.Absolute, out var uri) && uri.Scheme == "https")
            return Task.FromResult(mediaRef);

        return Task.FromResult($"console://tenant-blob/{tenantId:D}/{mediaRef}");
    }
}
