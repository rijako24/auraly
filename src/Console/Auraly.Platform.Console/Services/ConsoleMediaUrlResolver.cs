using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Console.Services;

/// <summary>
/// Stub de IMediaUrlResolver para el simulador de consola (sin Azure Blob).
/// </summary>
public sealed class ConsoleMediaUrlResolver : IMediaUrlResolver
{
    public Task<string> ResolveAsync(Guid businessId, string mediaRef, CancellationToken ct = default)
    {
        if (Uri.TryCreate(mediaRef, UriKind.Absolute, out var uri) && uri.Scheme == "https")
            return Task.FromResult(mediaRef);

        return Task.FromResult($"console://blob/{businessId:D}/{mediaRef}");
    }
}
