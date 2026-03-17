using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Infrastructure.Services;

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
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

            var blobClient = containerClient.GetBlobClient(fileName);
            await blobClient.UploadAsync(imageStream, overwrite: true);

            var url = blobClient.Uri.ToString();
            _logger.LogInformation("Imagen subida: {FileName}", fileName);
            return url;
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
