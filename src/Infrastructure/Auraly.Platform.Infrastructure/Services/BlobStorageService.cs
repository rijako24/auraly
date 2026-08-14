using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Infrastructure.Services;

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(
        BlobServiceClient blobServiceClient,
        ILogger<BlobStorageService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    private static string GetContainerName(Guid businessId) => $"business-{businessId:N}".ToLowerInvariant();

    public async Task<string> UploadImageAsync(Guid businessId, Stream imageStream, string fileName)
    {
        try
        {
            var container = GetContainerName(businessId);
            var containerClient = _blobServiceClient.GetBlobContainerClient(container);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

            var blobClient = containerClient.GetBlobClient(fileName);
            var contentType = Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };
            await blobClient.UploadAsync(imageStream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            });
            _logger.LogInformation("Imagen subida: {FileName}", fileName);
            return fileName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al subir imagen {FileName}", fileName);
            throw;
        }
    }

    public async Task<string> GetImageUrlAsync(Guid businessId, string fileName)
    {
        try
        {
            var container = GetContainerName(businessId);
            var containerClient = _blobServiceClient.GetBlobContainerClient(container);
            var blobClient = containerClient.GetBlobClient(fileName);
            
            if (await blobClient.ExistsAsync())
            {
                return blobClient.Uri.ToString();
            }

            _logger.LogWarning("Imagen no encontrada: {FileName}", fileName);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener URL de imagen {FileName}", fileName);
            return string.Empty;
        }
    }

    public async Task<bool> ImageExistsAsync(Guid businessId, string fileName)
    {
        try
        {
            var container = GetContainerName(businessId);
            var containerClient = _blobServiceClient.GetBlobContainerClient(container);
            var blobClient = containerClient.GetBlobClient(fileName);
            return await blobClient.ExistsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar existencia de imagen {FileName}", fileName);
            return false;
        }
    }
}
