namespace Auraly.Platform.Application.Services;

/// <summary>Resolves a private application file selected by trusted server configuration.
/// The caller owns authorization; container and blob must never come from user input.</summary>
public interface IApplicationFileUrlResolver
{
    Task<string> ResolveApplicationFileAsync(string containerName, string blobName,
        CancellationToken ct = default);
}
