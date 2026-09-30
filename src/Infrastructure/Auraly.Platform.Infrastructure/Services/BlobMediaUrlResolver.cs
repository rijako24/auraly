using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Infrastructure.Services;

/// <summary>
/// Resuelve MediaRef a URL pública. Rutas de blob → SAS temporal. URLs https → sin cambios.
/// </summary>
public class BlobMediaUrlResolver : IMediaUrlResolver
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

    public async Task<string> ResolveTenantAsync(Guid tenantId, string mediaRef, CancellationToken ct = default)
        => await ResolveFromContainerAsync($"tenant-{tenantId:N}".ToLowerInvariant(), mediaRef, false, ct);

    private async Task<string> ResolveFromContainerAsync(
        string containerName, string mediaRef, bool verifyExists, CancellationToken ct)
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
            var key = await GetDelegationKeyAsync(ct);
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
