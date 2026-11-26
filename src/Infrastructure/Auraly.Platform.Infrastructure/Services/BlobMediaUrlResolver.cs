using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Infrastructure.Services;

/// <summary>
/// Resuelve MediaRef a URL pública. Rutas de blob → SAS temporal. URLs https → sin cambios.
/// </summary>
public class BlobMediaUrlResolver : IMediaUrlResolver, IApplicationFileUrlResolver
{
    private static readonly TimeSpan SasExpiry = TimeSpan.FromMinutes(15);

    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<BlobMediaUrlResolver> _logger;
    private readonly SemaphoreSlim _delegationKeyGate = new(1, 1);
    private DelegationKeySnapshot? _delegationKey;
    private sealed record DelegationKeySnapshot(UserDelegationKey Key, DateTimeOffset ExpiresAt);

    public BlobMediaUrlResolver(
        BlobServiceClient blobServiceClient,
        ILogger<BlobMediaUrlResolver> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    public async Task<string> ResolveAsync(Guid businessId, string mediaRef, CancellationToken ct = default)
        => await ResolveFromContainerAsync($"business-{businessId:N}".ToLowerInvariant(), mediaRef, true, ct);

    public async Task<string> ResolveApplicationFileAsync(string containerName, string blobName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(containerName) || string.IsNullOrWhiteSpace(blobName) ||
            Uri.TryCreate(blobName, UriKind.Absolute, out _))
            throw new ArgumentException("La descarga requiere un contenedor y una ruta relativa configurados.");
        // Preserve the storage error (including 404) and check only metadata.
        // No executable bytes pass through the application server.
        await _blobServiceClient.GetBlobContainerClient(containerName).GetBlobClient(blobName)
            .GetPropertiesAsync(cancellationToken: ct);
        return await ResolveFromContainerAsync(containerName, blobName, false, ct);
    }

    public async Task<string> ResolveTenantAsync(Guid tenantId, string mediaRef, CancellationToken ct = default)
        => await ResolveFromContainerAsync($"tenant-{tenantId:N}".ToLowerInvariant(), mediaRef, false, ct);

    public Task<IReadOnlyDictionary<string, string>> ResolveBusinessBatchAsync(
        Guid businessId, IReadOnlyCollection<string> mediaRefs, CancellationToken ct = default)
    {
        if (mediaRefs.Count == 0)
            return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(StringComparer.Ordinal));
        var containerName = $"business-{businessId:N}".ToLowerInvariant();
        // Only this storage account's current business container may gain a SAS.
        var root = _blobServiceClient.Uri;
        var prefix = root.AbsolutePath.TrimEnd('/') + "/" + containerName + "/";
        var references = mediaRefs.Distinct(StringComparer.Ordinal).Select(reference => {
            var path = reference;
            if (Uri.TryCreate(reference, UriKind.Absolute, out var uri) &&
                uri.Scheme == root.Scheme && uri.Authority.Equals(root.Authority, StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal))
                path = Uri.UnescapeDataString(uri.AbsolutePath[prefix.Length..]);
            return (Reference: reference, Path: path);
        }).ToArray();
        return ResolveBatchAsync(containerName, references, ct);
    }

    public Task<IReadOnlyDictionary<string, string>> ResolveTenantBatchAsync(
        Guid tenantId, IReadOnlyCollection<string> mediaRefs, CancellationToken ct = default)
        => ResolveBatchAsync($"tenant-{tenantId:N}".ToLowerInvariant(),
            mediaRefs.Distinct(StringComparer.Ordinal).Select(reference => (Reference: reference, Path: reference)).ToArray(), ct);

    private async Task<IReadOnlyDictionary<string, string>> ResolveBatchAsync(
        string containerName, IReadOnlyCollection<(string Reference, string Path)> references, CancellationToken ct)
    {
        if (references.Count == 0) return new Dictionary<string, string>(StringComparer.Ordinal);
        UserDelegationKey? key = null;
        // Prepare the credential once; signing each item performs no Blob I/O.
        if (references.Any(reference => !Uri.TryCreate(reference.Path, UriKind.Absolute, out var uri) || uri.Scheme != "https") &&
            !_blobServiceClient.GetBlobContainerClient(containerName).GetBlobClient("products/").CanGenerateSasUri)
            key = await GetDelegationKeyAsync(ct);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reference in references)
        {
            ct.ThrowIfCancellationRequested();
            result.Add(reference.Reference, await ResolveFromContainerAsync(containerName, reference.Path, false, ct, key));
        }
        return result;
    }

    private async Task<string> ResolveFromContainerAsync(
        string containerName, string mediaRef, bool verifyExists, CancellationToken ct,
        UserDelegationKey? preparedKey = null)
    {
        if (string.IsNullOrWhiteSpace(mediaRef))
            throw new ArgumentException("MediaRef no puede estar vacío", nameof(mediaRef));

        if (Uri.TryCreate(mediaRef, UriKind.Absolute, out var uri) && uri.Scheme == "https")
        {
            if (verifyExists)
                _logger.LogInformation("MediaRef es URL absoluta, retornando tal cual: {MediaRef}", mediaRef);
            return mediaRef;
        }

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(mediaRef);

        if (verifyExists)
            _logger.LogInformation(
                "Resolviendo MediaRef: MediaRef={MediaRef}, Container={ContainerName}",
                mediaRef, containerName);

        if (verifyExists && !(await blobClient.ExistsAsync(ct)).Value)
        {
            _logger.LogError("El blob NO existe: Container={Container}, Blob={Blob}", containerName, mediaRef);
            throw new InvalidOperationException($"Blob no encontrado: {mediaRef}");
        }

        var expiresOn = DateTimeOffset.UtcNow.Add(SasExpiry);
        Uri sasUri;
        if (blobClient.CanGenerateSasUri)
        {
            sasUri = blobClient.GenerateSasUri(BlobSasPermissions.Read, expiresOn);
        }
        else
        {
            // App Service authenticates to Blob Storage with managed identity. A
            // user delegation SAS lets the browser read the same private blob.
            var startsOn = DateTimeOffset.UtcNow.AddMinutes(-5);
            var key = preparedKey ?? await GetDelegationKeyAsync(ct);
            var sas = new BlobSasBuilder
            {
                BlobContainerName = containerName,
                BlobName = mediaRef,
                Resource = "b",
                StartsOn = startsOn,
                ExpiresOn = expiresOn
            };
            sas.SetPermissions(BlobSasPermissions.Read);
            sasUri = new BlobUriBuilder(blobClient.Uri)
            {
                Sas = sas.ToSasQueryParameters(key, _blobServiceClient.AccountName)
            }.ToUri();
        }
        if (verifyExists)
            _logger.LogInformation(
                "SAS generado correctamente para BlobPath={MediaRef}, expira en {Minutes} min",
                mediaRef, SasExpiry.TotalMinutes);
        return sasUri.ToString();
    }

    private async Task<UserDelegationKey> GetDelegationKeyAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _delegationKey) is { } current &&
            current.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(20))
            return current.Key;

        await _delegationKeyGate.WaitAsync(ct);
        try
        {
            if (Volatile.Read(ref _delegationKey) is { } cached &&
                cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(20))
                return cached.Key;

            var now = DateTimeOffset.UtcNow;
            var expiresAt = now.AddHours(1);
            var key = await _blobServiceClient.GetUserDelegationKeyAsync(
                now.AddMinutes(-5), expiresAt, ct);
            Volatile.Write(ref _delegationKey, new DelegationKeySnapshot(key.Value, expiresAt));
            return key.Value;
        }
        finally
        {
            _delegationKeyGate.Release();
        }
    }
}
