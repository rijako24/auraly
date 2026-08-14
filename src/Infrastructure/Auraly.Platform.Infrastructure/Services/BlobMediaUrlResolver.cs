using Azure.Storage.Blobs;
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

    public BlobMediaUrlResolver(
        BlobServiceClient blobServiceClient,
        ILogger<BlobMediaUrlResolver> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    public async Task<string> ResolveAsync(Guid businessId, string mediaRef, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(mediaRef))
            throw new ArgumentException("MediaRef no puede estar vacío", nameof(mediaRef));

        if (Uri.TryCreate(mediaRef, UriKind.Absolute, out var uri) && uri.Scheme == "https")
        {
            _logger.LogInformation("MediaRef es URL absoluta, retornando tal cual: {MediaRef}", mediaRef);
            return mediaRef;
        }

        var containerName = $"business-{businessId:N}".ToLowerInvariant();
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(mediaRef);

        _logger.LogInformation(
            "Resolviendo MediaRef: BusinessId={BusinessId}, MediaRef={MediaRef}, Container={ContainerName}",
            businessId, mediaRef, containerName);

        var exists = await blobClient.ExistsAsync(ct);
        if (!exists.Value)
        {
            _logger.LogError("El blob NO existe: Container={Container}, Blob={Blob}", containerName, mediaRef);
            throw new InvalidOperationException($"Blob no encontrado: {mediaRef}");
        }

        if (!blobClient.CanGenerateSasUri)
        {
            _logger.LogWarning(
                "BlobClient no puede generar SAS (¿credenciales Shared Key?). MediaRef={MediaRef}",
                mediaRef);
            throw new InvalidOperationException(
                "El almacenamiento configurado no soporta generación de SAS. Use URLs públicas para MediaRef.");
        }

        var sasUri = blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(SasExpiry));
        _logger.LogInformation(
            "SAS generado correctamente para BlobPath={MediaRef}, expira en {Minutes} min",
            mediaRef, SasExpiry.TotalMinutes);
        return sasUri.ToString();
    }
}
